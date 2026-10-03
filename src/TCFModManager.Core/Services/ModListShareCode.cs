using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM): a collection as a line of text, to paste to a friend - in a Discord message, a
// forum post, anywhere text goes. The friend's app reads it back into the same list: same Id, same
// revision, so a newer code of a list they already have updates it rather than adding a second.
//
// What travels is what the list file carries for each mod that can be fetched - sp-mod.com's mod
// id, the version's id and number, scope, addon or not - and nothing that can be looked up again
// on arrival. Names and plugin GUIDs come back from the catalog (see the App's CollectionSharing);
// they are what made the full file five times longer, and a Discord message stops at 2000
// characters. Measured on a real 97-mod list: about 1200 characters.
//
// A mod sp-mod.com does not have (installed by hand) travels as its name alone - the friend's
// app can only say they need it, as an imported file does.
//
// The text is "SSPTMM1." then the list as compact JSON, deflated and in URL-safe base64, so it
// survives chat clients that mangle '+' and '/' and can be found inside surrounding text.
//
public static class ModListShareCode
{
    public const string Prefix = "SSPTMM1.";

    // The highest compact format this app reads. Raised only for a change an older reader would
    // misread - the same rule as ModListFile's schema version.
    private const int Format = 1;

    public static string Encode(ModList list, string? author)
    {
        var entries = new JsonArray();
        foreach (var e in list.Entries)
        {
            if (e.ModId is { } id)
            {
                var fields = new List<JsonNode?>
                {
                    id,
                    e.VersionId,
                    e.Version,
                    e.Scope is { } scope && scope != ModListEntryScope.Everyone ? (int)scope : null,
                    e.IsAddon ? 1 : null,
                };

                // Empty fields at the end say nothing a reader doesn't assume, so they don't travel.
                while (fields.Count > 1 && fields[^1] is null) fields.RemoveAt(fields.Count - 1);
                entries.Add(new JsonArray([.. fields]));
            }
            else
            {
                entries.Add(new JsonArray(e.Name));
            }
        }

        var root = new JsonObject
        {
            ["f"] = Format,
            ["i"] = list.Id.ToString("N"),
            ["r"] = list.Revision,
            ["n"] = list.Name,
            ["a"] = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
            ["s"] = list.SptVersion,
            ["p"] = list.Policy == ModListPolicy.Additive ? 1 : null,
            ["e"] = entries,
        };

        var json = Encoding.UTF8.GetBytes(root.ToJsonString());
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);

        return Prefix + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Whether this text holds a share code anywhere in it.
    public static bool Contains(string? text) => Find(text) is not null;

    //
    // Reads the first share code found in the text into a list ready to store, with Origin
    // Imported and the sharer as its Source - as ModListFile.Read does for a file. Each entry's
    // Name is a placeholder ("#<id>") until the App fills it from the catalog. Never throws.
    //
    public static ModListImport Decode(string? text)
    {
        if (Find(text) is not { } code) return ModListImport.Failed("there is no share code in it");

        JsonObject? root;
        try
        {
            var base64 = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');

            using var packed = new MemoryStream(Convert.FromBase64String(base64));
            using var deflate = new DeflateStream(packed, CompressionMode.Decompress);
            using var json = new MemoryStream();
            deflate.CopyTo(json);
            root = JsonNode.Parse(json.ToArray()) as JsonObject;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException)
        {
            return ModListImport.Failed("the code is damaged - copy it again, all of it");
        }

        if (root is null) return ModListImport.Failed("the code is damaged - copy it again, all of it");

        if ((int?)root["f"] is not { } format || format > Format)
        {
            return ModListImport.Failed("it was made by a newer version of this app - update this one to read it");
        }

        if (!Guid.TryParseExact((string?)root["i"], "N", out var id) || id == Guid.Empty)
            return ModListImport.Failed("the code is damaged - copy it again, all of it");

        var name = ((string?)root["n"])?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return ModListImport.Failed("the collection has no name");

        var list = new ModList
        {
            Id = id,
            Name = name,
            Revision = Math.Max(1, (int?)root["r"] ?? 1),
            Origin = ModListOrigin.Imported,
            Policy = (int?)root["p"] == 1 ? ModListPolicy.Additive : ModListPolicy.Exclusive,
            Source = ((string?)root["a"])?.Trim() is { Length: > 0 } author ? author : null,
            SptVersion = (string?)root["s"],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var node in root["e"] as JsonArray ?? [])
        {
            if (node is not JsonArray entry || entry.Count == 0) continue;

            try
            {
                // A string first is a mod sp-mod.com doesn't have, by name. A number first is a mod
                // id - which can stand alone too, for an unpinned mod once its empty fields are
                // trimmed, so the length alone can't tell the two apart.
                if (entry[0]?.GetValueKind() == JsonValueKind.String)
                {
                    if (((string?)entry[0])?.Trim() is { Length: > 0 } handName)
                        list.Entries.Add(new ModListEntry { Name = handName });
                    continue;
                }

                if ((int?)entry[0] is not { } modId) continue;

                list.Entries.Add(new ModListEntry
                {
                    Name = "#" + modId,
                    ModId = modId,
                    VersionId = entry.Count > 1 ? (int?)entry[1] : null,
                    Version = entry.Count > 2 ? (string?)entry[2] : null,
                    Scope = entry.Count > 3 && (int?)entry[3] is { } s ? (ModListEntryScope?)s : null,
                    IsAddon = entry.Count > 4 && (int?)entry[4] == 1,
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                // One entry of the wrong shape is skipped rather than costing the whole list.
            }
        }

        return new ModListImport(list, null);
    }

    // The code itself, from its prefix to the first character a code can't contain.
    private static string? Find(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var start = text.IndexOf(Prefix, StringComparison.Ordinal);
        if (start < 0) return null;

        var end = start + Prefix.Length;
        while (end < text.Length && IsCodeChar(text[end])) end++;

        return end > start + Prefix.Length ? text[start..end] : null;
    }

    private static bool IsCodeChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
