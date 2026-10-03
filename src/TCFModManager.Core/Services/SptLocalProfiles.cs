using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM): the install's profiles read from its own files (user\profiles\<id>.json), so the
// Play page can offer them before the server is up - the server answers the same list
// (/launcher/v2/profiles) once it is.
//
// From each file: info.id, info.username, info.edition, info.wipe, and the PMC's Info.Nickname,
// Level and Side (measured on the user's SPT 4.1.6 profiles, 2026-10-03). A file that is not a
// profile, or cannot be read, is left out.
//
public static class SptLocalProfiles
{
    private static readonly Dictionary<string, (DateTime Written, long Length, SptMiniProfile? Profile)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    public static IReadOnlyList<SptMiniProfile> Read(string? installPath)
    {
        if (ProfileBackups.ProfilesFolder(installPath) is not { } folder) return [];

        var found = new List<SptMiniProfile>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("DirectLaunch", $"couldn't list {folder}: {ex.Message}");
            return [];
        }

        foreach (var file in files)
        {
            if (ReadOne(file) is { } profile) found.Add(profile);
        }

        return [.. found.OrderBy(p => p.Nickname.Length == 0 ? p.Username : p.Nickname, StringComparer.OrdinalIgnoreCase)];
    }

    private static SptMiniProfile? ReadOne(string file)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(file);
            lock (Gate)
            {
                if (Cache.TryGetValue(file, out var hit) && hit.Written == info.LastWriteTimeUtc && hit.Length == info.Length)
                    return hit.Profile;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var profile = Parse(file);
        lock (Gate) Cache[file] = (info.LastWriteTimeUtc, info.Length, profile);
        return profile;
    }

    internal static SptMiniProfile? Parse(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return FromJson(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("DirectLaunch", $"couldn't read profile {file}: {ex.Message}");
            return null;
        }
    }

    internal static SptMiniProfile? FromJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            return null;

        var id = Text(info, "id");
        var username = Text(info, "username");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(username)) return null;

        string nickname = "", side = "";
        var level = 0;
        if (root.TryGetProperty("characters", out var characters) && characters.ValueKind == JsonValueKind.Object
            && characters.TryGetProperty("pmc", out var pmc) && pmc.ValueKind == JsonValueKind.Object
            && pmc.TryGetProperty("Info", out var pmcInfo) && pmcInfo.ValueKind == JsonValueKind.Object)
        {
            nickname = Text(pmcInfo, "Nickname");
            side = Text(pmcInfo, "Side");
            if (pmcInfo.TryGetProperty("Level", out var lvl) && lvl.TryGetInt32(out var n)) level = n;
        }

        return new SptMiniProfile
        {
            ProfileId = id,
            Username = username,
            Edition = Text(info, "edition"),
            Wipe = info.TryGetProperty("wipe", out var wipe) && wipe.ValueKind == JsonValueKind.True,
            Nickname = nickname,
            Side = side,
            Level = level,
        };
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

//
// Fork (SSPTMM): what the SPT launcher's own settings say that matters to starting the game here -
// the clean-up's exclusions, Clear cache on launch, and the profile it last started (so the Play
// page picks the same one the first time). <server>\user\Launcher\LauncherSettings.json, measured on
// the user's SPT 4.1.6 install.
//
public sealed record SptLauncherSettings
{
    public IReadOnlyList<string> ExcludeFromCleanup { get; init; } = [];

    public bool ClearCacheOnLaunch { get; init; }

    public string? PreferredProfileId { get; init; }

    public static SptLauncherSettings Read(string serverRoot)
    {
        var path = Path.Combine(serverRoot, "user", "Launcher", "LauncherSettings.json");
        if (!File.Exists(path)) return new SptLauncherSettings();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;

            var excluded = root.TryGetProperty("ExcludeFromCleanup", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
                : [];

            string? preferred = null;
            if (root.TryGetProperty("PreferredProfile", out var pref) && pref.ValueKind == JsonValueKind.Object
                && pref.TryGetProperty("ProfileId", out var pid) && pid.ValueKind == JsonValueKind.String)
            {
                preferred = pid.GetString();
            }

            return new SptLauncherSettings
            {
                ExcludeFromCleanup = excluded,
                ClearCacheOnLaunch = root.TryGetProperty("ClearCacheOnLaunch", out var clear) && clear.ValueKind == JsonValueKind.True,
                PreferredProfileId = preferred,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("DirectLaunch", $"couldn't read {path}: {ex.Message}");
            return new SptLauncherSettings();
        }
    }
}
