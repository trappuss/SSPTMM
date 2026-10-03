using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

//
// App-lifetime download queue. Each item goes through three stages:
//
//   1. Prepare, one item at a time in the order queued: its version is looked up and its
//      dependencies asked about - so questions come one after another, in order.
//   2. Download, up to MaxDownloads at once, started as soon as an item is prepared - or read
//      from the archives kept from before (ModArchiveCache), with nothing downloaded.
//   3. Install, one item at a time in the order queued, each as soon as its download is there -
//      installs write into the same folders, so they never overlap.
//
// So a mod list of forty downloads three at a time while the first ones install, where it used to
// download and install one after another.
//
public sealed partial class DownloadQueueViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Stage 1's queue, and stage 3's.
    private readonly Channel<DownloadQueueItemViewModel> _channel = Channel.CreateUnbounded<DownloadQueueItemViewModel>();
    private readonly Channel<InstallTurn> _installs = Channel.CreateUnbounded<InstallTurn>();

    //
    // One item's turn to install: the attempt it belongs to (its token) and that attempt's archive.
    // Carried together because a retry starts a NEW attempt on the same item - a turn left in the
    // channel by the attempt it replaced must not install the old archive, or settle the new
    // attempt with the old one's cancellation.
    //
    private sealed record InstallTurn(DownloadQueueItemViewModel Item, CancellationToken Token, Task<string> Archive);

    // How many downloads run at once. Three keeps a slow host from holding up the rest without
    // opening more connections at once than a browser would to one site.
    private const int MaxDownloads = 3;
    private readonly SemaphoreSlim _downloadSlots = new(MaxDownloads);

    // Archives downloaded before, and the ones in use now (being written, or waiting to install),
    // which tidying never deletes. Touched on the UI thread only.
    private readonly ModArchiveCache _archives = new(ModArchiveCache.DefaultDirectory, ModArchiveCache.DefaultBudget);
    // Counted: two cards can use one kept archive (the same version queued twice).
    private readonly Dictionary<string, int> _archivesInUse = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlySet<string> ArchivesInUse => _archivesInUse.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void Use(string archive) => _archivesInUse[archive] = _archivesInUse.GetValueOrDefault(archive) + 1;

    // True when nothing else is using it now.
    private bool Unuse(string archive)
    {
        if (!_archivesInUse.TryGetValue(archive, out var count)) return true;

        if (count > 1)
        {
            _archivesInUse[archive] = count - 1;
            return false;
        }

        _archivesInUse.Remove(archive);
        return true;
    }

    // A download that fails in a way that can pass (the connection dropped, the host busy) is
    // tried again after these waits, as pictures are.
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)];

    public ObservableCollection<DownloadQueueItemViewModel> Items { get; } = [];

    //
    // The whole queue's state, one value per box: how far through it is, how much is left to fetch
    // and how long that should take. Worth having at all because a mod list can queue forty items
    // at once - per-card progress answers "how is this one doing", not "how long until I can play".
    //
    // Split into separate values rather than one line so DownloadsPage can give each its own fixed
    // container; run together they shuffle sideways every time a figure changes width.
    //
    [ObservableProperty]
    private bool _hasSummary;

    // "1 of 2 done"
    [ObservableProperty]
    private string _summaryProgress = NoValue;

    // "85.3 MB"
    [ObservableProperty]
    private string _summaryRemaining = NoValue;

    // How many queued items The Forge gave no size for, so the remaining figure never silently
    // under-reports. Its own box rather than a note on the remaining one, which would make that box
    // change width as items resolve.
    [ObservableProperty]
    private bool _hasUnsized;

    [ObservableProperty]
    private string _summaryUnsized = NoValue;

    // "11s"
    [ObservableProperty]
    private string _summaryEta = NoValue;

    private const string NoValue = "\u2014";

    // ---- the downloads bar along the bottom of the window (Views/DownloadsBar) -------------------

    // "DOWNLOADING" / "INSTALLING" while the queue has work, "DOWNLOADS" otherwise.
    [ObservableProperty]
    private string _barTitle = Strings.Downloads_BarIdle;

    // What is happening now - "SAIN 4.4.3 - 2 of 5 items complete" - or "Manage" when nothing is.
    [ObservableProperty]
    private string _barDetail = Strings.Downloads_BarManage;

    // How far the whole queue is, 0 to 1: finished items count whole, the rest by their own progress.
    [ObservableProperty]
    private double _barFraction;

    [ObservableProperty]
    private bool _barActive;

    // Raised after an item finishes installing successfully. BrowseViewModel subscribes to refresh its cards' install/update status dots.
    public event EventHandler? ItemInstalled;

    public DownloadQueueViewModel()
    {
        _ = PrepareLoopAsync();
        _ = InstallLoopAsync();

        // Leftovers from downloads the app was closed during, and anything over the budget.
        Task.Run(() => _archives.Trim(new HashSet<string>()));
    }

    /// <summary>What the kept archives take up, in bytes.</summary>
    public long KeptDownloadsSize() => _archives.Size();

    /// <summary>Deletes the kept archives, except those the queue is using now.</summary>
    public void ClearKeptDownloads()
    {
        _archives.Clear(ArchivesInUse);
        AppLog.Info("Downloads", "kept downloads cleared");
    }

    // Adds a request to the end of the queue and returns immediately; the download/install
    // happens later when the worker reaches it. When <paramref name="dependencyOf"/> is set, the
    // new item is registered against it so cancelling that item cancels this one too.
    //
    // <paramref name="downloadOnly"/> saves the archive for the user to install instead (Monitor
    // mode). A dependency takes its parent's value, whatever is passed here.
    public DownloadQueueItemViewModel Enqueue(
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        bool checkDependencies = true,
        DownloadQueueItemViewModel? dependencyOf = null,
        long? totalBytes = null,
        bool downloadOnly = false,
        string? downloadSubfolder = null)
    {
        if (dependencyOf is not null)
        {
            downloadOnly = dependencyOf.DownloadOnly;
            downloadSubfolder = dependencyOf.DownloadSubfolder;
        }

        var item = new DownloadQueueItemViewModel(
            target, versionLabel, installPath, resolveVersion, checkDependencies, totalBytes,
            downloadOnly, downloadSubfolder);
        dependencyOf?.AddDependency(item);
        item.PropertyChanged += OnItemChanged;

        // How a failed card gets to go round again. The channel is the queue's, so the card asks
        // rather than writing to it - and an item that was never enqueued has no retry, which is
        // what CanRetry checks.
        item.Requeue = queued => _channel.Writer.TryWrite(queued);

        Items.Add(item);
        _channel.Writer.TryWrite(item);
        UpdateSummary();
        return item;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadQueueItemViewModel.Status)
            or nameof(DownloadQueueItemViewModel.Progress)
            or nameof(DownloadQueueItemViewModel.TotalBytes))
        {
            UpdateSummary();
        }
    }

    //
    // Sizes come from the catalog's content_length, so they are known for a mod list apply (which
    // resolves every version before queueing) and fill in one at a time for anything else. The
    // estimate is deliberately built from what is known rather than extrapolated over what isn't -
    // it says how much is left to fetch, and only adds a time once a real rate has been observed.
    //
    private void UpdateBar(List<DownloadQueueItemViewModel> unfinished)
    {
        if (unfinished.Count == 0)
        {
            BarActive = false;
            BarTitle = Strings.Downloads_BarIdle;
            BarDetail = Strings.Downloads_BarManage;
            BarFraction = 0;
            return;
        }

        // The one to name: an install in progress first (only one runs at a time), else the first
        // download, else the next one waiting - in queue order.
        var current = unfinished.FirstOrDefault(i => i.Status == DownloadQueueItemStatus.Installing)
            ?? unfinished.FirstOrDefault(i => i.Status == DownloadQueueItemStatus.Downloading)
            ?? unfinished[0];

        var done = Items.Count - unfinished.Count;

        BarActive = true;
        BarTitle = current.Status == DownloadQueueItemStatus.Installing ? Strings.Downloads_BarInstalling : Strings.Downloads_BarDownloading;
        BarDetail = Text(Strings.Downloads_BarDetailFormat, current.ModName, current.VersionLabel, done, Items.Count);
        BarFraction = Items.Count == 0 ? 0 : (done + unfinished.Sum(i => Math.Clamp(i.Progress, 0, 1))) / Items.Count;
    }

    private void UpdateSummary()
    {
        var unfinished = Items.Where(i => !i.IsFinished).ToList();

        UpdateBar(unfinished);

        if (unfinished.Count == 0)
        {
            HasSummary = false;
            HasUnsized = false;
            SummaryProgress = SummaryRemaining = SummaryEta = SummaryUnsized = NoValue;

            // Still notified on the way out: a queue that has just finished with failures in it is
            // exactly when the retry button has to appear.
            OnPropertyChanged(nameof(HasRetryable));
            return;
        }

        var done = Items.Count - unfinished.Count;
        var remaining = unfinished.Sum(i => i.RemainingBytes ?? 0);
        var unknown = unfinished.Count(i => i.RemainingBytes is null);

        HasSummary = true;
        SummaryProgress = Text(Strings.Downloads_SummaryProgressFormat, done, Items.Count);

        SummaryRemaining = remaining > 0 ? DownloadQueueItemViewModel.SizeLabel(remaining) : NoValue;

        HasUnsized = unknown > 0;
        // Just the count: the caption above this box already says what they are, so there is no
        // sentence for a plural to agree with.
        SummaryUnsized = unknown.ToString(CultureInfo.CurrentCulture);

        // Every download running now together - up to three at once.
        var rate = unfinished.Sum(i => i.BytesPerSecond ?? 0);
        SummaryEta = remaining > 0 && rate > 0
            ? DownloadQueueItemViewModel.RemainingLabel(TimeSpan.FromSeconds(remaining / rate))
            : NoValue;

        OnPropertyChanged(nameof(HasRetryable));
    }

    //
    // Every failed or cancelled item back on the queue, oldest first.
    //
    // Worth having as well as the per-card button because of how these actually fail: a mod list
    // apply queues dozens at once, and one flaky spell on The Forge takes out a run of them
    // together. Clicking Retry fourteen times is the same work with more chances to miss one.
    //
    public void RetryFailed()
    {
        foreach (var item in Items.Where(i => i.CanRetry).ToList()) item.RetryCommand.Execute(null);

        UpdateSummary();
    }

    // Whether anything is sitting there retryable, so the button can stay out of the way otherwise.
    public bool HasRetryable => Items.Any(i => i.CanRetry);

    // Removes every Completed/Failed/Cancelled card from the list; queued/in-progress items are left alone.
    public void ClearFinished()
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!Items[i].IsFinished) continue;

            Items[i].PropertyChanged -= OnItemChanged;
            Items.RemoveAt(i);
        }

        UpdateSummary();
    }

    // Stage 1, one item at a time in the order queued, for the whole session. A failed item
    // doesn't stop the loop.
    private async Task PrepareLoopAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            // Nothing that goes wrong with one item may stop the queue for the rest of the session.
            try
            {
                await PrepareAsync(item);
            }
            catch (Exception ex)
            {
                AppLog.Error("Downloads", $"preparing {item.ModName} failed: {ex}");
                Settle(item, ex);
            }
        }
    }

    // Stage 3, likewise.
    private async Task InstallLoopAsync()
    {
        await foreach (var turn in _installs.Reader.ReadAllAsync())
        {
            var item = turn.Item;
            try
            {
                await InstallAsync(turn);
            }
            catch (Exception ex)
            {
                AppLog.Error("Downloads", $"installing {item.ModName} failed: {ex}");
                Settle(item, ex);
            }
        }
    }

    // Looks the version up, asks about its dependencies, and starts its download (stage 2), then
    // hands it to the installs.
    private async Task PrepareAsync(DownloadQueueItemViewModel item)
    {
        // Cancelled while it sat in the queue behind something else.
        if (item.Status == DownloadQueueItemStatus.Cancelled || item.Token.IsCancellationRequested)
        {
            item.Status = DownloadQueueItemStatus.Cancelled;
            item.StatusMessage = Strings.Downloads_CancelledBeforeStart;
            return;
        }

        // Already under way: an item cancelled while waiting and then retried is in the queue
        // twice, and the second time round is not a second attempt.
        if (item.Status != DownloadQueueItemStatus.Pending) return;

        // Cancel says so at once - while the item waits for a download slot, or its turn to
        // install - rather than when the installs reach it. (An install under way finishes
        // placing files first, so that one settles when it is done.)
        var token = item.Token;
        token.Register(() =>
        {
            if (!item.IsFinished && item.Status != DownloadQueueItemStatus.Installing)
                Settle(item, new OperationCanceledException(token));
        });

        try
        {
            item.Status = DownloadQueueItemStatus.Downloading;
            item.StatusMessage = Strings.Downloads_ResolvingLink;

            var version = await item.ResolveVersionAsync();

            // Retried since: the new attempt is the one that goes on.
            if (token != item.Token) return;

            if (version?.Link is null)
            {
                item.Status = DownloadQueueItemStatus.Failed;
                item.StatusMessage = Text(
                    Strings.Downloads_NoLinkFormat, item.ModName, item.VersionLabel);
                return;
            }

            item.TotalBytes ??= version.ContentLength;

            token.ThrowIfCancellationRequested();

            // A version sp-mod.com marks as not working with Fika, on an install that runs Fika:
            // said before it is downloaded, and it can stop here.
            if (string.Equals(version.FikaCompatibility, "incompatible", StringComparison.OrdinalIgnoreCase)
                && await RunsFikaAsync(item.InstallPath)
                && !ConfirmFikaIncompatible(item.ModName, askAgain: item.IsRetry, downloadOnly: item.DownloadOnly))
            {
                item.CancelCommand.Execute(null);
                token.ThrowIfCancellationRequested();
                return;
            }

            // Asked before this item's own download starts, so an accepted missing dependency
            // lands right behind it in the queue.
            if (item.CheckDependencies)
            {
                item.StatusMessage = Strings.Downloads_CheckingDependencies;
                await CheckDependenciesAsync(item, version, token);
            }

            token.ThrowIfCancellationRequested();
            if (token != item.Token) return;

            // 1.19: Download only keeps the archive (and its record) without installing it - it never
            // takes an install turn. Fork: it takes one of the download slots like any other download,
            // and runs beside this loop rather than in it, so the items queued after it are not held up.
            if (item.DownloadOnly)
            {
                _ = SaveOnlyAsync(item, version, token);
                return;
            }

            item.Version = version;
            _installs.Writer.TryWrite(new InstallTurn(item, token, FetchArchiveAsync(item, version, token)));
        }
        catch (Exception ex)
        {
            // An attempt a retry has replaced says nothing: the card is the new attempt's now.
            if (token == item.Token) Settle(item, ex);
        }
    }

    // Stage 2: the archive for this item, from the ones kept or downloaded now, as a file path.
    private async Task<string> FetchArchiveAsync(DownloadQueueItemViewModel item, ModVersion version, CancellationToken token)
    {
        // An archive the user already has: a copy of it is what the install uses (and deletes after),
        // never the user's own file.
        if (LocalArchive.TryGetPath(version.Link, out var local))
        {
            var copy = _archives.TemporaryPath();
            Use(copy);
            try
            {
                item.StatusMessage = Strings.Downloads_CopyingLocal;
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                await Task.Run(() => File.Copy(local, copy, overwrite: true), token);
                item.Progress = 1.0;
            }
            catch (Exception ex)
            {
                if (Unuse(copy)) TryDelete(copy);
                if (token == item.Token) Settle(item, ex);
                throw;
            }

            item.StatusMessage = Strings.Downloads_WaitingToInstall;
            return copy;
        }

        var keep = new SettingsService().Load().KeepDownloads;

        if (keep && _archives.TryGet(item.Target, version, out var kept, ArchivesInUse))
        {
            Use(kept);
            item.Progress = 1.0;
            item.StatusMessage = Strings.Downloads_UsingKept;
            AppLog.Info("Downloads", $"{item.ModName} {item.VersionLabel}: using the archive kept from before");
            return kept;
        }

        // A one-off file when not keeping - or when the same version is being fetched for another
        // card already, which must not share its file.
        var keptPath = keep ? _archives.PathFor(item.Target, version) : null;
        var path = keptPath is not null && !_archivesInUse.ContainsKey(keptPath) ? keptPath : _archives.TemporaryPath();
        var part = ModArchiveCache.PartPathFor(path);

        var slot = false;
        try
        {
            Use(path);

            item.StatusMessage = Strings.Downloads_WaitingToDownload;
            await _downloadSlots.WaitAsync(token);
            slot = true;

            var progress = new Progress<double>(p => item.Progress = p);

            for (var attempt = 0; ; attempt++)
            {
                item.StatusMessage = ModInstallWording.Describe(
                    new ModInstallProgress(ModInstallStage.Downloading, item.ModName, version.Version));
                try
                {
                    // A second attempt carries on from where the first broke off, when it can - never
                    // the first, which could only join onto a .part left by an earlier session.
                    await AppServices.Downloads.DownloadAsync(version.Link!, part, progress, token,
                        resume: attempt > 0, resumable: true);
                    File.Move(part, path, overwrite: true);

                    // Fork: what sp-mod.com listed for it, so the kept file is judged by that later.
                    if (path == keptPath) ModArchiveCache.NoteListed(path, version.ContentLength);
                    break;
                }
                catch (Exception ex) when (attempt < RetryDelays.Length && IsPassing(ex, token))
                {
                    var wait = RetryDelays[attempt];
                    AppLog.Info("Downloads", $"{item.ModName} {item.VersionLabel}: {ex.Message} - trying again in {wait.TotalSeconds:F0}s");
                    item.StatusMessage = Text(Strings.Downloads_RetryingFormat, wait.TotalSeconds);
                    await Task.Delay(wait, token);
                }
            }
        }
        catch (Exception ex)
        {
            if (Unuse(path)) TryDelete(path);
            TryDelete(part);
            TryDelete(ModDownloadService.ValidatorPathFor(part));

            // Said now, not when the installs reach this item - unless a retry has replaced this attempt.
            if (token == item.Token) Settle(item, ex);
            throw;
        }
        finally
        {
            if (slot) _downloadSlots.Release();
        }

        item.StatusMessage = Strings.Downloads_WaitingToInstall;
        return path;
    }

    // A failure that may not happen again: the connection dropped, reset or timed out, the host was
    // busy or failing (5xx, 408, 429), or the file arrived short. Not one that will happen again the
    // same way - a refusal (403, 404), a name that does not resolve, a certificate, a full disk.
    private static bool IsPassing(Exception ex, CancellationToken token) => ex switch
    {
        _ when token.IsCancellationRequested => false,
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 || (int)code is 408 or 429,
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded or HttpRequestError.Unknown } => true,
        HttpIOException => true,
        ModInstallException { Reason: ModInstallFailure.DownloadIncomplete } => true,
        TaskCanceledException => true,
        _ => false,
    };

    // Stage 3.
    private async Task InstallAsync(InstallTurn turn)
    {
        var item = turn.Item;

        //
        // Settled already - its download failed, or it was cancelled while waiting its turn - or a
        // retry has replaced this attempt. Its archive, if it arrived, is let go: left marked in
        // use it would never be trimmed or cleared, and a one-off file would stay on disk.
        //
        if (item.IsFinished || turn.Token != item.Token)
        {
            try
            {
                Release(await turn.Archive, keep: true);
            }
            catch (Exception)
            {
                // It failed or was cancelled; its own failure path let the file go.
            }

            return;
        }

        string? archive = null;
        var keepArchive = true;
        try
        {
            archive = await turn.Archive;

            // Cancelled and retried while the archive arrived: the new attempt installs, not this one.
            if (turn.Token != item.Token) return;

            turn.Token.ThrowIfCancellationRequested();

            // Core reports which stage it is in; every phase past the download (removing the
            // previous version, extracting, copying files) is bucketed under Installing.
            var status = new Progress<ModInstallProgress>(p =>
            {
                item.StatusMessage = ModInstallWording.Describe(p);
                item.Status = p.Stage == ModInstallStage.Downloading
                    ? DownloadQueueItemStatus.Downloading
                    : DownloadQueueItemStatus.Installing;
            });

            item.Status = DownloadQueueItemStatus.Installing;

            var result = await AppServices.ModInstall.InstallAsync(
                item.Target, item.Version!, item.InstallPath, status, null, turn.Token, downloadedArchive: archive);

            item.Status = DownloadQueueItemStatus.Completed;
            item.Progress = 1.0;

            // An update that changed one of the mod's own config files says so here rather than
            // leaving the user to find out in game - see ConfigUpdateWording.
            var configs = result.Configs is { } report ? ConfigUpdateWording.Summary(report) : null;

            var installed = Text(Strings.Downloads_InstalledFormat, item.ModName, item.VersionLabel);

            //
            // What the install deliberately didn't do is said on the card (D5): SPT's own files it left
            // alone - named one per line in the tooltip - and originals it kept to put back later (D22).
            //
            var skipped = result.SkippedProtected ?? [];

            // Fork: a refused file aimed at SPT's user folder is a warning of its own, named, rather
            // than counted with SPT's and the game's own files (the user\patchers bug).
            var userSkips = skipped.Where(ProtectedInstallPaths.IsUnderServerUser).ToList();
            var quietSkips = skipped.Except(userSkips).ToList();
            var keptSpt = quietSkips.Count > 0 ? Strings.Downloads_KeptSptFiles(quietSkips.Count, quietSkips.Count) : null;
            var keptOriginals = result.OriginalsKept > 0
                ? Strings.Downloads_KeptOriginals(result.OriginalsKept, result.OriginalsKept)
                : null;

            // Fork: the user's plugin settings the archive would have put its defaults over.
            var keptSettings = result.KeptSettings.Count > 0
                ? Strings.Downloads_KeptSettings(result.KeptSettings.Count, result.KeptSettings.Count)
                : null;

            item.StatusMessage = string.Join(
                Strings.Common_SentenceSeparator,
                new[] { installed, configs, keptSpt, keptSettings, keptOriginals }.Where(s => !string.IsNullOrEmpty(s)));
            var named = quietSkips.Concat(result.KeptSettings).ToList();
            item.StatusDetail = named.Count > 0
                ? string.Join(Environment.NewLine, new[] { item.StatusMessage, "" }.Concat(named))
                : null;
            item.WarningText = InstallWarning(result.NotAsInArchive, userSkips); // Fork
            ItemInstalled?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // An archive that would not install (not a mod package, would not extract) is not
            // kept, or every retry would try the same file again. One the install never got to -
            // cancelled, SPT running, no install folder - is fine.
            keepArchive = ex is OperationCanceledException
                || ex is ModInstallException { Reason: ModInstallFailure.InstallInUse or ModInstallFailure.NoInstallFolder };

            // The card is a retry's by now, if there was one: this attempt says nothing on it.
            if (turn.Token == item.Token) Settle(item, ex);
        }
        finally
        {
            if (archive is not null) Release(archive, keepArchive);
        }
    }

    // Done with an archive: kept (within the budget) when downloads are kept and it installed,
    // deleted otherwise - once nothing else is using it.
    private void Release(string archive, bool keep)
    {
        if (!Unuse(archive)) return;

        if (keep && _archives.IsKeepable(archive) && new SettingsService().Load().KeepDownloads)
            _archives.Trim(ArchivesInUse);
        else
            TryDelete(archive);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Downloads", $"couldn't delete {path}: {ex.Message}");
        }
    }

    // Says how an item stopped, whichever stage it stopped in.
    private static void Settle(DownloadQueueItemViewModel item, Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException when item.Token.IsCancellationRequested:
                item.Status = DownloadQueueItemStatus.Cancelled;
                item.Progress = 0;
                item.StatusMessage = Text(
                    Strings.Downloads_CancelledItemFormat, item.ModName, item.VersionLabel);
                return;

            case SpModApiRateLimitedException ex:
                item.StatusMessage = ApiProblems.Describe(ex);
                break;

            case SpModApiException ex:
                item.StatusMessage = ApiProblems.Describe(ex);
                break;

            case HttpRequestException ex:
                item.StatusMessage = ApiProblems.Describe(ex);
                break;

            case ModInstallException ex:
                // ModInstallService refused or gave up part way; ModInstallProblems words it.
                item.StatusMessage = ModInstallProblems.Describe(ex);
                break;

            case InvalidOperationException ex:
                // Anything else that reached here already carries a readable message of its own.
                item.StatusMessage = ex.Message;
                break;

            default:
                item.StatusMessage = Text(Strings.Downloads_UnexpectedFormat, exception.Message);
                break;
        }

        item.Status = DownloadQueueItemStatus.Failed;

        // Fork (SSPTMM): the reason was only ever on the card, so a failure left no line in the log -
        // a refused SVM install showed nothing after "using the archive kept from before".
        AppLog.Warn("Downloads",
            $"{item.ModName} {item.VersionLabel} failed: {item.StatusMessage} ({exception.GetType().Name}: {exception.Message})");
    }

    // Fork: Download only, in a download slot. Failures settle the card here - nothing awaits this.
    private async Task SaveOnlyAsync(DownloadQueueItemViewModel item, ModVersion version, CancellationToken token)
    {
        var slot = false;
        try
        {
            item.StatusMessage = Strings.Downloads_WaitingToDownload;
            await _downloadSlots.WaitAsync(token);
            slot = true;

            if (token != item.Token) return;
            await SaveArchiveAsync(item, version);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                AppLog.Error("Downloads", $"saving {item.ModName} failed: {ex}");
            if (token == item.Token) Settle(item, ex);
        }
        finally
        {
            if (slot) _downloadSlots.Release();
        }
    }

    //
    // Monitor mode's branch: the same download, saved to the user's folder instead of installed.
    // Nothing in the SPT install is touched, so there is no running-SPT check and no ItemInstalled -
    // what is on disk has not changed.
    //
    private static async Task SaveArchiveAsync(DownloadQueueItemViewModel item, ModVersion version)
    {
        var folder = DownloadFolders.Resolve(new SettingsService().Load().Monitor.DownloadFolder);

        item.Status = DownloadQueueItemStatus.Downloading;
        item.StatusMessage = Text(Strings.Downloads_SavingToFormat, folder);

        var downloadProgress = new Progress<double>(p => item.Progress = p);

        var result = await AppServices.ModArchive.SaveArchiveAsync(
            item.Target, version, folder, item.InstallPath, item.DownloadSubfolder, downloadProgress, item.Token);

        item.Status = DownloadQueueItemStatus.Completed;
        item.Progress = 1.0;
        item.SavedPath = result.Record.ArchivePath;

        var saved = Text(
            result.Record.Unrecognised ? Strings.Downloads_SavedUnrecognisedFormat : Strings.Downloads_SavedFormat,
            item.ModName,
            item.VersionLabel,
            result.Record.ArchivePath);

        item.StatusMessage = ReplacesConfigs(result.Record, item.InstallPath)
            ? string.Join(Strings.Common_SentenceSeparator, saved, Strings.Downloads_SavedConfigsNote)
            : saved;
    }

    //
    // §8: config protection only runs on an install this app does. Said when the archive carries a
    // server config file that is already on disk - an update the user will be copying over their
    // own settings - and not for a first download, where there is nothing of theirs to lose.
    //
    private static bool ReplacesConfigs(DownloadedModRecord download, string installPath) =>
        download.ExpectedFiles.Any(f =>
            ModConfigFiles.IsServerModConfig(f.Path)
            && File.Exists(Path.Combine(installPath, f.Path.Replace('/', Path.DirectorySeparatorChar))));

    // Resolves item's full dependency tree for the version being installed, cross-references it against
    // a fresh disk scan + catalog match, and offers to queue anything missing via one
    // ReadModPageConfirmationWindow listing every missing mod. Anything queued is registered as a
    // dependency of <paramref name="item"/>, so cancelling it cancels them too. Best-effort: a
    // failed lookup silently skips the check rather than failing the queued item.
    private async Task CheckDependenciesAsync(DownloadQueueItemViewModel item, ModVersion version, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(version.Version)) return;

        var check = await FindMissingDependenciesAsync(item.Target, version.Version, item.InstallPath, token, version);
        if (check is null) return;

        token.ThrowIfCancellationRequested();

        // What is installed but will not do (disabled, too new, the wrong version, nothing for this
        // SPT): said before anything is downloaded, and the install can be stopped here.
        if (check.Problems.Count > 0 && !ConfirmDespite(item.ModName, check.Problems, askAgain: item.IsRetry, downloadOnly: item.DownloadOnly))
        {
            item.CancelCommand.Execute(null);
            token.ThrowIfCancellationRequested();
            return;
        }

        var missing = check.Missing;
        if (missing.Count == 0) return;

        // One gate covering every missing dependency at once: each mod's page must be opened
        // before Continue unlocks, replacing what was previously a separate Yes/No prompt plus a
        // per-dependency read-page confirmation.
        if (!ReadModPageConfirmationWindow.ConfirmAll(PageLinks(missing))) return;

        token.ThrowIfCancellationRequested();

        EnqueueDependencies(item, missing);
    }

    /// <summary>A mod another one needs that this install does not have yet, with the version the
    /// resolver picked for it.</summary>
    public sealed record MissingDependency(DependencyNode Node, Mod Mod);

    public enum DependencyProblemKind
    {
        // Needed, not installed, and nothing published fits this SPT.
        NoCompatibleVersion,

        // Installed, but only in a disabled folder.
        Disabled,

        // Installed, newer than the newest version that fits.
        TooNew,

        // Installed, and not one of the versions the mod being installed accepts.
        WrongVersion,

        // The mods installed need versions of it that cannot all be met.
        Conflict,
    }

    /// <summary>A dependency that is there (or cannot be) but will not do.</summary>
    public sealed record DependencyProblem(string Name, DependencyProblemKind Kind, string? Installed, string? Wanted);

    /// <summary>What a mod needs: the dependencies to queue, and the ones that will not do as they are.</summary>
    public sealed record DependencyCheck(IReadOnlyList<MissingDependency> Missing, IReadOnlyList<DependencyProblem> Problems);

    /// <summary>One line per problem.</summary>
    public static string Describe(DependencyProblem problem) => problem.Kind switch
    {
        DependencyProblemKind.NoCompatibleVersion => Text(Strings.Downloads_DepNoCompatibleFormat, problem.Name, AppServices.SptEnvironment.InstalledVersion),
        DependencyProblemKind.Disabled => Text(Strings.Downloads_DepDisabledFormat, problem.Name, problem.Installed),
        DependencyProblemKind.TooNew => Text(Strings.Downloads_DepTooNewFormat, problem.Name, problem.Installed, problem.Wanted),
        DependencyProblemKind.WrongVersion => Text(Strings.Downloads_DepWrongVersionFormat, problem.Name, problem.Installed, problem.Wanted),
        _ => Text(Strings.Downloads_DepConflictFormat, problem.Name),
    };

    //
    // The answers given about each problem, for a few minutes: Update all or a collection queues many
    // mods that share a dependency, and each one would otherwise ask the same question again.
    //
    private readonly Dictionary<string, (bool Install, DateTime At)> _problemAnswers = [];

    private static readonly TimeSpan ProblemAnswerLifetime = TimeSpan.FromMinutes(10);

    // A "no" stands for the batch it was given in (a collection can take a few minutes to queue),
    // and never for a card's own Retry - which asks again.
    private static readonly TimeSpan ProblemRefusalLifetime = TimeSpan.FromMinutes(5);

    private static bool StillStands((bool Install, DateTime At) answer, DateTime now) =>
        now - answer.At <= (answer.Install ? ProblemAnswerLifetime : ProblemRefusalLifetime);

    // Whether Fika is installed (any plugin of Project Fika's, "com.fika.*", loaded from the
    // install, or an enabled server mod named for it or carrying such a ModGuid), read at most
    // every few minutes - a collection queues many mods at once.
    private (string Path, bool Runs, DateTime At)? _fika;

    private async Task<bool> RunsFikaAsync(string installPath)
    {
        if (_fika is { } known && known.Path == installPath && DateTime.UtcNow - known.At < TimeSpan.FromMinutes(5)) return known.Runs;

        // Its client plugins, or - on a machine that only hosts - its server mod's folder.
        var runs = await Task.Run(() =>
            InstalledModScanner.LoadedPluginGuids(installPath).Any(g => g.StartsWith("com.fika.", StringComparison.OrdinalIgnoreCase))
            || InstalledModScanner.Scan(installPath).Any(m => m is { Target: InstalledModTarget.Server, IsDisabled: false }
                && (IsFikaServer(m.Name) || IsFikaServer(Path.GetFileName(m.FolderPath))
                    || (m.Guid?.StartsWith("com.fika.", StringComparison.OrdinalIgnoreCase) ?? false))));
        _fika = (installPath, runs, DateTime.UtcNow);
        return runs;
    }

    // Fika's own server mod ("fika-server", "Fika.Server"...), not a mod that merely mentions Fika.
    private static bool IsFikaServer(string? name) =>
        name is not null
        && name.StartsWith("fika", StringComparison.OrdinalIgnoreCase)
        && name.Contains("server", StringComparison.OrdinalIgnoreCase);

    //
    // Fork (SSPTMM): the warning under a finished install's status line - files that didn't land as
    // the archive has them (InstallVerification), then archive files aimed at SPT's user folder that
    // were refused. One per line, so each can be looked for. Null when there is neither.
    //
    private static string? InstallWarning(IReadOnlyList<InstallMismatch> mismatches, IReadOnlyList<string> userSkips)
    {
        var lines = new List<string>();

        if (mismatches.Count > 0)
        {
            lines.Add(Strings.Downloads_NotAsInArchiveHeader);
            foreach (var m in mismatches)
            {
                lines.Add("\u2022 " + (m.Kind switch
                {
                    InstallMismatchKind.Missing => Text(Strings.Downloads_MismatchMissingFormat, m.Path),
                    InstallMismatchKind.DifferentSize => Text(Strings.Downloads_MismatchSizeFormat, m.Path, (m.DiskSize ?? 0).ToString("N0"), m.ArchiveSize.ToString("N0")),
                    InstallMismatchKind.DifferentContents => Text(Strings.Downloads_MismatchContentsFormat, m.Path),
                    _ => Text(Strings.Downloads_MismatchUnreadableFormat, m.Path),
                }));
            }
        }

        if (userSkips.Count > 0)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add(Strings.Downloads_SkippedUserFilesHeader);
            lines.AddRange(userSkips.Select(p => "\u2022 " + p));
        }

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : null;
    }

    // Fork: downloadOnly words it as a download rather than an install ("Download it anyway?").
    private bool ConfirmFikaIncompatible(string modName, bool askAgain = false, bool downloadOnly = false)
    {
        var key = "fika:" + modName;
        if (!askAgain && _problemAnswers.TryGetValue(key, out var answer) && StillStands(answer, DateTime.UtcNow)) return answer.Install;

        var install = System.Windows.MessageBox.Show(
            Text(downloadOnly ? Strings.Downloads_FikaIncompatibleDownloadFormat : Strings.Downloads_FikaIncompatibleFormat, modName),
            Strings.Downloads_FikaIncompatibleTitle,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

        _problemAnswers[key] = (install, DateTime.UtcNow);
        return install;
    }

    /// <summary>Says what will not do and asks whether to install anyway. Problems already answered
    /// in the last few minutes are not asked about again (unless <paramref name="askAgain"/>): a
    /// "no" to any of them is a no, and only new ones are asked.</summary>
    public bool ConfirmDespite(string modName, IReadOnlyList<DependencyProblem> problems, bool askAgain = false, bool downloadOnly = false)
    {
        var now = DateTime.UtcNow;
        foreach (var stale in _problemAnswers.Where(a => !StillStands(a.Value, now)).Select(a => a.Key).ToList())
            _problemAnswers.Remove(stale);

        var unanswered = new List<DependencyProblem>();
        foreach (var problem in problems)
        {
            if (askAgain || !_problemAnswers.TryGetValue(Describe(problem), out var answer)) unanswered.Add(problem);
            else if (!answer.Install) return false;
        }

        if (unanswered.Count == 0) return true;

        var install = System.Windows.MessageBox.Show(
            Text(downloadOnly ? Strings.Downloads_DepProblemsDownloadFormat : Strings.Downloads_DepProblemsFormat, modName, string.Join("\n", unanswered.Select(p => "\u2022 " + Describe(p)))),
            Strings.Downloads_DepProblemsTitle,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

        foreach (var problem in unanswered) _problemAnswers[Describe(problem)] = (install, now);
        return install;
    }

    //
    // The mods <paramref name="target"/> at <paramref name="version"/> needs that are neither
    // installed nor already queued, its whole tree flattened. Null when the lookup could not be
    // made (no SPT version known, rate limited, offline) - the caller then leaves the check to the
    // queue, as it always did - and empty when nothing is missing.
    //
    // Subscribe asks this before its gate, so the mod and everything it needs are confirmed in one
    // window instead of the dependencies' window appearing once the download has started.
    //
    public async Task<DependencyCheck?> FindMissingDependenciesAsync(
        InstallTarget target, string version, string installPath, CancellationToken token = default,
        ModVersion? versionDetails = null)
    {
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        if (string.IsNullOrWhiteSpace(sptVersion) || string.IsNullOrWhiteSpace(version)) return null;

        List<DependencyNode> nodes;
        try
        {
            // Both endpoints resolve to ordinary mods, so everything below this point is identical
            // for an addon - what an addon requires is mods, not other addons. An addon's parent is
            // deliberately not part of this: it isn't returned here, and it's handled where the
            // addon is offered instead.
            var identifier = string.IsNullOrWhiteSpace(target.Guid) ? target.Id.ToString() : target.Guid;
            var result = target.IsAddon
                ? await AppServices.SpModApi.GetAddonDependenciesAsync($"{identifier}:{version}", sptVersion)
                : await AppServices.SpModApi.GetModDependenciesAsync($"{identifier}:{version}", sptVersion);
            nodes = result.Values.FirstOrDefault() ?? [];
        }
        catch (Exception)
        {
            // Rate limited, network error, or an unrecognized SPT version - skip the check.
            return null;
        }

        if (nodes.Count == 0) return new DependencyCheck([], []);

        token.ThrowIfCancellationRequested();

        // What is still to run or running now, before the scan: one that finishes while the scan
        // runs would be neither on the scan nor unfinished afterwards, and be queued a second time.
        var unfinishedBefore = Items
            .Where(i => !i.IsFinished && !i.Target.IsAddon)
            .Select(i => i.Target.Id)
            .ToHashSet();

        // Fresh disk scan each time so it reflects whatever was just installed in this same batch.
        await AppServices.ModCache.EnsureLoadedAsync();
        var scanned = await Task.Run(() => InstalledModScanner.Scan(installPath));
        var installedMatches = InstalledModCardViewModel.BuildFrom(
            scanned, AppServices.ModCache.AllMods, sptVersion, AppServices.InstallManifest.Load().ModsFor(installPath),
            AppServices.Addons.AllAddons);

        // Every dependency node is a mod, so addon cards - whose ModId is an addon id - are left
        // out of both sets rather than being compared against mod ids.
        var installedIds = installedMatches.Where(m => m is { IsAddon: false, ModId: not null })
            .Select(m => m.ModId!.Value).ToHashSet();
        var installedGuids = installedMatches.Where(m => m.Guid is not null)
            .Select(m => m.Guid!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Guards against two mods sharing a dependency both queuing the same download twice. Only
        // cards still to run or running count: one that finished is either on disk now (the scan
        // above finds it) or is not - installed once and removed since, failed, or cancelled - and
        // must be offered again. Counting finished cards was why a mod subscribed to a second time,
        // after its dependencies had been removed, went ahead without asking for them.
        var queuedIds = Items
            .Where(i => !i.IsFinished && !i.Target.IsAddon)
            .Select(i => i.Target.Id)
            .ToHashSet();
        queuedIds.UnionWith(unfinishedBefore);

        // Prefer each cached catalog Mod when available; fall back to a minimal Mod built from
        // the dependency node's own fields.
        var missing = Flatten(nodes)
            .Where(n => n.LatestCompatibleVersion is not null)
            .Where(n => !installedIds.Contains(n.Id) && !queuedIds.Contains(n.Id))
            .Where(n => n.Guid is null || !installedGuids.Contains(n.Guid))
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .Select(dep => new MissingDependency(
                dep,
                AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == dep.Id)
                    ?? new Mod { Id = dep.Id, Guid = dep.Guid, Name = dep.Name, Slug = dep.Slug }))
            .ToList();

        return new DependencyCheck(missing, DependencyProblems(nodes, installedMatches, queuedIds, versionDetails));
    }

    //
    // The dependencies that are there - or cannot be - but will not do as they are. Until this,
    // one with nothing published for this SPT was dropped without a word, and one installed but
    // disabled, too new or not an accepted version counted as present: the mod then did not load.
    //
    // A direct dependency is checked against the versions the mod being installed accepts, when the
    // listing says which (ModVersionDependency.Versions); anything deeper, against the newest version
    // that fits, as the Dependencies page does.
    //
    private static List<DependencyProblem> DependencyProblems(
        List<DependencyNode> nodes,
        IReadOnlyList<InstalledModCardViewModel> installed,
        IReadOnlySet<int> queuedIds,
        ModVersion? versionDetails)
    {
        var accepted = new Dictionary<int, List<string>>();
        foreach (var declared in versionDetails?.Dependencies ?? [])
        {
            if (declared.IsOptional || declared.Versions is not { Count: > 0 } versions) continue;
            var id = declared.ModId != 0 ? declared.ModId : declared.Id;
            accepted[id] = [.. versions.Select(v => v.Version).OfType<string>()];
        }

        var problems = new List<DependencyProblem>();
        foreach (var node in Flatten(nodes).GroupBy(n => n.Id).Select(g => g.First()))
        {
            if (queuedIds.Contains(node.Id)) continue;

            var name = node.Name ?? AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == node.Id)?.Name ?? node.Guid ?? $"#{node.Id}";
            var wanted = node.LatestCompatibleVersion?.Version;

            var cards = installed
                .Where(c => !c.IsAddon && (c.ModId == node.Id
                    || (node.Guid is not null && string.Equals(c.Guid, node.Guid, StringComparison.OrdinalIgnoreCase))))
                .ToList();
            var enabled = cards.FirstOrDefault(c => !c.IsDisabled);

            if (node.Conflict)
            {
                problems.Add(new DependencyProblem(name, DependencyProblemKind.Conflict, enabled?.InstalledVersion, wanted));
            }
            else if (cards.Count == 0)
            {
                if (wanted is null) problems.Add(new DependencyProblem(name, DependencyProblemKind.NoCompatibleVersion, null, null));
            }
            else if (enabled is null)
            {
                problems.Add(new DependencyProblem(name, DependencyProblemKind.Disabled, cards[0].InstalledVersion, wanted));
            }
            else if (accepted.TryGetValue(node.Id, out var versions) && enabled.InstalledVersion is { } have
                && (enabled.IsAppManaged || enabled.IsManualOverride))
            {
                // The exact list, for a version known exactly (a DLL's own version is not always the
                // published one, so a hand-installed copy is left to the looser check below).
                if (!versions.Any(v => ModVersionComparer.Compare(v, have) == 0))
                {
                    var newest = versions.OrderByDescending(v => v, Comparer<string>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0)).FirstOrDefault();
                    problems.Add(new DependencyProblem(name, DependencyProblemKind.WrongVersion, have, newest));
                }
            }
            else if (DependencyStatusResolver.Resolve(node, enabled.InstalledVersion, wanted,
                         exactVersion: enabled.IsAppManaged || enabled.IsManualOverride) == ModStatus.TooNew)
            {
                problems.Add(new DependencyProblem(name, DependencyProblemKind.TooNew, enabled.InstalledVersion, wanted));
            }
        }

        return problems;
    }

    /// <summary>The gate's rows for a set of missing dependencies.</summary>
    public static List<ModPageLink> PageLinks(IEnumerable<MissingDependency> missing) =>
        missing
            .Select(d => new ModPageLink(
                d.Mod.Name ?? d.Node.Name ?? Text(Strings.Downloads_UnnamedModFormat, d.Node.Id),
                d.Mod.DetailUrl)
            {
                ModId = d.Node.Id,
            })
            .ToList();

    /// <summary>Queues each missing dependency behind <paramref name="item"/>, as one of its own:
    /// cancelling the item cancels them.</summary>
    public void EnqueueDependencies(DownloadQueueItemViewModel item, IEnumerable<MissingDependency> missing)
    {
        foreach (var (dep, depMod) in missing)
        {
            // LatestCompatibleVersion already has the Link/ContentLength needed; no further lookup required.
            var depVersion = new ModVersion
            {
                Id = dep.LatestCompatibleVersion!.Id,
                Version = dep.LatestCompatibleVersion.Version,
                Link = dep.LatestCompatibleVersion.Link,
                ContentLength = dep.LatestCompatibleVersion.ContentLength,
                FikaCompatibility = dep.LatestCompatibleVersion.FikaCompatibility,
            };

            Enqueue(
                InstallTarget.For(depMod),
                depVersion.Version ?? Strings.Common_Unknown,
                item.InstallPath,
                () => Task.FromResult<ModVersion?>(depVersion),
                checkDependencies: false,
                dependencyOf: item,
                totalBytes: depVersion.ContentLength);
        }
    }

    // Walks a resolved dependency tree depth-first, flattening every nested level into one sequence.
    private static IEnumerable<DependencyNode> Flatten(IEnumerable<DependencyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Dependencies)) yield return child;
        }
    }
}
