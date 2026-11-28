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
        var blocked = new List<ModBlockedUpdateEntry>();
        var requests = 0;
        var incompatible = new List<ModIncompatibleEntry>();

        foreach (var chunk in ModsQueryChunks(installed))
        {
            var result = await api.GetModUpdatesAsync(chunk, sptVersion, ct).ConfigureAwait(false);
            requests++;

            var ids = result.Updates.Select(u => u.CurrentVersion?.ModId).OfType<int>().ToList();
            updated.AddRange(ids);

            answered.UnionWith(ids);
            answered.UnionWith(result.BlockedUpdates.Select(u => u.CurrentVersion?.ModId).OfType<int>());
            answered.UnionWith(result.UpToDate.Select(u => u.ModId));
            answered.UnionWith(result.IncompatibleWithSpt.Select(u => u.ModId));

            blocked.AddRange(result.BlockedUpdates);
            incompatible.AddRange(result.IncompatibleWithSpt);
        }

        var asked = installed.Where(m => !string.IsNullOrWhiteSpace(m.Version)).Select(m => m.ModId);

        return new InstalledUpdateCheck(
            [.. updated.Distinct()],
            [.. asked.Distinct().Where(id => !answered.Contains(id))])
        {
            Blocked = blocked,
            Incompatible = incompatible,
            Requests = requests,
        };
    }

    // Above this many held-back updates, none are grouped.
    internal const int MaxGroupCandidates = 12;

    //
    // Fork (1.4.9), from TCF Mod Manager 71708aa (MIT): the held-back updates that can go together.
    // Two mods that each need an exact release of the other - ISB Aishi and ISB Special Forces - are
    // each held back by sp-mod.com, so neither was ever offered. Candidates are the held-back mods
    // blocked only by other held-back mods, joined by the blockers sp-mod.com names and, for one it
    // names none for, by the held-back mods in its dependency tree. sp-mod.com then confirms each
    // group: every member must be a plain update once the others are at their held releases.
    //
    // Answers each grouped mod's id with the ids it goes with. Any failure answers with none, which
    // leaves every update held as sp-mod.com gave it.
    //
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> HeldBackTogetherAsync(
        IReadOnlyList<(int ModId, string Version)> installed, IEnumerable<ModBlockedUpdateEntry> blocked,
        string sptVersion, CancellationToken ct = default)
    {
        var none = new Dictionary<int, IReadOnlyList<int>>();

        var held = blocked
            .Where(b => b.CurrentVersion is not null && !string.IsNullOrWhiteSpace(b.LatestVersion?.Version))
            .GroupBy(b => b.CurrentVersion!.ModId)
            .ToDictionary(g => g.Key, g => g.First());

        var candidates = held
            .Where(h => h.Value.BlockingMods.All(b => held.ContainsKey(b.ModId)))
            .Select(h => h.Key)
            .ToList();

        if (candidates.Count < 2) return none;

        if (candidates.Count > MaxGroupCandidates)
        {
            AppLog.Info("Updates", $"{candidates.Count} held-back updates - too many to try grouping");
            return none;
        }

        try
        {
            var links = candidates.ToDictionary(id => id, _ => new HashSet<int>());
            void Link(int a, int b)
            {
                if (a == b || !links.ContainsKey(a) || !links.ContainsKey(b)) return;
                links[a].Add(b);
                links[b].Add(a);
            }

            foreach (var id in candidates)
                foreach (var blocker in held[id].BlockingMods)
                    Link(id, blocker.ModId);

            var chained = candidates.Where(id => held[id].BlockingMods.Count == 0).ToList();
            if (chained.Count > 0)
            {
                var trees = await api.GetModDependenciesAsync(
                    string.Join(',', chained.Select(id => $"{id.ToString(CultureInfo.InvariantCulture)}:{held[id].LatestVersion!.Version!.Trim()}")),
                    sptVersion, ct).ConfigureAwait(false);

                foreach (var (key, nodes) in trees)
                {
                    if (!int.TryParse(key.Split(':')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;
                    foreach (var node in Flatten(nodes)) Link(id, node.Id);
                }
            }

            var together = new Dictionary<int, IReadOnlyList<int>>();

            foreach (var members in Components(candidates, links))
            {
                // A member sp-mod.com still holds with the others moved is dropped, and the rest asked again.
                while (members.Count >= 2)
                {
                    var failed = new List<int>();
                    foreach (var id in members)
                    {
                        if (!await GoesWithAsync(installed, members, id, held, sptVersion, ct).ConfigureAwait(false)) failed.Add(id);
                    }

                    if (failed.Count == 0) break;
                    members.ExceptWith(failed);
                }

                if (members.Count < 2) continue;

                foreach (var id in members) together[id] = [.. members.Where(other => other != id)];
                AppLog.Info("Updates", $"held-back updates go together: {string.Join(", ", members.Select(id => $"{id} {held[id].LatestVersion!.Version}"))}");
            }

            return together;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // (A request that timed out is a cancellation by type, and is a failure like any other.)
            AppLog.Warn("Updates", $"couldn't check which held-back updates go together: {ex.Message}");
            return none;
        }
    }

    // Whether sp-mod.com offers this member its held release once every other member is at its own.
    private async Task<bool> GoesWithAsync(
        IReadOnlyList<(int ModId, string Version)> installed, IReadOnlySet<int> members, int modId,
        IReadOnlyDictionary<int, ModBlockedUpdateEntry> held, string sptVersion, CancellationToken ct)
    {
        var wanted = held[modId].LatestVersion!.Version;

        // The members first, so a long list split into several requests keeps them in one.
        var state = installed
            .OrderByDescending(m => members.Contains(m.ModId))
            .Select(m => members.Contains(m.ModId) && m.ModId != modId ? (m.ModId, held[m.ModId].LatestVersion!.Version!) : m)
            .ToList();

        foreach (var chunk in ModsQueryChunks(state))
        {
            var result = await api.GetModUpdatesAsync(chunk, sptVersion, ct).ConfigureAwait(false);
            if (result.Updates.Any(u => u.CurrentVersion?.ModId == modId
                    && string.Equals(u.RecommendedVersion?.Version, wanted, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    //
    // Fork (1.4.10): what a held-back update is missing. sp-mod.com holds an update back as
    // "missing_dependency" and names nothing - ECOT 2.0.2 on SPT 4.1.6, which needs WTT - CommonLib
    // 3.0.8, a release listed for SPT 4.1.3 only (read on 2026-11-27). Its dependency tree for the
    // held release says which: the mods in it with no version that fits.
    //
    // Answers each such mod's id with those mods' names. Any failure answers with none.
    //
    public const string MissingDependency = "missing_dependency";

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> MissingForAsync(
        IEnumerable<ModBlockedUpdateEntry> blocked, string sptVersion, CancellationToken ct = default)
    {
        var missing = new Dictionary<int, IReadOnlyList<string>>();

        var asked = blocked
            .Where(b => b.BlockReason == MissingDependency && b.CurrentVersion is not null && !string.IsNullOrWhiteSpace(b.LatestVersion?.Version))
            .Select(b => (b.CurrentVersion!.ModId, Version: b.LatestVersion!.Version!))
            .DistinctBy(b => b.ModId)
            .ToList();

        if (asked.Count == 0) return missing;

        try
        {
            foreach (var chunk in ModsQueryChunks(asked))
            {
                var trees = await api.GetModDependenciesAsync(chunk, sptVersion, ct).ConfigureAwait(false);

                foreach (var (key, nodes) in trees)
                {
                    if (!int.TryParse(key.Split(':')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) continue;

                    var names = Flatten(nodes)
                        .Where(n => n.LatestCompatibleVersion is null && !string.IsNullOrWhiteSpace(n.Name))
                        .Select(n => n.Name!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (names.Count > 0) missing[id] = names;
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            AppLog.Warn("Updates", $"couldn't read what the held-back updates are missing: {ex.Message}");
        }

        return missing;
    }

    private static List<HashSet<int>> Components(IReadOnlyList<int> ids, IReadOnlyDictionary<int, HashSet<int>> links)
    {
        var seen = new HashSet<int>();
        var components = new List<HashSet<int>>();

        foreach (var start in ids)
        {
            if (!seen.Add(start)) continue;

            var component = new HashSet<int> { start };
            var pending = new Stack<int>();
            pending.Push(start);
            while (pending.TryPop(out var id))
            {
                foreach (var next in links[id].Where(seen.Add))
                {
                    component.Add(next);
                    pending.Push(next);
                }
            }

            components.Add(component);
        }

        return components;
    }

    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Dependencies)) yield return child;
        }
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
    // Fork (1.3.0): what the same answers held back, and what doesn't run on this SPT - for
    // HeldBackUpdates, so the background check needs no second request.
    public IReadOnlyList<ModBlockedUpdateEntry> Blocked { get; init; } = [];

    public IReadOnlyList<ModIncompatibleEntry> Incompatible { get; init; } = [];

    // How many /mods/updates requests the answer took (the install is split when its list is long).
    public int Requests { get; init; }

    //
    // Fork (1.4.10): the held-back ones too. Their listings were never read again, so the release
    // sp-mod.com held back was not in the list the cards are made from: the card said "held back"
    // under a mod that showed no update, and "Needs update" left it out.
    //
    public IReadOnlyList<int> ToRefetch =>
        [.. Updated.Concat(NotRecognised).Concat(Blocked.Select(b => b.CurrentVersion?.ModId).OfType<int>()).Distinct()];
}
