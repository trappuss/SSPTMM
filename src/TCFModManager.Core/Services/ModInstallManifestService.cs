using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Loads/saves the ModInstallManifest as JSON under &lt;app folder&gt;\Data\installed-mods.json.
// A corrupt or hand-edited manifest falls back to an empty one rather than blocking the app.
// 
public sealed class ModInstallManifestService
{
    private readonly string _filePath;

    public ModInstallManifestService(string? filePath = null)
    {
        // Optional path for testability, matching ModListStore's accepted deviation.
        _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "installed-mods.json");
    }

    public ModInstallManifest Load()
    {
        if (!File.Exists(_filePath)) return new ModInstallManifest();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ModInstallManifest>(json) ?? new ModInstallManifest();
        }
        catch (JsonException)
        {
            // Kept aside before the next save writes an empty manifest over it (D18).
            SafeFile.PreserveDamaged(_filePath);
            return new ModInstallManifest();
        }
    }

    public void Save(ModInstallManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteText(_filePath, json, keepBackups: true);
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
        int modId, string? guid, string name, string version, int? versionId, IReadOnlyList<string> folders,
        bool isAddon = false)
    {
        var manifest = Load();
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
            Fingerprints = existing?.Fingerprints ?? [],
            InstallPath = existing?.InstallPath,
            Overwrote = existing?.Overwrote ?? [],
        };

        manifest.Mods.RemoveAll(m => m.ModId == modId && m.IsAddon == isAddon);
        manifest.Mods.Add(record);
        Save(manifest);

        return record;
    }

    //
    // Records a Monitor mode download the user has confirmed installing by hand.
    //
    // Over an app-managed record - a mod this app installed, then updated by hand - the record stays
    // app-managed and takes the download's file list. Keeping the old list would leave Remove
    // deleting the previous version's files and missing the new version's extras; the new list was
    // checked on disk by DownloadMatcher before the user was asked, so the app does know exactly what
    // is there. Anything else goes through SetManualVersion as an IsAppManaged: false record.
    //
    //
    // With installPath, every confirmed file still on disk is fingerprinted as it is now, so removal can
    // tell later whether it was changed (D21), and the record is stamped with that install (D17).
    // SPT's own files are never part of the record (D4).
    //
    public InstalledModRecord ConfirmDownload(DownloadedModRecord download, string? installPath = null)
    {
        var manifest = Load();
        var existing = manifest.Mods.FirstOrDefault(m => m.ModId == download.ModId && m.IsAddon == download.IsAddon);

        if (existing is not { IsAppManaged: true })
        {
            return SetManualVersion(
                download.ModId, download.Guid, download.Name, download.Version, download.VersionId,
                download.ExpectedFolders, download.IsAddon);
        }

        var files = download.ExpectedFiles
            .Select(f => f.Path)
            .Where(p => !ProtectedInstallPaths.IsProtected(p))
            .ToList();

        var fingerprints = new List<FileFingerprint>();
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            foreach (var path in files)
            {
                if (InstallPathGuard.CheckRecordedPath(installPath, path, out var full) is null
                    && FileFingerprint.Compute(full, path) is { } print)
                {
                    fingerprints.Add(print);
                }
            }
        }

        var record = new InstalledModRecord
        {
            ModId = download.ModId,
            IsAddon = download.IsAddon,
            Guid = download.Guid ?? existing.Guid,
            Name = existing.Name,
            VersionId = download.VersionId,
            Version = download.Version,
            InstalledAt = DateTimeOffset.UtcNow,
            Files = files,
            Folders = download.ExpectedFolders.Count > 0
                ? [.. download.ExpectedFolders]
                : InstalledModFolders.FromPlacedFiles(files),
            Incomplete = false,
            IsAppManaged = true,

            // The originals an earlier install replaced are still in Data and still owed back.
            Fingerprints = fingerprints,
            InstallPath = string.IsNullOrWhiteSpace(installPath) ? existing.InstallPath : InstallStamp.Of(installPath),
            Overwrote = existing.Overwrote,
        };

        manifest.Mods.RemoveAll(m => m.ModId == download.ModId && m.IsAddon == download.IsAddon);
        manifest.Mods.Add(record);
        Save(manifest);

        return record;
    }

    //
    // Undoes SetManualVersion, dropping the record entirely so the mod goes back to auto-detecting
    // its version from the files on disk. No-op for an app-managed record - that reflects a real
    // install, and clearing it would misrepresent what this app actually placed.
    //
    public void ClearManualVersion(int modId, bool isAddon = false)
    {
        var manifest = Load();
        var existing = manifest.Mods.FirstOrDefault(m => m.ModId == modId && m.IsAddon == isAddon);
        if (existing is null || existing.IsAppManaged) return;

        manifest.Mods.RemoveAll(m => m.ModId == modId && m.IsAddon == isAddon);
        Save(manifest);
    }
}
