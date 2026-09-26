using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Loads/saves the ModInstallManifest as JSON under &lt;app folder&gt;\Data\installed-mods.json.
// A corrupt or hand-edited manifest falls back to an empty one rather than blocking the app.
// 
public sealed class ModInstallManifestService(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "installed-mods.json");

    public ModInstallManifest Load()
    {
        // A damaged file is kept aside and its backup put back - see SafeFile. Losing this one would
        // make every mod the app installed look hand-installed.
        //
        // One that cannot be read at all (held open elsewhere) throws, as it always did: read as
        // empty, an update would take its mod for a new one and a removal would find no record.
        //
        return SafeFile.ReadJson(_filePath, json => JsonSerializer.Deserialize<ModInstallManifest>(json), throwIfUnreadable: true)
            ?? new ModInstallManifest();
    }

    public void Save(ModInstallManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        if (!SafeFile.WriteAllText(_filePath, json, keepBackup: true))
            throw new IOException($"{Path.GetFileName(_filePath)} could not be read this session, so it is not saved over");
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
            Replaced = existing?.Replaced ?? [],
        };

        manifest.Mods.RemoveAll(m => m.ModId == modId && m.IsAddon == isAddon);
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
