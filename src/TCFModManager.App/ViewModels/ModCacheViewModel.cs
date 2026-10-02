using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime cache of the sp-mod.com catalog, backed by ModCacheStore on disk with a background refresh. IsLoading/LoadedCount/TotalCount track live fetch progress.
public partial class ModCacheViewModel : LocalizedViewModel
{
    private readonly ModCacheService _cacheService = new(AppServices.SpModApi);
    private readonly ModCacheStore _store = new();
    private Task<List<Mod>>? _loadTask;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _loadedCount;

    [ObservableProperty]
    private int _totalCount;

    public IReadOnlyList<Mod> AllMods { get; private set; } = [];

    // How old a saved catalog can be and still be used without fetching it again at start.
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(20);

    public bool IsLoaded => _loadTask?.IsCompletedSuccessfully == true;

    // Loads the catalog (from disk if cached, otherwise a live fetch) on first call; later calls await the same task.
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
    private async Task<List<Mod>> MarkingFailureAsync(Task<List<Mod>> load)
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

    // Forces a fresh live fetch of the catalog, driving IsLoading/LoadedCount/TotalCount for the loading overlay.
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        AppLog.Debug("Catalog", "RefreshAsync: start");
        IsLoading = true;
        LoadedCount = 0;
        TotalCount = 0;
        try
        {
            var progress = new Progress<(int Loaded, int? Total)>(p =>
            {
                LoadedCount = p.Loaded;
                TotalCount = p.Total ?? 0;
            });

            var mods = await _cacheService.FetchAllAsync(progress, ct);
            AppLog.Debug("Catalog", $"RefreshAsync: live fetch done, {mods.Count} mods");
            AllMods = mods;
            _ = Task.Run(() => _store.Save(mods));

            // Replace the memoized task so future EnsureLoadedAsync calls see this fresh fetch.
            _loadTask = Task.FromResult(mods);
        }
        finally
        {
            IsLoading = false;
            AppLog.Debug("Catalog", "RefreshAsync: end (IsLoading = false)");
        }
    }

    //
    // Swaps in fresh listings for a handful of mods, found by the update watcher, without a whole
    // catalog fetch. A listing that isn't in the catalog is left out rather than added: the catalog
    // is the set the full fetch keeps (above the SPT floor), and the watcher only asks about mods
    // that are installed, so a missing one is one the catalog chose not to hold.
    //
    // Only a listing whose releases changed is swapped in. Some of what the watcher re-reads is
    // unchanged on every check (hand installs The Forge couldn't place), and rewriting a couple of
    // megabytes of cache for those each time would be for nothing.
    //
    // Returns whether anything was replaced. The memoized task is replaced as well, so a page
    // awaiting EnsureLoadedAsync afterwards sees the patched list.
    //
    public bool Patch(IReadOnlyList<Mod> fresh)
    {
        if (fresh.Count == 0 || AllMods.Count == 0) return false;

        var byId = fresh.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last());
        var replaced = 0;

        var patched = AllMods
            .Select(m =>
            {
                if (!byId.TryGetValue(m.Id, out var listing) || ReleasesOf(listing) == ReleasesOf(m)) return m;
                replaced++;
                return listing;
            })
            .ToList();

        if (replaced == 0) return false;

        AllMods = patched;
        _loadTask = Task.FromResult(patched);
        _ = Task.Run(() => _store.Save(patched));

        AppLog.Debug("Catalog", $"Patch: replaced {replaced} of {fresh.Count} fetched listings");
        return true;
    }

    private static string ReleasesOf(Mod mod) =>
        string.Join(',', (mod.Versions ?? []).Select(v => $"{v.Id}:{v.Version}:{v.SptVersionConstraint}"));

    private async Task<List<Mod>> LoadAsync(CancellationToken ct)
    {
        AppLog.Debug("Catalog", "LoadAsync: start");

        // Off the UI thread: the cache file is a couple of megabytes of JSON for ~3000 mods, and
        // deserializing it inline was a visible stall on every launch with a warm cache.
        var cached = await Task.Run(_store.Load, ct);
        if (cached is not null)
        {
            // Use the disk cache immediately, then refresh in the background.
            AppLog.Debug("Catalog", $"LoadAsync: disk cache hit, {cached.Mods.Count} mods");
            AllMods = cached.Mods;

            // A copy from a few minutes ago - the app restarted - is not fetched again (Refresh does).
            // (A copy dated in the future - the clock was ahead, then put right - counts as old.)
            var age = DateTimeOffset.UtcNow - cached.FetchedAt;
            if (age > FreshFor || age < TimeSpan.Zero) _ = RefreshInBackgroundAsync(ct);
            else AppLog.Debug("Catalog", $"LoadAsync: saved copy is from {cached.FetchedAt:t}; not fetching again yet");
            return cached.Mods;
        }

        AppLog.Debug("Catalog", "LoadAsync: disk cache miss, starting live fetch");
        IsLoading = true;
        LoadedCount = 0;
        TotalCount = 0;
        try
        {
            var progress = new Progress<(int Loaded, int? Total)>(p =>
            {
                AppLog.Debug("Catalog", $"LoadAsync: progress {p.Loaded}/{p.Total}");
                LoadedCount = p.Loaded;
                TotalCount = p.Total ?? 0;
            });

            // Stays on the UI thread since IsLoading below is a WPF-bound property.
            var mods = await _cacheService.FetchAllAsync(progress, ct);
            AppLog.Debug("Catalog", $"LoadAsync: live fetch done, {mods.Count} mods");
            AllMods = mods;

            // Save off the UI thread.
            _ = Task.Run(() => _store.Save(mods));

            return mods;
        }
        finally
        {
            IsLoading = false;
            AppLog.Debug("Catalog", "LoadAsync: end (IsLoading = false)");
        }
    }

    private async Task RefreshInBackgroundAsync(CancellationToken ct)
    {
        try
        {
            var fresh = await _cacheService.FetchAllAsync(null, ct).ConfigureAwait(false);
            AllMods = fresh;
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
}
