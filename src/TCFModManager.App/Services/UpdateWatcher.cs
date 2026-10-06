using System.Net.Http;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

//
// Update notifications' timer (D4, §5): every so often, ask The Forge about what is installed,
// patch what it says moved into the catalog and addon caches, and announce any update that is news.
//
// The check never decides what counts as an update. It rebuilds the Installed page's own cards
// (BuildFrom) over the patched caches and takes the ones showing an arrow, so a notification can
// never name an update Installed doesn't show (D2). Which of those are news is UpdateAnnouncer's
// call, in Core, where it is tested.
//
// Runs whichever page is open. The loop lives on the UI thread's context so every cache swap and
// event happens there, the same as a page's own scan; the disk scan and the matching go to the
// thread pool, and the requests are asynchronous, so nothing here holds the window up.
//
internal sealed class UpdateWatcher
{
    private readonly SettingsService _settings = new();
    private readonly UpdateNotificationStore _store = new();
    private readonly UpdateCheckService _checker = new(AppServices.SpModApi);

    private CancellationTokenSource? _loop;
    private bool _checking;

    // The arrows the last check left showing, so the pages are only told when they changed.
    private HashSet<(int ModId, bool IsAddon, string Version)>? _lastShown;

    //
    // Raised on the UI thread after a check that changed which installed mods show an update, or
    // at which version. Installed rescans if it has been opened; Browse redraws its status dots.
    //
    public event EventHandler? UpdatesFound;

    public bool IsRunning => _loop is not null;

    //
    // Starts the timer if the feature is on, or restarts it on a new interval. The first check
    // runs one interval from now - never at launch (R2), so opening the app never raises a toast.
    // Must be called on the UI thread.
    //
    public void Start()
    {
        Stop();

        var settings = _settings.Load().UpdateNotifications;
        if (!settings.Enabled) return;

        var interval = settings.Interval.ToTimeSpan();
        _loop = new CancellationTokenSource();
        _ = RunAsync(interval, _loop.Token);

        AppLog.Info("Updates", $"checking every {interval:g}, first check at {DateTime.Now + interval:HH:mm}");
    }

    public void Stop()
    {
        if (_loop is null) return;

        _loop.Cancel();
        _loop.Dispose();
        _loop = null;

        AppLog.Info("Updates", "checks stopped");
    }

    //
    // Called when the feature is switched on: the next check records what's already available
    // without announcing it (D6), and the timer starts.
    //
    public void SwitchedOn()
    {
        _store.ResetBaseline();
        UpdateToasts.Listen();
        Start();
    }

    //
    // Runs a check straight away - the Options page's Check now, and the tray's "Check for updates
    // now" (§8a, step 5). Counts exactly like a timer tick: a baseline check announces nothing, and
    // what it announces isn't announced again. The timer keeps its own schedule.
    //
    public Task<UpdateCheckOutcome> CheckNowAsync() => CheckAsync(_loop?.Token ?? CancellationToken.None);

    private async Task RunAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await CheckAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Switched off, or the interval changed and a new loop has started.
        }
    }

    private async Task<UpdateCheckOutcome> CheckAsync(CancellationToken ct)
    {
        // A tick that lands while the previous check is still going is skipped, not queued (§5).
        if (_checking) return new UpdateCheckOutcome(UpdateCheckResult.AlreadyChecking);
        _checking = true;

        try
        {
            return await CheckCoreAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SpModApiRateLimitedException ex)
        {
            // Nothing is retried within the tick; the next one tries again (§5).
            AppLog.Warn("Updates", $"check skipped: The Forge is rate limiting ({ex.Message}), trying again next time");
            return new UpdateCheckOutcome(UpdateCheckResult.RateLimited);
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException or TaskCanceledException)
        {
            // Offline, or The Forge is down. Skipped, not caught up later.
            AppLog.Warn("Updates", $"check skipped: couldn't reach The Forge ({ex.Message})");
            return new UpdateCheckOutcome(UpdateCheckResult.Offline);
        }
        catch (Exception ex)
        {
            // A check that fails for any other reason must not take the timer down with it.
            AppLog.Error("Updates", "check failed", ex);
            return new UpdateCheckOutcome(UpdateCheckResult.Failed);
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task<UpdateCheckOutcome> CheckCoreAsync(CancellationToken ct)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;

        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(sptVersion))
        {
            AppLog.Info("Updates", "check skipped: no SPT install set");
            return new UpdateCheckOutcome(UpdateCheckResult.NoInstall);
        }

        // An install or a list apply in progress would be scanned half-placed (§5).
        if (AppServices.DownloadQueue.Items.Any(i => !i.IsFinished))
        {
            AppLog.Info("Updates", "check skipped: the download queue is busy");
            return new UpdateCheckOutcome(UpdateCheckResult.QueueBusy);
        }

        await AppServices.ModCache.EnsureLoadedAsync(ct);
        await AppServices.Addons.EnsureLoadedAsync(ct);

        var records = AppServices.InstallManifest.Load().ModsFor(installPath);

        var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath), ct);
        var cards = await BuildCardsAsync(scanned, sptVersion, records, ct);

        // Only matched mods with a version can be asked about. Addons aren't covered by the
        // endpoint (D3) and are re-read below instead.
        var mods = cards
            .Where(c => !c.IsAddon && c.ModId is not null && !string.IsNullOrWhiteSpace(c.InstalledVersion))
            .Select(c => (c.ModId!.Value, c.InstalledVersion!))
            .DistinctBy(m => m.Item1)
            .ToList();

        var addonIds = cards
            .Where(c => c.IsAddon && c.ModId is not null)
            .Select(c => c.ModId!.Value)
            .Distinct()
            .ToList();

        // The mods with an update, and the ones The Forge couldn't place - a hand install whose
        // version came from its files and matches no release (R7). Both are re-read in one go.
        var heldBackTicket = AppServices.HeldBack.Begin();
        var answer = mods.Count == 0 ? null : await _checker.FindUpdatedModsAsync(mods, sptVersion, ct);
        var refetch = answer?.ToRefetch ?? [];

        // Fork (1.3.0, as TCF be28c66): the same answer says which updates would break another
        // installed mod. Taken in first, so none of those is announced below. Only an answer about the
        // whole install in one request: split into several, a blocker in one part can't hold back an
        // update in another, and Subscribed items' own (single) check stands instead.
        if (answer is { Requests: 1 }) AppServices.HeldBack.Apply(heldBackTicket, answer.Blocked, answer.Incompatible, sptVersion);

        var patched = false;
        if (refetch.Count > 0) patched |= AppServices.ModCache.Patch(await _checker.FetchModsAsync(refetch, ct));
        if (addonIds.Count > 0) patched |= AppServices.Addons.Patch(await _checker.FetchAddonsAsync(addonIds, ct));

        // Rebuilt over the same scan, so the only thing that differs is what the caches now say.
        if (patched) cards = await BuildCardsAsync(scanned, sptVersion, records, ct);

        // Exactly the cards Installed shows an arrow on: a disabled mod's arrow is hidden (§7).
        // Held-back updates are not: Update all leaves them out too.
        var available = cards
            .Where(c => c.UpdateAvailable == true && !c.IsDisabled && c.ModId is not null)
            .Where(c => c.IsAddon || !AppServices.HeldBack.Holds(c.ModId, c.UpdateVersion ?? c.LatestPublishedVersion))
            .Select(c => new UpdateCandidate(
                c.ModId!.Value, c.IsAddon, c.DisplayTitle, c.UpdateVersion ?? c.LatestPublishedVersion ?? string.Empty))
            .ToList();

        var previous = _store.Load();
        var result = UpdateAnnouncer.Pick(available, previous, AppServices.DownloadLedger.Pending(), DateTimeOffset.Now);
        _store.Save(result.State);

        UpdateToasts.Show(result.New);

        AppLog.Info("Updates",
            $"checked {mods.Count} mods and {addonIds.Count} addons: {answer?.Updated.Count ?? 0} moved on The Forge, "
            + $"{answer?.NotRecognised.Count ?? 0} read directly, "
            + $"{available.Count} with an update, {result.New.Count} announced"
            + (previous.BaselineTaken ? string.Empty : " (baseline - nothing is announced on the first check)"));

        var shown = available.Select(c => (c.ModId, c.IsAddon, c.Version)).ToHashSet();
        // A patched cache always goes to the pages - a release that isn't an update still changes
        // a card's "Latest published" line - and so does a different set of arrows.
        var changed = patched || (_lastShown is not null && !shown.SetEquals(_lastShown));
        _lastShown = shown;

        if (changed) UpdatesFound?.Invoke(this, EventArgs.Empty);

        return new UpdateCheckOutcome(UpdateCheckResult.Checked, available.Count, result.New.Count, !previous.BaselineTaken);
    }

    private static Task<List<InstalledModCardViewModel>> BuildCardsAsync(
        IReadOnlyList<InstalledMod> scanned, string sptVersion, IReadOnlyList<InstalledModRecord> records, CancellationToken ct)
    {
        // Read here, on the UI thread, so a cache swap landing mid-build can't change the list under it.
        var catalog = AppServices.ModCache.AllMods;
        var addons = AppServices.Addons.AllAddons;

        return Task.Run(() => InstalledModCardViewModel.BuildFrom(scanned, catalog, sptVersion, records, addons), ct);
    }
}

public enum UpdateCheckResult
{
    Checked,
    AlreadyChecking,
    NoInstall,
    QueueBusy,
    Offline,
    RateLimited,
    Failed,
}

// What one check came to. The counts are only meaningful when Result is Checked.
internal sealed record UpdateCheckOutcome(
    UpdateCheckResult Result, int Available = 0, int Announced = 0, bool WasBaseline = false);
