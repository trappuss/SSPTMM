using System.Text.Json;

namespace TCFModManager.Core.Services;

/// <summary>One profile's items and traders that SPT's own database doesn't have - what mods added.</summary>
public sealed record ProfileModContent(string ProfileName, IReadOnlyDictionary<string, int> Items, IReadOnlySet<string> Traders);

/// <summary>What a profile would lose with one mod gone: how many items (counting every copy) and traders.</summary>
public sealed record ProfileLoss(string ProfileName, string ModName, int Items, int Traders);

//
// Fork (1.4.5): what a profile holds from a mod, read before the mod goes.
//
// A mod that adds items or traders leaves them in every profile that picked them up - in the
// stash, on the PMC, in insurance, in mail. With the mod gone SPT no longer knows them and won't
// load the profile, or, with its own clean-up on (SptProfileFixes), takes them out for good. So
// before a removal, a disable, a preset or a collection turns mods off, the install's profiles
// are read for that.
//
// What a mod added is found without running it: an item in a profile (any "_tpl") or a trader
// (the PMC's TradersInfo) that SPT's own database doesn't have is a mod's, and the mod is the one
// whose files carry that id - in its JSON, or as a string in its DLL (UTF-16 there). Measured on
// the user's SPT 4.1.6 install, 2026-10-29: 75 of the 76 modded items in three profiles were found
// in one mod's files. An id a mod makes up while it runs isn't in any file, and is never warned
// about.
//
// Only server mods (user\mods) are looked at, on both sides: SPT's database is the server's, so a
// client plugin can't add an item - and one naming a mod's items (measured: a weapon plugin's own
// files carry the ids of the guns its server half adds) would otherwise hide them.
//
// An id still in the files of a mod that stays is not counted as lost: something else may add it,
// and a mod that only names another's items (a loot or quest mod) would otherwise be warned about
// in place of the one that adds them. The cost is the other way round - a mod that only names an
// item keeps the warning from showing when the one adding it goes.
//
// Only reads. Profiles on another PC (a remote server, a Fika host elsewhere) are not seen.
//
public static class ProfileModItems
{
    // Files that are never searched: art, sound, video, archives and debug symbols - big, and an
    // item id is not written in them. HUNCH that nothing adds items through these alone.
    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bundle", ".png", ".jpg", ".jpeg", ".dds", ".bmp", ".gif", ".webp", ".tga", ".psd", ".ico",
        ".wav", ".ogg", ".mp3", ".mp4", ".webm", ".bank", ".pdb", ".zip", ".7z", ".rar",
    };

    // HUNCH: nothing a mod keeps its item definitions in is bigger than this.
    private const long LargestFileSearched = 64L * 1024 * 1024;

    private static readonly Dictionary<string, (DateTime Written, long Length, HashSet<string> Ids)> FileIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (DateTime Written, long Length, ProfileModContent? Content)> Profiles = new(StringComparer.OrdinalIgnoreCase);
    private static (string Path, DateTime Written, HashSet<string> Ids)? _baseItems;
    private static readonly Lock Gate = new();

    //
    // What each profile would lose with these mods gone. <paramref name="leaving"/> is each mod by
    // name with its folders (or loose files); everything else in the install's mod folders stays.
    // Empty when the install has no profiles, or SPT's own item list can't be read.
    //
    public static IReadOnlyList<ProfileLoss> Check(string? installPath, IReadOnlyList<(string Mod, IReadOnlyList<string> Paths)> leaving)
    {
        if (leaving.Count == 0 || string.IsNullOrWhiteSpace(installPath)) return [];

        var profiles = Read(installPath);
        if (profiles.Count == 0 || profiles.All(p => p.Items.Count == 0 && p.Traders.Count == 0)) return [];

        var containers = DisabledModPaths.ServerContainers(installPath).Select(Full).ToList();
        bool OnServer(string path) => containers.Any(c => Inside(path, c));

        var gone = leaving.SelectMany(l => l.Paths).Select(Full).Where(OnServer).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var staying = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ServerModEntries(containers))
        {
            if (!gone.Any(g => Same(g, entry) || Inside(entry, g))) staying.UnionWith(IdsIn(entry));
        }

        var mods = leaving
            .Select(l => (l.Mod, Ids: (IReadOnlySet<string>)l.Paths.Select(Full).Where(OnServer).SelectMany(IdsIn).ToHashSet(StringComparer.Ordinal)))
            .ToList();
        return Losses(profiles, mods, staying);
    }

    // The arithmetic of Check, apart from the disk.
    public static IReadOnlyList<ProfileLoss> Losses(
        IReadOnlyList<ProfileModContent> profiles,
        IReadOnlyList<(string Mod, IReadOnlySet<string> Ids)> leaving,
        IReadOnlySet<string> staying)
    {
        var losses = new List<ProfileLoss>();
        foreach (var profile in profiles)
        {
            foreach (var (mod, ids) in leaving)
            {
                var items = profile.Items.Where(i => ids.Contains(i.Key) && !staying.Contains(i.Key)).Sum(i => i.Value);
                var traders = profile.Traders.Count(t => ids.Contains(t) && !staying.Contains(t));
                if (items > 0 || traders > 0) losses.Add(new ProfileLoss(profile.ProfileName, mod, items, traders));
            }
        }

        return losses;
    }

    // Each profile's modded items and traders. Empty when there are no profiles, or SPT's item list
    // isn't where SPT 4 or SPT 3 keep it - without it nothing can be told apart.
    public static IReadOnlyList<ProfileModContent> Read(string installPath)
    {
        if (ProfileBackups.ProfilesFolder(installPath) is not { } folder) return [];
        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return [];

        var database = Path.Combine(installPath, serverRoot, "SPT_Data", "database");
        if (!Directory.Exists(database)) database = Path.Combine(installPath, serverRoot, "SPT_Data", "Server", "database");
        if (BaseItems(Path.Combine(database, "templates", "items.json")) is not { } baseItems) return [];

        var tradersFolder = Path.Combine(database, "traders");
        var baseTraders = Directory.Exists(tradersFolder)
            ? Directory.EnumerateDirectories(tradersFolder).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var found = new List<ProfileModContent>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (ReadProfile(file, baseItems, baseTraders) is { } content) found.Add(content);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Profiles", $"couldn't list {folder}: {ex.Message}");
        }

        return found;
    }

    internal static ProfileModContent? FromJson(JsonElement root, IReadOnlySet<string> baseItems, IReadOnlySet<string> baseTraders)
    {
        if (SptLocalProfiles.FromJson(root) is not { } mini) return null;

        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        CountTemplates(root, items);
        foreach (var known in items.Keys.Where(baseItems.Contains).ToList()) items.Remove(known);

        var traders = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("characters", out var characters) && characters.ValueKind == JsonValueKind.Object
            && characters.TryGetProperty("pmc", out var pmc) && pmc.ValueKind == JsonValueKind.Object
            && pmc.TryGetProperty("TradersInfo", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            foreach (var trader in info.EnumerateObject())
            {
                if (IsId(trader.Name) && !baseTraders.Contains(trader.Name)) traders.Add(trader.Name);
            }
        }

        return new ProfileModContent(mini.Nickname.Length == 0 ? mini.Username : mini.Nickname, items, traders);
    }

    // Every "_tpl" anywhere in the profile, counted.
    private static void CountTemplates(JsonElement element, Dictionary<string, int> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "_tpl" && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } tpl)
                        into[tpl] = into.GetValueOrDefault(tpl) + 1;
                    else
                        CountTemplates(property.Value, into);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) CountTemplates(item, into);
                break;
        }
    }

    //
    // The ids written in a file or anywhere in a folder: every run of exactly 24 hex digits, as
    // bytes (JSON, scripts) and as UTF-16 (strings in a .NET DLL). Remembered per file until it
    // changes.
    //
    public static IReadOnlySet<string> IdsIn(string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(path))
            {
                ids.UnionWith(IdsInFile(path));
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) ids.UnionWith(IdsInFile(file));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Profiles", $"couldn't search {path}: {ex.Message}");
        }

        return ids;
    }

    private static HashSet<string> IdsInFile(string file)
    {
        if (SkippedExtensions.Contains(Path.GetExtension(file))) return [];

        try
        {
            var info = new FileInfo(file);
            if (info.Length > LargestFileSearched) return [];
            lock (Gate)
            {
                if (FileIds.TryGetValue(file, out var hit) && hit.Written == info.LastWriteTimeUtc && hit.Length == info.Length) return hit.Ids;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            CollectIds(File.ReadAllBytes(file), ids);
            lock (Gate) FileIds[file] = (info.LastWriteTimeUtc, info.Length, ids);
            return ids;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Profiles", $"couldn't read {file}: {ex.Message}");
            return [];
        }
    }

    internal static void CollectIds(ReadOnlySpan<byte> data, HashSet<string> into)
    {
        // As bytes.
        var run = 0;
        for (var i = 0; i <= data.Length; i++)
        {
            if (i < data.Length && IsHex(data[i])) { run++; continue; }
            if (run == 24) into.Add(Lower(data, i - 24, 1));
            run = 0;
        }

        // As UTF-16: each digit followed by a zero byte, at either alignment.
        for (var start = 0; start < 2; start++)
        {
            run = 0;
            for (var i = start; ; i += 2)
            {
                if (i + 1 < data.Length && IsHex(data[i]) && data[i + 1] == 0) { run++; continue; }
                if (run == 24) into.Add(Lower(data, i - 48, 2));
                run = 0;
                if (i + 1 >= data.Length) break;
            }
        }
    }

    private static bool IsHex(byte b) => b is >= (byte)'0' and <= (byte)'9' or >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';

    private static bool IsId(string s) => s.Length == 24 && s.All(c => c < 128 && IsHex((byte)c));

    private static string Lower(ReadOnlySpan<byte> data, int from, int step)
    {
        Span<char> chars = stackalloc char[24];
        for (var k = 0; k < 24; k++) chars[k] = char.ToLowerInvariant((char)data[from + k * step]);
        return new string(chars);
    }

    // The enabled server mods' folders (and anything loose beside them).
    private static IEnumerable<string> ServerModEntries(IEnumerable<string> containers)
    {
        foreach (var container in containers)
        {
            if (!Directory.Exists(container)) continue;
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(container).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var entry in entries) yield return Full(entry);
        }
    }

    private static HashSet<string>? BaseItems(string file)
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(file);
            lock (Gate)
            {
                if (_baseItems is { } hit && string.Equals(hit.Path, file, StringComparison.OrdinalIgnoreCase) && hit.Written == written) return hit.Ids;
            }

            if (!File.Exists(file)) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var ids = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            lock (Gate) _baseItems = (file, written, ids);
            return ids;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("Profiles", $"couldn't read {file}: {ex.Message}");
            return null;
        }
    }

    private static ProfileModContent? ReadProfile(string file, IReadOnlySet<string> baseItems, IReadOnlySet<string> baseTraders)
    {
        try
        {
            var info = new FileInfo(file);
            lock (Gate)
            {
                if (Profiles.TryGetValue(file, out var hit) && hit.Written == info.LastWriteTimeUtc && hit.Length == info.Length) return hit.Content;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var content = FromJson(doc.RootElement, baseItems, baseTraders);
            lock (Gate) Profiles[file] = (info.LastWriteTimeUtc, info.Length, content);
            return content;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("Profiles", $"couldn't read profile {file}: {ex.Message}");
            return null;
        }
    }

    private static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Inside(string path, string folder) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
