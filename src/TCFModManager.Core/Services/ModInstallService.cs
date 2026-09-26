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
    string? replacedFilesRoot = null,
    ProfileBackups? profileBackups = null)
{
    // Copies of files installs have put their own over - see ReplacedFileStore.
    private readonly ReplacedFileStore _replaced = new(replacedFilesRoot ?? Path.Combine(AppPaths.DataDirectory, "ReplacedFiles"));

    private readonly ConfigCarryOver _configs = configCarryOver ?? new ConfigCarryOver();
    private readonly ModConfigOptionsStore _options = configOptions ?? new ModConfigOptionsStore();
    private readonly ConfigUpdateLog _configLog = configUpdateLog ?? new ConfigUpdateLog();

    //
    // One change to the install at a time: an install's placing-and-recording and a removal each read
    // the manifest, change files and write the manifest back, and two at once would each write over
    // the other's record. The download is outside it - only the part that touches the install waits.
    //
    private readonly SemaphoreSlim _changing = new(1, 1);

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
                found = Process.GetProcessesByName(name);
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
    // <paramref name="downloadedArchive"/>: the version's archive, already downloaded (the queue
    // downloads ahead of installing, and keeps archives - see ModArchiveCache). Read where it is,
    // not moved or deleted; nothing is downloaded then.
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

        // An earlier install left half-done in this folder is put back before anything else changes
        // it - otherwise putting it back later would undo this one too. (Again once this install has
        // its turn, below; this one is so a download is not spent on an install that cannot go ahead.)
        await _changing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RecoverOrRefuse(installPath);
        }
        finally
        {
            _changing.Release();
        }

        AppLog.Info("Install",
            $"{target.Name} {version.Version} ({(target.IsAddon ? "addon" : "mod")} {target.Id}) -> {installPath}");

        var workDir = CreateWorkDirectory(installPath, out var canMoveIntoInstall);
        AppLog.Debug("Install", $"work dir {workDir} (move into install: {canMoveIntoInstall})");
        var archivePath = downloadedArchive ?? Path.Combine(workDir, "download.bin");
        var extractDir = Path.Combine(workDir, "extracted");
        var holding = false;

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
                //
                // Off the caller's thread for the rest, as the download's ConfigureAwait(false) put
                // it before archives were downloaded ahead: the queue calls this from the UI thread,
                // and extracting a .7z or .rar (synchronous, in SharpCompress), placing the files
                // and saving the records would otherwise all run there - the window frozen for the
                // whole install.
                //
                await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            }

            ct.ThrowIfCancellationRequested();

            var archiveBytes = new FileInfo(archivePath).Length;
            AppLog.Debug("Install", $"downloaded {archiveBytes:N0} bytes, zip={IsZipArchive(archivePath)}");

            status?.Report(new ModInstallProgress(ModInstallStage.Extracting));
            // Auto-detects archive format from the file header rather than assuming zip.
            var extractTimer = System.Diagnostics.Stopwatch.StartNew();
            await ExtractArchiveAsync(archivePath, extractDir, status, ct).ConfigureAwait(false);
            AppLog.Debug("Install", $"extracted in {extractTimer.ElapsedMilliseconds}ms");

            ct.ThrowIfCancellationRequested();

            // What the archive holds, and where each file goes - its wrapper folders looked through,
            // read-me files left out, and loose plugin DLLs given their BepInEx folder.
            var layout = ArchiveLayout.Read(extractDir);
            if (layout is null)
            {
                AppLog.Warn("Install",
                    $"{target.Name} {version.Version} archive has no known root folder; top level: " +
                    string.Join(", ", Directory.GetFileSystemEntries(FindContentRoot(extractDir)).Select(Path.GetFileName)));

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

            ct.ThrowIfCancellationRequested();

            // This install's turn to change the install - see _changing.
            await _changing.WaitAsync(ct).ConfigureAwait(false);
            holding = true;
            RecoverOrRefuse(installPath);

            // Re-checked now the download is finished: SPT may have been started while it ran, and
            // everything past this point deletes or places files inside the install.
            EnsureInstallNotInUse(ModInstallAction.Install, installPath);

            // A copy of the SPT profiles from before the change, when they changed since the last one
            // (off the caller's thread by now, and only once SPT is known not to be running).
            profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeInstall);

            var manifest = manifestService.Load();
            var existing = manifest.Mods.FirstOrDefault(target.Matches);

            //
            // Where each source file is going, worked out before anything is removed: the config
            // files the archive is about to place over have to be known while they are still there.
            //
            var placements = layout
                .Select(entry =>
                {
                    var installRelative = RemapForServerRoot(entry.Relative, serverRoot);
                    // Forward-slash regardless of OS, matching InstalledModRecord.Files's documented format.
                    return (File: entry.Source, Relative: installRelative, Forward: installRelative.Replace('\\', '/'));
                })
                .ToList();
            var sourceFiles = placements.Select(p => p.File).ToArray();

            // Copying (not moving) into the install needs the room for it.
            if (!canMoveIntoInstall) EnsureFreeSpace(installPath, sourceFiles, target, version);

            var timestamp = DateTimeOffset.UtcNow;

            var pending = _configs.Prepare(
                installPath, existing, placements.Select(p => p.Forward), target.Name, timestamp);

            //
            // The files the archive does not get to place, whatever else happens: a config that could
            // not be copied aside, one of the user's own documents that is already there, and a
            // BepInEx config already in the install. That last one holds the user's settings for the
            // plugin (BepInEx writes it on first run and keeps it); a copy shipped in the archive is
            // only its defaults, which BepInEx adds back by itself for any setting that is missing.
            //
            var kept = new HashSet<string>(pending.Untouchable, StringComparer.OrdinalIgnoreCase);
            kept.UnionWith(pending.Preserved);
            foreach (var placement in placements)
            {
                if (ModConfigFiles.IsBepInExConfig(placement.Forward) && File.Exists(Path.Combine(installPath, placement.Relative)))
                    kept.Add(placement.Forward);
            }

            // What this install writes; the rest of the archive's paths are "kept" (see above).
            var planned = placements.Where(p => !kept.Contains(p.Forward)).ToList();
            var isOwnLeftover = OwnLeftoverTest(installPath, manifest, target, planned.Select(p => p.Forward));
            var placedFiles = new List<string>(sourceFiles.Length);
            var replaced = new List<string>();
            InstalledModRecord? record = null;
            ConfigUpdateReport? report = null;

            //
            // The undo record, before anything in the install changes - see InstallJournal. From here
            // until the record is written, any failure at all - or the app stopping - puts the install
            // back as it was, rather than leaving the previous version gone and the new one half there.
            //
            var journal = InstallJournal.Begin(workDir, installPath, target);
            journal.Planned = [.. planned.Select(p => p.Forward)];
            journal.PreviousReplaced = existing is null ? [] : [.. existing.Replaced];
            journal.PreviousInstalledAt = existing?.InstalledAt;
            journal.StaleCopies = StaleCopiesOf(manifest, existing);
            journal.Save();

            try
            {
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
                    // Moved into the work folder rather than deleted, so they can come back.
                    //
                    RemoveRecordedFiles(
                        installPath,
                        existing,
                        ConfigAction.Preserve,
                        pending.Protected.ToHashSet(StringComparer.OrdinalIgnoreCase),
                        CancellationToken.None,
                        stashDirectory: journal.PreviousDirectory);
                }

                //
                // Every file still in the way of a planned one - the mod's own leftovers, another mod's,
                // or one placed by hand - is copied into the work folder, and every planned path with
                // nothing there is written down as such. Only those are ever deleted when undoing:
                // whatever else is found there then is put back from a copy, or left alone.
                //
                foreach (var placement in planned)
                {
                    var destination = Path.Combine(installPath, placement.Relative);
                    if (File.Exists(destination))
                    {
                        var copy = journal.BeforeCopyOf(placement.Forward);
                        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                        File.Copy(destination, copy, overwrite: true);

                        // Someone else's (or placed by hand): kept for good, to put back when this
                        // mod is removed - once the install is written, below.
                        if (!isOwnLeftover(placement.Forward)) replaced.Add(placement.Forward);
                    }
                    else if (!File.Exists(Path.Combine(journal.PreviousDirectory, placement.Relative)))
                    {
                        // Nothing there, and not the previous version's either (that one is in
                        // "previous", and comes back from there).
                        journal.Absent.Add(placement.Forward);
                    }
                }

                journal.Save();

                status?.Report(new ModInstallProgress(
                    ModInstallStage.Installing, Total: sourceFiles.Length));
                var reportClock = Stopwatch.StartNew();

                for (var i = 0; i < placements.Count; i++)
                {
                    var (file, installRelative, installRelativeForward) = placements[i];

                    // Not placed, but still recorded as this install's files so a later removal knows
                    // about them - see "kept" above.
                    if (kept.Contains(installRelativeForward))
                    {
                        placedFiles.Add(installRelativeForward);
                        continue;
                    }

                    var destination = Path.Combine(installPath, installRelative);
                    var destinationDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

                    if (canMoveIntoInstall) File.Move(file, destination, overwrite: true);
                    else File.Copy(file, destination, overwrite: true);

                    placedFiles.Add(installRelativeForward);

                    if (reportClock.Elapsed >= ProgressInterval)
                    {
                        status?.Report(new ModInstallProgress(
                            ModInstallStage.Installing, Done: i + 1, Total: sourceFiles.Length));
                        reportClock.Restart();
                    }
                }

                // Files the previous version had replaced that this one places again: still this
                // mod's to put back, from the copy already kept.
                if (existing is not null)
                {
                    foreach (var old in existing.Replaced)
                    {
                        if (!replaced.Contains(old, StringComparer.OrdinalIgnoreCase)
                            && planned.Any(p => string.Equals(p.Forward, old, StringComparison.OrdinalIgnoreCase)))
                            replaced.Add(old);
                    }
                }

                record = BuildRecord(target, version, placedFiles, incomplete: false, replaced);

                // The configs, merged into what was just placed. Part of the install: a failure here,
                // or the app stopping, puts the previous version back as well.
                report = _configs.Settle(pending, installPath, target, existing, record, timestamp);

                // Written last: once the record says this version, the install is done.
                journal.Record = record;
                journal.Save();
                SaveRecord(record);

                journal.Committed = true;
                journal.Save();
            }
            catch (Exception ex) when (record is null || !IsRecorded(record))
            {
                AppLog.Error("Install",
                    $"{target.Name} {version.Version} failed after {placedFiles.Count}/{sourceFiles.Length} file(s); putting the install back", ex);

                // Everything back as it was: the previous version, and whatever it had replaced.
                var notUndone = journal.Undo();

                if (notUndone.Count == 0)
                {
                    journal.Delete();
                    throw new ModInstallException(ModInstallFailure.RolledBack, ex)
                    {
                        ModName = target.Name,
                        Version = version.Version,
                    };
                }

                //
                // Some of it would not go back (held open). The journal and the work folder - the
                // previous version's files and the copies - stay, and the undo is finished before
                // anything else is installed or removed (RecoverOrRefuse), or at the next start. The
                // manifest still holds the previous record, which is what that undo puts back.
                //
                AppLog.Error("Install", $"{target.Name}: {notUndone.Count} file(s) could not be put back yet: {string.Join(", ", notUndone.Take(10))}");

                throw new ModInstallException(ModInstallFailure.NotPutBackYet, ex)
                {
                    ModName = target.Name,
                    Version = version.Version,
                };
            }

            //
            // Bookkeeping for later removals; the install itself is already complete and recorded.
            // The journal goes only once that is done: if it could not be, the work folder (with its
            // copies) stays and it is tried again at the next start - see RecoverInterruptedInstalls.
            //
            if (KeepReplacedCopies(installPath, journal, target, replaced)) journal.Delete();

            AppLog.Info("Install",
                $"{target.Name} {version.Version} placed {placedFiles.Count} file(s) in folders [{string.Join(", ", record.Folders)}]" +
                (replaced.Count > 0 ? $"; will put back {replaced.Count} file(s) it replaced when removed" : ""));

            if (report.Files.Count > 0)
            {
                _configLog.Add(report);
                AppLog.Info("Configs",
                    $"{target.Name} {record.Version}: " +
                    string.Join(", ", report.Files.Select(f => $"{f.Path} {f.Kind}{(f.Reason is { } r ? $" ({r})" : "")}")));
            }

            status?.Report(new ModInstallProgress(ModInstallStage.Done));
            return new ModInstallResult(record, report.Files.Count > 0 ? report : null);
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
            //
            // A journal still in the work folder here means something is left to finish: undoing a
            // failed install, or keeping the copies of what a finished one replaced. The folder holds
            // what that needs, so it stays for RecoverInterruptedInstalls rather than being deleted.
            //
            if (File.Exists(Path.Combine(workDir, InstallJournal.FileName)))
                AppLog.Error("Install", $"{target.Name}: kept {workDir} to finish at the next start");
            else
                TryDeleteDirectory(workDir);

            if (holding) _changing.Release();
        }
    }

    // True when the manifest holds exactly this record - the install finished writing it.
    private bool IsRecorded(InstalledModRecord record) => RecordedState(record) == true;

    // As IsRecorded; null when the manifest cannot be read, so it cannot be told.
    private bool? RecordedState(InstalledModRecord record)
    {
        try
        {
            // By when it was installed, not by version: confirming or choosing a version by hand
            // changes those on the same record (SetManualVersion keeps InstalledAt).
            return manifestService.Load().Mods.Any(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon
                && m.InstalledAt == record.InstalledAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    //
    // Whether a file already at a planned path is this mod's own leftover - an earlier copy of it put
    // there by hand - rather than something to keep a copy of and put back when the mod is removed.
    //
    // Only in a mod's own folder (BepInEx/plugins/<x>, user/mods/<x>), only when no other installed
    // mod's record lists it, and only when every DLL (and package.json) already in that folder is
    // one this install places again: the same mod put there by hand. A patch writing into ANOTHER
    // mod's folder leaves that mod's DLL alone, so the folder is not its own and what it replaced
    // there is kept - removing the patch puts it back.
    //
    private static Func<string, bool> OwnLeftoverTest(
        string installPath, ModInstallManifest manifest, InstallTarget target, IEnumerable<string> planned)
    {
        var othersFiles = manifest.Mods
            .Where(m => !target.Matches(m))
            .SelectMany(m => m.Files)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var placing = planned.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        return forward =>
        {
            if (othersFiles.Contains(forward) || OwnFolderOf(forward) is not { } folder) return false;
            if (folders.TryGetValue(folder, out var own)) return own;

            var full = Path.Combine(installPath, folder.Replace('/', Path.DirectorySeparatorChar));
            var main = Directory.Exists(full)
                ? Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                    .Where(f => string.Equals(Path.GetExtension(f), ".dll", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileName(f), "package.json", StringComparison.OrdinalIgnoreCase))
                    .Select(f => folder + "/" + Path.GetRelativePath(full, f).Replace('\\', '/'))
                    .ToList()
                : [];

            own = main.Count > 0 && main.All(m => placing.Contains(m) && !othersFiles.Contains(m));
            folders[folder] = own;
            return own;
        };
    }

    //
    // After an install is written: the copies kept for putting back on removal (ReplacedFileStore).
    //
    // - Files this install replaced that were someone else's: their copy from the work folder is kept.
    // - Files the previous version had replaced that this one no longer places: put back now.
    //
    // Another mod that had kept a copy of a file this install has now placed over keeps it: that copy
    // is what was under THAT mod, and removing it hands the copy up to this one (see
    // RemoveRecordedFiles), so the file that was there first is never lost.
    //
    // Safe to run again (at the next start, when the app stopped before it was done). False when a
    // step failed: the work folder then stays, with its copies, to try again.
    //
    private bool KeepReplacedCopies(string installPath, InstallJournal journal, InstallTarget target, IReadOnlyList<string> replaced)
    {
        try
        {
            //
            // A mod installed over the previous version kept a copy of its file. That version is gone
            // (this one places the file on top again, or no longer ships it), so what is under that
            // mod now is what was under the previous version: this mod's own kept copy goes to it
            // (or, with none, its copy goes).
            // Before the copies below, since the first copy of a path is the one kept. Each one is
            // crossed off the journal as it is done, so a second attempt does not hand anything twice.
            //
            while (journal.StaleCopies.Count > 0)
            {
                var stale = journal.StaleCopies[0];
                var handed = _replaced.HandOver(target.Id, target.IsAddon, stale.ModId, stale.IsAddon, stale.Path);
                if (!handed) _replaced.Drop(stale.ModId, stale.IsAddon, stale.Path);
                UpdateReplaced(stale.ModId, stale.IsAddon, stale.Path, keep: handed);

                journal.StaleCopies.RemoveAt(0);
                journal.Save();
            }

            foreach (var relative in replaced)
            {
                var copy = journal.BeforeCopyOf(relative);
                if (File.Exists(copy)) _replaced.KeepCopy(copy, target.Id, target.IsAddon, relative);
            }

            //
            // What the previous version had replaced and this one no longer places: back where it
            // was - unless a mod installed over the previous version holds that file now, in which
            // case what was under the previous version is now under that mod, and the copy is its.
            //
            var gone = journal.PreviousReplaced.Where(o => !replaced.Contains(o, StringComparer.OrdinalIgnoreCase)).ToList();
            if (gone.Count > 0)
            {
                var others = manifestService.Load().Mods.Where(m => !target.Matches(m)).ToList();
                foreach (var old in gone)
                {
                    var above = others
                        .Where(m => journal.PreviousInstalledAt is { } then && m.InstalledAt > then
                            && m.Files.Contains(old, StringComparer.OrdinalIgnoreCase))
                        .OrderBy(m => m.InstalledAt)
                        .FirstOrDefault();

                    if (above is null) _replaced.Restore(installPath, target.Id, target.IsAddon, old);
                    else if (_replaced.HandOver(target.Id, target.IsAddon, above.ModId, above.IsAddon, old))
                        UpdateReplaced(above.ModId, above.IsAddon, old, keep: true);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Install", $"{target.Name}: couldn't keep every copy of the files it replaced yet: {ex.Message}");
            return false;
        }
    }

    // Adds a path to (keep) or takes it off another record's Replaced list, in the manifest.
    private void UpdateReplaced(int modId, bool isAddon, string relative, bool keep)
    {
        var manifest = manifestService.Load();
        var i = manifest.Mods.FindIndex(m => m.ModId == modId && m.IsAddon == isAddon);
        if (i < 0) return;

        var other = manifest.Mods[i];
        var has = other.Replaced.Contains(relative, StringComparer.OrdinalIgnoreCase);
        if (has == keep) return;

        manifest.Mods[i] = WithReplaced(other, keep
            ? [.. other.Replaced, relative]
            : [.. other.Replaced.Where(r => !string.Equals(r, relative, StringComparison.OrdinalIgnoreCase))]);
        manifestService.Save(manifest);
    }

    //
    // A mod installed over this one's previous version kept a copy of that version's file, to put
    // back when it goes. That version is gone - replaced by this one, placed on top again, or no
    // longer shipped - so the copy is replaced by what was under the previous version, or let go
    // when nothing was (see KeepReplacedCopies).
    //
    private static List<StaleCopy> StaleCopiesOf(ModInstallManifest manifest, InstalledModRecord? existing)
    {
        if (existing is null) return [];

        //
        // Every file of the previous version - placed again or not. For each, only the mod directly
        // above it (the next one installed over it) holds a copy of the previous version's file; the
        // ones higher up hold copies of the mods under them, which are still there.
        //
        var copies = new List<StaleCopy>();
        foreach (var path in existing.Files)
        {
            var above = manifest.Mods
                .Where(m => !(m.ModId == existing.ModId && m.IsAddon == existing.IsAddon)
                    && m.InstalledAt > existing.InstalledAt
                    && m.Files.Contains(path, StringComparer.OrdinalIgnoreCase))
                .OrderBy(m => m.InstalledAt)
                .FirstOrDefault();

            if (above is not null && above.Replaced.Contains(path, StringComparer.OrdinalIgnoreCase))
                copies.Add(new StaleCopy(above.ModId, above.IsAddon, path));
        }

        return copies;
    }

    private static InstalledModRecord WithReplaced(InstalledModRecord record, List<string> replaced) => new()
    {
        ModId = record.ModId,
        IsAddon = record.IsAddon,
        Guid = record.Guid,
        Name = record.Name,
        VersionId = record.VersionId,
        Version = record.Version,
        InstalledAt = record.InstalledAt,
        Files = record.Files,
        Folders = record.Folders,
        Incomplete = record.Incomplete,
        IsAppManaged = record.IsAppManaged,
        Replaced = replaced,
    };

    //
    // The folder a file belongs to as part of one mod: "BepInEx/plugins/<mod>" or ".../user/mods/<mod>".
    // Null for a file directly in a shared folder (BepInEx/plugins itself, the install root) - those
    // are the files mods tread on each other with.
    //
    private static string? OwnFolderOf(string forward)
    {
        var segments = forward.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i + 2 < segments.Length; i++)
        {
            var isPlugins = string.Equals(segments[i], "BepInEx", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(segments[i + 1], "plugins", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(segments[i + 1], "patchers", StringComparison.OrdinalIgnoreCase));
            var isServer = string.Equals(segments[i], "user", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[i + 1], "mods", StringComparison.OrdinalIgnoreCase);

            // A folder below it, with the file (at least) inside.
            if ((isPlugins || isServer) && i + 3 < segments.Length)
                return string.Join('/', segments[..(i + 3)]);
        }

        return null;
    }

    private InstalledModRecord SaveRecord(InstallTarget target, ModVersion version, List<string> placedFiles, bool incomplete, List<string> replaced) =>
        SaveRecord(BuildRecord(target, version, placedFiles, incomplete, replaced));

    // Writes the record for what an install placed, replacing any previous record for the same mod.
    // The manifest is reloaded rather than reusing an earlier copy, since UninstallAsync may have
    // saved a removal of the old record in between.
    private InstalledModRecord SaveRecord(InstalledModRecord record)
    {
        var current = manifestService.Load();
        current.Mods.RemoveAll(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon);
        current.Mods.Add(record);
        manifestService.Save(current);

        return record;
    }

    private static InstalledModRecord BuildRecord(InstallTarget target, ModVersion version, List<string> placedFiles, bool incomplete, List<string> replaced)
    {
        return new InstalledModRecord
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
            Replaced = replaced,
        };
    }

    // Removes every file InstalledModRecord.Files lists, then deletes any directory left
    // empty (working bottom-up), then drops the record from the manifest. Files that can't be
    // deleted are collected into the result instead of aborting the rest of the removal.
    // <paramref name="configs"/> decides what happens to the mod's own config JSON files first.
    public async Task<UninstallResult> UninstallAsync(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs = ConfigAction.Keep,
        CancellationToken ct = default)
    {
        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        await _changing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // As for an install: anything left half-done is put back first.
            RecoverOrRefuse(installPath);

            // The record as it is now - an install may have updated it while this waited.
            var current = manifestService.Load().Mods.FirstOrDefault(m => m.ModId == record.ModId && m.IsAddon == record.IsAddon);
            if (current is null) return new UninstallResult(0, [], 0, null);

            profileBackups?.BackupIfChanged(installPath, ProfileBackups.BeforeRemove);

            var result = RemoveRecordedFiles(installPath, current, configs, null, ct);

            // A mod that is gone has no shipped copies worth keeping. An update does not come through
            // here, which is why this is safe to do unconditionally - see InstallAsync.
            _configs.Baselines.Remove(current.ModId, current.IsAddon);

            return result;
        }
        finally
        {
            _changing.Release();
        }
    }

    /// <summary>Finishes putting back any install left half-done in this folder; throws
    /// <see cref="ModInstallException"/> (EarlierInstallPending) while one cannot be. For changes to
    /// the install made outside this service - disabling, enabling, applying a list.</summary>
    public void EnsureNothingPending(string installPath)
    {
        _changing.Wait();
        try
        {
            RecoverOrRefuse(installPath);
        }
        finally
        {
            _changing.Release();
        }
    }

    //
    // The removal itself, shared by a real uninstall and the update path.
    //
    // <paramref name="keep"/> names files this must not touch whatever else it is told - the configs
    // ConfigCarryOver could not copy aside. Null on the removal path, which has nothing to protect.
    //
    // <paramref name="stashDirectory"/>: the update path. Files are moved there instead of deleted, so
    // a failed update can put them back (see InstallJournal), and the record is left for the install
    // to replace - nothing here is final until the new version is in place.
    //
    // Never removed, whoever asks:
    // - a file another installed mod's record also lists - it is that mod's too, and it stays;
    // - a BepInEx config, unless the configs are being deleted - it holds the user's settings for the
    //   plugin, which BepInEx itself keeps when a plugin goes, and a reinstall picks up again.
    // A file this mod replaced when it was installed is put back rather than deleted.
    //
    private UninstallResult RemoveRecordedFiles(
        string installPath,
        InstalledModRecord record,
        ConfigAction configs,
        IReadOnlySet<string>? keep,
        CancellationToken ct,
        string? stashDirectory = null)
    {
        var failed = new List<string>();
        var deleted = 0;
        var restored = 0;
        var shared = 0;
        var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removal = stashDirectory is null;

        var manifest = manifestService.Load();
        bool IsThis(InstalledModRecord m) => m.ModId == record.ModId && m.IsAddon == record.IsAddon;

        // Every file some other installed mod's record lists.
        var ownedElsewhere = manifest.Mods
            .Where(m => !IsThis(m))
            .SelectMany(m => m.Files)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Moved out before the delete loop runs, so the loop simply finds them gone.
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

        var replaced = record.Replaced.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The other records as this removal leaves them (a copy handed up changes theirs).
        var others = manifest.Mods.Where(m => !IsThis(m)).ToList();
        var changedOthers = false;

        // The mod installed over this one's file since, directly above it - null when this one's copy
        // is the one in place.
        int Above(string relative)
        {
            var best = -1;
            for (var i = 0; i < others.Count; i++)
            {
                if (others[i].InstalledAt <= record.InstalledAt
                    || !others[i].Files.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
                if (best < 0 || others[i].InstalledAt < others[best].InstalledAt) best = i;
            }

            return best;
        }

        foreach (var relative in record.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (keep is not null && keep.Contains(relative)) continue;
            if (preserve.Contains(relative)) continue;
            if (configs != ConfigAction.Delete && ModConfigFiles.IsBepInExConfig(relative)) continue;

            var fullPath = Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                //
                // Another mod was installed over this one's file since: the file is theirs now and
                // stays. What was under this mod is now what is under them, so this mod's copy of it
                // is handed up; with none, nothing was under it, and their copy of this mod's file is
                // not wanted back.
                //
                if (removal && Above(relative) is var above and >= 0)
                {
                    var upper = others[above];
                    var theirs = upper.Replaced.Contains(relative, StringComparer.OrdinalIgnoreCase);

                    if (replaced.Contains(relative) && _replaced.HandOver(record.ModId, record.IsAddon, upper.ModId, upper.IsAddon, relative))
                    {
                        if (!theirs) others[above] = WithReplaced(upper, [.. upper.Replaced, relative]);
                    }
                    else if (theirs)
                    {
                        _replaced.Drop(upper.ModId, upper.IsAddon, relative);
                        others[above] = WithReplaced(upper, [.. upper.Replaced.Where(r => !string.Equals(r, relative, StringComparison.OrdinalIgnoreCase))]);
                    }

                    changedOthers |= !ReferenceEquals(upper, others[above]);
                    shared++;
                    continue;
                }

                // What was there before this mod: back in its place.
                if (removal && replaced.Contains(relative) && _replaced.Restore(installPath, record.ModId, record.IsAddon, relative))
                {
                    restored++;
                    continue;
                }

                // Another mod's file too: it stays.
                if (ownedElsewhere.Contains(relative))
                {
                    shared++;
                    continue;
                }

                if (File.Exists(fullPath))
                {
                    if (stashDirectory is not null)
                    {
                        var stashed = Path.Combine(stashDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(stashed)!);
                        File.Move(fullPath, stashed, overwrite: true);
                    }
                    else
                    {
                        File.Delete(fullPath);
                    }

                    deleted++;
                }

                for (var dir = Path.GetDirectoryName(fullPath); IsUnderInstallPath(dir, installPath); dir = Path.GetDirectoryName(dir))
                    touchedDirectories.Add(dir!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(relative);
            }
        }

        foreach (var dir in touchedDirectories.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Directory that won't delete is left alone.
            }
        }

        if (shared > 0 || restored > 0)
            AppLog.Info("Install", $"{record.Name}: left {shared} file(s) other mods also use; put back {restored} file(s) it had replaced");

        if (removal)
        {
            // The copies of this mod's own that were never put back (their files already gone).
            _replaced.DropAll(record.ModId, record.IsAddon);

            manifest.Mods.Clear();
            manifest.Mods.AddRange(others);
            manifestService.Save(manifest);
            if (changedOthers) AppLog.Info("Install", $"{record.Name}: handed what was under its files to the mods installed over them");
        }

        return new UninstallResult(deleted, failed, kept.Count, kept.Folder);
    }

    //
    // Installs the app was stopped in the middle of - closed, killed, or the PC losing power - put back
    // as they were before: each leaves its journal in its work folder (see InstallJournal). One that
    // had placed every file is finished instead, by writing its record. Returns the names of the mods
    // put back or finished.
    //
    public IReadOnlyList<string> RecoverInterruptedInstalls(string installPath) =>
        Recover(installPath).Recovered;

    //
    // Recovery, then a refusal when some earlier install is still not put back: changing the install
    // now would be undone along with it when that is finally put back.
    //
    private void RecoverOrRefuse(string installPath)
    {
        var (_, pending, unreadable) = Recover(installPath);
        if (pending.Count == 0) return;

        throw new ModInstallException(unreadable ? ModInstallFailure.RecordsUnreadable : ModInstallFailure.EarlierInstallPending)
        {
            ModName = string.Join(", ", pending),
        };
    }

    private (List<string> Recovered, List<string> Pending, bool RecordsUnreadable) Recover(string installPath)
    {
        var recovered = new List<string>();
        var pending = new List<string>();
        var unreadable = false;
        if (string.IsNullOrWhiteSpace(installPath)) return (recovered, pending, unreadable);

        var roots = new[] { Path.Combine(installPath, WorkFolderName), Path.Combine(Path.GetTempPath(), "TCFModManager") };
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var workDir in Directory.EnumerateDirectories(root))
            {
                if (InstallJournal.Load(workDir) is not { } journal) continue;
                if (!string.Equals(Path.GetFullPath(journal.InstallPath).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;

                var state = journal.Committed ? true : journal.Record is { } record ? RecordedState(record) : false;
                if (state is null)
                {
                    // The manifest cannot be read, so whether it finished cannot be told: left as it is.
                    AppLog.Warn("Install", $"{journal.ModName}: an interrupted install is left for now - the install records could not be read");
                    pending.Add(journal.ModName);
                    unreadable = true;
                    continue;
                }

                if (state == true)
                {
                    // Written in full: only the tidying up was left - and only while its record is
                    // still the current one; once a later change replaced it, there is nothing to tidy.
                    if (journal.Record is { } finished && IsRecorded(finished))
                    {
                        var target = new InstallTarget(journal.ModId, journal.IsAddon, journal.ModName, null, null, null);
                        if (!KeepReplacedCopies(installPath, journal, target, finished.Replaced))
                        {
                            pending.Add(journal.ModName);
                            continue;
                        }
                    }

                    AppLog.Info("Install", $"{journal.ModName}: tidied up after an install that had finished");
                }
                else
                {
                    var failed = journal.Undo();
                    if (failed.Count > 0)
                    {
                        AppLog.Error("Install", $"{journal.ModName}: {failed.Count} file(s) of an interrupted install could not be put back yet; kept {workDir}");
                        pending.Add(journal.ModName);
                        continue;
                    }

                    AppLog.Warn("Install", $"{journal.ModName}: put back an install the app was stopped in the middle of");
                    recovered.Add(journal.ModName);
                }

                journal.Delete();
                TryDeleteDirectory(workDir);
            }
        }

        return (recovered, pending, unreadable);
    }

    // Copying into the install needs room for every file; checked before anything is changed.
    private static void EnsureFreeSpace(string installPath, IEnumerable<string> sourceFiles, InstallTarget target, ModVersion version)
    {
        try
        {
            var needed = sourceFiles.Sum(f => new FileInfo(f).Length);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(installPath))!);

            // A margin, so the install does not leave the disk with nothing at all.
            const long margin = 64L * 1024 * 1024;
            if (drive.AvailableFreeSpace >= needed + margin) return;

            throw new ModInstallException(ModInstallFailure.NotEnoughSpace)
            {
                ModName = target.Name,
                Version = version.Version,
                ExpectedBytes = needed + margin,
                ReceivedBytes = drive.AvailableFreeSpace,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Unknown free space is not a reason to refuse; the copy says so itself if it fails.
            AppLog.Debug("Install", $"free space not checked: {ex.Message}");
        }
    }

    // Deletes a mod's whole folder (or, for a loose top-level DLL, just that file) - the
    // removal path for a mod this app didn't install itself, since there's no per-file manifest
    // record to work from. Callers should confirm the exact path with the user before calling this.
    // <paramref name="installPath"/> is the install <paramref name="path"/> lives in, and only
    // scopes the in-use check - the deletion itself is driven by path alone.
    public static void RemoveLegacyPath(string path, string? installPath = null)
    {
        EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    // The config files a hand-installed mod keeps in its own folder, as install-relative paths.
    // Read off disk rather than from a record, since this path has none.
    public static List<string> FindLegacyConfigs(string installPath, IEnumerable<string> modFolderPaths) =>
        modFolderPaths.SelectMany(folder => ModConfigFiles.InFolder(installPath, folder)).Distinct().ToList();

    // Moves a hand-installed mod's config files out of the install before its folder is deleted.
    public static KeptConfigs KeepLegacyConfigs(string installPath, IEnumerable<string> relativeFiles, string modName) =>
        ModConfigFiles.MoveOut(installPath, relativeFiles, modName, DateTimeOffset.UtcNow);

    // Where the archive's content starts, past any wrapper folders - see ArchiveLayout.
    private static string FindContentRoot(string extractDir) => ArchiveLayout.ContentRoot(extractDir);

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
            var fallback = Path.Combine(Path.GetTempPath(), "TCFModManager", id);
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

                // An install that was stopped part-way: the previous version's files are in there.
                // RecoverInterruptedInstalls puts it back; until then it stays.
                if (File.Exists(Path.Combine(directory, InstallJournal.FileName))) continue;

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

    // Extracts every file entry in the archive at <paramref name="archivePath"/> into
    // <paramref name="extractDir"/>. Zip archives go through System.IO.Compression; every other
    // format goes through SharpCompress's forward-only reader. Zip-slip protection: any entry whose
    // resolved destination would land outside extractDir is rejected before anything is written.
    private static async Task ExtractArchiveAsync(
        string archivePath,
        string extractDir,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct)
    {
        Directory.CreateDirectory(extractDir);
        var extractRoot = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

        if (IsZipArchive(archivePath))
        {
            await ExtractZipAsync(archivePath, extractDir, extractRoot, status, ct).ConfigureAwait(false);
            return;
        }

        ExtractWithSharpCompress(archivePath, extractDir, extractRoot, status, ct);
    }

    // Reads the local-file-header magic rather than trusting the file extension, matching
    // how the previous SharpCompress-only path detected format.
    private static bool IsZipArchive(string archivePath)
    {
        try
        {
            using var stream = File.OpenRead(archivePath);
            Span<byte> header = stackalloc byte[4];
            if (stream.ReadAtLeast(header, 4, throwOnEndOfStream: false) < 4) return false;

            return header[0] == 0x50 && header[1] == 0x4B
                && ((header[2] == 0x03 && header[3] == 0x04)
                    || (header[2] == 0x05 && header[3] == 0x06)
                    || (header[2] == 0x07 && header[3] == 0x08));
        }
        catch (IOException)
        {
            return false;
        }
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

    private static void ExtractWithSharpCompress(
        string archivePath,
        string extractDir,
        string extractRoot,
        IProgress<ModInstallProgress>? status,
        CancellationToken ct)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);

        var total = TryCountEntries(archive);

        // Forward-only reader rather than random-access Entries: a solid archive decompresses its
        // blocks once here, instead of once per entry.
        using var reader = archive.ExtractAllEntries();
        var extracted = 0;
        var reportClock = Stopwatch.StartNew();

        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.Entry.IsDirectory) continue;
            if (reader.Entry.Key is not { Length: > 0 } key) continue;

            var destination = ResolveEntryDestination(key, extractDir, extractRoot);
            reader.WriteEntryToFile(destination, new ExtractionOptions { Overwrite = true });

            extracted++;
            if (reportClock.Elapsed >= ProgressInterval)
            {
                status?.Report(new ModInstallProgress(
                    ModInstallStage.Extracting, Done: extracted, Total: total));
                reportClock.Restart();
            }
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

    // Remaps the "user" top-level folder to <paramref name="serverRoot"/>. No-op when
    // <paramref name="serverRoot"/> is "".
    private static string RemapForServerRoot(string archiveRelative, string serverRoot)
    {
        if (string.IsNullOrEmpty(serverRoot)) return archiveRelative;

        var firstSegment = archiveRelative.Split(Path.DirectorySeparatorChar, 2)[0];
        return string.Equals(firstSegment, "user", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(serverRoot, archiveRelative)
            : archiveRelative;
    }

    private static bool IsUnderInstallPath(string? dir, string installPath)
    {
        if (string.IsNullOrEmpty(dir)) return false;

        var fullDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        var fullInstall = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar);
        return fullDir.Length > fullInstall.Length
            && fullDir.StartsWith(fullInstall, StringComparison.OrdinalIgnoreCase);
    }
}

//
// What an install placed, and what it did to the mod's own config files.
//
// Configs is null when the mod has none - most client-only mods, and anything whose settings live in
// BepInEx\config rather than inside its own folder.
//
public sealed record ModInstallResult(InstalledModRecord Record, ConfigUpdateReport? Configs);

// Result of ModInstallService.UninstallAsync. FailedFiles lists files that couldn't be
// deleted; the mod is still removed from the manifest regardless. ConfigsKept/ConfigsFolder
// describe the mod's own config files when they were moved out rather than deleted.
public sealed record UninstallResult(int FilesDeleted, List<string> FailedFiles, int ConfigsKept = 0, string? ConfigsFolder = null);

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
