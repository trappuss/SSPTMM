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
        _ = Task.Run(() => _store.Save(addons), CancellationToken.None);
        _loadTask = Task.FromResult(addons);
    }

    private async Task<List<Addon>> LoadAsync(CancellationToken ct)
    {
        var cached = await Task.Run(_store.Load, ct);
        if (cached is not null)
        {
            AppLog.Debug("Addons", $"disk cache hit, {cached.Addons.Count} addons");
            Publish(cached.Addons);

            // A copy from a few minutes ago - the app restarted - is not fetched again (Refresh does).
            // (A copy dated in the future - the clock was ahead, then put right - counts as old.)
            var age = DateTimeOffset.UtcNow - cached.FetchedAt;
            if (age > ModCacheViewModel.FreshFor || age < TimeSpan.Zero) _ = RefreshInBackgroundAsync(ct);
            else AppLog.Debug("Addons", $"saved copy is from {cached.FetchedAt:t}; not fetching again yet");
            return cached.Addons;
        }

        AppLog.Debug("Addons", "disk cache miss, starting live fetch");
        var addons = await _cacheService.FetchAllAsync(null, ct);
        Publish(addons);
        _ = Task.Run(() => _store.Save(addons), CancellationToken.None);
        return addons;
    }

    private async Task RefreshInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            var fresh = await _cacheService.FetchAllAsync(null, ct).ConfigureAwait(false);
            Publish(fresh);
            _store.Save(fresh);
        }
        catch (OperationCanceledException)
        {
            // Cancelled or shutting down.
        }
        catch (SpModApiException)
        {
            // Best-effort refresh; ignore API errors.
        }
        catch (HttpRequestException)
        {
            // Best-effort refresh; ignore network errors.
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
