using System.Text.Json;
using System.Text.Json.Serialization;

namespace TCFModManager.Core.Services;

// What an update does with one mod's config files when the user has changed them.
public enum ModConfigPolicy
{
    // Start from the new version's defaults and carry the user's changed values into them.
    Merge,

    // Leave the user's file exactly as it is.
    KeepMine,

    // Take the new version's file. The user's copy is archived, as it always is.
    TakeNew,
}

//
// Per-mod config settings, in Data\mod_configs.json, keyed by the mod's folder name lowercased - the
// same key rule as ModGroupStore, with the same accepted tradeoff that renaming the folder loses the
// entry.
//
// Deliberately not on InstalledModRecord: a record is rewritten by every install, and a choice about
// how to treat somebody's config has to outlive an uninstall and reinstall.
//
public sealed class ModConfigOptionsStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "mod_configs.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath => _filePath;

    public static string KeyFor(string folderName) => folderName.Trim().ToLowerInvariant();

    //
    // The stored entries with the built-in seeds filled in for mods the file says nothing about.
    //
    // What everything judging a path should read. A seeded entry is a DEFAULT, not a rule: the moment
    // the user changes anything about that mod an entry is written for it, and the stored entry wins
    // from then on - including when what they did was empty the lists.
    //
    public Dictionary<string, ModConfigOptions> Effective()
    {
        var stored = Load();

        foreach (var (key, seed) in ModConfigSeeds.All)
            if (!stored.ContainsKey(key)) stored[key] = seed.Copy();

        return stored;
    }

    public Dictionary<string, ModConfigOptions> Load()
    {
        if (!File.Exists(_filePath)) return new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, ModConfigOptions>>(
                File.ReadAllText(_filePath), Options);

            return read is null
                ? new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ModConfigOptions>(read, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A hand-edited file that no longer parses means every mod is back on the default, which
            // is what it was before anybody set anything.
            AppLog.Warn("Configs", $"couldn't read {_filePath}: {ex.Message}");
            if (ex is JsonException) SafeFile.PreserveDamaged(_filePath);
            return new Dictionary<string, ModConfigOptions>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // One mod's entry, seeds included. A mod nobody has said anything about answers with the defaults.
    public ModConfigOptions For(string folderName) =>
        Effective().TryGetValue(KeyFor(folderName), out var options) ? options : new ModConfigOptions();

    //
    // The policy for a mod, asked of every folder it occupies: a client+server mod is two folders with
    // one setting between them, and the answer has to be the same whichever one is asked about.
    //
    // The first folder that has an entry wins, rather than the strictest of them - two folders of one
    // mod disagreeing is not a state the UI can produce, and picking a winner silently is better than
    // an update that does something neither folder's entry asked for.
    //
    public ModConfigPolicy PolicyFor(IEnumerable<string> folderNames)
    {
        var stored = Effective();

        foreach (var folder in folderNames)
            if (stored.TryGetValue(KeyFor(folder), out var options)) return options.Policy;

        return ModConfigPolicy.Merge;
    }

    //
    // Sets one mod's policy. An entry that ends up saying nothing at all is removed rather than
    // stored, so the file only holds real choices - unless a seed exists for that mod, in which case
    // the empty entry has to stay to say the seed was cleared.
    //
    public void SetPolicy(string folderName, ModConfigPolicy policy) =>
        Update(folderName, options => options.Policy = policy);

    //
    // A folder inside the mod holding documents the user authored - SVM's presets. Never merged, left
    // exactly where it is by an update, rescued by a removal.
    //
    public void SetUserData(string folderName, IEnumerable<string> paths) =>
        Update(folderName, options => Replace(options.UserData, paths));

    // Extra settings files an update should carry across even though they match no convention.
    public void SetSettings(string folderName, IEnumerable<string> paths) =>
        Update(folderName, options => Replace(options.Settings, paths));

    // Files or folders that are data rather than settings, whatever they are called.
    public void SetExclude(string folderName, IEnumerable<string> paths) =>
        Update(folderName, options => Replace(options.Exclude, paths));

    private void Update(string folderName, Action<ModConfigOptions> change)
    {
        var stored = Load();
        var key = KeyFor(folderName);

        // Starts from the seed when there is one, so changing the policy of a seeded mod doesn't
        // quietly drop the locations that came with it.
        if (!stored.TryGetValue(key, out var options))
        {
            options = ModConfigSeeds.All.TryGetValue(key, out var seed) ? seed.Copy() : new ModConfigOptions();
            stored[key] = options;
        }

        change(options);

        if (options.SaysNothing && !ModConfigSeeds.All.ContainsKey(key)) stored.Remove(key);

        Save(stored);
    }

    private static void Replace(List<string> target, IEnumerable<string> paths)
    {
        target.Clear();

        foreach (var path in paths)
            if (ModConfigPaths.Normalise(path) is { } clean && !target.Contains(clean, StringComparer.OrdinalIgnoreCase))
                target.Add(clean);
    }

    public void Save(Dictionary<string, ModConfigOptions> options)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(options, Options), keepBackups: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't write {_filePath}: {ex.Message}");
        }
    }
}

//
// One mod's entry. Offered for hand-editing, which is why the enum is written as its name and the
// paths are plain strings relative to the mod's own folder.
//
public sealed class ModConfigOptions
{
    public ModConfigPolicy Policy { get; set; } = ModConfigPolicy.Merge;

    //
    // Folders inside the mod holding whole documents the user authored, not a tree of values to
    // reconcile - SVM's Presets. An update leaves them exactly as they are; a removal rescues them
    // like a config.
    //
    public List<string> UserData { get; init; } = [];

    //
    // Files that ARE settings but match no convention, so nothing would otherwise recognise them -
    // SVM's Loader/loader.json, which holds the selected preset and was being defaulted on every
    // update.
    //
    public List<string> Settings { get; init; } = [];

    //
    // Files or folders that look like config and are not: locale tables and barter lists living in a
    // folder literally called "config". Excluded from everything - the page, the merge and the rescue.
    //
    public List<string> Exclude { get; init; } = [];

    // True when the entry carries no choice at all, so storing it would say nothing.
    public bool SaysNothing =>
        Policy == ModConfigPolicy.Merge && UserData.Count == 0 && Settings.Count == 0 && Exclude.Count == 0;

    public ModConfigOptions Copy() => new()
    {
        Policy = Policy,
        UserData = [.. UserData],
        Settings = [.. Settings],
        Exclude = [.. Exclude],
    };
}

//
// Mods whose layout is known to need an entry, so they work out of the box.
//
// Every one of these is a DEFAULT the user can clear, not a hardcoded rule - see
// ModConfigOptionsStore.Effective. Keyed the same way the file is: the mod's folder name, lowercased.
//
public static class ModConfigSeeds
{
    public static readonly Dictionary<string, ModConfigOptions> All = new(StringComparer.OrdinalIgnoreCase)
    {
        //
        // SVM keeps the presets its own generator writes in Presets\, and which one is selected in
        // Loader\loader.json. The presets are untracked, so an update never deleted them - but
        // loader.json ships in the archive, matches no convention, and was replaced on every update,
        // which silently put the server back on the default preset.
        //
        ["[svm] server value modifier"] = new()
        {
            UserData = { "Presets" },
            Settings = { "Loader/loader.json" },
        },
    };
}

// The shape of a path inside a mod's folder, as an entry stores it.
public static class ModConfigPaths
{
    //
    // Forward slashes, no leading or trailing separator, and nothing that climbs out of the mod's own
    // folder. Null for anything that isn't usable, so a hand-edited file can't point the rescue or the
    // merge at the rest of the install.
    //
    public static string? Normalise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var cleaned = path.Replace('\\', '/').Trim().Trim('/');
        if (cleaned.Length == 0) return null;

        if (Path.IsPathRooted(cleaned)) return null;

        var segments = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or "..")) return null;

        return string.Join('/', segments);
    }

    //
    // Whether a path inside a mod's folder is, or is inside, one of the named entries. A file matches
    // itself; a folder matches everything under it.
    //
    public static bool Covers(IEnumerable<string> entries, string modRelativePath)
    {
        foreach (var entry in entries)
        {
            if (Normalise(entry) is not { } clean) continue;

            if (modRelativePath.Equals(clean, StringComparison.OrdinalIgnoreCase)) return true;
            if (modRelativePath.StartsWith(clean + "/", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
