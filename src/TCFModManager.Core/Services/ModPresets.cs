using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM 1.1.0): presets - named sets of which installed mods are on and which are off, as Mod
// Organizer 2's profiles keep them. "Everything but Fika", "troubleshooting: all off", "my Fika
// setup". Applying one only enables and disables (ModDisableService: folders move between a container
// and its ".disabled" sibling), so nothing is ever deleted and every apply can be undone.
//
// A preset holds one entry per mod folder (or loose DLL), keyed by its path in the install as it
// reads when enabled ("BepInEx/plugins/SAIN", "SPT_Runtime/user/mods/SAIN-ServerMod"), so a mod
// with only its client half turned off is kept exactly that way. Mods installed after a preset was
// saved aren't in it, and applying it leaves them as they are.
//
// Kept per SPT install, in Data\mod_presets.json.
//

/// <summary>One mod folder's state in a preset.</summary>
public sealed record ModPresetEntry
{
    /// <summary>Install-relative, forward slashes, as the path reads when enabled.</summary>
    public required string Path { get; init; }

    public required bool Enabled { get; init; }

    /// <summary>What the mod was called when the preset was saved - for showing a missing mod.</summary>
    public string? Name { get; init; }
}

public sealed class ModPreset
{
    public required string Name { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    public List<ModPresetEntry> Entries { get; set; } = [];
}

public sealed class ModPresetInstall
{
    /// <summary>The install's folder, for reading the file by eye; the key is what matches.</summary>
    public string? Folder { get; set; }

    public List<ModPreset> Presets { get; set; } = [];

    /// <summary>How the mods were just before the last preset (or Disable all / Enable all) was
    /// applied - kept so that can be put back after a restart, when Undo is gone.</summary>
    public ModPreset? BeforeLastApply { get; set; }
}

public sealed class ModPresetData
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Keyed by ModPresets.InstallKey.</summary>
    public Dictionary<string, ModPresetInstall> Installs { get; set; } = [];
}

/// <summary>What applying a preset would change.</summary>
public sealed record ModPresetPlan(
    IReadOnlyList<InstalledMod> ToDisable,
    IReadOnlyList<InstalledMod> ToEnable,
    IReadOnlyList<ModPresetEntry> NotInstalled,
    IReadOnlyList<InstalledMod> NotInPreset)
{
    public bool ChangesNothing => ToDisable.Count == 0 && ToEnable.Count == 0;
}

public static class ModPresets
{
    public const int MaxNameLength = 60;

    /// <summary>The key an install's presets sit under: the same hash ProfileBackups files under.</summary>
    public static string InstallKey(string installPath)
    {
        var full = System.IO.Path.GetFullPath(installPath)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
    }

    /// <summary>A mod's preset key: its install-relative path as it reads when enabled.</summary>
    public static string KeyFor(string installPath, InstalledMod mod) =>
        DisabledModPaths.ToEnabledRelativePath(
            DisabledModPaths.ToRelative(System.IO.Path.GetRelativePath(installPath, mod.FolderPath)));

    /// <summary>The mods as they are now, as a preset's entries.</summary>
    public static List<ModPresetEntry> Capture(string installPath, IEnumerable<InstalledMod> mods) =>
        mods.Select(m => new ModPresetEntry { Path = KeyFor(installPath, m), Enabled = !m.IsDisabled, Name = m.Name })
            .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every mod on, or every mod off - Enable all and Disable all, planned as presets are.</summary>
    public static List<ModPresetEntry> All(string installPath, IEnumerable<InstalledMod> mods, bool enabled) =>
        Capture(installPath, mods).Select(e => e with { Enabled = enabled }).ToList();

    //
    // Which mods move. A mod that is in the install twice - a copy in the container and another in its
    // ".disabled" sibling - is left alone: which copy is meant is a question for Subscribed items'
    // duplicate prompt, not for a preset.
    //
    public static ModPresetPlan Plan(string installPath, IEnumerable<ModPresetEntry> entries, IEnumerable<InstalledMod> mods)
    {
        var wanted = new Dictionary<string, ModPresetEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) wanted.TryAdd(entry.Path, entry);

        var byKey = mods.GroupBy(m => KeyFor(installPath, m), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var toDisable = new List<InstalledMod>();
        var toEnable = new List<InstalledMod>();
        var notInPreset = new List<InstalledMod>();

        foreach (var (key, copies) in byKey)
        {
            if (!wanted.TryGetValue(key, out var entry))
            {
                notInPreset.AddRange(copies);
                continue;
            }

            if (copies.Count != 1) continue;

            var mod = copies[0];
            if (entry.Enabled && mod.IsDisabled) toEnable.Add(mod);
            else if (!entry.Enabled && !mod.IsDisabled) toDisable.Add(mod);
        }

        var notInstalled = wanted.Values
            .Where(e => !byKey.ContainsKey(e.Path))
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModPresetPlan(toDisable, toEnable, notInstalled, notInPreset);
    }

    /// <summary>The preset (by name, ignoring case) the mods are set exactly as now, if any - for a
    /// tick in the menu. Mods the preset doesn't name don't count against it.</summary>
    public static ModPreset? Matching(string installPath, IEnumerable<ModPreset> presets, IReadOnlyCollection<InstalledMod> mods) =>
        presets.FirstOrDefault(p => p.Entries.Count > 0 && Plan(installPath, p.Entries, mods).ChangesNothing);

    /// <summary>A name as it is kept: trimmed, single-line, at most MaxNameLength. Null when blank.</summary>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var oneLine = string.Join(' ', name.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (oneLine.Length == 0) return null;
        return oneLine.Length <= MaxNameLength ? oneLine : oneLine[..MaxNameLength].TrimEnd();
    }
}

//
// Data\mod_presets.json. A damaged file is set aside (SafeFile.PreserveDamaged) and the presets read
// as none, rather than stopping Subscribed items from opening; every write keeps backups.
//
public sealed class ModPresetStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly Lock _gate = new();

    public ModPresetStore() : this(Path.Combine(AppPaths.DataDirectory, "mod_presets.json"))
    {
    }

    // A file of its own - for the tests.
    public ModPresetStore(string filePath) => _filePath = filePath;

    // Reading only: a file that can't be read right now gives no presets. (Change lets the same
    // error through instead - writing after a failed read would replace every preset with one.)
    public ModPresetInstall For(string installPath)
    {
        lock (_gate)
        {
            try
            {
                return Load().Installs.TryGetValue(ModPresets.InstallKey(installPath), out var install)
                    ? install
                    : new ModPresetInstall { Folder = installPath };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Presets", $"mod_presets.json couldn't be read: {ex.Message}");
                return new ModPresetInstall { Folder = installPath };
            }
        }
    }

    /// <summary>Saves a preset under its name, replacing one of the same name (ignoring case).</summary>
    public void Save(string installPath, string name, IReadOnlyList<ModPresetEntry> entries, DateTimeOffset when) =>
        Change(installPath, install =>
        {
            var existing = Find(install, name);
            if (existing is null)
            {
                install.Presets.Add(new ModPreset { Name = name, SavedAt = when, Entries = [.. entries] });
            }
            else
            {
                existing.SavedAt = when;
                existing.Entries = [.. entries];
            }

            install.Presets.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        });

    /// <summary>False when another preset already has the new name.</summary>
    public bool Rename(string installPath, string name, string newName)
    {
        var renamed = false;
        Change(installPath, install =>
        {
            var preset = Find(install, name);
            var clash = Find(install, newName);
            if (preset is null || (clash is not null && !ReferenceEquals(clash, preset))) return;

            preset.Name = newName;
            install.Presets.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
            renamed = true;
        });
        return renamed;
    }

    public void Delete(string installPath, string name) =>
        Change(installPath, install => install.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Keeps how the mods are now, before a preset changes them.</summary>
    public void KeepBeforeApply(string installPath, string appliedName, IReadOnlyList<ModPresetEntry> entries, DateTimeOffset when) =>
        Change(installPath, install => install.BeforeLastApply = new ModPreset { Name = appliedName, SavedAt = when, Entries = [.. entries] });

    public static ModPreset? Find(ModPresetInstall install, string name) =>
        install.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private void Change(string installPath, Action<ModPresetInstall> change)
    {
        lock (_gate)
        {
            var data = Load();
            var key = ModPresets.InstallKey(installPath);
            if (!data.Installs.TryGetValue(key, out var install))
                data.Installs[key] = install = new ModPresetInstall();

            install.Folder = installPath;
            change(install);
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(data, Options), keepBackups: true);
        }
    }

    private ModPresetData Load()
    {
        if (!File.Exists(_filePath)) return new ModPresetData();

        try
        {
            var data = JsonSerializer.Deserialize<ModPresetData>(File.ReadAllText(_filePath)) ?? new ModPresetData();
            data.Installs ??= [];
            foreach (var install in data.Installs.Values)
            {
                install.Presets ??= [];
                install.Presets.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Name));
                foreach (var preset in install.Presets) preset.Entries ??= [];
            }

            return data;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            AppLog.Warn("Presets", $"mod_presets.json couldn't be read ({ex.Message}); set aside and started empty");
            SafeFile.PreserveDamaged(_filePath);
            return new ModPresetData();
        }
    }
}
