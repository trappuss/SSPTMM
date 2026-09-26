using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// The stage a DownloadQueueItemViewModel is currently in. Drives its status badge and progress bar rendering.
public enum DownloadQueueItemStatus
{
    Pending,
    Downloading,
    Installing,
    Completed,
    Failed,
    Cancelled,
}

// 
// One entry in AppServices.DownloadQueue's queue - one card in DownloadsPage's list, one mod
// version being installed. Created and owned by DownloadQueueViewModel.Enqueue; holds the data
// and notification surface the queue's worker loop writes into as it processes the item.
// 
public sealed partial class DownloadQueueItemViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // What is being installed - a catalog mod or an addon attached to one.
    public InstallTarget Target { get; }

    // The version being installed, shown as-is (e.g. "1.4.2"). Display label only; the full ModVersion is resolved lazily via ResolveVersionAsync.
    public string VersionLabel { get; }

    public string ModName => Target.Name;
    public string? Thumbnail => Target.Thumbnail;

    // Distinguishes an addon card from a mod card in the queue, since an addon's name rarely says
    // on its own that it needs a parent mod.
    public bool IsAddon => Target.IsAddon;

    // The install folder, captured at enqueue time rather than re-read later.
    public string InstallPath { get; }

    // Whether the worker should resolve this item's dependency tree and offer to queue anything missing before downloading it. False for items added as a dependency of another queued item.
    public bool CheckDependencies { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private DownloadQueueItemStatus _status = DownloadQueueItemStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransferredValue))]
    [NotifyPropertyChangedFor(nameof(RateValue))]
    [NotifyPropertyChangedFor(nameof(EtaValue))]
    [NotifyPropertyChangedFor(nameof(HasTransferDetails))]
    private double _progress;

    //
    // The archive's size, when it is known: the catalog carries content_length on a version, and a
    // mod list resolves every version before it queues anything, so a list apply knows the whole
    // size up front. A single Install resolves lazily, so this fills in once the worker gets there.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransferredValue))]
    [NotifyPropertyChangedFor(nameof(RateValue))]
    [NotifyPropertyChangedFor(nameof(EtaValue))]
    [NotifyPropertyChangedFor(nameof(HasTransferDetails))]
    private long? _totalBytes;

    // Runs for the download stage only, which is the only stage with a measurable rate.
    private readonly Stopwatch _downloading = new();

    //
    // Recent progress, as (when, how many bytes had arrived) - the rate and the estimate are both
    // measured across this window rather than from the start of the download.
    //
    // A since-the-start average is anchored to whatever the first seconds looked like and takes as
    // long as the download itself to forget them: a transfer that begins slowly and then comes up to
    // speed reads low for an hour, and a stall halfway through never fully washes out. Over the
    // couple of minutes a mod archive takes that hardly shows; over the 5.5 GB one that prompted
    // this, it is the difference between a useful estimate and a decorative one.
    //
    private readonly Queue<(TimeSpan At, double Bytes)> _samples = new();

    // Long enough to ride out a stalled second, short enough to notice the line actually changing.
    private const double RateWindowSeconds = 8;

    // Below this there is not enough elapsed time for a rate to mean anything, whatever the numbers
    // say.
    private const double MinimumSampleSeconds = 1.5;

    // One sample per quarter second at most. Progress arrives far faster than that, and an unbounded
    // queue would hold thousands of entries to answer a question two of them can.
    private const double SampleIntervalSeconds = 0.25;

    //
    // The transfer detail, split into one value per box - how much has arrived, how fast, and how
    // long is left. DownloadsPage renders each in its own fixed container so a number changing
    // width can't shift the ones beside it.
    //
    // Rate and time stay blank until there is enough to be honest with: a rate computed off the
    // first fraction of a percent is nonsense, and an estimate that swings between 8 seconds and
    // four minutes is worse than no estimate.
    //

    // Placeholder that holds a box's shape before its number exists.
    private const string NoValue = "\u2014";

    // Only while downloading, and only once the size is known - there is nothing to put in the
    // boxes otherwise.
    // Not once the download is complete: a card waiting its turn to install has nothing moving.
    public bool HasTransferDetails =>
        Status == DownloadQueueItemStatus.Downloading && TotalBytes is > 0 && Progress < 1;

    // "173 of 258.3 MB" - both halves in the same unit, so the pair doesn't switch units partway
    // through and jump.
    public string TransferredValue =>
        TotalBytes is > 0 and var total ? SizePair(Progress * total, total) : NoValue;

    public string RateValue =>
        BytesPerSecond is { } rate ? Text(Strings.Downloads_RateFormat, Size(rate)) : NoValue;

    public string EtaValue => BytesPerSecond is > 0 and var rate && RemainingBytes is { } left
        ? Remaining(TimeSpan.FromSeconds(left / rate))
        : NoValue;


    // What is left to fetch for this item, or null when its size isn't known yet. A pending item
    // hasn't started, so its whole archive is still to come.
    public long? RemainingBytes =>
        IsFinished || TotalBytes is not > 0 ? null : (long)(TotalBytes.Value * (1 - Progress));

    //
    // The rate observed across the recent window, or null while there is not yet enough of one.
    //
    // The gate is elapsed TIME and nothing else. It used to be `Progress > 0.02`, which sounds
    // equivalent and is not: two percent is a fraction of the file, so the wait for a first reading
    // grows with the download. On a 6 MB mod that is 120 KB and instant; on the 5.5 GB archive that
    // prompted this it is 113 MB, which on a domestic line is minutes of Speed and Time left sitting
    // at a dash on the one download where they matter most. What makes a rate honest is having
    // watched for long enough, not having watched a set share of the file.
    //
    public double? BytesPerSecond
    {
        get
        {
            if (Status != DownloadQueueItemStatus.Downloading || TotalBytes is not > 0 || Progress >= 1) return null;
            if (_samples.Count < 2) return null;

            var oldest = _samples.Peek();
            var newest = _samples.Last();

            var seconds = (newest.At - oldest.At).TotalSeconds;
            var bytes = newest.Bytes - oldest.Bytes;

            return seconds >= MinimumSampleSeconds && bytes > 0 ? bytes / seconds : null;
        }
    }

    //
    // Samples are taken here rather than on a timer: progress is the only thing that knows a byte
    // arrived, and a timer would happily record a flat window as a real measurement.
    //
    partial void OnProgressChanged(double value)
    {
        if (Status != DownloadQueueItemStatus.Downloading || TotalBytes is not > 0) return;

        var at = _downloading.Elapsed;

        if (_samples.Count > 0
            && (at - _samples.Last().At).TotalSeconds < SampleIntervalSeconds)
        {
            return;
        }

        _samples.Enqueue((at, value * TotalBytes.Value));

        // Two are kept whatever their age, so a download that slows to a crawl still reports the
        // crawl instead of dropping back to a dash.
        while (_samples.Count > 2 && (at - _samples.Peek().At).TotalSeconds > RateWindowSeconds)
        {
            _samples.Dequeue();
        }
    }

    public static string SizeLabel(double bytes) => Size(bytes);

    public static string RemainingLabel(TimeSpan left) => Remaining(left);

    // Both halves of "x of y" scaled to the larger one's unit.
    private static string SizePair(double done, double total)
    {
        var (scale, unit) = total switch
        {
            >= 1024d * 1024 * 1024 => (1024d * 1024 * 1024, Strings.Common_UnitGb),
            >= 1024d * 1024 => (1024d * 1024, Strings.Common_UnitMb),
            >= 1024d => (1024d, Strings.Common_UnitKb),
            _ => (1d, Strings.Common_UnitB),
        };

        return Text(Strings.Downloads_TransferredFormat, done / scale, total / scale, unit);
    }

    // Shared with the Mod footprint page's own sizes, which is why the keys are Common_.
    public static string Size(double bytes) => Text(
        bytes switch
        {
            >= 1024d * 1024 * 1024 => Strings.Common_SizeGbFormat,
            >= 1024d * 1024 => Strings.Common_SizeMbFormat,
            >= 1024d => Strings.Common_SizeKbFormat,
            _ => Strings.Common_SizeBFormat,
        },
        bytes switch
        {
            >= 1024d * 1024 * 1024 => bytes / (1024d * 1024 * 1024),
            >= 1024d * 1024 => bytes / (1024d * 1024),
            >= 1024d => bytes / 1024d,
            _ => bytes,
        });

    private static string Remaining(TimeSpan left) => left switch
    {
        { TotalSeconds: < 10 } => Strings.Downloads_TimeAFewSeconds,
        { TotalMinutes: < 1 } => Text(Strings.Downloads_TimeSecondsFormat, left.TotalSeconds),
        { TotalMinutes: < 60 } => Text(Strings.Downloads_TimeMinutesFormat, left.TotalMinutes),
        _ => Text(Strings.Downloads_TimeHoursFormat, left.TotalHours),
    };

    [ObservableProperty]
    private string _statusMessage = Strings.Downloads_WaitingInQueue;

    // True while installing, since that stage has no byte count to report fractional progress for.
    public bool IsIndeterminateProgress => Status == DownloadQueueItemStatus.Installing;

    // Short badge text for Status.
    public string StatusLabel => Status switch
    {
        DownloadQueueItemStatus.Pending => Strings.Downloads_StatusPending,
        DownloadQueueItemStatus.Downloading => Strings.Downloads_StatusDownloading,
        DownloadQueueItemStatus.Installing => Strings.Downloads_StatusInstalling,
        DownloadQueueItemStatus.Completed => Strings.Downloads_StatusCompleted,
        DownloadQueueItemStatus.Failed => Strings.Downloads_StatusFailed,
        DownloadQueueItemStatus.Cancelled => Strings.Downloads_StatusCancelled,
        _ => Strings.Downloads_StatusUnknown,
    };

    // Whether this item is still in a stage the user can back out of.
    public bool CanCancel => Status is DownloadQueueItemStatus.Pending
        or DownloadQueueItemStatus.Downloading
        or DownloadQueueItemStatus.Installing;

    // True once this item has finished one way or another, whatever the outcome.
    public bool IsFinished => Status is DownloadQueueItemStatus.Completed
        or DownloadQueueItemStatus.Failed
        or DownloadQueueItemStatus.Cancelled;

    //
    // Replaced on retry, not reused: a CancellationTokenSource that has been cancelled stays
    // cancelled forever, so a retried item would be refused by the worker's first check before it
    // ever started. One attempt, one source.
    //
    private CancellationTokenSource _cancellation = new();

    // Cancels the download/install this item is running. The install's file-copy stage
    // runs to completion regardless, so a mod is never left half-placed.
    internal CancellationToken Token => _cancellation.Token;

    //
    // Puts this item back on the queue. Set by DownloadQueueViewModel.Enqueue, because the queue is
    // the only thing that owns the channel - the card just asks to go round again.
    //
    internal Action<DownloadQueueItemViewModel>? Requeue { get; set; }

    // Only what has stopped, one way or the other, and only while the queue can still take it back.
    public bool CanRetry =>
        Requeue is not null && Status is DownloadQueueItemStatus.Failed or DownloadQueueItemStatus.Cancelled;

    //
    // Runs this item again from the top.
    //
    // The same card rather than a new one: the failure is what you are looking at, and answering it
    // by appending a second card for the same mod leaves the first sitting there implying two of
    // them are outstanding. Everything the first attempt needed is still on this object - the
    // target, the install path, and the closure that resolves the version - so a retry is a reset
    // rather than a rebuild.
    //
    // A mod that failed HALF INSTALLED is not a problem here: ModInstallService removes a previous
    // version before placing files, and a retry goes through the same path, so the second attempt
    // tidies up after the first.
    //
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        if (!CanRetry) return;

        var previous = _cancellation;
        _cancellation = new CancellationTokenSource();
        previous.Dispose();

        Progress = 0;
        Status = DownloadQueueItemStatus.Pending;
        StatusMessage = Strings.Downloads_WaitingInQueue;
        IsRetry = true;

        Requeue!(this);
    }

    // Set by Retry: the user asked for this one again, so a question answered "no" is asked again.
    internal bool IsRetry { get; private set; }

    // Items queued because this one declared them as missing dependencies. Cancelling this
    // item cancels them too.
    private readonly List<DownloadQueueItemViewModel> _dependencies = [];

    // Resolves the full ModVersion (with its download Link) for this item.
    private readonly Func<Task<ModVersion?>> _resolveVersion;

    internal DownloadQueueItemViewModel(
        InstallTarget target,
        string versionLabel,
        string installPath,
        Func<Task<ModVersion?>> resolveVersion,
        bool checkDependencies,
        long? totalBytes = null)
    {
        Target = target;
        VersionLabel = versionLabel;
        InstallPath = installPath;
        _resolveVersion = resolveVersion;
        CheckDependencies = checkDependencies;
        _totalBytes = totalBytes;
    }

    internal Task<ModVersion?> ResolveVersionAsync() => _resolveVersion();

    // Set once the item is prepared: the version being installed.
    internal ModVersion? Version { get; set; }

    internal void AddDependency(DownloadQueueItemViewModel item) => _dependencies.Add(item);

    // Cancels this item and every dependency queued on its behalf. A still-pending item is
    // marked Cancelled immediately; an in-flight one is signalled and settles when its worker
    // unwinds.
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        foreach (var dependency in _dependencies)
        {
            if (dependency.CanCancel) dependency.Cancel();
        }

        if (!CanCancel) return;

        _cancellation.Cancel();

        if (Status == DownloadQueueItemStatus.Pending)
        {
            Status = DownloadQueueItemStatus.Cancelled;
            StatusMessage = Strings.Downloads_CancelledBeforeStart;
        }
        else if (!IsFinished)
        {
            // Not when the cancellation settled it already (waiting for a slot or its turn): that
            // said "Cancelled" itself, and nothing would come along to replace "Cancelling...".
            StatusMessage = Strings.Downloads_Cancelling;
        }
    }

    partial void OnStatusChanged(DownloadQueueItemStatus value)
    {
        if (value == DownloadQueueItemStatus.Downloading) _downloading.Restart();
        else _downloading.Stop();

        // Measurements belong to one run of one stage. A retry that kept them would read its first
        // seconds against a clock that had already been restarted underneath them.
        _samples.Clear();

        OnPropertyChanged(nameof(TransferredValue));
        OnPropertyChanged(nameof(RateValue));
        OnPropertyChanged(nameof(EtaValue));
        OnPropertyChanged(nameof(HasTransferDetails));
        OnPropertyChanged(nameof(IsIndeterminateProgress));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(IsFinished));
    }
}
