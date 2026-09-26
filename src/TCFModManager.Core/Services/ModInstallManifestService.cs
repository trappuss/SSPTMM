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
// A corrupt or hand-edited list falls back to its backup, else an empty one (see SafeFile).
//
public sealed class ModInstallManifestService
{
    private const string RecordsFileName = "installed-mods.json";

    private readonly string? _fixedFile;
    private readonly string? _fixedReplaced;
    private readonly string _dataDirectory;

    // One migration at a time, and one read-modify-write of the old list.
    private static readonly Lock Migrating = new();

    /// <summary>Records for every install under <paramref name="dataDirectory"/> (the app's Data
    /// folder when null), one set per install.</summary>
    public ModInstallManifestService(string? dataDirectory = null) =>
        _dataDirectory = dataDirectory ?? AppPaths.DataDirectory;

    private ModInstallManifestService(string filePath, string? replacedFilesRoot)
    {
        _fixedFile = filePath;
        _fixedReplaced = replacedFilesRoot;
        _dataDirectory = Path.GetDirectoryName(filePath) ?? AppPaths.DataDirectory;
    }

    /// <summary>One list at <paramref name="filePath"/> whatever the install - for tests that deal in
    /// a single install.</summary>
    public static ModInstallManifestService SingleFile(string filePath, string? replacedFilesRoot = null) =>
        new(filePath, replacedFilesRoot);

    /// <summary>The folder an install's records live in.</summary>
    public string FolderFor(string installPath) =>
        Path.Combine(_dataDirectory, "InstallRecords", KeyFor(installPath));

    /// <summary>Where the copies of files this install's installs replaced are kept.</summary>
    public string ReplacedFilesRootFor(string installPath) =>
        _fixedFile is not null
            ? _fixedReplaced ?? Path.Combine(_dataDirectory, "ReplacedFiles")
            : Path.Combine(FolderFor(installPath), "ReplacedFiles");

    private string FileFor(string installPath) => _fixedFile ?? Path.Combine(FolderFor(installPath), RecordsFileName);

    /// <summary>The file this install's records are in (moved over from the old list first, when
    /// they have not been yet) - for the Data files editor.</summary>
    public string RecordsFileFor(string installPath)
    {
        Load(installPath);
        return FileFor(installPath);
    }

    // The same key for the same folder however it is written (case, a trailing separator).
    private static string KeyFor(string installPath)
    {
        var full = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
    }

    /// <summary>This install's records - none when there is no install.</summary>
    public ModInstallManifest Load(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) && _fixedFile is null) return new ModInstallManifest();

        var file = FileFor(installPath!);
        if (_fixedFile is null && !File.Exists(file)) MigrateInto(installPath!);

        // A damaged file is kept aside and its backup put back - see SafeFile. Losing this one would
        // make every mod the app installed look hand-installed.
        //
        // One that cannot be read at all (held open elsewhere) throws, as it always did: read as
        // empty, an update would take its mod for a new one and a removal would find no record.
        //
        return SafeFile.ReadJson(file, json => JsonSerializer.Deserialize<ModInstallManifest>(json), throwIfUnreadable: true)
            ?? new ModInstallManifest();
    }

    public void Save(string installPath, ModInstallManifest manifest)
    {
        var file = FileFor(installPath);
        if (_fixedFile is null) WriteInstallName(installPath);

        var json = JsonSerializer.Serialize(manifest, Json);
        if (!SafeFile.WriteAllText(file, json, keepBackup: true))
            throw new IOException($"{Path.GetFileName(file)} could not be read this session, so it is not saved over");
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private void WriteInstallName(string installPath)
    {
        var folder = FolderFor(installPath);
        Directory.CreateDirectory(folder);
        var name = Path.Combine(folder, "install.txt");
        if (!File.Exists(name)) File.WriteAllText(name, installPath);
    }

    //
    // The first time an install is opened: the records in the old single list whose files are in
    // it, with their kept copies, become its own. Always leaves the install a records file (empty
    // when nothing was its), so this runs once per install.
    //
    private void MigrateInto(string installPath)
    {
        lock (Migrating)
        {
            var file = FileFor(installPath);
            if (File.Exists(file)) return;

            var legacyFile = Path.Combine(_dataDirectory, RecordsFileName);
            var legacy = File.Exists(legacyFile)
                ? SafeFile.ReadJson(legacyFile, json => JsonSerializer.Deserialize<ModInstallManifest>(json), throwIfUnreadable: true)
                  ?? new ModInstallManifest()
                : new ModInstallManifest();

            var mine = legacy.Mods.Where(r => BelongsTo(r, installPath)).ToList();

            // The kept copies first, then the records: a record without its copies would put nothing
            // back on removal, where copies without their record are only picked up next time.
            var legacyReplaced = Path.Combine(_dataDirectory, "ReplacedFiles");
            var replacedRoot = ReplacedFilesRootFor(installPath);
            foreach (var record in mine)
            {
                var name = (record.IsAddon ? "addon-" : "mod-") + record.ModId;
                var from = Path.Combine(legacyReplaced, name);
                var to = Path.Combine(replacedRoot, name);
                if (!Directory.Exists(from) || Directory.Exists(to)) continue;

                Directory.CreateDirectory(replacedRoot);
                Directory.Move(from, to);
            }

            Save(installPath, new ModInstallManifest { Mods = mine });

            if (mine.Count > 0)
            {
                legacy.Mods.RemoveAll(r => mine.Contains(r));
                var json = JsonSerializer.Serialize(legacy, Json);
                if (!SafeFile.WriteAllText(legacyFile, json, keepBackup: true))
                    throw new IOException($"{RecordsFileName} could not be read this session, so it is not saved over");

                AppLog.Info("Install", $"records of {mine.Count} mod(s) moved to their install, {installPath}; {legacy.Mods.Count} left for other installs");
            }
        }
    }

    //
    // Whether a record from the old list is this install's: a file it placed is there (or in the
    // disabled folder beside it), or - for a version confirmed by hand, which placed nothing - one
    // of its folders is.
    //
    private static bool BelongsTo(InstalledModRecord record, string installPath)
    {
        if (record.Files.Count > 0)
        {
            return record.Files.Any(relative =>
                File.Exists(Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar)))
                || (DisabledModPaths.TryGetRelativeCounterpart(relative, out var other)
                    && File.Exists(Path.Combine(installPath, other.Replace('/', Path.DirectorySeparatorChar)))));
        }

        if (record.Folders.Count == 0) return false;

        var present = InstalledModScanner.Scan(installPath)
            .Select(m => Path.GetFileName(m.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return record.Folders.Any(present.Contains);
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
