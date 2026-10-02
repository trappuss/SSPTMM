using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Finds the config files an SPT install's mods actually have, and ties each one back to the
// installed mod it belongs to.
//
// The two halves are found in completely different ways, which is why they read differently in the
// UI. A server mod's config sits inside its own folder, so attribution is exact - the file is
// simply in there. A client mod's sits in the shared BepInEx\config folder, named after the
// plugin's GUID, so it has to be matched back to a mod: InstalledModScanner already reads the real
// [BepInPlugin] GUID off each plugin DLL, and matching a file's name against that is exact too. The
// folder-name tiers below only exist for the minority of plugins that name their config file after
// themselves rather than their GUID.
//
// Deliberately separate from ModConfigFiles, which answers a narrower question for the uninstall
// path (which files to rescue when a server mod is deleted) and is not changed here - widening its
// rule would silently change what removal preserves.
//
public static class ModConfigDiscovery
{
    // Skipped when walking a server mod for config folders - large, and never where settings live.
    private static readonly string[] SkippedFolderNames = ["node_modules", ".git"];

    // How far into a server mod's folder to look for a config folder. Deep enough for the layouts
    // mods actually use (src/config, dist/config), shallow enough not to walk a whole database tree.
    private const int MaxServerDepth = 6;

    //
    // Every config file in the install, ordered for display: mods first in name order, then the
    // framework's own files, then anything unclaimed.
    //
    // <param name="scanned">The current scan. Passed in rather than re-scanned so a config file is
    // attributed to exactly the mods the rest of the app believes are installed, including disabled
    // ones - a disabled server mod's FolderPath already points into user\mods.disabled, so its
    // config is found there with no special casing.</param>
    //
    // <param name="options">Per-mod entries from mod_configs.json - the extra settings files a mod
    // keeps somewhere unconventional, and the data sitting in a folder called "config" that is not a
    // config at all. Read once by the caller; omitted, the conventions answer alone.</param>
    //
    public static List<ModConfigEntry> Find(
        string installPath,
        IReadOnlyList<InstalledMod> scanned,
        IReadOnlyDictionary<string, ModConfigOptions>? options = null)
    {
        var entries = new List<ModConfigEntry>();

        entries.AddRange(FindClientConfigs(installPath, scanned));

        foreach (var mod in scanned.Where(m => m.Target == InstalledModTarget.Server))
            entries.AddRange(FindServerConfigs(installPath, mod, ModConfigFiles.EntryForFolder(mod.Name, options)));

        return entries
            .OrderBy(e => SourceRank(e.Source))
            .ThenBy(e => e.ModName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.DisplayPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Mods before the framework's own files, and anything unclaimed last.
    private static int SourceRank(ModConfigSource source) => source switch
    {
        ModConfigSource.Client => 0,
        ModConfigSource.Server => 0,
        ModConfigSource.Framework => 1,
        _ => 2,
    };

    // The BepInEx\config folder, which is shared by every client plugin rather than owned by any of them.
    public static string ClientConfigFolder(string installPath) =>
        Path.Combine(installPath, "BepInEx", "config");

    private static List<ModConfigEntry> FindClientConfigs(string installPath, IReadOnlyList<InstalledMod> scanned)
    {
        var results = new List<ModConfigEntry>();
        var folder = ClientConfigFolder(installPath);
        if (!Directory.Exists(folder)) return results;

        // Built once rather than per file: a large install has hundreds of plugins and BepInEx\config
        // holds one file per plugin that has ever run.
        var byGuid = new Dictionary<string, InstalledMod>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedName = new Dictionary<string, List<InstalledMod>>(StringComparer.Ordinal);

        foreach (var mod in scanned)
        {
            // Every GUID the mod's folder registers, not just its primary one - a folder holding an
            // API or config-UI assembly alongside the plugin itself has a config file for each.
            // Client mods only: an SPT 4.x server half often declares the same GUID as its client
            // half, and a BepInEx config file is always the client's.
            if (mod.Target == InstalledModTarget.Client)
                foreach (var guid in mod.AllGuids) byGuid.TryAdd(guid, mod);

            var normalized = Normalize(mod.Name);
            if (normalized.Length == 0) continue;

            if (!byNormalizedName.TryGetValue(normalized, out var list)) byNormalizedName[normalized] = list = [];
            if (!list.Any(m => ReferenceEquals(m, mod))) list.Add(mod);
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.cfg", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't read {folder}: {ex.Message}");
            return results;
        }

        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file);

            if (IsFrameworkConfig(stem))
            {
                results.Add(Entry(installPath, file, ModConfigFormat.BepInExCfg, ModConfigSource.Framework));
                continue;
            }

            var match = MatchClientConfig(folder, file, stem, byGuid, byNormalizedName);

            results.Add(match is null
                ? Entry(installPath, file, ModConfigFormat.BepInExCfg, ModConfigSource.Unmatched)
                : Entry(installPath, file, ModConfigFormat.BepInExCfg, ModConfigSource.Client) with
                {
                    ModName = match.Value.Mod.Name,
                    ModGuid = match.Value.Guid,
                });
        }

        return results;
    }

    //
    // The installed mod a BepInEx config file belongs to, or null when nothing claims it.
    //
    // Three tiers, in order of how much they can be trusted. The GUID is exact - it is the name
    // BepInEx itself gives the file, read back off the plugin DLL it came from. The two name tiers
    // are for plugins that name their file after themselves, and both require exactly one candidate:
    // attributing a config to the wrong mod would have someone editing settings that belong to
    // something else entirely, which is worse than leaving the file unclaimed.
    //
    private static ClientMatch? MatchClientConfig(
        string configFolder,
        string file,
        string stem,
        Dictionary<string, InstalledMod> byGuid,
        Dictionary<string, List<InstalledMod>> byNormalizedName)
    {
        // The file's own name is the GUID that wrote it, so that GUID is reported rather than the
        // mod's primary one - in a folder registering several, they are not the same thing.
        if (byGuid.TryGetValue(stem, out var byGuidMatch)) return new ClientMatch(byGuidMatch, stem);

        if (OnlyMatch(byNormalizedName, stem) is { } byName) return new ClientMatch(byName, byName.Guid);

        // A plugin that keeps several .cfg files puts them in a subfolder of BepInEx\config named
        // after itself. Only the folder directly under BepInEx\config is considered - anything
        // deeper is that plugin's own arrangement, not another mod's name.
        var parent = Path.GetDirectoryName(file);
        if (parent is null || PathsEqual(parent, configFolder)) return null;

        var grandparent = Path.GetDirectoryName(parent);
        if (grandparent is null || !PathsEqual(grandparent, configFolder)) return null;

        var folderName = Path.GetFileName(parent);

        if (byGuid.TryGetValue(folderName, out var folderGuidMatch)) return new ClientMatch(folderGuidMatch, folderName);

        return OnlyMatch(byNormalizedName, folderName) is { } byFolderName
            ? new ClientMatch(byFolderName, byFolderName.Guid)
            : null;
    }

    // The mod a config file was attributed to, and the GUID it was attributed by.
    private readonly record struct ClientMatch(InstalledMod Mod, string? Guid);

    private static InstalledMod? OnlyMatch(Dictionary<string, List<InstalledMod>> byNormalizedName, string candidate)
    {
        var normalized = Normalize(candidate);
        if (normalized.Length == 0) return null;

        return byNormalizedName.TryGetValue(normalized, out var mods) && mods.Count == 1 ? mods[0] : null;
    }

    //
    // BepInEx's own config files rather than a mod's - BepInEx.cfg itself, and the handful of
    // first-party plugins that ship with it (the configuration manager, the console). Checked before
    // any mod matching so one of these is never attributed to a mod whose name happens to normalize
    // the same way.
    //
    private static bool IsFrameworkConfig(string stem) =>
        stem.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
        || stem.StartsWith("com.bepis.", StringComparison.OrdinalIgnoreCase)
        || stem.StartsWith("com.bepinex.", StringComparison.OrdinalIgnoreCase);

    private static List<ModConfigEntry> FindServerConfigs(string installPath, InstalledMod mod, ModConfigOptions? options)
    {
        var results = new List<ModConfigEntry>();
        if (!Directory.Exists(mod.FolderPath)) return results;

        foreach (var file in ServerConfigFiles(installPath, mod.FolderPath, options))
        {
            results.Add(Entry(installPath, file, ModConfigFormat.Json, ModConfigSource.Server) with
            {
                ModName = mod.Name,
                IsModDisabled = mod.IsDisabled,
            });
        }

        return results;
    }

    //
    // The config files in one server mod's folder: the conventionally-named ones sitting at its root,
    // plus everything inside any folder it calls "config".
    //
    // Both sets are gated through ModConfigFiles.IsServerModConfig rather than judged here, so the
    // Configs page lists exactly the files uninstalling the mod would preserve. The walk below only
    // decides which files are worth asking about - it is a way of not enumerating a mod's whole data
    // folder, not a second opinion on what a config is.
    //
    private static IEnumerable<string> ServerConfigFiles(string installPath, string modFolder, ModConfigOptions? options)
    {
        // A file can be reached twice - once by a walk and once by being named in the mod's entry - and
        // the page must not list it twice.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in SafeEnumerateFiles(modFolder))
        {
            if (ModConfigFiles.IsServerModConfig(Relative(installPath, file), options) && seen.Add(file))
                yield return file;
        }

        foreach (var folder in ConfigFoldersIn(modFolder))
        {
            foreach (var file in SafeEnumerateFiles(folder, SearchOption.AllDirectories))
            {
                if (ModConfigFiles.IsServerModConfig(Relative(installPath, file), options) && seen.Add(file))
                    yield return file;
            }
        }

        //
        // Files the mod's own entry names, which no walk above would have reached: SVM keeps the preset
        // it is running in Loader\loader.json, in a folder called nothing in particular.
        //
        foreach (var named in options?.Settings ?? [])
        {
            if (ModConfigPaths.Normalise(named) is not { } clean) continue;

            var path = Path.Combine(modFolder, clean.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(path)
                && ModConfigFiles.IsServerModConfig(Relative(installPath, path), options)
                && seen.Add(path))
            {
                yield return path;
            }
        }
    }

    //
    // Folders named "config" (or a variant) anywhere in a mod, found by walking breadth-first to a
    // bounded depth rather than enumerating the whole folder. A server mod's own data - item
    // templates, bot presets, locale tables - can run to thousands of JSON files, and none of it
    // should be walked to find a settings file.
    //
    private static IEnumerable<string> ConfigFoldersIn(string modFolder)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((modFolder, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= MaxServerDepth) continue;

            foreach (var child in SafeEnumerateDirectories(current))
            {
                var name = Path.GetFileName(child);
                if (SkippedFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                if (ModConfigFiles.IsConfigFolderName(name))
                {
                    // Not queued as well: everything under it is already collected wholesale.
                    yield return child;
                    continue;
                }

                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static ModConfigEntry Entry(string installPath, string file, ModConfigFormat format, ModConfigSource source) =>
        new()
        {
            FullPath = file,
            DisplayPath = Relative(installPath, file),
            FileName = Path.GetFileName(file),
            Format = format,
            Source = source,
        };

    private static string Relative(string installPath, string file)
    {
        try
        {
            return Path.GetRelativePath(installPath, file).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return file.Replace('\\', '/');
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string folder, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", option).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't read {folder}: {ex.Message}");
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    // Same normalization the catalog matching uses: letters and digits only, lowercased, so
    // "SAIN-Presets" and "sain presets" are the same name.
    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
