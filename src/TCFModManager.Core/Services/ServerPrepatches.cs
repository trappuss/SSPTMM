using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.8): what the server's enum prepatches add. SPT 4.1 reads user\patchers\<ModGuid>\*.json
// before it starts, each a list of { enumType, constantName, constantValue } - how a mod gives the
// server a skill, say, that SPT doesn't have. Seen in a real install: Skills Extended's
// EnumExtensions.json, adding Hacking and SignalsIntelligence to SkillTypes.
//
// Turning a mod off leaves its folder here, so the server goes on knowing the value while the game,
// whose half is off, does not. Diagnose logs uses this to say whose value the game tripped over.
//
public static class ServerPrepatches
{
    /// <summary>Every value added, with the folder (a mod's ModGuid) that adds it. Files that can't be read are skipped.</summary>
    public static IReadOnlyList<(string Guid, string Value)> Read(string installPath)
    {
        var found = new List<(string, string)>();

        foreach (var mods in DisabledModPaths.ServerContainers(installPath))
        {
            if (Path.GetDirectoryName(mods) is not { } user) continue;

            var patchers = Path.Combine(user, "patchers");
            if (!Directory.Exists(patchers)) continue;

            try
            {
                foreach (var folder in Directory.EnumerateDirectories(patchers))
                    foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                        foreach (var value in ValuesIn(file))
                            found.Add((Path.GetFileName(folder), value));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Diagnose", $"couldn't read {patchers}: {ex.Message}");
            }
        }

        return found;
    }

    private static List<string> ValuesIn(string file)
    {
        var values = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Array) return values;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("constantName", out var name)
                    && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } value)
                {
                    values.Add(value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("Diagnose", $"couldn't read {file}: {ex.Message}");
        }

        return values;
    }
}
