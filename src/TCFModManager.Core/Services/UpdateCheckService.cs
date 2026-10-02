using System.Globalization;
using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

//
// The network half of an update check (D1-D3): ask The Forge only about what is installed, then
// fetch fresh listings for just the mods it says moved, and for every installed addon.
//
// It decides nothing about what counts as an update. The listings it returns are patched into the
// catalog and addon caches, and the Installed page's own rule (BuildFrom) makes the call - so a
// toast can never claim an update the Installed page doesn't show (D2).
//
public sealed class UpdateCheckService(SpModApiClient api)
{
    //
    // /mods/updates takes every installed mod in one "id:version" list. The spike sent 80 of them in
    // 877 characters; longer lists are split well before any server or proxy would object.
    //
    internal const int MaxModsQueryLength = 1500;

    // The catalog's page size for a filter[id] lookup.
    private const int PageSize = 50;

    //
    // What /mods/updates says about <paramref name="installed"/> on <paramref name="sptVersion"/>.
    //
    // Updated is the ids it lists under "updates". Blocked and incompatible entries are not updates:
    // incompatible is about what is installed not fitting this SPT, which the Installed page already
    // shows.
    //
    // NotRecognised is every id it left out of all four lists. It does that, silently, for a version
    // that isn't one of the mod's releases - found live on 2026-09-27: 1.0.1.0, v1.0.1 or banana for
    // a mod with a 1.0.1 release all vanish, HTTP 200. That is exactly a hand install whose version
    // came from its files (R7), so those mods are re-read directly rather than dropped.
    //
    public async Task<InstalledUpdateCheck> FindUpdatedModsAsync(
        IReadOnlyList<(int ModId, string Version)> installed, string sptVersion, CancellationToken ct = default)
    {
        var updated = new List<int>();
        var answered = new HashSet<int>();

        foreach (var chunk in ModsQueryChunks(installed))
        {
            var result = await api.GetModUpdatesAsync(chunk, sptVersion, ct).ConfigureAwait(false);

            var ids = result.Updates.Select(u => u.CurrentVersion?.ModId).OfType<int>().ToList();
            updated.AddRange(ids);

            answered.UnionWith(ids);
            answered.UnionWith(result.BlockedUpdates.Select(u => u.CurrentVersion?.ModId).OfType<int>());
            answered.UnionWith(result.UpToDate.Select(u => u.ModId));
            answered.UnionWith(result.IncompatibleWithSpt.Select(u => u.ModId));
        }

        var asked = installed.Where(m => !string.IsNullOrWhiteSpace(m.Version)).Select(m => m.ModId);

        return new InstalledUpdateCheck(
            [.. updated.Distinct()],
            [.. asked.Distinct().Where(id => !answered.Contains(id))]);
    }

    // Fresh listings, with categories and versions, shaped like the catalog cache's own.
    public async Task<IReadOnlyList<Mod>> FetchModsAsync(IEnumerable<int> modIds, CancellationToken ct = default)
    {
        var mods = new List<Mod>();

        foreach (var ids in modIds.Distinct().Chunk(PageSize))
        {
            var page = await api.GetModsAsync(new ModsQuery
            {
                FilterId = string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                Include = "category,versions",
                PerPage = PageSize,
            }, ct).ConfigureAwait(false);

            mods.AddRange(page.Data);
        }

        return mods;
    }

    //
    // Addons are not covered by /mods/updates (D3). Few are ever installed, so each check simply
    // re-reads their listings, versions included, a page at a time.
    //
    public async Task<IReadOnlyList<Addon>> FetchAddonsAsync(IEnumerable<int> addonIds, CancellationToken ct = default)
    {
        var addons = new List<Addon>();

        foreach (var ids in addonIds.Distinct().Chunk(PageSize))
        {
            var page = await api.GetAddonsAsync(new AddonsQuery
            {
                FilterId = string.Join(',', ids.Select(i => i.ToString(CultureInfo.InvariantCulture))),
                Include = "versions",
                PerPage = PageSize,
            }, ct).ConfigureAwait(false);

            addons.AddRange(page.Data);
        }

        return addons;
    }

    // "id:version,id:version..." lists no longer than MaxModsQueryLength, in the order given.
    internal static IEnumerable<string> ModsQueryChunks(IEnumerable<(int ModId, string Version)> installed)
    {
        var current = new List<string>();
        var length = 0;

        foreach (var (modId, version) in installed)
        {
            if (string.IsNullOrWhiteSpace(version)) continue;

            var item = $"{modId.ToString(CultureInfo.InvariantCulture)}:{version.Trim()}";
            var added = item.Length + (current.Count > 0 ? 1 : 0);

            if (current.Count > 0 && length + added > MaxModsQueryLength)
            {
                yield return string.Join(',', current);
                current.Clear();
                length = 0;
                added = item.Length;
            }

            current.Add(item);
            length += added;
        }

        if (current.Count > 0) yield return string.Join(',', current);
    }
}

// What one /mods/updates round said. ToRefetch is every mod whose listing a check should re-read.
public sealed record InstalledUpdateCheck(IReadOnlyList<int> Updated, IReadOnlyList<int> NotRecognised)
{
    public IReadOnlyList<int> ToRefetch => [.. Updated.Concat(NotRecognised).Distinct()];
}
