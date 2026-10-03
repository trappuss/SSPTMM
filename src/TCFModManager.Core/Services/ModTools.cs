using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>What a mod's tool needs from the SPT server, when this app knows.</summary>
public enum ModToolNeed
{
    // Not known: it opens with no note.
    None,

    // It talks to the running server (give-ui).
    ServerRunning,

    // It changes what the server reads when it starts, so the server should be down (SVM's Greed).
    ServerStopped,
}

/// <param name="FullPath">Where the .exe is now - in a ".disabled" container while its mod is disabled.</param>
/// <param name="Key">The .exe's install-relative path as it reads with its mod enabled, lowercased:
/// what a hidden tool is remembered by, so disabling and enabling the mod doesn't lose it.</param>
/// <param name="ModFolder">The folder Open folder shows: the mod's server folder when it has one
/// (where SVM keeps its presets), else the folder the .exe is in.</param>
public sealed record ModTool(
    string FullPath,
    string Key,
    string ModName,
    string ModFolder,
    bool IsModDisabled,
    ModToolNeed Need,
    bool IsHidden)
{
    /// <summary>"Greed" for Greed.exe.</summary>
    public string Name => Path.GetFileNameWithoutExtension(FullPath);
}

//
// Fork (SSPTMM): the Play page's "Mod tools" - every .exe an installed mod put in the SPT folder,
// found from what this app recorded installing (SVM's Greed.exe, give-ui's app) and, for a mod
// installed by hand, from the .exe files inside its own mod folder.
//
// Nothing is guessed about whether an .exe is meant to be opened: every one is listed, and one that
// isn't a tool is hidden by the user (AppSettings.HiddenModTools).
//
public static class ModTools
{
    // How deep and how many .exe files are looked for in a hand-installed mod's folder - a mod folder
    // is small; these only stop a pathological one costing a slow Play page.
    private const int MaxDepth = 4;
    private const int MaxPerMod = 10;

    //
    // What the known tools need, by .exe name, from each one's own instructions:
    // give-ui's mod page: "With the server and preferably with T***** running, open the app".
    // SVM's Greed.exe (its built-in help, per the 2026-10-03 research): save and apply with the server
    // stopped; the server reads the chosen preset when it starts.
    //
    private static readonly (string Pattern, ModToolNeed Need)[] Known =
    [
        ("give-ui*.exe", ModToolNeed.ServerRunning),
        ("greed.exe", ModToolNeed.ServerStopped),
    ];

    public static ModToolNeed NeedFor(string exePath)
    {
        var name = Path.GetFileName(exePath);
        foreach (var (pattern, need) in Known)
        {
            if (Matches(name, pattern)) return need;
        }

        return ModToolNeed.None;
    }

    public static string KeyFor(string installRelative) =>
        DisabledModPaths.ToEnabledRelativePath(installRelative).ToLowerInvariant();

    /// <param name="records">Every install record; only this install's are used.</param>
    /// <param name="scanned">The install's mods as the scanner found them, for hand installs.</param>
    /// <param name="hidden">Keys (see <see cref="ModTool.Key"/>) the user hid.</param>
    public static List<ModTool> Find(
        string installPath,
        IEnumerable<InstalledModRecord> records,
        IEnumerable<InstalledMod> scanned,
        IReadOnlyCollection<string> hidden)
    {
        var hiddenKeys = new HashSet<string>(hidden.Select(h => h.ToLowerInvariant()));
        var tools = new List<ModTool>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stamp = InstallStamp.Of(installPath);

        // What this app installed.
        foreach (var record in records)
        {
            if (record.InstallPath is { } stamped && !string.Equals(stamped, stamp, StringComparison.OrdinalIgnoreCase)) continue;

            var exes = record.Files.Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            if (exes.Count == 0) continue;

            var disabled = IsRecordDisabled(installPath, record);
            var folder = FolderToOpen(installPath, record);

            foreach (var exe in exes)
            {
                if (Locate(installPath, exe) is not { } full) continue;

                var key = KeyFor(exe);
                if (!seen.Add(key)) continue;

                tools.Add(new ModTool(full, key, record.Name, folder ?? Path.GetDirectoryName(full)!, disabled, NeedFor(full), hiddenKeys.Contains(key)));
            }
        }

        // What was installed by hand: .exe files inside the mod's own folder.
        foreach (var mod in scanned)
        {
            if (!Directory.Exists(mod.FolderPath)) continue;

            foreach (var full in ExesIn(mod.FolderPath))
            {
                var key = KeyFor(Path.GetRelativePath(installPath, full).Replace('\\', '/'));
                if (!seen.Add(key)) continue;

                tools.Add(new ModTool(full, key, mod.Name, mod.FolderPath, mod.IsDisabled, NeedFor(full), hiddenKeys.Contains(key)));
            }
        }

        return [.. tools
            .OrderBy(t => t.ModName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // A recorded file where it is now: its own path, or the same path in its container's ".disabled"
    // sibling while the mod is disabled. Null when it is neither - removed by hand.
    private static string? Locate(string installPath, string relative)
    {
        var full = Full(installPath, relative);
        if (File.Exists(full)) return full;

        if (DisabledModPaths.TryGetRelativeCounterpart(relative, out var counterpart))
        {
            var moved = Full(installPath, counterpart);
            if (File.Exists(moved)) return moved;
        }

        return null;
    }

    //
    // Disabled when none of the record's files in a mod container is where it was placed and some are
    // in that container's ".disabled" sibling. A tool outside every container (SVM's Greed.exe, in the
    // game folder) is never moved, so this is how it follows its mod being disabled.
    //
    private static bool IsRecordDisabled(string installPath, InstalledModRecord record)
    {
        var anyMoved = false;

        foreach (var file in record.Files)
        {
            if (!DisabledModPaths.TryFindRelativeContainer(file, out _, out _)) continue;

            if (File.Exists(Full(installPath, file))) return false;

            if (DisabledModPaths.TryGetRelativeCounterpart(file, out var counterpart) && File.Exists(Full(installPath, counterpart)))
                anyMoved = true;
        }

        return anyMoved;
    }

    // The mod's server folder (user\mods\<folder>) when it has one, else its first mod folder of any
    // kind - wherever it is now. Null when it has none, and the .exe's own folder is used instead.
    private static string? FolderToOpen(string installPath, InstalledModRecord record)
    {
        var folders = record.Files
            .Select(InstallPathGuard.ModFolderOf)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f.Contains("/user/mods", StringComparison.OrdinalIgnoreCase) || f.StartsWith("user/mods", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

        foreach (var folder in folders)
        {
            var full = Full(installPath, folder);
            if (Directory.Exists(full)) return full;

            if (DisabledModPaths.TryGetRelativeCounterpart(folder, out var counterpart) && Directory.Exists(Full(installPath, counterpart)))
                return Full(installPath, counterpart);
        }

        return null;
    }

    private static IEnumerable<string> ExesIn(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.exe", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = MaxDepth,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }).Take(MaxPerMod).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Full(string installPath, string relative) =>
        Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));

    // "*" at the end only - all the known patterns need.
    private static bool Matches(string name, string pattern) =>
        pattern.EndsWith("*.exe", StringComparison.Ordinal)
            ? name.StartsWith(pattern[..^5], StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            : string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
}
