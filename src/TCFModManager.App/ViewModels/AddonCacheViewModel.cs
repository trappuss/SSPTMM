using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// 
// Shared, app-lifetime cache of every addon published on sp-mod.com, backed by AddonCacheStore on
// disk with a background refresh. There are under a hundred addons in total against thousands of
// mods, so the whole set is held in memory and every lookup below is served from it - no addon
// screen ever waits on the network.
// 
public partial class AddonCacheViewModel : LocalizedViewModel
{
    private readonly AddonCacheService _cacheService = new(AppServices.SpModApi);
    private readonly AddonCacheStore _store = new();
    private Task<List<Addon>>? _loadTask;

    // Addon ids grouped by the mod they attach to, rebuilt whenever AllAddons is replaced.
    private IReadOnlyDictionary<int, List<Addon>> _byParent = new Dictionary<int, List<Addon>>();

    public IReadOnlyList<Addon> AllAddons { get; private set; } = [];

    public bool IsLoaded => _loadTask?.IsCompletedSuccessfully == true;

    // Loads the addon catalog (from disk if cached, otherwise a live fetch) on first call; later calls await the same task.
    //
    // A load that failed (offline, the site down) or was cancelled is not kept: the next call tries
    // again, rather than every page staying empty until Refresh is pressed. The shared load is not
    // tied to the first caller's token - one page being left must not cancel it for the others.
    //
    public Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        // Not straight away, though: every page and queued install asks, and a site that hangs
        // rather than refusing would make each of them wait out the timeout in turn.
        if (_loadTask is { IsFaulted: true } or { IsCanceled: true } && DateTime.UtcNow - _failedAt > RetryAfter)
            _loadTask = null;

        if (_loadTask is null)
        {
            _loadTask = MarkingFailureAsync(LoadAsync(CancellationToken.None));
        }
        else if (_loadTask.IsCompletedSuccessfully)
        {
            RefreshIfStale();
        }

        return ct.CanBeCanceled ? _loadTask.WaitAsync(ct) : _loadTask;
    }

    // When the last load failed, and how long until another is tried by itself (Refresh tries at once).
    private DateTime _failedAt;

    // Set before anyone awaiting the load sees it fail, so a call straight after waits its turn too.
    private async Task<List<Addon>> MarkingFailureAsync(Task<List<Addon>> load)
    {
        try
        {
            return await load;
        }
        catch
        {
            _failedAt = DateTime.UtcNow;
            throw;
        }
    }

    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(60);

    // Fork (1.4.9): read again, quietly, when asked for and older than a saved copy counts as fresh -
    // as ModCacheViewModel does, and for the same reason.
    private void RefreshIfStale()
    {
        lock (_refreshLock)
        {
            if (_refreshing is { IsCompleted: false } || _fetchedAt is not { } at) return;

            // (A copy dated in the future - the clock was ahead, then put right - counts as old.)
            var age = DateTimeOffset.UtcNow - at;
            if (age <= ModCacheViewModel.FreshFor && age >= TimeSpan.Zero) return;
            if (DateTime.UtcNow - _refreshFailedAt < RetryAfter) return;

            _refreshing = RefreshInBackgroundAsync();
        }
    }

    private readonly object _refreshLock = new();
    private Task? _refreshing;
    private DateTime _refreshFailedAt;

    // When the whole list was last read from sp-mod.com; null before it has loaded.
    private DateTimeOffset? _fetchedAt;

    // 
    // The addons attached to a mod, most downloaded first. An addon whose parent isn't in the mod
    // catalog is never returned simply because nothing asks for it - which is also how detached
    // addons and addons to mods below the SPT cache floor stay out of the UI without a filter of
    // their own.
    // 
    public IReadOnlyList<Addon> ForMod(int modId) =>
        _byParent.TryGetValue(modId, out var addons) ? addons : [];

    public int CountFor(int modId) => ForMod(modId).Count;

    public Addon? ById(int addonId) => AllAddons.FirstOrDefault(a => a.Id == addonId);

    // Forces a fresh live fetch, replacing both the in-memory set and the disk cache.
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var addons = await _cacheService.FetchAllAsync(null, ct);
        Publish(addons);
        _fetchedAt = DateTimeOffset.UtcNow;
        _ = Task.Run(() => _store.Save(addons), CancellationToken.None);
        _loadTask = Task.FromResult(addons);
    }

    //
    // Swaps in fresh listings for the installed addons, found by the update watcher (D3). Same
    // rule as ModCacheViewModel.Patch: an addon the cache doesn't hold is not added.
    //
    // Every installed addon is re-read on every check, so a listing is only swapped in when its
    // releases changed. Otherwise each check would raise AddonsChanged and redraw Browse under
    // whoever is reading it, hourly, for nothing.
    //
    public bool Patch(IReadOnlyList<Addon> fresh)
    {
        if (fresh.Count == 0 || AllAddons.Count == 0) return false;

        var byId = fresh.GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.Last());
        var replaced = 0;

        var patched = AllAddons
            .Select(a =>
            {
                if (!byId.TryGetValue(a.Id, out var listing) || ReleasesOf(listing) == ReleasesOf(a)) return a;
                replaced++;
                return listing;
            })
            .ToList();

        if (replaced == 0) return false;

        Publish(patched);
        _loadTask = Task.FromResult(patched);

        // Under the date the whole list was read: a few listings swapped in do not make it fresh.
        var fetchedAt = _fetchedAt;
        _ = Task.Run(() => _store.Save(patched, fetchedAt), CancellationToken.None);
        return true;
    }

    private static string ReleasesOf(Addon addon) =>
        string.Join(',', (addon.Versions ?? []).Select(v => $"{v.Id}:{v.Version}:{v.ContentLength}:{v.Link}"));

    private async Task<List<Addon>> LoadAsync(CancellationToken ct)
    {
        var cached = await Task.Run(_store.Load, ct);
        if (cached is not null)
        {
            AppLog.Debug("Addons", $"disk cache hit, {cached.Addons.Count} addons");
            Publish(cached.Addons);

            // A copy from a few minutes ago - the app restarted - is not fetched again (Refresh does).
            _fetchedAt = cached.FetchedAt;
            RefreshIfStale();
            return cached.Addons;
        }

        AppLog.Debug("Addons", "disk cache miss, starting live fetch");
        var addons = await _cacheService.FetchAllAsync(null, ct);
        Publish(addons);
        _fetchedAt = DateTimeOffset.UtcNow;
        _ = Task.Run(() => _store.Save(addons), CancellationToken.None);
        return addons;
    }

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            var fresh = await Task.Run(() => _cacheService.FetchAllAsync(null, CancellationToken.None)).ConfigureAwait(false);
            // Nothing at all is not a list to replace one with.
            if (fresh.Count == 0 && AllAddons.Count > 0)
            {
                _refreshFailedAt = DateTime.UtcNow;
                AppLog.Warn("Addons", "sp-mod.com listed no addons; keeping the list in hand");
                return;
            }

            var fetchedAt = DateTimeOffset.UtcNow;
            _store.Save(fresh, fetchedAt);

            // Swapped in on the UI thread, where the pages read it and Patch and Refresh write it.
            void Swap()
            {
                var differs = fresh.Count != AllAddons.Count
                    || !fresh.Select(a => (a.Id, ReleasesOf(a))).ToHashSet().SetEquals(AllAddons.Select(a => (a.Id, ReleasesOf(a))));

                _fetchedAt = fetchedAt;
                _loadTask = Task.FromResult(fresh);

                // Only a list that differs is drawn; the counts in an unchanged one can wait.
                if (differs) Publish(fresh);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) Swap();
            else _ = dispatcher.BeginInvoke(Swap);
        }
        catch (Exception ex)
        {
            // Offline, the site down, an answer that did not read: the copy in hand stays.
            _refreshFailedAt = DateTime.UtcNow;
            AppLog.Warn("Addons", $"couldn't read sp-mod.com's addons again ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private void Publish(List<Addon> addons)
    {
        AllAddons = addons;
        _byParent = addons
            .Where(a => a.ModId is not null)
            .GroupBy(a => a.ModId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.Downloads ?? 0).ToList());

        if (AddonsChanged is null) return;

        // The background refresh raises this off the UI thread, and every subscriber redraws
        // something bound to it.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) AddonsChanged.Invoke(this, EventArgs.Empty);
        else dispatcher.BeginInvoke(() => AddonsChanged?.Invoke(this, EventArgs.Empty));
    }

    // Raised after AllAddons is replaced, so Browse can refresh its cards' addon badges.
    public event EventHandler? AddonsChanged;
}
