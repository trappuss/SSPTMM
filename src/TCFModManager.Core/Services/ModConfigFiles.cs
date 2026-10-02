using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// What counts as one of a server mod's own config files, and how to move those out of an install
// instead of deleting them along with the rest of the mod.
//
// The rule here is the single one both callers share: uninstalling a mod reads it to decide what to
// keep, and ModConfigDiscovery gates every file it finds through it, so the Configs page and the
// removal path can never end up disagreeing about what a config is.
//
// Client mods keep their config in BepInEx\config, outside the mod folder. An archive that ships one
// does not get to overwrite the user's, and a removal leaves it where it is - see IsBepInExConfig.
//
public static class ModConfigFiles
{
    // Matched anywhere in a path, since server content is remapped under the install's own server
    // root (e.g. "SPT_Runtime/user/mods/..."). The ".disabled" sibling is recognised too, so a
    // disabled mod's config is still identified as one.
    private static readonly string[] ServerModsContainers =
        ["user/mods/", $"user/mods{DisabledModPaths.DisabledSuffix}/"];

    // Folder names a server mod keeps its config in, matched at any depth below the mod's own folder.
    private static readonly string[] ConfigFolderNames = ["config", "configs", "cfg"];

    //
    // JSON5 and JSONC are at least as common as plain JSON in SPT server mods - most of them ship a
    // config full of comments explaining each setting - and a file the mod itself reads happily is
    // one of its configs whatever extension it was given.
    //
    private static readonly string[] ConfigExtensions = [".json", ".json5", ".jsonc"];

    //
    // Names that count as a config when the file sits directly in a mod's own folder, where there is
    // no config folder to go by. This is the most common layout of all, but it is also the one that
    // has to be judged by name: a server mod's root holds bundle manifests, build output and shipped
    // data alongside its config, and only the file actually named after the job is one.
    //
    private static readonly string[] RootConfigStems = ["config", "configs", "cfg", "settings"];

    // Never a config when it sits in a mod's root: package.json is the mod's own manifest, and the
    // lock file beside it is generated.
    private static readonly string[] NeverConfigFileNames = ["package.json", "package-lock.json"];

    //
    // True for a file in BepInEx\config - a client plugin's settings. BepInEx writes it the first time
    // the plugin runs and keeps it from then on, so the copy on disk is the user's: an archive's copy
    // never goes over one that is already there, unless it is the copy this app placed, untouched
    // (see ConfigCarryOver.Prepare), and a removal leaves one that changed where it is.
    //
    public static bool IsBepInExConfig(string relativePath) =>
        relativePath.Replace('\\', '/').StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase);

    // True for a folder name a server mod would keep its config in.
    public static bool IsConfigFolderName(string name) =>
        ConfigFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    //
    // True for an install-relative path that is one of a server mod's own config files. Two layouts
    // count, and between them they cover what mods actually do:
    //
    //   <mod>/config/settings.json  - anything JSON inside a folder the mod calls "config"
    //   <mod>/config.json           - a conventionally-named file sitting at the mod's root
    //
    // Plus whatever the mod's own entry in mod_configs.json says, which is the only way a mod that
    // matches no convention is ever recognised - and the only way something that only looks like a
    // config is ever ruled out. Passing no options answers on the conventions alone.
    //
    // ONE rule, read by everything: the Configs page lists exactly what an update carries and what a
    // removal rescues, so the three can never end up disagreeing.
    //
    public static bool IsServerModConfig(string relativePath, ModConfigOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var path = relativePath.Replace('\\', '/');
        if (!HasConfigExtension(path)) return false;

        if (SegmentsBelowServerMods(path) is not { } segments) return false;

        if (options is not null && segments.Length > 1)
        {
            var inMod = string.Join('/', segments[1..]);

            // Data pretending to be settings loses before anything else is asked.
            if (ModConfigPaths.Covers(options.Exclude, inMod)) return false;

            // A user-data folder holds documents, not settings. It is preserved and rescued, never
            // merged, so it is not one of these.
            if (ModConfigPaths.Covers(options.UserData, inMod)) return false;

            if (ModConfigPaths.Covers(options.Settings, inMod)) return true;
        }

        // <mod folder>/<file> - judged by the file's own name, since a mod's root is full of JSON
        // that isn't settings.
        if (segments.Length == 2)
        {
            var fileName = segments[1];

            return !NeverConfigFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
                && RootConfigStems.Contains(StemOf(fileName), StringComparer.OrdinalIgnoreCase);
        }

        // <mod folder>/.../<config folder>/<file> - at least a mod folder, a config folder and the
        // file itself, and the config folder has to be below the mod folder rather than be it.
        if (segments.Length < 3) return false;

        return segments[1..^1].Any(IsConfigFolderName);
    }

    // The path's segments below the server mods container, or null when it isn't under one.
    private static string[]? SegmentsBelowServerMods(string path)
    {
        // The live container is looked for first. Neither string is a prefix of the other once the
        // trailing separator is counted ("user/mods/" against "user/mods.disabled/"), so a path
        // under one is never taken for a path under the other.
        foreach (var container in ServerModsContainers)
        {
            var index = path.IndexOf(container, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            return path[(index + container.Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        return null;
    }

    private static bool HasConfigExtension(string path) =>
        ConfigExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // "config.json" -> "config". Note this also means "config.default.json" -> "config.default",
    // which deliberately isn't a config: a file shipped as a pristine copy of the defaults is not
    // the one being edited.
    private static string StemOf(string fileName) => Path.GetFileNameWithoutExtension(fileName);

    //
    // True for a path inside a folder the mod's entry names as user data - whole documents the user
    // wrote. Never merged and never replaced by an update; rescued by a removal like a config.
    //
    public static bool IsUserData(string relativePath, ModConfigOptions? options)
    {
        if (options is null || options.UserData.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var path = relativePath.Replace('\\', '/');
        if (SegmentsBelowServerMods(path) is not { } segments || segments.Length < 2) return false;

        return ModConfigPaths.Covers(options.UserData, string.Join('/', segments[1..]));
    }

    // The mod folder an install-relative path sits in, or null when it isn't under user/mods at all.
    public static string? ServerModFolderOf(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var segments = SegmentsBelowServerMods(relativePath.Replace('\\', '/'));
        return segments is { Length: > 0 } ? segments[0] : null;
    }

    // The entry for whichever mod a path belongs to, out of a whole set read once.
    public static ModConfigOptions? OptionsFor(
        string relativePath,
        IReadOnlyDictionary<string, ModConfigOptions>? options) =>
        ServerModFolderOf(relativePath) is { } folder ? EntryForFolder(folder, options) : null;

    // The entry for a mod folder name.
    public static ModConfigOptions? EntryForFolder(
        string? folderName,
        IReadOnlyDictionary<string, ModConfigOptions>? options)
    {
        if (options is null || options.Count == 0 || string.IsNullOrWhiteSpace(folderName)) return null;

        return options.TryGetValue(ModConfigOptionsStore.KeyFor(folderName), out var found) ? found : null;
    }

    // The install-relative config paths in a record's file list.
    public static List<string> InRecord(
        InstalledModRecord record,
        IReadOnlyDictionary<string, ModConfigOptions>? options = null) =>
        [.. record.Files.Where(f => IsServerModConfig(f, OptionsFor(f, options)))];

    // The user-data paths in a record's file list - the ones an update leaves alone.
    public static List<string> UserDataInRecord(
        InstalledModRecord record,
        IReadOnlyDictionary<string, ModConfigOptions>? options) =>
        [.. record.Files.Where(f => IsUserData(f, OptionsFor(f, options)))];

    //
    // Every file on disk inside the user-data folders of the mod's own folders, whether or not the
    // record lists it - SVM's presets are written by its own generator and are in no record, and they
    // are exactly what a removal has to rescue.
    //
    // Bounded: a folder holding more files than a person would ever have authored is data that got
    // opted in by mistake, and walking it is the cost this refuses to pay.
    //
    public static List<string> UserDataOnDisk(
        string installPath,
        InstalledModRecord record,
        IReadOnlyDictionary<string, ModConfigOptions>? options)
    {
        var results = new List<string>();
        if (options is null || options.Count == 0) return results;

        foreach (var prefix in ServerModPrefixes(record))
        {
            if (EntryForFolder(Path.GetFileName(prefix), options) is not { } entry) continue;

            foreach (var relative in entry.UserData)
            {
                if (ModConfigPaths.Normalise(relative) is not { } clean) continue;

                if (InstallPathGuard.CheckRecordedPath(installPath, $"{prefix}/{clean}", out var folder) is { } refusal)
                {
                    AppLog.Warn("Configs", $"not reading {prefix}/{clean} ({refusal})");
                    continue;
                }

                if (!Directory.Exists(folder) || InstallPathGuard.ContainsLink(folder)) continue;

                try
                {
                    var found = Directory
                        .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                        .Take(MaxUserDataFiles + 1)
                        .ToList();

                    if (found.Count > MaxUserDataFiles)
                    {
                        AppLog.Warn("Configs",
                            $"{prefix}/{clean} holds more than {MaxUserDataFiles} files; not treating it as user data");
                        continue;
                    }

                    var relatives = found
                        .Select(f => Path.GetRelativePath(installPath, f).Replace('\\', '/'))
                        .ToList();

                    results.AddRange(relatives.Where(r => !results.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn("Configs", $"couldn't read {folder}: {ex.Message}");
                }
            }
        }

        return results;
    }

    // Past this a folder is a database rather than somebody's presets. Also what the UI refuses to
    // opt in, so nobody arrives here by accident.
    public const int MaxUserDataFiles = 500;

    //
    // The install-relative path of each server mod folder a record placed files in, e.g.
    // "SPT/user/mods/[SVM] Server Value Modifier".
    //
    private static IEnumerable<string> ServerModPrefixes(InstalledModRecord record)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in record.Files)
        {
            var path = file.Replace('\\', '/');

            foreach (var container in ServerModsContainers)
            {
                var index = path.IndexOf(container, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;

                var after = path[(index + container.Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (after.Length < 2) break;

                var prefix = path[..(index + container.Length)] + after[0];
                if (seen.Add(prefix)) yield return prefix;

                break;
            }
        }
    }

    private static string ToNative(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);

    //
    // Config files inside a mod folder on disk, as install-relative paths. Used by the
    // hand-installed removal path, where there's no record to read a file list from.
    //
    public static List<string> InFolder(
        string installPath,
        string modFolderPath,
        IReadOnlyDictionary<string, ModConfigOptions>? options = null)
    {
        var results = new List<string>();
        if (!Directory.Exists(modFolderPath)) return results;

        try
        {
            // "*.json*" rather than "*.json" so JSON5 and JSONC are seen too, and rather than "*" so
            // the walk isn't handed every file in a mod's data folder just to reject them one by one.
            foreach (var file in Directory.EnumerateFiles(modFolderPath, "*.json*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(installPath, file).Replace('\\', '/');
                if (IsServerModConfig(relative, OptionsFor(relative, options))) results.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort: a folder that can't be walked just reports no configs to keep.
        }

        return results;
    }

    //
    // Moves <paramref name="relativeFiles"/> out of the install into a timestamped folder under
    // AppPaths.LegacyConfigsDirectory, keeping each file's path relative to the install root so the
    // whole tree can be copied back over an SPT install to restore it. Returns the folder they were
    // moved into, with the count actually moved.
    //
    public static KeptConfigs MoveOut(string installPath, IEnumerable<string> relativeFiles, string modName, DateTimeOffset timestamp)
    {
        var destinationRoot = ArchiveFolder(AppPaths.LegacyConfigsDirectory, modName, timestamp);

        var moved = 0;

        foreach (var relative in relativeFiles)
        {
            // Moving a file out of the install is as final for the install as deleting it (D13).
            if (InstallPathGuard.CheckRecordedPath(installPath, relative, out var source) is { } refusal)
            {
                AppLog.Warn("Configs", $"left {relative} in place ({refusal})");
                continue;
            }

            if (!File.Exists(source)) continue;

            var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                var destinationDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDir)) Directory.CreateDirectory(destinationDir);

                File.Move(source, destination, overwrite: true);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Configs", $"couldn't keep {relative}: {ex.Message}");
            }
        }

        if (moved == 0) return new KeptConfigs(0, null);

        AppLog.Info("Configs", $"kept {moved} config file(s) from {modName} in {destinationRoot}");
        return new KeptConfigs(moved, destinationRoot);
    }

    // <root>\<yyyyMMdd-HHmmss>_<mod> - one folder per removal or update.
    internal static string ArchiveFolder(string root, string modName, DateTimeOffset timestamp) =>
        Path.Combine(root, $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}_{SafeFolderName(modName)}");

    private static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "mod" : cleaned;
    }
}

// How many config files were moved out of the install, and the folder holding them.
public sealed record KeptConfigs(int Count, string? Folder);
