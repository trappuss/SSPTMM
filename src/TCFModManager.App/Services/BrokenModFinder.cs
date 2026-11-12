using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

/// <summary>How the last search ended, as worded then - kept in Data\broken_mod_result.json so it is
/// still on the page after the app was closed, until Done clears it.</summary>
public sealed class SavedSearchResult
{
    public string Title { get; set; } = "";

    public string? Body { get; set; }

    public string? Next { get; set; }

    public string? Lines { get; set; }

    public string? Note { get; set; }

    public List<string[]> Found { get; set; } = [];
}

/// <summary>A mod a search ended on, with the button that shows it in Subscribed items.</summary>
public sealed partial class FoundMod(string name, string title, string? path) : ObservableObject
{
    public string Name { get; } = name;

    public string Title { get; } = title;

    /// <summary>Its folder as it reads when the mod is on; null for a result kept by an older build.</summary>
    public string? Path { get; } = path;

    public string ShowText => LocalizationService.Text(Strings.Search_ShowModFormat, Title);

    public string TurnOffText => LocalizationService.Text(Strings.Search_TurnOffFormat, Title);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTurnOff))]
    private bool _isOff = path is not null && !Directory.Exists(path) && !File.Exists(path);

    public bool CanTurnOff => Path is not null && !IsOff;
}

//
// Fork (1.4.7): Find the broken mod, on the Diagnose logs page - Core's BrokenModSearch run against
// this install: the SPT server started with no window, several times, each time with some of the
// server mods off, until the one that keeps it from starting (or logs the errors) is left.
//
// One for the whole app, not the page's: a search goes on while other pages are looked at.
//
// What keeps it safe:
// - refused while SPT runs, a mod is being placed or downloads are queued; downloads are paused
//   for as long as it runs;
// - a copy of the SPT profiles first, which must succeed - and when the starts changed a profile,
//   the copy is put back, since nothing was played in between;
// - the mods it switches are written down first (BrokenModSessionStore), and every one is turned
//   back on at the end, however it ends. A search cut short - the app closed or crashed - is found
//   at the next start and put back then.
//
public sealed partial class BrokenModFinder : LocalizedViewModel
{
    private readonly BrokenModSessionStore _store = new();
    private CancellationTokenSource? _cancel;

    // The progress line: which start it is on, how long the ones before it took.
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private readonly List<TimeSpan> _starts = [];
    private readonly System.Windows.Threading.DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private BrokenModProgress? _now;
    private TimeSpan _nowBegan;

    private static string ResultFile => Path.Combine(AppPaths.DataDirectory, "broken_mod_result.json");

    public BrokenModFinder()
    {
        _tick.Tick += (_, _) => SayProgress();
        LoadResult();
    }

    /// <summary>Raised when a search ends and the mods are back - the logs have changed.</summary>
    public event EventHandler? Ended;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanStart))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isStopping;

    [ObservableProperty]
    private string? _progressText;

    /// <summary>0 to 100; how far through the starts it expects to make.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _statusMessage;

    // ---- how the last search ended ---------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultTitle;

    [ObservableProperty]
    private string? _resultBody;

    /// <summary>What to do about it, when a mod was found.</summary>
    [ObservableProperty]
    private string? _resultNext;

    [ObservableProperty]
    private string? _resultLines;

    [ObservableProperty]
    private string? _resultNote;

    /// <summary>Said once a found mod was turned off from here, with Start server beside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultDone))]
    private string? _resultDone;

    public bool HasResultDone => ResultDone is not null;

    public ObservableCollection<FoundMod> Found { get; } = [];

    public bool HasResult => ResultTitle is not null;

    // ---- a search that was cut short -------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInterrupted), nameof(CanStart))]
    private string? _interruptedText;

    public bool IsInterrupted => InterruptedText is not null;

    public bool IsIdle => !IsRunning;

    public bool CanStart => !IsRunning && !IsInterrupted;

    private static string T(string format, params object?[] values) => LocalizationService.Text(format, values);

    private static string TitleOf(InstalledMod mod) => mod.DeclaredName is { Length: > 0 } declared ? declared : mod.Name;

    // ---- start -----------------------------------------------------------------------------------

    [RelayCommand]
    private async Task StartAsync()
    {
        if (!CanStart) return;

        if (AppServices.SptEnvironment.InstallPath is not { Length: > 0 } install)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (AppServices.DownloadQueue.Items.Any(i => i.Status is DownloadQueueItemStatus.Pending
                or DownloadQueueItemStatus.Downloading or DownloadQueueItemStatus.Installing))
        {
            StatusMessage = Strings.Search_WaitForDownloads;
            return;
        }

        if (ModInstallService.RunningBlockers(install) is { Count: > 0 } running)
        {
            StatusMessage = ModInstallProblems.InstallInUse(running, ModInstallAction.Disable);
            return;
        }

        var (mods, needs) = await Task.Run(() => BrokenModSearch.Suspects(InstalledModScanner.Scan(install)));
        if (mods.Count == 0)
        {
            StatusMessage = Strings.Search_NoServerMods;
            return;
        }

        var about = 2 + new ModBisect(mods.Count, needs).TriesLeft;
        if (SteamDialog.Show(
                Strings.Search_Title,
                new System.Windows.Controls.TextBlock
                {
                    Text = T(Strings.Search_ConfirmFormat, mods.Count, about),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520,
                },
                new SteamDialogChoice(Strings.Search_Start, SteamDialogButton.Blue, IsDefault: true),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey)) != 0)
        {
            return;
        }

        ClearResult();
        StatusMessage = null;
        IsRunning = true;
        _clock.Restart();
        _starts.Clear();
        _now = null;
        Progress = 0;
        ProgressText = Strings.Search_Preparing;

        var cancel = _cancel = new CancellationTokenSource();
        var queueWasPaused = AppServices.DownloadQueue.IsPaused;
        AppServices.DownloadQueue.IsPaused = true;

        var session = new BrokenModSession
        {
            InstallPath = install,
            StartedAt = DateTimeOffset.Now,
            Mods = [.. mods.Select(m => m.FolderPath)],
        };

        BrokenModResult? result = null;
        string? problem = null;
        var stopped = false;
        ProfileBackup? profiles = null;

        try
        {
            profiles = await Task.Run(() =>
                AppServices.ProfileBackups.EnsureBackupBefore(install, ProfileBackups.BeforeSearch)
                    ? AppServices.ProfileBackups.List(install).FirstOrDefault() ?? NoProfiles
                    : null);

            if (profiles is null)
            {
                problem = Strings.Search_BackupFailed;
            }
            else
            {
                session.Logs = ServerTrial.LogLengths(install);
                _store.Save(session);
                AppLog.Info("Search", $"searching {mods.Count} server mod(s) of {install}");

                var progress = new Progress<BrokenModProgress>(p =>
                {
                    // Posted here: one may arrive after the search is over.
                    if (!_tick.IsEnabled && _now is not null) return;

                    if (_now is not null) _starts.Add(_clock.Elapsed - _nowBegan);
                    _now = p;
                    _nowBegan = _clock.Elapsed;
                    _tick.Start();

                    Progress = 100.0 * (p.Try - 1) / Math.Max(p.About, p.Try);
                    SayProgress();
                });

                // The mods changed since this install's server was last seen up are tried first.
                var recent = await Task.Run(() => new ServerUpLog().Last(install) is { } up
                    ? BrokenModSearch.ChangedSince(session.Mods, up)
                    : null);
                if (recent is not null) AppLog.Info("Search", $"{recent.Count} mod(s) changed since the server was last up");

                result = await Task.Run(() => BrokenModSearch.RunAsync(mods.Count, needs, async (on, errors, ct) =>
                {
                    var wanted = on.Select(i => session.Mods[i]).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var moved = BrokenModSessionStore.Set(session, wanted);
                    if (moved.Failed.Count > 0) throw new SearchFailed(Failures(moved.Failed));

                    var outcome = await ServerTrial.RunAsync(install, ct, settle: errors).ConfigureAwait(false);
                    AppLog.Info("Search", $"{on.Count} on: {(outcome.Started ? "started" : outcome.TimedOut ? "timed out" : "did not start")}, {outcome.Errors.Count} error(s)");
                    return outcome;
                }, progress, cancel.Token, recent));
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        catch (ServerTrialException ex)
        {
            problem = Describe(ex);
        }
        catch (ModInstallException ex)
        {
            problem = ModInstallProblems.Describe(ex);
        }
        catch (SearchFailed ex)
        {
            problem = ex.Message;
        }
        catch (Exception ex)
        {
            // Whatever it was: the mods still go back on below.
            AppLog.Error("Search", $"the search failed: {ex}");
            problem = ex.Message;
        }

        _tick.Stop();
        ProgressText = Strings.Search_PuttingBack;
        Progress = 100;

        var back = await PutBackAsync(session, profiles == NoProfiles ? null : profiles, trimLogs: true);

        AppServices.DownloadQueue.IsPaused = queueWasPaused;
        _cancel = null;
        cancel.Dispose();
        IsStopping = false;
        IsRunning = false;
        ProgressText = null;

        if (problem is not null)
        {
            AppLog.Warn("Search", $"couldn't finish: {problem}");
            StatusMessage = Sentences(T(Strings.Search_FailedFormat, problem), back.AllBack ? Strings.Search_AllBack : null);
        }
        else if (stopped)
        {
            StatusMessage = back.AllBack ? Sentences(Strings.Search_Stopped, Narrowed(mods)) : null;
        }
        else if (result is not null)
        {
            Show(result, mods, back, _clock.Elapsed);
            SaveResult();

            // A search takes minutes, and the window is seldom the one in front when it ends.
            if (Application.Current?.MainWindow is not { IsActive: true })
                UpdateToasts.ShowMessage("search", Strings.Search_Title, Sentences(ResultTitle, ResultBody));
        }

        InstalledViewModel.NotifyModsMoved();
        Ended?.Invoke(this, EventArgs.Empty);
    }

    // What a stopped search had got to: the mods the fault could still be, named when they are few.
    private string? Narrowed(IReadOnlyList<InstalledMod> mods)
    {
        if (_now?.Suspects is not { Count: > 0 } suspects || suspects.Count >= mods.Count) return null;

        var names = suspects.Where(i => i >= 0 && i < mods.Count).Select(i => TitleOf(mods[i])).ToList();
        AppLog.Info("Search", $"stopped with {names.Count} suspect(s): {string.Join(", ", names)}");

        return names.Count <= 8
            ? T(Strings.Search_StoppedNarrowedFormat, names.Count, mods.Count, TextLists.Join(names))
            : T(Strings.Search_StoppedNarrowedCountFormat, names.Count, mods.Count);
    }

    // The progress line, again every second: which start, the time so far and about how much is left.
    private void SayProgress()
    {
        if (_now is not { } p || IsStopping || !_tick.IsEnabled) return;

        var left = BrokenModSearch.TimeLeft(_starts, p.About - p.Try + 1, _clock.Elapsed - _nowBegan);
        ProgressText = Sentences(
            T(Strings.Search_ProgressFormat, p.Try, p.About, p.ModsOn, p.Mods),
            T(Strings.Search_ElapsedFormat, Duration(_clock.Elapsed)),
            left is not { } time ? null
                : time.TotalSeconds < 60 ? Strings.Search_LeftUnderMinute
                : T(Strings.Search_LeftMinutesFormat, (int)Math.Ceiling(time.TotalMinutes)));
    }

    private static string Duration(TimeSpan took) => took.TotalMinutes >= 1
        ? T(Strings.Search_DurationMinutesFormat, (int)took.TotalMinutes, took.Seconds)
        : T(Strings.Search_DurationSecondsFormat, Math.Max(1, (int)Math.Round(took.TotalSeconds)));

    // Stands for "there are no profiles to copy" - the search goes ahead, with nothing to put back.
    private static readonly ProfileBackup NoProfiles = new("", default, "", 0, 0);

    private sealed class SearchFailed(string message) : Exception(message);

    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning || IsStopping) return;

        IsStopping = true;
        ProgressText = Strings.Search_Stopping;
        _cancel?.Cancel();
    }

    [RelayCommand]
    private void Dismiss() => ClearResult();

    [RelayCommand]
    private void ShowMod(FoundMod? mod)
    {
        if (mod is not null) AppNavigation.ShowInSubscribedItems(mod.Name);
    }

    //
    // The result's "Turn off": Subscribed items' own Disable, with what it asks - the mods that
    // need this one, a profile holding its items - and its undo.
    //
    [RelayCommand]
    private async Task TurnOffModAsync(FoundMod? mod)
    {
        if (IsRunning || mod is not { Path: { } path, IsOff: false }) return;

        var (done, said) = await InstalledViewModel.ForActions.DisableModFolderAsync(path);
        if (!done)
        {
            StatusMessage = said;
            return;
        }

        AppLog.Info("Search", $"turned off {mod.Name} from the result");
        StatusMessage = null;
        mod.IsOff = true;
        SayTurnedOff();
    }

    private void SayTurnedOff()
    {
        var off = Found.Where(f => f.IsOff).Select(f => f.Title).ToList();
        ResultDone = off.Count > 0 ? T(Strings.Search_TurnedOffFormat, TextLists.Join(off)) : null;
    }

    [RelayCommand]
    private static void StartServer() => AppNavigation.StartServerOnPlay();

    /// <summary>Diagnose logs with the search's question open - from where a server was seen to stop.</summary>
    public void OpenAndStart()
    {
        AppNavigation.Navigate(typeof(DiagnosePage));
        Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () => { if (CanStart) StartCommand.Execute(null); });
    }

    private void ClearResult()
    {
        ResultTitle = null;
        ResultBody = null;
        ResultNext = null;
        ResultLines = null;
        ResultNote = null;
        ResultDone = null;
        Found.Clear();

        try
        {
            File.Delete(ResultFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Search", $"couldn't clear the kept result: {ex.Message}");
        }
    }

    private void SaveResult()
    {
        if (ResultTitle is null) return;

        try
        {
            SafeFile.WriteText(ResultFile, JsonSerializer.Serialize(new SavedSearchResult
            {
                Title = ResultTitle,
                Body = ResultBody,
                Next = ResultNext,
                Lines = ResultLines,
                Note = ResultNote,
                Found = [.. Found.Select(f => new[] { f.Name, f.Title, f.Path ?? string.Empty })],
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Search", $"couldn't keep the result: {ex.Message}");
        }
    }

    private void LoadResult()
    {
        try
        {
            if (!File.Exists(ResultFile)) return;
            if (JsonSerializer.Deserialize<SavedSearchResult>(File.ReadAllText(ResultFile)) is not { Title.Length: > 0 } saved) return;

            ResultTitle = saved.Title;
            ResultBody = saved.Body;
            ResultNext = saved.Next;
            ResultLines = saved.Lines;
            ResultNote = saved.Note;
            foreach (var mod in saved.Found.Where(f => f is { Length: >= 2 }))
                Found.Add(new FoundMod(mod[0], mod[1], mod.Length > 2 && mod[2].Length > 0 ? mod[2] : null));

            SayTurnedOff();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Debug("Search", $"couldn't read the kept result: {ex.Message}");
        }
    }

    // ---- the end ---------------------------------------------------------------------------------

    private sealed record PutBack(bool AllBack, bool ProfilesRestored);

    // Every mod of the session back on and, when the starts changed a profile, the profiles as
    // they were. The session's file goes only when every mod is back.
    //
    // trimLogs: whether what the server logged since the search began is its own. Not when the
    // server was found running afterwards - that run may be the user's.
    private async Task<PutBack> PutBackAsync(BrokenModSession session, ProfileBackup? profiles, bool trimLogs)
    {
        var (problem, restored) = await Task.Run(async () =>
        {
            try
            {
                await ServerTrial.StopAsync(session.InstallPath).ConfigureAwait(false);

                var outcome = BrokenModSessionStore.PutBack(session);
                if (outcome.Failed.Count > 0) return (Failures(outcome.Failed), false);

                // The search's own starts are not the server's last run: out of its log again.
                if (trimLogs && session.Logs is { } logs) ServerTrial.TrimLogs(session.InstallPath, logs);

                var changed = profiles is not null
                    && AppServices.ProfileBackups.BackupIfChanged(session.InstallPath, ProfileBackups.BeforeSearch) is not null;
                if (changed)
                {
                    AppServices.ProfileBackups.Restore(profiles!, session.InstallPath);
                    AppLog.Info("Search", "the starts changed the profiles; put back as they were");
                }

                return ((string?)null, changed);
            }
            catch (ServerTrialException ex)
            {
                return (Describe(ex), false);
            }
            catch (ModInstallException ex)
            {
                return (ModInstallProblems.Describe(ex), false);
            }
            catch (Exception ex)
            {
                AppLog.Error("Search", $"putting back failed: {ex}");
                return (ex.Message, false);
            }
        });

        var left = BrokenModSessionStore.TurnedOff(session);
        if (left == 0)
        {
            _store.Clear();
            InterruptedText = null;
            if (problem is not null) StatusMessage = problem; // the mods are back; the profiles may not be
            return new PutBack(true, restored);
        }

        AppLog.Warn("Search", $"{left} mod(s) still off: {problem}");
        InterruptedText = Sentences(T(Strings.Search_InterruptedFormat, left), problem);
        return new PutBack(false, restored);
    }

    private void Show(BrokenModResult result, IReadOnlyList<InstalledMod> mods, PutBack back, TimeSpan took)
    {
        var down = result.Symptom == BrokenModSymptom.ServerDoesNotStart;

        switch (result.Verdict)
        {
            case BrokenModVerdict.NothingWrong:
                ResultTitle = Strings.Search_NothingWrongTitle;
                ResultBody = Strings.Search_NothingWrongBody;
                break;

            case BrokenModVerdict.NotTheMods:
                ResultTitle = Strings.Search_NotTheModsTitle;
                ResultBody = down ? Strings.Search_NotTheModsDownBody : Strings.Search_NotTheModsErrorsBody;
                break;

            default:
                var culprit = mods[result.Culprit!.Value];
                Found.Add(new FoundMod(culprit.Name, TitleOf(culprit), culprit.FolderPath));

                if (result.Partner is { } other)
                {
                    var partner = mods[other];
                    Found.Add(new FoundMod(partner.Name, TitleOf(partner), partner.FolderPath));
                    ResultTitle = T(Strings.Search_FoundPairTitleFormat, TitleOf(culprit), TitleOf(partner));
                    ResultBody = T(down ? Strings.Search_FoundPairDownFormat : Strings.Search_FoundPairErrorsFormat, TitleOf(culprit), TitleOf(partner));
                    ResultNext = Strings.Search_WhatToDoPair;
                }
                else
                {
                    ResultTitle = T(Strings.Search_FoundTitleFormat, TitleOf(culprit));
                    ResultBody = T(down ? Strings.Search_FoundDownFormat : Strings.Search_FoundErrorsFormat, TitleOf(culprit));
                    ResultNext = Strings.Search_WhatToDo;
                }

                break;
        }

        // The lines themselves - a few, each with a line or two under it.
        var lines = result.Errors.Take(4)
            .Select(e => string.Join("\n", new[] { "[" + e.Source + "] " + e.Message }.Concat(e.More.Take(2))))
            .ToList();
        ResultLines = lines.Count > 0 ? string.Join("\n", lines) : null;

        ResultNote = Sentences(
            T(Strings.Search_SummaryFormat, mods.Count, result.Tries, Duration(took)),
            result.RecentTried > 0 ? T(Strings.Search_RecentFirstFormat, result.RecentTried) : null,
            back.AllBack ? Strings.Search_AllBack : null,
            back.ProfilesRestored ? Strings.Search_ProfilesPutBack : null);

        AppLog.Info("Search", $"{result.Verdict} after {result.Tries} start(s) in {took.TotalSeconds:0} s: {ResultTitle}");
    }

    // ---- a search cut short ----------------------------------------------------------------------

    /// <summary>At startup: a search the last run of the app did not finish is put back now, when SPT
    /// is not running from that install; otherwise the Diagnose logs page says so and offers it.</summary>
    public async Task RecoverAsync()
    {
        if (IsRunning || _store.Load() is not { } session) return;

        var left = BrokenModSessionStore.TurnedOff(session);
        if (left == 0)
        {
            _store.Clear();
            return;
        }

        AppLog.Info("Search", $"a search of {session.InstallPath} was cut short; {left} mod(s) off");

        if (ModInstallService.RunningBlockers(session.InstallPath).Count > 0)
        {
            InterruptedText = T(Strings.Search_InterruptedFormat, left);
            SteamMessageBox.Show(Sentences(InterruptedText, Strings.Search_InterruptedWhere), Strings.Search_Title);
            return;
        }

        // Nothing of SPT runs, and the app was not there to see anything else start: the lines
        // since are taken to be the search's.
        var back = await PutBackAsync(session, null, trimLogs: true);
        SteamMessageBox.Show(
            back.AllBack ? T(Strings.Search_PutBackDoneFormat, left) : Sentences(InterruptedText, Strings.Search_InterruptedWhere),
            Strings.Search_Title);

        if (back.AllBack) InstalledViewModel.NotifyModsMoved();
    }

    /// <summary>The Diagnose logs page's "Turn them back on", for a search that was cut short. The
    /// server is stopped first when it runs - asked, since it may be one the user started.</summary>
    [RelayCommand]
    private async Task TurnBackOnAsync()
    {
        if (IsRunning || _store.Load() is not { } session)
        {
            InterruptedText = null;
            return;
        }

        var running = ModInstallService.RunningBlockers(session.InstallPath);
        var serverOnly = running.Count > 0 && running.All(r => r.Contains("Server", StringComparison.OrdinalIgnoreCase));
        if (running.Count > 0 && !serverOnly)
        {
            StatusMessage = ModInstallProblems.InstallInUse(running, ModInstallAction.Enable);
            return;
        }

        if (serverOnly && SteamDialog.Show(
                Strings.Search_Title,
                Strings.Search_StopServerFirst,
                new SteamDialogChoice(Strings.Search_StopServerAndTurnOn, SteamDialogButton.Blue, IsDefault: true),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey)) != 0)
        {
            return;
        }

        var left = BrokenModSessionStore.TurnedOff(session);
        var back = await PutBackAsync(session, null, trimLogs: !serverOnly);
        if (back.AllBack) StatusMessage = T(Strings.Search_PutBackDoneFormat, left);

        InstalledViewModel.NotifyModsMoved();
        Ended?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the app may close now. While a search runs it may not: the search is told to
    /// stop, which turns the mods back on, and closing waits for that.</summary>
    public bool MayClose()
    {
        if (!IsRunning) return true;

        Stop();
        SteamMessageBox.Show(Strings.Search_CloseWhileRunning, Strings.Search_Title);
        return false;
    }

    // ---- wording ---------------------------------------------------------------------------------

    private static string Describe(ServerTrialException ex) => ex.Problem switch
    {
        ServerTrialProblem.AlreadyRunning => Strings.Search_ProblemAlreadyRunning,
        ServerTrialProblem.PortTaken => Strings.Search_ProblemPortTaken,
        ServerTrialProblem.CouldNotStop => Strings.Search_ProblemCouldNotStop,
        _ => T(Strings.Search_ProblemCouldNotStartFormat, ex.Message),
    };

    private static string Failures(IReadOnlyList<ModDisableFailure> failed) =>
        string.Join(" ", failed.Select(f => T(Strings.Search_MoveFailedFormat, f.ModName, f.Reason)));

    private static string Sentences(params string?[] parts) => string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
