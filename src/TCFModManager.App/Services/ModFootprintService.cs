using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

// One installed mod and what it ships, as the Performance page consumes it.
public sealed record ModFootprintResult(
    string Name,
    string? Version,
    bool IsDisabled,
    ModFootprint Footprint);

//
// Reads every installed mod's footprint, using the cache for anything that hasn't changed on disk.
//
// Does its own scan rather than reading the Installed page, for the same reason ModListService
// does: InstalledViewModel is per-page rather than an AppServices singleton, and this page has to
// work whether or not that one has been visited.
//
// References no WPF type, so it compiles into the headless probe alongside ModListCandidates - and
// this is the half worth proving, since a wrong cache key silently means either a stale answer or
// a full re-read of the install on every visit.
//
public sealed class ModFootprintService
{
    private readonly ModFootprintStore _store = new();

    //
    // <paramref name="force"/> re-analyses everything regardless of the cache. Wired to the page's
    // Rescan button, for when the user has changed files under the app rather than through it.
    //
    public async Task<IReadOnlyList<ModFootprintResult>> ReadAsync(bool force = false)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return [];

        await AppServices.ModCache.EnsureLoadedAsync();
        await AppServices.Addons.EnsureLoadedAsync();

        var records = AppServices.InstallManifest.Load(installPath).Mods;
        var catalog = AppServices.ModCache.AllMods;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;

        return await Task.Run(() =>
        {
            var found = InstalledModScanner.Scan(installPath);

            var cards = InstalledModCardViewModel.BuildFrom(
                found, catalog, sptVersion, records, AppServices.Addons.AllAddons);

            var cached = _store.Load();
            var current = new Dictionary<string, ModFootprint>();
            var results = new List<ModFootprintResult>();

            foreach (var card in cards)
            {
                var entries = card.Entries;

                // An addon that installs into its parent's folder has no folder of its own, so
                // there is nothing here to measure that the parent's own row doesn't already cover.
                if (entries.Count == 0) continue;

                var key = ModFootprintAnalyzer.KeyFor(entries[0].FolderPath);
                var stamp = ModFootprintAnalyzer.StampFor(entries);
                cached.TryGetValue(key, out var previous);

                var footprint = !force && ModFootprintStore.IsCurrent(previous, stamp)
                    ? previous!
                    : ModFootprintAnalyzer.Analyze(entries);

                // Keyed on what was just scanned rather than merged into what was loaded, so a mod
                // the user has since removed drops out of the file instead of accumulating there.
                current[key] = footprint;

                results.Add(new ModFootprintResult(card.Name, card.InstalledVersion, card.IsDisabled, footprint));
            }

            _store.Save(current);
            return results;
        });
    }
}
