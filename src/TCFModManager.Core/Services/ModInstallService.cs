using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Downloads, extracts, and installs a mod version's files into an SPT install, and records what it placed for later uninstall.
public sealed class ModInstallService(
    ModDownloadService downloadService,
    ModInstallManifestService manifestService,
    ConfigCarryOver? configCarryOver = null,
    ConfigUpdateLog? configUpdateLog = null,
    ModConfigOptionsStore? configOptions = null,
    RemovedMods? removedMods = null,
    ProfileBackups? profileBackups = null)
{
    private readonly RemovedMods _removed = removedMods ?? new RemovedMods();

    private readonly ConfigCarryOver _configs = configCarryOver ?? new ConfigCarryOver();
    private readonly ModConfigOptionsStore _options = configOptions ?? new ModConfigOptionsStore();
    private readonly ConfigUpdateLog _configLog = configUpdateLog ?? new ConfigUpdateLog();

    // Scratch folder created inside the SPT install so extracted files can be moved into
    // place rather than copied across volumes. Falls back to %TEMP% when it can't be created.
    private const string WorkFolderName = ".tcfmm-work";

    private const int CopyBufferSize = 1 << 20;

    // Minimum gap between status reports during extract/install, so a several-thousand-file
    // archive doesn't post one UI update per file.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    // Processes that hold handles on files inside an SPT install. Placing or deleting a mod's
    // files while one of these is running fails partway through, which on an update leaves the old
    // version already removed - so both install and uninstall refuse to start until they're closed.
    private static readonly string[] BlockingProcessNames = ["EscapeFromTarkov", "SPT.Server", "Aki.Server"];

    //
    // Fork: how the guard finds running processes by name. Replaced by the tests (see TestSetup
    // there): their installs are temporary folders, and a game or server running on the machine that
    // runs them - one whose path Windows will not show counts as blocking every install - failed
    // tests that have nothing to do with it.
    //
    internal static Func<string, Process[]> FindProcesses { get; set; } = Process.GetProcessesByName;

    //
    // The blocking processes running OUT OF THIS INSTALL, or an empty list when it is safe to modify.
    //
    // Scoped to the install on purpose. More than one SPT lives on a machine as soon as anyone runs
    // a second version or keeps a dedicated server apart from the copy they play - and the two share
    // nothing but the name of an executable. A server running from D:\ holds no handle anywhere in
    // an install on E:\, so refusing to touch E:\ because of it blocks work that was never at risk,
    // with a message telling the user to close the one thing they cannot close: the server they are
    // modding the other install FOR.
    //
    // Passing no path keeps the old machine-wide behaviour, for callers that genuinely have no
    // install in hand.
    //
    public static IReadOnlyList<string> RunningBlockers(string? installPath = null)
    {
        var running = new List<string>();

        foreach (var name in BlockingProcessNames)
        {
            Process[] found;

            try
            {
                found = FindProcesses(name);
            }
            catch (InvalidOperationException)
            {
                // Process list unavailable - treated as nothing running rather than blocking the user.
                continue;
            }

            try
            {
                if (found.Any(p => BlocksInstall(p, installPath))) running.Add(name + ".exe");
            }
            finally
            {
                foreach (var process in found) process.Dispose();
            }
        }

        return running;
    }

    //
    // Whether one running process is a reason not to touch this install.
    //
    // An unreadable path counts as blocking. Windows refuses MainModule for a process this one has
    // no right to inspect - another user's, or an elevated one - and "I could not tell" is not
    // "it is fine": guessing wrong the other way corrupts an install mid-update, which is the exact
    // thing this guard exists to prevent. The old behaviour was to block on every match, so this
    // costs nothing that was ever available.
    //
    private static bool BlocksInstall(Process process, string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return true;

        string? executable;

        try
        {
            executable = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            AppLog.Debug("Install",
                $"could not read the path of {process.ProcessName}; treating it as in use");
            return true;
        }

        return executable is null || IsInside(executable, installPath);
    }

    //
    // Whether a file sits inside a folder.
    //
    // Compared as full paths with a trailing separator, so "E:\SPT Server" is not read as containing
    // "E:\SPT Server 4.1\...". Case-insensitively, which is right on Windows and near enough
    // everywhere this runs.
    //
    internal static bool IsInside(string filePath, string folder)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;

            return Path.GetFullPath(filePath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path neither side can resolve is not one this can reason about; the caller treats
            // that as still blocking.
            return true;
        }
    }

    //
    // Throws when a blocking process is running, carrying what to close and which operation was
    // refused. It takes the operation rather than a verb phrase: the caller says what it was doing,
    // App/Services/ModInstallProblems says it in English.
    //
    public static void EnsureInstallNotInUse(ModInstallAction action, string? installPath = null)
    {
        var running = RunningBlockers(installPath);
        if (running.Count == 0) return;

        throw new ModInstallException(ModInstallFailure.InstallInUse)
        {
            Running = running,
            Action = action,
        };
    }

    // Downloads and installs <paramref name="version"/> of <paramref name="target"/> into
    // <paramref name="installPath"/>. If a record already exists for this target (an update), its
    // old files are removed once the new archive has downloaded and extracted successfully.
    // Cancellation is honoured up to the point the old version is removed; once files
    // start being placed into the install the operation runs to completion.
    //
    // A mod and an addon are installed by exactly the same path: an addon's archive is an ordinary
    // SPT mod package, and its download link and size come from the same fields.
    //
    // The result carries the record plus what the update did to the mod's own config files - see
    // ConfigCarryOver. Null configs means there were none to have an opinion about.
    //
    // Fork - <paramref name="downloadedArchive"/>: the version's archive, already on this PC (the
    // queue downloads ahead of installing and keeps archives - see ModArchiveCache - and Install from
    // file has one to begin with). Read where it is, not moved or deleted; nothing is downloaded then.
    public async Task<ModInstallResult> InstallAsync(
        InstallTarget target,
        ModVersion version,
        string installPath,
        IProgress<ModInstallProgress>? status = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default,
        string? downloadedArchive = null)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            throw new ModInstallException(ModInstallFailure.NoInstallFolder);

        if (string.IsNullOrWhiteSpace(version.Link))
            throw new ModInstallException(ModInstallFailure.NoDownloadLink)
            {
                ModName = target.Name,
                Version = version.Version,
            };

        EnsureInstallNotInUse(ModInstallAction.Install, installPath);

        AppLog.Info("Install",
            $"{target.Name} {version.Version} ({(target.IsAddon ? "addon" : "mod")} {target.Id}) -> {installPath}");

        var workDir = CreateWorkDirectory(installPath, out var canMoveIntoInstall);
        AppLog.Debug("Install", $"work dir {workDir} (move into install: {canMoveIntoInstall})");
        var archivePath = downloadedArchive ?? Path.Combine(workDir, "download.bin");
        var extractDir = Path.Combine(workDir, "extracted");

        try
        {
            ct.ThrowIfCancellationRequested();

            if (downloadedArchive is null)
            {
                status?.Report(new ModInstallProgress(
                    ModInstallStage.Downloading, target.Name, version.Version));
                await downloadService.DownloadAsync(version.Link, archivePath, downloadProgress, ct).ConfigureAwait(false);
            }
            else
            {
                // Fork: off the caller's thread for the rest, as the download's ConfigureAwait(false)
                // would have put it - the queue calls this from the UI thread, and extracting and
                // placing would otherwise freeze the window for the whole install.
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }

            ct.ThrowIfCancellationRequested();

            var archiveBytes = new FileInfo(archivePath).Length;
            AppLog.Debug("Install", $"downloaded {archiveBytes:N0} bytes, zip={ArchiveLayout.IsZipArchive(archivePath)}");

            status?.Report(new ModInstallProgress(ModInstallStage.Extracting));
            // Auto-detects archive format from the file header rather than assuming zip.
            var extractTimer = System.Diagnostics.Stopwatch.StartNew();
            await ExtractArchiveAsync(archivePath, extractDir, status, ct).ConfigureAwait(false);
            AppLog.Debug("Install", $"extracted in {extractTimer.ElapsedMilliseconds}ms");

            //
            // Neither extractor is trusted to have refused links (SharpCompress 0.50.4 writes 7z links
            // as plain files; RAR was never tested), so the tree is checked before anything in the
            // install is touched (D16).
            //
            if (InstallPathGuard.FirstLink(extractDir) is { } link)
            {
                AppLog.Warn("Install", $"{target.Name} {version.Version} archive holds a link: {link}");

                throw new ModInstallException(ModInstallFailure.ArchiveContainsLink)
                {
                    ModName = target.Name,
                    Version = version.Version,
                    ArchiveEntry = link,
                };
            }

            ct.ThrowIfCancellationRequested();

            var contentRoot = ArchiveLayout.FindContentRoot(extractDir);
            var topLevelNames = Directory.GetFileSystemEntries(contentRoot).Select(Path.GetFileName);

            // Fork: plugins/ and patchers/ on their own go under BepInEx\ (ArchiveLayout).
            var bareBepInEx = ArchiveLayout.IsBareBepInEx(contentRoot);

            if (!bareBepInEx && !topLevelNames.Any(ArchiveLayout.IsKnownRoot))
            {
                AppLog.Warn("Install",
                    $"{target.Name} {version.Version} archive has no known root folder; top level: " +
                    string.Join(", ", Directory.GetFileSystemEntries(contentRoot).Select(Path.GetFileName)));

                throw new ModInstallException(ModInstallFailure.UnrecognisedArchive)
                {
                    ModName = target.Name,
                    Version = version.Version,
                };
            }

            // Archives package server-side content as "user/..."; remap it to wherever this install
            // actually keeps user/mods (e.g. nested under "SPT" or "SPT_Runtime"). BepInEx stays at
            // the install root. Falls back to no remapping if the server exe can't be found.
            SptInstallationService.TryGetServerRoot(installPath, out var serverRoot);

            var sourceFiles = Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories);

            ct.ThrowIfCancellationRequested();

            // Re-checked now the download is finished: SPT may have been started while it ran, and
            // everything past this point deletes or places files inside the install.
            EnsureInstallNotInUse(ModInstallAction.Install, installPath);

            var manifest = manifestService.Load();
            var existing = manifest.Mods.FirstOrDefault(target.Matches);

            if (existing is not null) EnsureRecordBelongsHere(existing, installPath);

            //
            // Where each source file is going, worked out before anything is removed: the config
            // files the archive is about to place over have to be known while they are still there.
            //
            var allPlacements = sourceFiles
                .Select(file =>
                {
                    var contentRelative = Path.GetRelativePath(contentRoot, file);

                    // Fork: a read-me, licence or picture at the top of the content - beside BepInEx/
                    // and user/ - is for the person installing, and is never put in the SPT folder.
                    if (ArchiveLayout.IsTopLevelBeside(contentRelative)) return null;

                    // Fork: a read-me or picture beside a bare plugins/ is not placed.
                    if (bareBepInEx)
                    {
                        if (ArchiveLayout.MapBareBepInEx(contentRelative) is not { } mapped) return null;
                        contentRelative = mapped;
                    }

                    var installRelative = ArchiveLayout.RemapForServerRoot(contentRelative, serverRoot);
                    // Forward-slash regardless of OS, matching InstalledModRecord.Files's documented format.
                    return ((string File, string Relative, string Forward)?)(file, installRelative, installRelative.Replace('\\', '/'));
                })
                .OfType<(string File, string Relative, string Forward)>()
                .ToList();

            //
            // Every destination is judged before anything in the install is removed or placed (D3, D7).
            // SPT's, BepInEx's and the game's own files - and this app's own folder - are skipped and
            // never recorded, so nothing can later remove them. A destination reached through a link
            // would write into whatever the link points at, so the whole install is refused instead.
            //
            var skippedProtected = new List<string>();
            var placements = new List<(string File, string Relative, string Forward)>(allPlacements.Count);

            foreach (var placement in allPlacements)
            {
                var refusal = InstallPathGuard.CheckPlacedPath(installPath, placement.Forward, out var destinationPath);

                //
                // In a new-file-only area (R17) the archive's file goes in only where nothing is there yet -
                // or where what is there is the previous version's own copy, proven by its fingerprint,
                // which the removal half below takes into holding first.
                //
                if (refusal is null
                    && ProtectedInstallPaths.IsNewFileOnly(placement.Forward)
                    && (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                    && !(existing is not null && InstallPathGuard.ProvenPlacedFile(installPath, existing, placement.Forward, out _)))
                {
                    refusal = PathRefusal.Protected;
                }

                switch (refusal)
                {
                    case null:
                        placements.Add(placement);
                        break;

                    case PathRefusal.Protected or PathRefusal.AppFolder:
                        skippedProtected.Add(placement.Forward);
                        AppLog.Warn("Install", $"{target.Name} {version.Version}: kept the install's own {placement.Forward}; the archive's copy was not placed");
                        break;

                    case PathRefusal.Link:
                        throw new ModInstallException(ModInstallFailure.InstallThroughLink)
                        {
                            ModName = target.Name,
                            Version = version.Version,
                            Folder = placement.Forward,
                        };

                    default:
                        throw new ModInstallException(ModInstallFailure.UnsafeArchiveEntry) { ArchiveEntry = placement.Forward };
                }
            }

            // Fork: a copy of the SPT profiles from before the change, when they changed since the
            // last one - off the caller's thread by now, once SPT is known not to be running, and
            // past every refusal above, so an install that is refused takes none.
            profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeInstall);

            var timestamp = DateTimeOffset.UtcNow;

            var pending = _configs.Prepare(
                installPath, existing, placements.Select(p => p.Forward), target.Name, timestamp);

            //
            // Files this install will place over that no record owns - a hand install's, or a game file
            // outside the protected set - are copied into Data before anything is touched (D22). One
            // that can't be copied stops the install here, while the install is still as it was.
            //
            var overwrote = KeepOriginals(installPath, target, version, existing, manifest, placements, pending, timestamp);

            if (existing is not null)
            {
                status?.Report(new ModInstallProgress(
                    ModInstallStage.RemovingPrevious, Version: existing.Version));

                //
                // Preserve, not Keep: Prepare has already copied every config aside, and moving them
                // again from here would leave the archive holding two copies of the same file. What
                // Preserve adds over Delete is the user's own documents - a mod's presets are not
                // reinstalled, so the removal half of an update must not take them out either.
                //
                // The files Prepare could not copy are named separately and left exactly as they are.
                //
                RemoveRecordedFiles(
                    installPath,
                    existing,
                    ConfigAction.Preserve,
                    pending.Protected.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    placements.Select(p => p.Forward).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    RemovalKind.ReplacedByUpdate,
                    CancellationToken.None);
            }

            status?.Report(new ModInstallProgress(
                ModInstallStage.Installing, Total: placements.Count));
            // Fork: the archive's own copy of every file about to be placed, read before placing moves
            // them, so what landed can be checked against it afterwards (InstallVerification).
            var archivePrints = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
            foreach (var placement in placements)
            {
                if (FileFingerprint.Compute(placement.File, placement.Forward) is { } print) archivePrints[placement.Forward] = print;
            }

            var placedFiles = new List<string>(placements.Count);
            var keptSettings = new List<string>();
            var reportClock = Stopwatch.StartNew();

            try
            {
                for (var i = 0; i < placements.Count; i++)
                {
                    var (file, installRelative, installRelativeForward) = placements[i];

                    //
                    // Two kinds of file the archive does not get to place: a config that could not be
                    // copied aside, and one of the user's own documents that is already there. Both
                    // keep the version on disk, and both are still recorded as this install's files so
                    // a later removal knows about them.
                    //
                    // Fork: the user's plugin settings already in BepInEx\config - see ConfigCarryOver.
                    if (pending.KeptSettings.Contains(installRelativeForward))
                    {
                        keptSettings.Add(installRelativeForward);
                        if (existing?.Files.Contains(installRelativeForward, StringComparer.OrdinalIgnoreCase) == true)
                            placedFiles.Add(installRelativeForward);
                        continue;
                    }

                    if (pending.Untouchable.Contains(installRelativeForward)
                        || pending.Preserved.Contains(installRelativeForward))
                    {
                        placedFiles.Add(installRelativeForward);
                        continue;
                    }

                    var destination = Path.Combine(installPath, installRelative);

                    // Checked again at the moment of placing: a new-file-only path (R17) is never
                    // written over, whatever was decided before the previous version was removed.
                    if (ProtectedInstallPaths.IsNewFileOnly(installRelativeForward)
                        && (File.Exists(destination) || Directory.Exists(destination)))
                    {
                        skippedProtected.Add(installRelativeForward);
                        AppLog.Warn("Install", $"{target.Name} {version.Version}: {installRelativeForward} appeared before it was placed; left as it is");
                        continue;
                    }

                    var destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

                    if (canMoveIntoInstall) File.Move(file, destination, overwrite: true);
                    else File.Copy(file, destination, overwrite: true);

                    placedFiles.Add(installRelativeForward);

                    if (reportClock.Elapsed >= ProgressInterval)
                    {
                        status?.Report(new ModInstallProgress(
                            ModInstallStage.Installing, Done: i + 1, Total: placements.Count));
                        reportClock.Restart();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // What was placed before the failure is recorded anyway, so those files stay
                // app-managed: a retry overwrites them and a removal cleans them up. Without this an
                // interrupted update leaves the old version deleted and the new one untracked. The
                // originals kept so far are recorded too - they are owed back whatever happens next.
                SaveRecord(target, version, placedFiles, incomplete: true, installPath, overwrote,
                    KeepSettingsPrints(Fingerprint(installPath, placedFiles), pending, existing));

                AppLog.Error("Install",
                    $"{target.Name} {version.Version} incomplete after {placedFiles.Count}/{placements.Count} file(s)", ex);

                throw new ModInstallException(ModInstallFailure.PartlyInstalled, ex)
                {
                    ModName = target.Name,
                    Version = version.Version,
                    PlacedFiles = placedFiles.Count,
                    TotalFiles = placements.Count,
                };
            }

            // Fork: the empty folders the archive ships, inside a mod's own folder (SVM's Presets\).
            CreateEmptyFolders(archivePath, extractDir, contentRoot, bareBepInEx, serverRoot, installPath, target, version);

            var record = SaveRecord(target, version, placedFiles, incomplete: false, installPath, overwrote, fingerprints: []);

            AppLog.Info("Install",
                $"{target.Name} {version.Version} placed {placedFiles.Count} file(s) in folders [{string.Join(", ", record.Folders)}]"
                + (skippedProtected.Count > 0 ? $"; kept {skippedProtected.Count} of the install's own file(s)" : "")
                + (overwrote.Count > 0 ? $"; kept {overwrote.Count} original(s) it replaced" : ""));

            var report = _configs.Settle(pending, installPath, target, existing, record, timestamp);

            if (report.Files.Count > 0)
            {
                _configLog.Add(report);
                AppLog.Info("Configs",
                    $"{target.Name} {record.Version}: " +
                    string.Join(", ", report.Files.Select(f => $"{f.Path} {f.Kind}{(f.Reason is { } r ? $" ({r})" : "")}")));
            }

            //
            // Fingerprinted last, after Settle has merged or restored configs, so what is recorded is
            // what is actually on disk now (D21).
            //
            var fingerprintClock = Stopwatch.StartNew();
            record = SaveRecord(target, version, placedFiles, incomplete: false, installPath, overwrote,
                KeepSettingsPrints(Fingerprint(installPath, placedFiles), pending, existing));
            AppLog.Debug("Install", $"fingerprinted {record.Fingerprints.Count} file(s) in {fingerprintClock.ElapsedMilliseconds}ms");

            status?.Report(new ModInstallProgress(ModInstallStage.Done));
            if (keptSettings.Count > 0)
                AppLog.Info("Install", $"{target.Name} {version.Version}: kept the settings already in {string.Join(", ", keptSettings)}");

            //
            // Fork: every archive file checked on disk against the archive's copy. Left out: what this
            // install deliberately did not place as the archive has it - SPT's own files it refused
            // (named on their own), the user's settings and documents it kept, and server configs the
            // config handling merged or kept.
            //
            var deliberate = new HashSet<string>(skippedProtected, StringComparer.OrdinalIgnoreCase);
            deliberate.UnionWith(keptSettings);
            deliberate.UnionWith(pending.Untouchable);
            deliberate.UnionWith(pending.Preserved);
            deliberate.UnionWith(report.Files.Select(f => f.Path));

            var notAsInArchive = InstallVerification.Check(
                archivePrints,
                record.Fingerprints,
                deliberate,
                path => File.Exists(Path.Combine(installPath, path.Replace('/', Path.DirectorySeparatorChar)))
                    ? new FileInfo(Path.Combine(installPath, path.Replace('/', Path.DirectorySeparatorChar))).Length
                    : null);

            foreach (var mismatch in notAsInArchive)
            {
                AppLog.Warn("Install",
                    $"{target.Name} {version.Version}: {mismatch.Path} is not as the archive has it ({mismatch.Kind}; archive {mismatch.ArchiveSize:N0} bytes, disk {(mismatch.DiskSize is { } d ? d.ToString("N0") : "none")})");
            }

            return new ModInstallResult(record, report.Files.Count > 0 ? report : null, skippedProtected)
            {
                OriginalsKept = overwrote.Count - (existing?.Overwrote.Count ?? 0),
                KeptSettings = keptSettings,
                NotAsInArchive = notAsInArchive,
            };
        }
        catch (OperationCanceledException)
        {
            AppLog.Info("Install", $"{target.Name} {version.Version} cancelled");
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("Install", $"{target.Name} {version.Version} failed", ex);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    // Writes the record for what an install placed, replacing any previous record for the same mod.
    // The manifest is reloaded rather than reusing an earlier copy, since UninstallAsync may have
    // saved a removal of the old record in between.
    private InstalledModRecord SaveRecord(
        InstallTarget target,
        ModVersion version,
        List<string> placedFiles,
        bool incomplete,
        string installPath,
        List<OverwrittenFile> overwrote,
        List<FileFingerprint> fingerprints)
    {
        var record = new InstalledModRecord
        {
            ModId = target.Id,
            IsAddon = target.IsAddon,
            Guid = target.Guid,
            Name = target.Name,
            VersionId = version.Id,
            Version = version.Version ?? "unknown",
            InstalledAt = DateTimeOffset.UtcNow,
            Files = placedFiles,
            Folders = InstalledModFolders.FromPlacedFiles(placedFiles),
            Incomplete = incomplete,
            Fingerprints = fingerprints,
            InstallPath = InstallStamp.Of(installPath),
            Overwrote = overwrote,
        };

        var current = manifestService.Load();
        current.Mods.RemoveAll(target.Matches);
        current.Mods.Add(record);
        manifestService.Save(current);

        return record;
    }

    //
    // The size and SHA-256 of every recorded file still on disk. A file that can't be read gets no
    // fingerprint, and removal falls back to the path checks for it - never a guessed one.
    //
    //
    // Fork: a kept BepInEx\config file keeps the PREVIOUS version's fingerprint rather than one taken
    // now. Taken now, the user's tuned copy would read as "exactly what this app placed", and the next
    // update would put its defaults over it, a removal take it out. With the old one it still reads as
    // changed since install, and stays; with none (a record from before fingerprints) it stays too.
    //
    private static List<FileFingerprint> KeepSettingsPrints(
        List<FileFingerprint> prints, PendingConfigs pending, InstalledModRecord? existing)
    {
        if (pending.KeptSettings.Count == 0) return prints;

        prints.RemoveAll(p => pending.KeptSettings.Contains(p.Path));
        foreach (var path in pending.KeptSettings)
        {
            if (existing?.FingerprintFor(path) is { } old) prints.Add(old);
        }

        return prints;
    }

    private static List<FileFingerprint> Fingerprint(string installPath, IEnumerable<string> recorded)
    {
        var prints = new List<FileFingerprint>();

        foreach (var path in recorded)
        {
            // CheckPlacedPath: a file this install just put in a new-file-only area (R17) needs its
            // fingerprint more than any - it is the only thing that lets a removal take it back out.
            if (InstallPathGuard.CheckPlacedPath(installPath, path, out var full) is null
                && FileFingerprint.Compute(full, path) is { } print)
            {
                prints.Add(print);
            }
        }

        return prints;
    }

    //
    // Copies every file this install is about to place over, that no record owns, into
    // Data\overwritten\<mod>\<time>\<path> and returns them, together with the originals an earlier
    // version of this mod already kept (still owed back). Skipped: files another record lists (that's
    // shared-file ownership, D9/D10), files the earlier version of this mod placed (its own old copy,
    // removed by the update), configs Prepare already copied aside, files that keep their version on
    // disk, and anything already kept by an earlier install of this mod.
    //
    private static List<OverwrittenFile> KeepOriginals(
        string installPath,
        InstallTarget target,
        ModVersion version,
        InstalledModRecord? existing,
        ModInstallManifest manifest,
        IReadOnlyList<(string File, string Relative, string Forward)> placements,
        PendingConfigs pending,
        DateTimeOffset timestamp)
    {
        var kept = new List<OverwrittenFile>(existing?.Overwrote ?? []);
        var alreadyKept = kept.Select(k => k.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var owned = manifest.Mods
            .SelectMany(m => m.Files)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The GUIDs each affected mod folder declares, read once per folder while it still holds what
        // was there before this install.
        var declared = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        var key = target.IsAddon ? $"{target.Id}-addon" : target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.Combine(OverwrittenDirectoryName, key, $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}");

        foreach (var (_, relative, forward) in placements)
        {
            if (owned.Contains(forward) || alreadyKept.Contains(forward)) continue;
            if (pending.Archived.ContainsKey(forward)) continue;
            if (pending.Untouchable.Contains(forward) || pending.Preserved.Contains(forward)) continue;

            var destination = Path.Combine(installPath, relative);
            if (!File.Exists(destination)) continue;

            var backupRelative = Path.Combine(folder, relative);
            var backup = Path.Combine(AppPaths.DataDirectory, backupRelative);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(destination, backup, overwrite: false);

                var print = FileFingerprint.Compute(backup, forward)
                    ?? throw new IOException($"couldn't read back {backup}");

                var sameMod = IsEarlierCopyOfSameMod(installPath, target, forward, declared);

                kept.Add(new OverwrittenFile(forward, print.Size, print.Sha256, backupRelative.Replace('\\', '/'), sameMod));
                alreadyKept.Add(forward);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Install", $"{target.Name} {version.Version}: couldn't keep the original {forward} before replacing it", ex);

                throw new ModInstallException(ModInstallFailure.OriginalNotKept, ex)
                {
                    ModName = target.Name,
                    Version = version.Version,
                    Folder = forward,
                };
            }
        }

        if (kept.Count > (existing?.Overwrote.Count ?? 0))
            AppLog.Info("Install", $"{target.Name} {version.Version}: kept {kept.Count - (existing?.Overwrote.Count ?? 0)} original file(s) in {folder}");

        return kept;
    }

    //
    // Whether the mod folder a replaced file sits in declares the GUID of the mod being installed -
    // proof that the file belongs to an earlier copy of the same mod (R16). False whenever that can't be
    // shown: an addon (sp-mod.com gives addons no GUID), a mod with no catalog GUID, a file outside any
    // mod folder, or a folder whose DLLs declare something else or nothing.
    //
    private static bool IsEarlierCopyOfSameMod(
        string installPath, InstallTarget target, string forward, Dictionary<string, IReadOnlySet<string>> declared)
    {
        if (target.IsAddon || string.IsNullOrWhiteSpace(target.Guid)) return false;

        // Fork: a server enum prepatch's folder is named after its mod's GUID by SPT's own rule, which
        // is the proof here - there are no DLLs in it to read. Without this, an earlier copy of the
        // same mod's prepatch that no record owned (Skills Extended's, after the user\patchers bug)
        // was put back by the removal, leaving the mod's prepatch behind again.
        if (InstallPathGuard.PrepatchFolderOf(forward) is { } prepatchFolder)
            return string.Equals(prepatchFolder, target.Guid, StringComparison.OrdinalIgnoreCase);

        if (InstallPathGuard.ModFolderOf(forward) is not { } folder) return false;

        if (!declared.TryGetValue(folder, out var guids))
        {
            guids = DeclaredGuids(Path.Combine(installPath, folder.Replace('/', Path.DirectorySeparatorChar)));
            declared[folder] = guids;
        }

        return guids.Contains(target.Guid);
    }

    // Every GUID the DLLs in a mod folder (or a loose DLL) declare, client and server alike.
    private static IReadOnlySet<string> DeclaredGuids(string modPath)
    {
        var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> dlls = File.Exists(modPath)
            ? [modPath]
            : Directory.Exists(modPath)
                ? Directory.EnumerateFiles(modPath, "*.dll", SearchOption.AllDirectories).Take(MaxDllsReadForIdentity)
                : [];

        foreach (var dll in dlls)
        {
            if (ModAssemblyMetadata.ReadPlugin(dll).Guid is { Length: > 0 } client) guids.Add(client);
            if (ModAssemblyMetadata.ReadServer(dll)?.Guid is { Length: > 0 } server) guids.Add(server);
        }

        return guids;
    }

    // A mod folder holding more DLLs than this is not read further for its identity.
    private const int MaxDllsReadForIdentity = 200;

    // Under the Data folder: where the originals an install replaced are kept (D22).
    public const string OverwrittenDirectoryName = "overwritten";

    //
    // Removes a mod this app installed (D23): every recorded file that is still exactly what was placed
    // and that no other mod owns is MOVED into the install's holding folder, never deleted; the files it
    // replaced are put back (D22, R16); every path's outcome is logged and written to removal.json
    // (D26). <paramref name="configs"/> decides what happens to the mod's own config JSON files first.
    //
    public Task<UninstallResult> UninstallAsync(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs = ConfigAction.Keep,
        CancellationToken ct = default)
    {
        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);
        EnsureRecordBelongsHere(record, installPath);

        // Fork: the SPT profiles as they were before the removal - see ProfileBackups.
        profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeRemove);

        var result = RemoveRecordedFiles(installPath, record, configs, null, null, RemovalKind.AppInstalled, ct);

        // A mod that is gone has no shipped copies worth keeping. An update does not come through
        // here, which is why this is safe to do unconditionally - see InstallAsync.
        _configs.Baselines.Remove(record.ModId, record.IsAddon);

        return Task.FromResult(result);
    }

    //
    // A record stamped with a different install is never acted on here (D17). An unstamped one - made
    // before v1.19.0 - is: the App names the install in its confirmation before anything happens.
    //
    private static void EnsureRecordBelongsHere(InstalledModRecord record, string installPath)
    {
        if (record.InstallPath is not { } stamped) return;
        if (string.Equals(stamped, InstallStamp.Of(installPath), StringComparison.OrdinalIgnoreCase)) return;

        AppLog.Warn("Remove", $"{record.Name}: its record belongs to {stamped}, not {installPath}; refused");
        throw new ModInstallException(ModInstallFailure.RecordFromAnotherInstall)
        {
            ModName = record.Name,
            Folder = stamped,
        };
    }

    //
    // The removal itself, shared by Remove and the update path. For every recorded file, in order:
    // kept for the user (keep/preserve), refused by InstallPathGuard (D13), kept because another record
    // owns it (D9), already gone, kept because it changed since install (D21 - unless an update is about
    // to place over it, when it is moved and so stays recoverable), otherwise moved into holding (D23).
    //
    // <paramref name="keep"/>: configs ConfigCarryOver could not copy aside. <paramref name="incoming"/>:
    // what the update will place. Both null on a real removal.
    //
    private UninstallResult RemoveRecordedFiles(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs,
        IReadOnlySet<string>? keep,
        IReadOnlySet<string>? incoming,
        RemovalKind kind,
        CancellationToken ct)
    {
        var failed = new List<string>();
        var refused = new List<string>();
        var keptChanged = new List<string>();
        var keptOwned = new List<string>();
        var moved = 0;
        var restored = 0;
        var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var options = _options.Effective();

        //
        // A real removal rescues both a mod's settings and the documents the user wrote in it, and the
        // user-data half is read off disk rather than from the record: SVM's presets are written by its
        // own generator and appear in no file list, which is exactly the case worth rescuing.
        //
        var kept = new KeptConfigs(0, null);

        List<string> configFiles = configs == ConfigAction.Keep
            ?
            [
                .. ModConfigFiles.InRecord(record, options),
                .. ModConfigFiles.UserDataOnDisk(installPath, record, options),
            ]
            : [];

        if (configFiles.Count > 0)
            kept = ModConfigFiles.MoveOut(installPath, configFiles, record.Name, DateTimeOffset.UtcNow);

        // An update: the settings are already copied aside and about to be replaced, but the user's own
        // documents stay where they are.
        HashSet<string> preserve = configs == ConfigAction.Preserve
            ? ModConfigFiles.UserDataInRecord(record, options).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Every file another record lists - those stay with their other owner (D9).
        var owned = manifestService.Load().Mods
            .Where(m => !(m.ModId == record.ModId && m.IsAddon == record.IsAddon))
            .SelectMany(m => m.Files)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var session = RemovedMods.Begin(installPath, record.Name, kind, record);
        session.Log.ConfigsKeptFolder = kept.Folder;

        foreach (var relative in record.Files)
        {
            ct.ThrowIfCancellationRequested();

            if ((keep is not null && keep.Contains(relative)) || preserve.Contains(relative))
            {
                session.Note(relative, RemovalOutcome.KeptForTheUser);
                continue;
            }

            // A file in a new-file-only area (R17) is taken only when its fingerprint proves it is the
            // copy this app placed; anything else there stays, as SPT's or the game's.
            if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var fullPath) is { } refusal
                && !(refusal == PathRefusal.Protected && InstallPathGuard.ProvenPlacedFile(installPath, record, relative, out fullPath)))
            {
                refused.Add(relative);
                session.Note(relative, RemovalOutcome.Refused, refusal);
                continue;
            }

            if (owned.Contains(relative))
            {
                keptOwned.Add(relative);
                session.Note(relative, RemovalOutcome.KeptOwnedByAnotherMod);
                continue;
            }

            if (!File.Exists(fullPath))
            {
                session.Note(relative, RemovalOutcome.AlreadyGone);
                NoteFoldersToTidy(installPath, fullPath, touchedDirectories);
                continue;
            }

            if (record.FingerprintFor(relative) is { } print
                && !print.Matches(fullPath)
                && !(kind == RemovalKind.ReplacedByUpdate && incoming is not null && incoming.Contains(relative)))
            {
                keptChanged.Add(relative);
                session.Note(relative, RemovalOutcome.KeptChangedSinceInstall);
                continue;
            }

            try
            {
                session.MoveFileIn(fullPath, relative);
                session.Note(relative, RemovalOutcome.Moved);
                moved++;
                NoteFoldersToTidy(installPath, fullPath, touchedDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Remove", $"{record.Name}: couldn't move {relative} out: {ex.Message}");
                failed.Add(relative);
                session.Note(relative, RemovalOutcome.Failed);
            }
        }

        // On a real removal, what this mod replaced comes back. An update carries them forward instead.
        if (kind == RemovalKind.AppInstalled)
            restored = RestoreOriginals(installPath, record, session);

        // Fork: the empty folders an install created (CreateEmptyFolders) go too, while still empty.
        TidyEmptyModFolders(installPath, record, touchedDirectories);

        foreach (var dir in touchedDirectories.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir)
                    && InstallPathGuard.MayRemoveEmptyFolder(installPath, dir)
                    && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Directory that won't delete is left alone.
            }
        }

        // On a real removal, the mod's folders that had to stay because they hold files it didn't
        // install - said in the result and marked on the leftover's card, never taken.
        var foldersLeft = kind == RemovalKind.ReplacedByUpdate ? [] : FoldersLeftBehind(installPath, record);
        session.Log.FoldersLeft = [.. foldersLeft.Select(f => f.Folder)];

        var manifest = manifestService.Load();
        manifest.Mods.RemoveAll(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon);
        manifestService.Save(manifest);

        var holding = _removed.Finish(session);

        return new UninstallResult(moved, failed, kept.Count, kept.Folder, refused)
        {
            KeptChanged = keptChanged,
            KeptOwned = keptOwned,
            OriginalsRestored = restored,
            HoldingFolder = holding,
            FoldersLeft = foldersLeft,
        };
    }

    //
    // Puts back what this mod replaced (D22): every original except an earlier copy of the same mod
    // proven by its GUID (R16), and only where the path is free and passes the path checks. Every kept
    // copy then moves out of Data into the holding folder, so Undo has it and retention clears it.
    //
    private static int RestoreOriginals(string installPath, InstalledModRecord record, RemovalSession session)
    {
        var restored = 0;

        foreach (var original in record.Overwrote)
        {
            var backup = Path.Combine(AppPaths.DataDirectory, original.BackupPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(backup))
            {
                session.Note(original.Path, RemovalOutcome.OriginalMissing);
                continue;
            }

            try
            {
                if (original.SameMod)
                {
                    session.Note(original.Path, RemovalOutcome.OriginalHeldSameMod);
                }
                else if (InstallPathGuard.CheckRecordedPath(installPath, original.Path, out var target) is { } refusal)
                {
                    session.Note(original.Path, RemovalOutcome.Refused, refusal);
                }
                else if (File.Exists(target))
                {
                    session.Note(original.Path, RemovalOutcome.OriginalNotRestoredOccupied);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(backup, target, overwrite: false);

                    if (!new FileFingerprint(original.Path, original.Size, original.Sha256).Matches(target))
                        AppLog.Warn("Remove", $"{record.Name}: put back {original.Path}, but its kept copy no longer matches what was kept");

                    session.Note(original.Path, RemovalOutcome.OriginalRestored);
                    restored++;
                }

                session.MoveOriginalIn(original.BackupPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Remove", $"{record.Name}: couldn't put back {original.Path}: {ex.Message}");
                session.Note(original.Path, RemovalOutcome.Failed);
            }
        }

        TidyEmptyOriginalsFolders(record);
        return restored;
    }

    // Removes the now-empty Data\overwritten\<mod>\<time>\... folders - only empty ones, only in there.
    private static void TidyEmptyOriginalsFolders(InstalledModRecord record)
    {
        var key = record.IsAddon ? $"{record.ModId}-addon" : record.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var root = Path.Combine(AppPaths.DataDirectory, OverwrittenDirectoryName, key);
        if (!Directory.Exists(root)) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(root))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Fork (SSPTMM): every folder inside the record's mod folders, so the tidy below removes the ones
    // left empty - including empty folders the install created that no file was ever removed from.
    private static void TidyEmptyModFolders(string installPath, InstalledModRecord record, HashSet<string> touched)
    {
        foreach (var folder in record.Files.Select(InstallPathGuard.ModFolderOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var full = Path.Combine(installPath, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full) || !InstallPathGuard.MayRemoveEmptyFolder(installPath, full)) continue;

            try
            {
                touched.Add(full);
                foreach (var dir in Directory.EnumerateDirectories(full, "*", new EnumerationOptions
                         {
                             RecurseSubdirectories = true,
                             AttributesToSkip = FileAttributes.ReparsePoint,
                             IgnoreInaccessible = true,
                         }))
                {
                    touched.Add(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left as it is; an empty folder is untidy, not broken.
            }
        }
    }

    private static void NoteFoldersToTidy(string installPath, string fullPath, HashSet<string> touched)
    {
        // Only a mod's own folder and what's below it are ever tidied away (D14).
        for (var dir = Path.GetDirectoryName(fullPath);
             dir is not null && InstallPathGuard.MayRemoveEmptyFolder(installPath, dir);
             dir = Path.GetDirectoryName(dir))
        {
            touched.Add(dir);
        }
    }

    //
    // Removes a mod installed by hand (D15, D23): every folder (or loose DLL) is checked first, and only
    // when all pass are they moved - whole - into the holding folder. <paramref name="configsFolder"/> is
    // where KeepLegacyConfigs put the mod's configs, if it did, so Undo can bring them back too.
    //
    public string? RemoveHandInstalled(
        IReadOnlyList<string> paths, string installPath, string modName, string? configsFolder = null)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ModInstallException(ModInstallFailure.NoInstallFolder);

        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        foreach (var path in paths)
        {
            if (InstallPathGuard.CheckModFolder(installPath, path) is { } refusal)
            {
                AppLog.Warn("Remove", $"refused to remove {path} ({refusal})");
                throw new ModInstallException(ModInstallFailure.RemovalRefused) { Folder = path, Refusal = refusal };
            }
        }

        var session = RemovedMods.Begin(installPath, modName, RemovalKind.HandInstalled, record: null);
        session.Log.ConfigsKeptFolder = configsFolder;

        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            var relative = Path.GetRelativePath(Path.GetFullPath(installPath), full).Replace('\\', '/');

            if (Directory.Exists(full)) session.MoveFolderIn(full, relative);
            else if (File.Exists(full)) session.MoveFileIn(full, relative);
            else
            {
                session.Note(relative, RemovalOutcome.AlreadyGone);
                continue;
            }

            session.Note(relative, RemovalOutcome.Moved);
        }

        return _removed.Finish(session);
    }

    //
    // Puts a removal back (D28): the original files this mod had replaced go back to Data, the mod's
    // files and folders come back to the install, its kept configs come back, and its record is
    // restored. A path something else now occupies is never overwritten - it is reported and its copy
    // stays in the holding folder, which is then kept rather than deleted.
    //
    public UndoResult UndoRemoval(string installPath, string folder)
    {
        EnsureInstallNotInUse(ModInstallAction.Undo, installPath);

        var log = RemovedMods.ReadLog(folder);
        if (log is null || log.Undone || log.Kind == RemovalKind.ReplacedByUpdate
            || !string.Equals(log.InstallPath, InstallStamp.Of(installPath), StringComparison.OrdinalIgnoreCase))
        {
            return new UndoResult(false, 0, []);
        }

        var blocked = new List<string>();
        var back = 0;

        // 1. Originals this removal put back leave the install again - into the holding folder, never deleted.
        if (log.Record is { } removedRecord)
        {
            foreach (var entry in log.Entries.Where(e => e.Outcome == RemovalOutcome.OriginalRestored))
            {
                var original = removedRecord.Overwrote.FirstOrDefault(o => string.Equals(o.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                if (original is null) continue;

                if (InstallPathGuard.CheckRecordedPath(installPath, entry.Path, out var target) is null
                    && File.Exists(target)
                    && new FileFingerprint(original.Path, original.Size, original.Sha256).Matches(target))
                {
                    var aside = Path.Combine(folder, "undo-displaced", entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
                    File.Move(target, aside, overwrite: false);
                }
            }
        }

        // 2. The mod's files and folders.
        foreach (var entry in log.Entries.Where(e => e.Outcome == RemovalOutcome.Moved))
        {
            var held = Path.Combine(folder, RemovedMods.FilesFolder, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(installPath, entry.Path.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(held) && !Directory.Exists(held)) { blocked.Add(entry.Path); continue; }

            // A held file may go back into a new-file-only area (R17): it only left it because its
            // fingerprint proved it this app's, and it only goes back to a free path.
            var check = File.Exists(held)
                ? InstallPathGuard.CheckPlacedPath(installPath, entry.Path, out _)
                : InstallPathGuard.CheckRecordedPath(installPath, entry.Path, out _);

            //
            // A held folder whose place has since been taken by a folder of the same name - the mod
            // put back first, then a folder of leftovers removed after it - is merged file by file,
            // each file only into a free path. Anything occupied stays held and is reported.
            //
            if (check is null && Directory.Exists(held) && Directory.Exists(target) && !InstallPathGuard.IsLink(target))
            {
                var merged = MergeHeldFolder(installPath, held, entry.Path, blocked);
                if (merged > 0) back++;
                continue;
            }

            if (check is not null || File.Exists(target) || Directory.Exists(target))
            {
                blocked.Add(entry.Path);
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Directory.Exists(held)) Directory.Move(held, target);
                else File.Move(held, target, overwrite: false);
                back++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Undo", $"couldn't put back {entry.Path}: {ex.Message}");
                blocked.Add(entry.Path);
            }
        }

        // 3. The copies of files it had replaced, back into Data where the record expects them.
        var originals = Path.Combine(folder, RemovedMods.OriginalsFolder);
        if (Directory.Exists(originals))
        {
            foreach (var file in Directory.EnumerateFiles(originals, "*", SearchOption.AllDirectories).ToList())
            {
                var dataRelative = Path.GetRelativePath(originals, file);
                var destination = Path.Combine(AppPaths.DataDirectory, dataRelative);
                if (File.Exists(destination)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(file, destination, overwrite: false);
            }
        }

        // 4. Configs the removal moved out to Data\LegacyConfigs.
        if (log.ConfigsKeptFolder is { } configsFolder && Directory.Exists(configsFolder))
        {
            foreach (var file in Directory.EnumerateFiles(configsFolder, "*", SearchOption.AllDirectories).ToList())
            {
                var relative = Path.GetRelativePath(configsFolder, file).Replace('\\', '/');

                if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var target) is not null || File.Exists(target))
                {
                    blocked.Add(relative);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: false);
            }

            DeleteEmptyConfigFolders(configsFolder);
        }

        // 5. The record.
        if (log.Record is { } record)
        {
            var manifest = manifestService.Load();
            manifest.Mods.RemoveAll(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon);
            manifest.Mods.Add(record);
            manifestService.Save(manifest);
        }

        log.Undone = true;
        SafeFile.WriteText(Path.Combine(folder, RemovedMods.LogName), System.Text.Json.JsonSerializer.Serialize(log));

        AppLog.Info("Undo", $"{log.ModName}: put back {back} item(s){(blocked.Count > 0 ? $"; {blocked.Count} path(s) were occupied and left in {folder}" : "")}");

        if (blocked.Count == 0) RemovedMods.DeleteHeld(installPath, folder);

        return new UndoResult(true, back, blocked);
    }

    // Moves each file of a held folder back to its place under an existing folder, only where that
    // place is free and passes the guard. Returns how many came back; the rest go in blocked.
    private static int MergeHeldFolder(string installPath, string held, string relativeFolder, List<string> blocked)
    {
        var moved = 0;
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

        foreach (var file in Directory.EnumerateFiles(held, "*", walk).ToList())
        {
            var relative = relativeFolder + "/" + Path.GetRelativePath(held, file).Replace('\\', '/');

            if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var target) is not null
                || File.Exists(target) || Directory.Exists(target))
            {
                blocked.Add(relative);
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: false);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Undo", $"couldn't put back {relative}: {ex.Message}");
                blocked.Add(relative);
            }
        }

        return moved;
    }

    //
    // Each of the record's mod folders still on disk after its removal, with how many files in it the
    // record doesn't list. Files the record does list and that stayed (changed, another mod's) are
    // reported on their own, so a folder left only for those isn't counted here. Read-only; links are
    // not followed.
    //
    private static List<FolderLeft> FoldersLeftBehind(string installPath, InstalledModRecord record)
    {
        var recorded = record.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = new List<FolderLeft>();
        var walk = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };

        foreach (var folder in record.Files.Select(InstallPathGuard.ModFolderOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (InstallPathGuard.CheckRecordedPath(installPath, folder, out var full) is not null || !Directory.Exists(full))
                continue;

            try
            {
                var foreign = Directory.EnumerateFiles(full, "*", walk)
                    .Count(f => !recorded.Contains(Path.GetRelativePath(installPath, f).Replace('\\', '/')));

                if (foreign > 0) left.Add(new FolderLeft(folder, foreign));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only a report - a folder that can't be read is simply not mentioned.
            }
        }

        return left;
    }

    //
    // Removes the folders an Undo emptied under Data\LegacyConfigs - the timestamped folder and the
    // install-shaped path inside it. Empty folders only, never a link, never outside LegacyConfigs.
    //
    private static void DeleteEmptyConfigFolders(string configsFolder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppPaths.LegacyConfigsDirectory)) + Path.DirectorySeparatorChar;
        var top = Path.GetFullPath(configsFolder);
        if (!top.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(top)) return;

        try
        {
            var folders = Directory.EnumerateDirectories(top, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                })
                .Append(top)
                .OrderByDescending(d => d.Length)
                .ToList();

            foreach (var dir in folders)
            {
                if (InstallPathGuard.IsLink(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) continue;
                Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tidying only: an empty folder that won't go is left.
        }
    }

    // The config files a hand-installed mod keeps in its own folder, as install-relative paths.
    // Read off disk rather than from a record, since this path has none.
    public static List<string> FindLegacyConfigs(string installPath, IEnumerable<string> modFolderPaths) =>
        modFolderPaths.SelectMany(folder => ModConfigFiles.InFolder(installPath, folder)).Distinct().ToList();

    // Moves a hand-installed mod's config files out of the install before its folder is deleted.
    public static KeptConfigs KeepLegacyConfigs(string installPath, IEnumerable<string> relativeFiles, string modName) =>
        ModConfigFiles.MoveOut(installPath, relativeFiles, modName, DateTimeOffset.UtcNow);

    // Creates a per-install scratch folder for the download and extraction. Prefers a
    // hidden folder inside <paramref name="installPath"/> so extracted files can be moved into
    // place; falls back to %TEMP% when that folder can't be created. <paramref name="canMove"/> is
    // true when the scratch folder ended up on the same volume as the install.
    private static string CreateWorkDirectory(string installPath, out bool canMove)
    {
        var id = Guid.NewGuid().ToString("N");
        var localRoot = Path.Combine(installPath, WorkFolderName);

        try
        {
            var directory = Path.Combine(localRoot, id);
            Directory.CreateDirectory(directory);
            TryHide(localRoot);
            CleanStaleWorkDirectories(localRoot, directory);
            canMove = true;
            return directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var fallback = Path.Combine(Path.GetTempPath(), SelfMod.ShortName, id);
            Directory.CreateDirectory(fallback);
            canMove = string.Equals(
                Path.GetPathRoot(Path.GetFullPath(fallback)),
                Path.GetPathRoot(Path.GetFullPath(installPath)),
                StringComparison.OrdinalIgnoreCase);
            return fallback;
        }
    }

    // Removes work folders left behind by a previous run that crashed or was killed.
    private static void CleanStaleWorkDirectories(string root, string current)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-6);
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(directory, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.GetCreationTimeUtc(directory) > cutoff) continue;
                TryDeleteDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private static void TryHide(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            if (!info.Attributes.HasFlag(FileAttributes.Hidden))
                info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Cosmetic only.
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Fork: extracts an archive (zip, 7z, rar, tar...) into a folder, as an install would -
    /// for looking at what is in it first (Install from file).</summary>
    public static Task ExtractAsync(string archivePath, string extractDir, CancellationToken ct = default) =>
        ExtractArchiveAsync(archivePath, extractDir, null, ct);

    // Extracts every file entry in the archive at <paramref name="archivePath"/> into
    // <paramref name="extractDir"/>. Zip archives go through System.IO.Compression; every other
    // format goes through SharpCompress's forward-only reader. Zip-slip protection: any entry whose
    // resolved destination would land outside extractDir is rejected before anything is written.
    internal static async Task ExtractArchiveAsync(
        string archivePath,
        string extractDir,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct)
    {
        Directory.CreateDirectory(extractDir);
        var extractRoot = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        if (ArchiveLayout.IsZipArchive(archivePath))
        {
            await ExtractZipAsync(archivePath, extractDir, extractRoot, status, ct).ConfigureAwait(false);
            return;
        }

        ExtractWithSharpCompress(archivePath, extractDir, extractRoot, status, ct);
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct)
    {
        await using var file = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var extracted = 0;
        var reportClock = Stopwatch.StartNew();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var destination = ResolveEntryDestination(entry.FullName, extractDir, extractRoot);

            await using var source = entry.Open();
            await using var target = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
            await source.CopyToAsync(target, CopyBufferSize, ct).ConfigureAwait(false);

            extracted++;
            if (reportClock.Elapsed >= ProgressInterval)
            {
                status?.Report(new ModInstallProgress(
                    ModInstallStage.Extracting, Done: extracted, Total: entries.Count));
                reportClock.Restart();
            }
        }
    }

    //
    // Three paths, by what the archive is (D24):
    //   - solid archives and 7z: the forward-only reader, so a solid archive decompresses its blocks
    //     once here, instead of once per entry;
    //   - other archives (a non-solid RAR, a plain tar): the random-access entries - the reader throws
    //     for these, which failed every non-solid RAR install before v1.19.0;
    //   - a compressed tar (.tar.gz, .tar.bz2...), which SharpCompress can't open as an archive: the
    //     stream reader.
    //
    private static void ExtractWithSharpCompress(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct)
    {
        IArchive archive;
        try
        {
            archive = ArchiveFactory.OpenArchive(archivePath);
        }
        catch (ArchiveOperationException)
        {
            ExtractWithStreamReader(archivePath, extractDir, extractRoot, status, ct);
            return;
        }

        using (archive)
        {
            var total = TryCountEntries(archive);
            var progress = new ExtractProgress(status, total);

            if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
            {
                using var reader = archive.ExtractAllEntries();
                ExtractFromReader(reader, extractDir, extractRoot, progress, ct);
                return;
            }

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.IsDirectory) continue;
                if (entry.Key is not { Length: > 0 } key) continue;

                var destination = ResolveEntryDestination(key, extractDir, extractRoot);
                entry.WriteToFile(destination, new ExtractionOptions { Overwrite = true });
                progress.Step();
            }
        }
    }

    private static void ExtractWithStreamReader(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct)
    {
        using var file = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(file);
        ExtractFromReader(reader, extractDir, extractRoot, new ExtractProgress(status, 0), ct);
    }

    private static void ExtractFromReader(
        IReader reader, string extractDir, string extractRoot, ExtractProgress progress, CancellationToken ct)
    {
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.Entry.IsDirectory) continue;
            if (reader.Entry.Key is not { Length: > 0 } key) continue;

            var destination = ResolveEntryDestination(key, extractDir, extractRoot);
            reader.WriteEntryToFile(destination, new ExtractionOptions { Overwrite = true });
            progress.Step();
        }
    }

    // Reports extraction progress at most once per ProgressInterval.
    private sealed class ExtractProgress(IProgress<ModInstallProgress>? status, int total)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _done;

        public void Step()
        {
            _done++;
            if (_clock.Elapsed < ProgressInterval) return;

            status?.Report(new ModInstallProgress(ModInstallStage.Extracting, Done: _done, Total: total));
            _clock.Restart();
        }
    }

    private static int TryCountEntries(IArchive archive)
    {
        try { return archive.Entries.Count(e => !e.IsDirectory); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return 0;
        }
    }

    //
    // Fork (SSPTMM): creates the archive's empty folders where the install put its files - mapped the
    // same way its files are - but only inside a mod's own folder or a server prepatch folder, never a
    // container itself or anywhere protected. They are not recorded (a record lists files); a removal
    // tidies them away if they are still empty (TidyEmptyModFolders), and leaves them if the mod's
    // tool has since put something in them.
    //
    private static void CreateEmptyFolders(
        string archivePath, string extractDir, string contentRoot, bool bareBepInEx, string serverRoot,
        string installPath, InstallTarget target, ModVersion version)
    {
        var prefix = Path.GetRelativePath(extractDir, contentRoot).Replace('\\', '/');
        if (prefix == ".") prefix = "";

        var created = 0;
        foreach (var folder in EmptyFoldersIn(archivePath))
        {
            var relative = folder;
            if (prefix.Length > 0)
            {
                if (!folder.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
                relative = folder[(prefix.Length + 1)..];
            }

            relative = relative.Replace('/', Path.DirectorySeparatorChar);
            if (bareBepInEx)
            {
                if (ArchiveLayout.MapBareBepInEx(relative) is not { } mapped) continue;
                relative = mapped;
            }

            var forward = ArchiveLayout.RemapForServerRoot(relative, serverRoot).Replace('\\', '/');

            if (InstallPathGuard.ModFolderOf(forward) is null && InstallPathGuard.PrepatchFolderOf(forward + "/x") is null) continue;
            if (InstallPathGuard.CheckPlacedPath(installPath, forward, out var full) is not null) continue;

            try
            {
                if (Directory.Exists(full)) continue;
                Directory.CreateDirectory(full);
                created++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Install", $"{target.Name} {version.Version}: couldn't create the archive's empty folder {forward}: {ex.Message}");
            }
        }

        if (created > 0) AppLog.Info("Install", $"{target.Name} {version.Version}: created {created} empty folder(s) the archive has");
    }

    //
    // Fork (SSPTMM): the folders an archive holds with nothing in them - SVM's Presets\, which its
    // configuration app refuses to run without. Read from the archive's own folder entries, since
    // neither extractor writes them. Forward slashes, no trailing one. Empty when it can't be read
    // that way (a compressed tar): the install goes ahead as it always did.
    //
    internal static List<string> EmptyFoldersIn(string archivePath)
    {
        var folders = new List<string>();
        var files = new List<string>();

        try
        {
            if (ArchiveLayout.IsZipArchive(archivePath))
            {
                using var zip = ZipFile.OpenRead(archivePath);
                foreach (var entry in zip.Entries)
                    (string.IsNullOrEmpty(entry.Name) ? folders : files).Add(entry.FullName);
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.Key is not { Length: > 0 } key) continue;
                    (entry.IsDirectory ? folders : files).Add(key);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArchiveOperationException or NotSupportedException or InvalidOperationException)
        {
            return [];
        }

        static string Clean(string key) => key.Replace('\\', '/').Trim('/');

        var cleanFiles = files.Select(Clean).ToList();
        var cleanFolders = folders.Select(Clean).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Empty: nothing in the archive below it - no file, and no other folder.
        return [.. cleanFolders.Where(f =>
            !cleanFiles.Any(x => x.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase))
            && !cleanFolders.Any(x => x.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase)))];
    }

    // Resolves an archive entry's key to an absolute destination under
    // <paramref name="extractDir"/>, rejecting anything that would escape it, and creates the
    // containing directory.
    private static string ResolveEntryDestination(string entryKey, string extractDir, string extractRoot)
    {
        var destination = Path.GetFullPath(Path.Combine(extractDir, entryKey));
        if (!destination.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModInstallException(ModInstallFailure.UnsafeArchiveEntry) { ArchiveEntry = entryKey };
        }

        var destinationDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

        return destination;
    }
}

//
// What an install placed, and what it did to the mod's own config files.
//
// Configs is null when the mod has none - most client-only mods, and anything whose settings live in
// BepInEx\config rather than inside its own folder.
//
//
// SkippedProtected lists the archive's files that were not placed because the install's own copy is
// SPT's, BepInEx's or the game's (D3-D5).
//
public sealed record ModInstallResult(
    InstalledModRecord Record,
    ConfigUpdateReport? Configs,
    IReadOnlyList<string>? SkippedProtected = null)
{
    // Files no record owned that this install replaced, kept in Data to put back on removal (D22).
    // Only the ones this install kept - originals an earlier version kept aren't counted again.
    public int OriginalsKept { get; init; }

    // Fork: BepInEx\config files the archive ships that were already there, left as they are - the
    // user's plugin settings (see ConfigCarryOver.Prepare).
    public IReadOnlyList<string> KeptSettings { get; init; } = [];

    // Fork: archive files that are not on disk as the archive has them once the install finished -
    // see InstallVerification. Empty when everything landed.
    public IReadOnlyList<InstallMismatch> NotAsInArchive { get; init; } = [];
}

// Result of ModInstallService.UninstallAsync. FailedFiles lists files that couldn't be
// deleted; the mod is still removed from the manifest regardless. ConfigsKept/ConfigsFolder
// describe the mod's own config files when they were moved out rather than deleted.
// RefusedFiles lists recorded paths that failed InstallPathGuard's checks and were left untouched.
//
// Since v1.19.0 FilesDeleted counts files MOVED into the holding folder - nothing is deleted outright.
// KeptChanged: left because they changed since install (D21). KeptOwned: left because another mod's
// record lists them (D9). OriginalsRestored: files this mod had replaced, put back (D22).
// HoldingFolder: where this removal is held, or null when the setting deleted it straight away.
public sealed record UninstallResult(
    int FilesDeleted,
    List<string> FailedFiles,
    int ConfigsKept = 0,
    string? ConfigsFolder = null,
    List<string>? RefusedFiles = null)
{
    public List<string> KeptChanged { get; init; } = [];
    public List<string> KeptOwned { get; init; } = [];
    public int OriginalsRestored { get; init; }
    public string? HoldingFolder { get; init; }

    // The mod's folders that stayed because they hold files it didn't install.
    public List<FolderLeft> FoldersLeft { get; init; } = [];
}

// A mod folder a removal left in place, install-relative, and how many files in it the mod didn't install.
public sealed record FolderLeft(string Folder, int Files);

// What Undo did: whether it ran at all, how many files and folders came back, and the paths it left in
// the holding folder because something else now occupies them.
public sealed record UndoResult(bool Ran, int PutBack, List<string> Blocked);

// What to do with a mod's own config and user-data files when its files are being removed.
public enum ConfigAction
{
    // Move them into AppPaths.LegacyConfigsDirectory instead of deleting them. A real removal.
    Keep,

    // Delete them along with the rest of the mod's files.
    Delete,

    //
    // Delete the configs but leave the user's own documents where they are - the update path, which
    // has already copied the configs aside and is about to place new ones over them.
    //
    // Update and removal want opposite things here, which is why this is its own member rather than a
    // flag: on an update a preset folder stays exactly as it is, and on a removal it is rescued.
    //
    Preserve,
}
