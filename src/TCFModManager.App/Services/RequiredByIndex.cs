using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Which mods need a given mod - the item page's "Required by" panel.
//
// sp-mod.com only answers the other way round (what a mod needs), so this asks that question for
// the whole catalog and turns the answers over. It uses the endpoint the API documents for mod
// managers, which takes many mods per call: 100 at a time, about eight calls for everything that
// runs on one SPT release, once per session.
//
// Each mod is asked about at the version the app would install for this SPT install - the same
// pick Browse's cards show - so "required by" means required by what you would get today. Mods with
// nothing for this SPT release are not asked about, and so never appear.
//
public sealed class RequiredByIndex
{
    private const int BatchSize = 100;

    // A build that could not ask about every mod (offline, rate limited) is asked again this long
    // after it finished, rather than kept incomplete for the rest of the session.
    private static readonly TimeSpan RetryIncompleteAfter = TimeSpan.FromMinutes(2);

    private sealed record Built(Dictionary<int, List<int>> Dependents, bool Complete, DateTime FinishedAt);

    private Task<Built>? _building;
    private string? _builtFor;

    /// <summary>The catalog mods whose shown version needs <paramref name="modId"/> directly, most
    /// downloaded first. Empty when nothing does or the lookup could not be made.</summary>
    public async Task<IReadOnlyList<Mod>> DependentsOfAsync(int modId)
    {
        // Without an SPT install to go by, the newest release stands in for it.
        var releases = AppServices.SptCatalog.Releases;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion ?? (releases.Count > 0 ? releases[0].Label : null);
        if (string.IsNullOrWhiteSpace(sptVersion)) return [];

        if (_building is null || _builtFor != sptVersion || IsStale(_building))
        {
            _builtFor = sptVersion;
            _building = BuildAsync(sptVersion);
        }

        var dependents = (await _building).Dependents;
        if (!dependents.TryGetValue(modId, out var ids)) return [];

        // Grouped rather than ToDictionary: a mod stored twice in the cached catalog must not throw.
        var byId = AppServices.ModCache.AllMods
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => g.First());
        return ids
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<Mod>()
            .OrderByDescending(m => m.Downloads ?? 0)
            .ToList();
    }

    // Failed outright (the catalog would not load), or finished without every answer a while ago.
    private static bool IsStale(Task<Built> building) =>
        building.IsFaulted || building.IsCanceled
        || (building.IsCompletedSuccessfully && !building.Result.Complete
            && DateTime.UtcNow - building.Result.FinishedAt > RetryIncompleteAfter);

    private static async Task<Built> BuildAsync(string sptVersion)
    {
        var dependents = new Dictionary<int, List<int>>();
        var complete = true;

        await AppServices.ModCache.EnsureLoadedAsync();

        var pairs = AppServices.ModCache.AllMods
            .Select(mod => (Mod: mod, Version: ModCardViewModel.PickDisplayVersion(mod, sptVersion)))
            .Where(p => p.Version?.Version is not null &&
                        SptVersionMatcher.IsSatisfiedBy(p.Version.SptVersionConstraint, sptVersion) == true)
            .Select(p => (p.Mod.Id, Key: $"{p.Mod.Id}:{p.Version!.Version}"))
            .ToList();

        for (var i = 0; i < pairs.Count; i += BatchSize)
        {
            var batch = pairs.Skip(i).Take(BatchSize).ToList();

            Dictionary<string, List<DependencyNode>> result;
            try
            {
                result = await AppServices.SpModApi.GetModDependenciesAsync(string.Join(",", batch.Select(p => p.Key)), sptVersion);
            }
            catch (Exception ex)
            {
                // A batch that fails leaves its mods out rather than failing the panel.
                AppLog.Warn("RequiredBy", $"dependency batch {i / BatchSize + 1} failed: {ex.Message}");
                complete = false;
                continue;
            }

            foreach (var (dependentId, key) in batch)
            {
                if (!result.TryGetValue(key, out var nodes)) continue;

                // Direct needs only: the top of each tree, not what those need in turn.
                foreach (var node in nodes)
                {
                    if (!dependents.TryGetValue(node.Id, out var list)) dependents[node.Id] = list = [];
                    if (!list.Contains(dependentId)) list.Add(dependentId);
                }
            }
        }

        AppLog.Info("RequiredBy", $"indexed {pairs.Count} mods for SPT {sptVersion}: {dependents.Count} are required by something{(complete ? "" : " (incomplete; asked again later)")}");
        return new Built(dependents, complete, DateTime.UtcNow);
    }
}
