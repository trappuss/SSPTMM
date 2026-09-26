using System.Text.Json;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.SpModLists;

/// <summary>Every public list on sp-mod.com at one moment, as its lists pages showed them.</summary>
public sealed record SpModListIndexData(
    DateTimeOffset FetchedAt,
    IReadOnlyList<SpModListSummary> Lists,
    IReadOnlyList<SpModListSptOption> SptOptions);

//
// All of sp-mod.com's public lists, read page by page and kept on disk.
//
// sp-mod.com orders its lists newest first and nothing else, and its search reads titles and
// descriptions but not who made a list. Ordering them another way, or finding a member's lists by
// name, needs every card at once - about 27 pages of 12 when this was written (317 lists,
// 2026-09-26). They are read one page after another, not at once, and kept in Data for an hour, so
// the site is asked for them at most that often and the page opens on them straight away.
//
public sealed class SpModListIndex(string path)
{
    public static string DefaultPath => Path.Combine(AppPaths.DataDirectory, "collections_index.json");

    /// <summary>How long a read is used before it is read again.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public string FilePath { get; } = path;

    /// <summary>The copy kept on disk, however old; null when there is none or it cannot be read.</summary>
    public SpModListIndexData? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<SpModListIndexData>(File.ReadAllText(FilePath), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            AppLog.Info("Lists", $"the kept list of collections could not be read ({ex.Message}); reading them again");
            return null;
        }
    }

    public void Save(SpModListIndexData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Info("Lists", $"the list of collections could not be kept: {ex.Message}");
        }
    }

    public static bool IsFresh(SpModListIndexData data, DateTimeOffset now) => now - data.FetchedAt < FreshFor;

    //
    // Every page, first to last. A list that moves to the next page while they are read (a new one
    // posted meanwhile pushes every card along one) would be read twice: the first reading is kept.
    // progress: pages read so far, and how many there are.
    //
    public static async Task<SpModListIndexData> FetchAsync(
        SpModListsClient client,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var first = await client.BrowseAsync(1, ct: ct).ConfigureAwait(false);
        var lists = new List<SpModListSummary>(first.Lists);
        var seen = lists.Select(l => l.Id).ToHashSet();
        progress?.Report((1, first.LastPage));

        for (var page = 2; page <= first.LastPage; page++)
        {
            var next = await client.BrowseAsync(page, ct: ct).ConfigureAwait(false);
            foreach (var list in next.Lists)
            {
                if (seen.Add(list.Id)) lists.Add(list);
            }

            progress?.Report((page, first.LastPage));
        }

        return new SpModListIndexData(DateTimeOffset.UtcNow, lists, first.SptOptions);
    }
}
