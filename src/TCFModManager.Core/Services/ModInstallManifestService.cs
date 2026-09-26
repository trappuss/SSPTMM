using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// The records of what this app installed, one set per SPT install.
//
// Each install keeps its own, under Data\InstallRecords\<key>\ (key: a hash of the install's full
// path; install.txt beside it says which install it is) - installed-mods.json, and the copies of
// files its installs replaced (ReplacedFiles). A record says which files an install placed and which
// version it is: kept in one list for every install, a mod installed on one showed on another as
// that version (whatever was really there), and removing it there deleted by the first install's
// file list and put back the first install's replaced files.
//
// The single list kept before (Data\installed-mods.json, Data\ReplacedFiles) is moved over the first
// time each install is opened: the records whose files are in that install go to it, with their
// kept copies; the rest wait in the old list for the install they belong to. A record none of whose
// files is anywhere stays there - it describes nothing on disk.
//
// Records belong to an install's folder: one moved to a new folder starts with none (see SourcesFor).
//
// A corrupt or hand-edited list falls back to its backup, else an empty one (see SafeFile).
//
public sealed class ModInstallManifestService
{
    private const string RecordsFileName = "installed-mods.json";
    private const string InstallNameFile = "install.txt";

    private readonly string _dataDirectory;

    // One migration at a time, and one read-modify-write of the old list.
    private static readonly Lock Migrating = new();

    /// <summary>Records for every install under <paramref name="dataDirectory"/> (the app's Data
    /// folder when null), one set per install.</summary>
    public ModInstallManifestService(string? dataDirectory = null) =>
        _dataDirectory = dataDirectory ?? AppPaths.DataDirectory;

    private string RecordsRoot => Path.Combine(_dataDirectory, "InstallRecords");

    /// <summary>The folder an install's records live in.</summary>
    public string FolderFor(string installPath) => Path.Combine(RecordsRoot, KeyFor(installPath));

    /// <summary>Where the copies of files this install's installs replaced are kept.</summary>
    public string ReplacedFilesRootFor(string installPath) => Path.Combine(FolderFor(installPath), "ReplacedFiles");

    private string FileFor(string installPath) => Path.Combine(FolderFor(installPath), RecordsFileName);

    /// <summary>The file this install's records are in (moved over from the old list first, when
    /// they have not been yet) - for the Data files editor.</summary>
    public string? RecordsFileFor(string installPath)
    {
        // Not for a folder that is not there: saving the editor's text there would mark the install
        // as having had its records moved over, with none moved.
        if (!Directory.Exists(installPath)) return null;

        Load(installPath);
        return FileFor(installPath);
    }

    // The same key for the same folder however it is written (case, a trailing separator).
    private static string KeyFor(string installPath)
    {
        var full = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
    }

    /// <summary>This install's records - none when there is no install, or its folder is not there
    /// right now (a drive not plugged in: nothing is moved to it, or marked as done, until it is).</summary>
    public ModInstallManifest Load(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return new ModInstallManifest();

        var file = FileFor(installPath);
        if (!File.Exists(file))
        {
            if (!Directory.Exists(installPath)) return new ModInstallManifest();
            MigrateInto(installPath);
        }

        // A damaged file is kept aside and its backup put back - see SafeFile. Losing this one would
        // make every mod the app installed look hand-installed.
        //
        // One that cannot be read at all (held open elsewhere) throws, as it always did: read as
        // empty, an update would take its mod for a new one and a removal would find no record.
        //
        return ReadList(file) ?? new ModInstallManifest();
    }

    public void Save(string installPath, ModInstallManifest manifest)
    {
        WriteInstallName(installPath);
        WriteList(FileFor(installPath), manifest);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static ModInstallManifest? ReadList(string file) =>
        SafeFile.ReadJson(file, json => JsonSerializer.Deserialize<ModInstallManifest>(json), throwIfUnreadable: true);

    private static void WriteList(string file, ModInstallManifest manifest)
    {
        if (!SafeFile.WriteAllText(file, JsonSerializer.Serialize(manifest, Json), keepBackup: true))
            throw new IOException($"{Path.GetFileName(file)} could not be read this session, so it is not saved over");
    }

    private void WriteInstallName(string installPath)
    {
        var folder = FolderFor(installPath);
        Directory.CreateDirectory(folder);
        var name = Path.Combine(folder, InstallNameFile);
        if (!File.Exists(name)) File.WriteAllText(name, installPath);
    }

    // Where records waiting for their install are: the old single list, and the copies it kept.
    //
    // Not the records of an install whose folder has gone. Taking those over for the install their
    // files are found in was tried and dropped: a folder renamed for a while, or being reinstalled,
    // looks the same as one that moved, and an unrelated install that merely has the same mods
    // would take them too. An install moved to a new folder starts with no records - its mods show
    // as installed by hand (their versions read from their files) until they are next installed or
    // updated from here.
    private sealed record Source(string File, string ReplacedRoot);

    private IEnumerable<Source> SourcesFor() =>
        [new Source(Path.Combine(_dataDirectory, RecordsFileName), Path.Combine(_dataDirectory, "ReplacedFiles"))];

    //
    // The first time an install is opened: the records waiting in the old single list whose files
    // are in it become its own, with their kept copies.
    // Always leaves the install a records file (empty when nothing was its), so this runs once per
    // install.
    //
    // Safe to stop at any point: copies are moved first, then this install's list is written, then
    // each source's. Stopped before the last step, a record is in both - and one already in an
    // install's list is never claimed again, only taken off the list it waited in.
    //
    private void MigrateInto(string installPath)
    {
        lock (Migrating)
        {
            var file = FileFor(installPath);
            if (File.Exists(file)) return;

            var evidence = new Evidence(installPath);
            var changes = new List<(Source Source, ModInstallManifest List, List<InstalledModRecord> Mine, List<InstalledModRecord> Done)>();

            foreach (var source in SourcesFor().Where(s => File.Exists(s.File)).ToList())
            {
                var list = ReadList(source.File) ?? new ModInstallManifest();

                // Already in another install's list (a move stopped part way): only taken off here.
                var elsewhere = RecordsInInstallsOtherThan(source.File);
                var done = list.Mods.Where(r => elsewhere.Contains(Key(r))).ToList();
                var mine = list.Mods.Where(r => !done.Contains(r) && evidence.BelongsHere(r)).ToList();

                if (mine.Count > 0 || done.Count > 0) changes.Add((source, list, mine, done));
            }

            // The kept copies first, then this install's records, then each source's: a record
            // without its copies would put nothing back on removal, where copies without their
            // record are picked up next time.
            var replacedRoot = ReplacedFilesRootFor(installPath);
            foreach (var (source, _, mine, _) in changes)
            {
                foreach (var record in mine)
                {
                    var name = (record.IsAddon ? "addon-" : "mod-") + record.ModId;
                    var from = Path.Combine(source.ReplacedRoot, name);
                    var to = Path.Combine(replacedRoot, name);
                    if (!Directory.Exists(from) || Directory.Exists(to)) continue;

                    Directory.CreateDirectory(replacedRoot);
                    Directory.Move(from, to);
                }
            }

            Save(installPath, new ModInstallManifest { Mods = [.. changes.SelectMany(c => c.Mine)] });

            foreach (var (source, list, mine, done) in changes)
            {
                list.Mods.RemoveAll(r => mine.Contains(r) || done.Contains(r));
                WriteList(source.File, list);

                if (mine.Count > 0)
                    AppLog.Info("Install", $"records of {mine.Count} mod(s) moved to {installPath} from the shared list; {list.Mods.Count} left for other installs");
            }
        }
    }

    // A record is the same install of a mod wherever its copy is: the mod, and when it was installed.
    private static (int, bool, DateTimeOffset) Key(InstalledModRecord record) => (record.ModId, record.IsAddon, record.InstalledAt);

    private HashSet<(int, bool, DateTimeOffset)> RecordsInInstallsOtherThan(string sourceFile)
    {
        var keys = new HashSet<(int, bool, DateTimeOffset)>();
        if (!Directory.Exists(RecordsRoot)) return keys;

        foreach (var file in Directory.EnumerateFiles(RecordsRoot, RecordsFileName, SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(sourceFile), StringComparison.OrdinalIgnoreCase)) continue;

            // One of another install that cannot be read now says nothing either way.
            try
            {
                foreach (var record in ReadList(file)?.Mods ?? []) keys.Add(Key(record));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Install", $"couldn't read {file}: {ex.Message}");
            }
        }

        return keys;
    }

    //
    // Whether a waiting record is this install's.
    //
    // By the files it placed in its own mod folders (BepInEx/plugins/<mod>/..., user/mods/<mod>/...),
    // there or in the ".disabled" folder beside it - not by files any install can have: a loose DLL
    // straight in BepInEx/plugins, or a file it placed over another's (Replaced), are only looked at
    // when it placed nothing else. A version confirmed by hand placed nothing: by its folders.
    //
    private sealed class Evidence(string installPath)
    {
        private HashSet<string>? _folders;

        public bool BelongsHere(InstalledModRecord record)
        {
            if (record.Files.Count > 0)
            {
                var own = record.Files
                    .Where(f => InOwnFolder(f) && !record.Replaced.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                return (own.Count > 0 ? own : record.Files).Any(Present);
            }

            if (record.Folders.Count == 0) return false;

            _folders ??= InstalledModScanner.Scan(installPath)
                .Select(m => Path.GetFileName(m.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return record.Folders.Any(_folders.Contains);
        }

        private bool Present(string relative) =>
            File.Exists(Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar)))
            || (DisabledModPaths.TryGetRelativeCounterpart(relative, out var other)
                && File.Exists(Path.Combine(installPath, other.Replace('/', Path.DirectorySeparatorChar))));

        // Inside a folder of its own under a mod container, not loose in the container.
        private static bool InOwnFolder(string relative)
        {
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 3 < segments.Length; i++)
            {
                var container = (segments[i], segments[i + 1]) switch
                {
                    var (a, b) when a.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)
                        && (b.StartsWith("plugins", StringComparison.OrdinalIgnoreCase) || b.StartsWith("patchers", StringComparison.OrdinalIgnoreCase)) => true,
                    var (a, b) when a.Equals("user", StringComparison.OrdinalIgnoreCase)
                        && b.StartsWith("mods", StringComparison.OrdinalIgnoreCase) => true,
                    _ => false,
                };
                if (container) return true;
            }

            return false;
        }
    }

    //
    // Creates or updates a manually-confirmed installed version for a matched mod - the Installed
    // page's "confirm/select/override version" and "mark up to date" actions all funnel through here.
    // If an app-managed record already exists (this app installed the mod itself), only its Version/
    // VersionId move - Files/Folders/IsAppManaged are carried over unchanged, since nothing about
    // what's on disk changed. Otherwise a fresh IsAppManaged: false record is written with no Files,
    // so it stays outside the app's own uninstall path.
    //
    public InstalledModRecord SetManualVersion(
        string installPath, int modId, string? guid, string name, string version, int? versionId, IReadOnlyList<string> folders,
        bool isAddon = false)
    {
        var manifest = Load(installPath);
        var existing = manifest.Mods.FirstOrDefault(m => m.ModId == modId && m.IsAddon == isAddon);

        var record = new InstalledModRecord
        {
            ModId = modId,
            IsAddon = isAddon,
            Guid = guid ?? existing?.Guid,
            Name = existing?.Name ?? name,
            VersionId = versionId ?? existing?.VersionId,
            Version = version,
            InstalledAt = existing?.InstalledAt ?? DateTimeOffset.UtcNow,
            Files = existing?.Files ?? [],
            Folders = existing is { Folders.Count: > 0 } ? existing.Folders : folders.ToList(),
            Incomplete = existing?.Incomplete ?? false,
            IsAppManaged = existing?.IsAppManaged ?? false,
            Replaced = existing?.Replaced ?? [],
        };

        manifest.Mods.RemoveAll(m => m.ModId == modId && m.IsAddon == isAddon);
        manifest.Mods.Add(record);
        Save(installPath, manifest);

        return record;
    }

    //
    // Undoes SetManualVersion, dropping the record entirely so the mod goes back to auto-detecting
    // its version from the files on disk. No-op for an app-managed record - that reflects a real
    // install, and clearing it would misrepresent what this app actually placed.
    //
    public void ClearManualVersion(string installPath, int modId, bool isAddon = false)
    {
        var manifest = Load(installPath);
        var existing = manifest.Mods.FirstOrDefault(m => m.ModId == modId && m.IsAddon == isAddon);
        if (existing is null || existing.IsAppManaged) return;

        manifest.Mods.RemoveAll(m => m.ModId == modId && m.IsAddon == isAddon);
        Save(installPath, manifest);
    }
}
