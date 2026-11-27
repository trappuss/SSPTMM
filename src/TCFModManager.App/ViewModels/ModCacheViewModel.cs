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

    // Fork (1.4.8): when the whole list was last fetched from sp-mod.com; null before it has loaded.
    public DateTimeOffset? FetchedAt { get; private set; }

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
        else if (_loadTask.IsCompletedSuccessfully)
        {
            RefreshIfStale();
        }

        return ct.CanBeCanceled ? _loadTask.WaitAsync(ct) : _loadTask;
    }

    //
    // Fork (1.4.9): the list is read again, quietly, whenever something asks for it and the copy in
    // hand is older than FreshFor. It used to be read at start only, so an app left open showed that
    // morning's sp-mod.com until Browse's Refresh was pressed: a mod published since was nowhere,
    // and Rescan on Subscribed items only read the SPT folder again.
    //
    private void RefreshIfStale()
    {
        lock (_refreshLock)
        {
            if (_refreshing is { IsCompleted: false } || IsLoading) return;
            if (FetchedAt is not { } at) return;

            // (A copy dated in the future - the clock was ahead, then put right - counts as old.)
            var age = DateTimeOffset.UtcNow - at;
            if (age <= FreshFor && age >= TimeSpan.Zero) return;
            if (DateTime.UtcNow - _refreshFailedAt < RetryAfter) return;

            _refreshing = RefreshInBackgroundAsync();
        }
    }

    private readonly object _refreshLock = new();
    private Task? _refreshing;
    private DateTime _refreshFailedAt;

    // Raised on the UI thread when a quiet refresh brought a list that differs from the one before
    // it - a mod more or fewer, or a release changed - so the pages can draw it.
    public event EventHandler? ModsChanged;

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
            AllMods = WithOlderSptVersions(mods);
            FetchedAt = DateTimeOffset.UtcNow;
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

        AllMods = WithOlderSptVersions(patched);
        _loadTask = Task.FromResult(patched);

        // Under the date the whole list was read: a few listings swapped in do not make it fresh.
        var fetchedAt = FetchedAt;
        _ = Task.Run(() => _store.Save(patched, fetchedAt));

        AppLog.Debug("Catalog", $"Patch: replaced {replaced} of {fresh.Count} fetched listings");
        return true;
    }

    private static string ReleasesOf(Mod mod) =>
        string.Join(',', (mod.Versions ?? []).Where(v => !v.AddedForSpt).Select(v => $"{v.Id}:{v.Version}:{v.SptVersionConstraint}"));

    //
    // Fork (1.4.9): the version for this install's SPT, for the mods whose ten newest do not reach
    // back that far (OlderSptVersions). The answers already known go in at once, on the list as it
    // is handed over; the mods not asked about yet are asked in the background, a few a second, and
    // the pages are told once when that added anything.
    //
    private List<Mod> WithOlderSptVersions(List<Mod> mods)
    {
        var spt = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(spt)) return mods;

        _olderAnswers ??= OlderSptVersions.Load();
        OlderSptVersions.Apply(mods, _olderAnswers, spt);

        if (_askingOlder is null or { IsCompleted: true }
            && OlderSptVersions.ToAsk(mods, _olderAnswers, spt, DateTimeOffset.UtcNow) is { Count: > 0 } toAsk)
        {
            _askingOlder = AskOlderAsync(toAsk, spt);
        }

        return mods;
    }

    private OlderSptVersions.Answers? _olderAnswers;
    private Task? _askingOlder;

    private async Task AskOlderAsync(List<Mod> toAsk, string spt)
    {
        var answers = _olderAnswers!;
        var asked = 0;
        var found = 0;

        try
        {
            var waits = 0;
            foreach (var mod in toAsk)
            {
                // Not beside a read of the whole list: the two together are what sp-mod.com limits.
                while (_refreshing is { IsCompleted: false } || IsLoading) await Task.Delay(1000);

                PagedResult<ModVersion> versions;
                while (true)
                {
                    try
                    {
                        versions = await AppServices.SpModApi.GetModVersionsAsync(
                            mod.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            new ModVersionsQuery { FilterSptVersion = spt, Sort = "-version", PerPage = 1 });
                        break;
                    }
                    catch (SpModApiRateLimitedException ex) when (waits < 5)
                    {
                        // Asked to slow down: waits as long as it says, a few times over, then gives
                        // up until the list is next handed over.
                        waits++;
                        await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(20));
                    }
                }

                var version = versions.Data.FirstOrDefault();
                answers.Set(mod.Id, spt, version is null ? null : new ModVersionSummary
                {
                    Id = version.Id,
                    Version = version.Version,
                    SptVersionConstraint = version.SptVersionConstraint,
                    Downloads = version.Downloads,
                    PublishedAt = version.PublishedAt,
                    CreatedAt = version.CreatedAt,
                    ContentLength = version.ContentLength,
                    FikaCompatibility = version.FikaCompatibility,
                }, DateTimeOffset.UtcNow);

                asked++;
                if (version is not null) found++;

                await Task.Delay(250);
            }
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Rate limited, offline, or an SPT version sp-mod.com does not list: what was answered
            // is kept, and the rest is asked the next time the list is handed over.
            AppLog.Info("Catalog", $"stopped asking for versions for SPT {spt} after {asked} of {toAsk.Count}: {ex.Message}");
        }

        if (asked == 0) return;

        _ = Task.Run(() => OlderSptVersions.Save(answers));
        AppLog.Info("Catalog", $"asked sp-mod.com for the SPT {spt} version of {asked} mod(s) whose ten newest have none: {found} found");

        if (OlderSptVersions.Apply(AllMods, answers, spt) > 0) ModsChanged?.Invoke(this, EventArgs.Empty);
    }

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
            AllMods = WithOlderSptVersions(cached.Mods);
            FetchedAt = cached.FetchedAt;

            // A copy from a few minutes ago - the app restarted - is not fetched again (Refresh does).
            AppLog.Debug("Catalog", $"LoadAsync: saved copy is from {cached.FetchedAt:g}");
            RefreshIfStale();
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
            AllMods = WithOlderSptVersions(mods);
            FetchedAt = DateTimeOffset.UtcNow;

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

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            var fresh = await Task.Run(() => _cacheService.FetchAllAsync(null, CancellationToken.None)).ConfigureAwait(false);

            // Nothing at all is not a list to replace one with.
            if (fresh.Count == 0)
            {
                _refreshFailedAt = DateTime.UtcNow;
                AppLog.Warn("Catalog", "sp-mod.com listed no mods; keeping the list in hand");
                return;
            }

            var fetchedAt = DateTimeOffset.UtcNow;
            _store.Save(fresh, fetchedAt);

            // Swapped in on the UI thread, where the pages read it and Patch and Refresh write it.
            void Publish()
            {
                var differs = Differs(AllMods, fresh);
                AllMods = WithOlderSptVersions(fresh);
                FetchedAt = fetchedAt;
                _loadTask = Task.FromResult(fresh);

                AppLog.Info("Catalog", $"read sp-mod.com again: {fresh.Count} mods, {(differs ? "changed" : "nothing changed")}");
                if (differs) ModsChanged?.Invoke(this, EventArgs.Empty);
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) Publish();
            else _ = dispatcher.BeginInvoke(Publish);
        }
        catch (Exception ex)
        {
            // Offline, the site down, an answer that did not read: the copy in hand stays, and the
            // next one to ask after RetryAfter tries again. Logged, where it used to pass unseen.
            _refreshFailedAt = DateTime.UtcNow;
            AppLog.Warn("Catalog", $"couldn't read sp-mod.com again ({ex.GetType().Name}: {ex.Message}); keeping the list from {FetchedAt:g}");
        }
    }

    // A mod more or fewer, or one whose releases changed. Download counts move on every read and
    // are not a reason to redraw a page under whoever is reading it.
    private static bool Differs(IReadOnlyList<Mod> before, IReadOnlyList<Mod> after)
    {
        if (before.Count != after.Count) return true;

        var releases = before.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => ReleasesOf(g.First()));
        return after.Any(m => !releases.TryGetValue(m.Id, out var was) || was != ReleasesOf(m));
    }
}
