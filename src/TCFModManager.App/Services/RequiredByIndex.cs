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

    private Task<Dictionary<int, List<int>>>? _building;
    private string? _builtFor;

    /// <summary>The catalog mods whose shown version needs <paramref name="modId"/> directly, most
    /// downloaded first. Empty when nothing does or the lookup could not be made.</summary>
    public async Task<IReadOnlyList<Mod>> DependentsOfAsync(int modId)
    {
        // Without an SPT install to go by, the newest release stands in for it.
        var releases = AppServices.SptCatalog.Releases;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion ?? (releases.Count > 0 ? releases[0].Label : null);
        if (string.IsNullOrWhiteSpace(sptVersion)) return [];

        if (_building is null || _builtFor != sptVersion)
        {
            _builtFor = sptVersion;
            _building = BuildAsync(sptVersion);
        }

        var dependents = await _building;
        if (!dependents.TryGetValue(modId, out var ids)) return [];

        var byId = AppServices.ModCache.AllMods.ToDictionary(m => m.Id);
        return ids
            .Select(id => byId.GetValueOrDefault(id))
            .OfType<Mod>()
            .OrderByDescending(m => m.Downloads ?? 0)
            .ToList();
    }

    private static async Task<Dictionary<int, List<int>>> BuildAsync(string sptVersion)
    {
        var dependents = new Dictionary<int, List<int>>();

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

        AppLog.Info("RequiredBy", $"indexed {pairs.Count} mods for SPT {sptVersion}: {dependents.Count} are required by something");
        return dependents;
    }
}
