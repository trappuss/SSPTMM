using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// The Play page: start this install's server, its launcher, and - on a headless setup - its Fika
// headless launcher, and say which of them is already up.
//
// The server and the headless can also be RESTARTED, one at a time and never as a side effect of
// each other, and the server STOPPED: while it is up, Start server gives way to a red Stop server,
// confirmed on the card like a restart.
//
public partial class PlayViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    //
    // All three targets are started outside this app, so there is nothing to await and no event to
    // subscribe to - a poll is the only way the buttons can tell that the server came up, or that
    // the game was closed from somewhere else. Runs only while the page is on screen.
    //
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerState))]
    [NotifyPropertyChangedFor(nameof(ServerPath))]
    [NotifyPropertyChangedFor(nameof(CanStartServer))]
    [NotifyPropertyChangedFor(nameof(CanRestartServer))]
    [NotifyPropertyChangedFor(nameof(IsServerRunning))]
    [NotifyPropertyChangedFor(nameof(CanStopServer))]
    [NotifyCanExecuteChangedFor(nameof(AskRestartServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskStopServerCommand))]
    private SptLaunchTargetInfo? _server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientState))]
    [NotifyPropertyChangedFor(nameof(ClientPath))]
    [NotifyPropertyChangedFor(nameof(CanStartClient))]
    private SptLaunchTargetInfo? _client;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadlessState))]
    [NotifyPropertyChangedFor(nameof(HeadlessPath))]
    [NotifyPropertyChangedFor(nameof(CanStartHeadless))]
    [NotifyPropertyChangedFor(nameof(CanRestartHeadless))]
    [NotifyPropertyChangedFor(nameof(HasHeadless))]
    [NotifyCanExecuteChangedFor(nameof(AskRestartHeadlessCommand))]
    private SptLaunchTargetInfo? _headless;

    // The result of the last button press, cleared the next time one is pressed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = "";

    // Named to match the shared ErrorText style in App.xaml, which binds to it.
    [ObservableProperty]
    private bool _hasError;

    //
    // The server's own log, shown under its card while opened there - see ServerLogs. Read on the
    // poll only while it is open.
    //
    [ObservableProperty]
    private bool _showServerLog;

    [ObservableProperty]
    private string _serverLogText = "";

    [ObservableProperty]
    private string? _serverLogPath;

    // Set by the page: false while the user has scrolled up to read, so the text is not replaced
    // (and their place lost) under them. Back at the bottom, it follows again.
    public bool FollowServerLog { get; set; } = true;

    partial void OnShowServerLogChanged(bool value)
    {
        if (!value) return;

        // Opened: at the bottom, following.
        FollowServerLog = true;
        UpdateServerLog(AppServices.SptEnvironment.InstallPath);
    }

    private void UpdateServerLog(string? installPath)
    {
        var file = ServerLogs.Newest(installPath);
        ServerLogPath = file;
        ServerLogText = file is null ? Strings.Play_ServerLogNone : string.Join(Environment.NewLine, ServerLogs.Tail(file));
    }

    [RelayCommand]
    private void OpenServerLogFolder()
    {
        if (ServerLogs.Folder(AppServices.SptEnvironment.InstallPath) is not { } folder) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Launch", $"couldn't open {folder}: {ex.Message}");
            Message = Strings.Common_FolderOpenFailed;
        }
    }

    public PlayViewModel()
    {
        _poll.Tick += (_, _) => Refresh();

        // The folder is set on the Options page, and this page has to notice when it changes
        // rather than showing what it found the first time it was opened.
        AppServices.SptEnvironment.PropertyChanged += OnEnvironmentChanged;

        Refresh();
    }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public string ServerState => Server is null ? "" : SptLaunchProblems.DescribeState(Server);

    public string ClientState => Client is null ? "" : SptLaunchProblems.DescribeState(Client);

    public string HeadlessState => Headless is null ? "" : SptLaunchProblems.DescribeState(Headless);

    public string ServerPath => Server?.ExePath ?? "";

    public string ClientPath => Client?.ExePath ?? "";

    public string HeadlessPath => Headless?.ExePath ?? "";

    public bool CanStartServer => Server?.CanLaunch == true;

    public bool CanStartClient => Client?.CanLaunch == true;

    public bool CanStartHeadless => Headless?.CanLaunch == true;

    //
    // Only while it is actually up. A restart is not a second start - offering it on something that
    // is down invites a press that quietly starts a server nobody asked for.
    //
    public bool CanRestartServer => Server?.IsRunning == true && !IsRestarting;

    // While the server is up the Start button is Stop server instead.
    public bool IsServerRunning => Server?.IsRunning == true;

    public bool CanStopServer => Server?.IsRunning == true && !IsRestarting;

    // A server that went down on its own takes the question about stopping it with it.
    partial void OnServerChanged(SptLaunchTargetInfo? value)
    {
        if (value?.IsRunning != true) ConfirmingServerStop = false;
    }

    // The wait for the launcher, called off by Stop server.
    private CancellationTokenSource? _launcherWait;

    public bool CanRestartHeadless => Headless?.IsRunning == true && !IsRestarting;

    //
    // Which target the page is asking about before it kills anything, or null when it is not asking.
    //
    // The confirmation is on the card rather than in a dialog: the warning is about the thing the
    // card is describing, and a modal over a page with three buttons on it is heavier than the
    // decision. Asking at all is not optional - a running server may have somebody in a raid on it,
    // and that is not recoverable by pressing the other button.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmingServerRestart))]
    [NotifyPropertyChangedFor(nameof(ConfirmingHeadlessRestart))]
    private SptLaunchTarget? _confirmingRestart;

    // Asking before Stop server, on the card, for the same reason as a restart.
    [ObservableProperty]
    private bool _confirmingServerStop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestartServer))]
    [NotifyPropertyChangedFor(nameof(CanRestartHeadless))]
    [NotifyPropertyChangedFor(nameof(CanStopServer))]
    [NotifyCanExecuteChangedFor(nameof(AskRestartServerCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskRestartHeadlessCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskStopServerCommand))]
    private bool _isRestarting;

    public bool ConfirmingServerRestart => ConfirmingRestart == SptLaunchTarget.Server;

    public bool ConfirmingHeadlessRestart => ConfirmingRestart == SptLaunchTarget.Headless;

    //
    // Whether the headless card belongs on this page.
    //
    // The launcher on disk is the usual answer - only a setup running a headless client has one, so
    // on every other install the card stays off the page rather than showing a dead button for
    // something that was never installed. The setting is the second way in, for a machine that
    // starts its headless some other way: it has told the app what it is, and hiding the card would
    // contradict that.
    //
    public bool HasHeadless => Headless?.Exists == true || RunsHeadlessClient;

    // Whether Start server runs it without its window (Options) - the card says which it does.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerDescription))]
    private bool _hidesServerWindow;

    public string ServerDescription => HidesServerWindow ? Strings.Play_ServerDescriptionHidden : Strings.Play_ServerDescription;

    // What Options has been told this machine is - see AppSettings.PlaysHere / RunsHeadlessClient.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeadless))]
    [NotifyPropertyChangedFor(nameof(RoleSummary))]
    [NotifyPropertyChangedFor(nameof(NeedsRoleAnswer))]
    private bool _runsHeadlessClient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleSummary))]
    private bool _playsHere = true;

    //
    // Shown on the card because it is the thing that decides what a served mod list brings to this
    // machine, and the Play page is where somebody stands when that matters.
    //
    public string RoleSummary => (PlaysHere, RunsHeadlessClient) switch
    {
        (false, true) => Strings.Play_RoleHeadless,
        (true, true) => Strings.Play_RolePlaysAndHosts,
        _ => "",
    };

    //
    // A launcher is here but nobody has said what the machine is, so a served list is being filtered
    // as though this were an ordinary player - which is the safe reading, and probably not the right
    // one on a box with a headless launcher in it.
    //
    // The prompt for this fires when the install folder is set, which someone who set theirs months
    // ago will never do again. This is how they find out there is a question to answer.
    //
    public bool NeedsRoleAnswer => Headless?.Exists == true && !RunsHeadlessClient;

    // Called by the page, so the poll only runs while it is the visible page.
    public void StartPolling()
    {
        Refresh();
        _poll.Start();
    }

    public void StopPolling() => _poll.Stop();

    [RelayCommand]
    private void Refresh()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;

        // Re-read rather than cached, because Options can change either of these while this page is
        // open and the poll is already running.
        var settings = new SettingsService().Load();

        Server = SptLaunchService.Describe(installPath, SptLaunchTarget.Server);
        Client = SptLaunchService.Describe(installPath, SptLaunchTarget.Client);
        Headless = SptLaunchService.Describe(
            installPath, SptLaunchTarget.Headless, settings.HeadlessLauncherPath);

        HidesServerWindow = settings.HideServerWindow;

        var roles = settings.Roles;
        PlaysHere = roles.HasFlag(InstallRoles.Player);
        RunsHeadlessClient = roles.HasFlag(InstallRoles.Headless);

        if (ShowServerLog && FollowServerLog) UpdateServerLog(installPath);
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        var ports = SptServerReadiness.PortsFor(Server?.ExePath);
        _portsBeforeStart = ports.Where(p => !SptServerReadiness.IsListening(new HashSet<int> { p })).ToHashSet();

        var settings = new SettingsService().Load();
        Start(SptLaunchTarget.Server, settings.HideServerWindow);

        // With no window, the log below is where the server says what it is doing.
        if (!HasError && settings.HideServerWindow) ShowServerLog = true;

        if (HasError) return;

        var openLauncher = settings.StartLauncherAfterServer && Client is { CanLaunch: true };

        // Watched for without the launcher too when it has no window: otherwise a server that gives
        // up on starting would just be gone, with nothing said.
        if (openLauncher || settings.HideServerWindow) await StartLauncherWhenServerIsUpAsync(openLauncher);
    }

    // Set while the page waits for the server to open its port.
    [ObservableProperty]
    private bool _isWaitingForServer;

    //
    // The Options switch: once the server is listening, the launcher. Given three minutes - a
    // heavily modded server can take a while to load - and dropped if the server goes down or the
    // launcher is started some other way meanwhile.
    //
    private async Task StartLauncherWhenServerIsUpAsync(bool openLauncher = true)
    {
        IsWaitingForServer = true;
        Message = openLauncher ? Strings.Play_WaitingForServer : Strings.Play_WaitingForServerOnly;

        _launcherWait?.Cancel();
        var cancel = _launcherWait = new CancellationTokenSource();

        try
        {
            var installPath = AppServices.SptEnvironment.InstallPath;
            var ports = _portsBeforeStart ?? SptServerReadiness.PortsFor(Server?.ExePath);
            if (ports.Count == 0)
            {
                // Everything the server could use was already taken before it started: whatever
                // answers there is not this server, so there is nothing to wait for.
                HasError = true;
                Message = Text(Strings.Play_ServerPortTakenFormat, string.Join(", ", SptServerReadiness.PortsFor(Server?.ExePath).Order()));
                return;
            }

            var started = DateTime.UtcNow;

            // Looked at afresh each second (the page's own poll stops when it is left). The first
            // seconds are given regardless: the new process may not be listed yet.
            bool StillWanted() =>
                DateTime.UtcNow - started < TimeSpan.FromSeconds(15)
                || (SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning
                    && (!openLauncher || !SptLaunchService.Describe(installPath, SptLaunchTarget.Client).IsRunning));

            bool up;
            try
            {
                up = await SptServerReadiness.WaitUntilListeningAsync(ports, TimeSpan.FromMinutes(3), StillWanted, cancel.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Stopped or restarted meanwhile: that says what happened.
            if (cancel.IsCancellationRequested) return;

            Refresh();
            if (up && openLauncher && Client is { CanLaunch: true })
            {
                Start(SptLaunchTarget.Client);
            }
            else if (up)
            {
                // Up, and no launcher to open (not asked for, or already open).
                Message = Strings.Play_ServerIsUp;
            }
            else if (!up && openLauncher && Server?.IsRunning == true && Client?.IsRunning != true)
            {
                HasError = true;
                Message = Text(Strings.Play_ServerNotUpFormat, string.Join(", ", ports.Order()));
            }
            else if (!up && Server?.IsRunning != true)
            {
                // It went before it was up - on startup SPT gives up on a taken port or a mod that
                // fails its checks, and says why in its log (the only place, when it has no window).
                HasError = true;
                Message = openLauncher ? Strings.Play_ServerStoppedStarting : Strings.Play_ServerStoppedStartingOnly;
                ShowServerLog = true;
            }
            else if (!up && !openLauncher)
            {
                HasError = true;
                Message = Text(Strings.Play_ServerNotUpOnlyFormat, string.Join(", ", ports.Order()));
            }
            else if (!up)
            {
                // The launcher was opened some other way meanwhile: nothing left to wait for or say.
                Message = "";
            }
        }
        finally
        {
            IsWaitingForServer = false;
            if (ReferenceEquals(_launcherWait, cancel)) _launcherWait = null;
        }
    }

    // The server's ports that nothing held before it was started - only those can say it is up.
    private IReadOnlySet<int>? _portsBeforeStart;

    [RelayCommand]
    private void StartClient() => Start(SptLaunchTarget.Client);

    [RelayCommand]
    private void StartHeadless() => Start(SptLaunchTarget.Headless);

    private void Start(SptLaunchTarget target, bool hidden = false)
    {
        var result = SptLaunchService.Launch(
            AppServices.SptEnvironment.InstallPath, target, HeadlessOverride(), hidden);

        HasError = !result.Started;
        Message = result.Started
            ? Text(Strings.Play_StartedFormat, result.Info.ProcessName)
            : SptLaunchProblems.Describe(result);

        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanRestartServer))]
    private void AskRestartServer()
    {
        ConfirmingServerStop = false;
        ConfirmingRestart = SptLaunchTarget.Server;
    }

    [RelayCommand(CanExecute = nameof(CanRestartHeadless))]
    private void AskRestartHeadless() => ConfirmingRestart = SptLaunchTarget.Headless;

    [RelayCommand]
    private void CancelRestart() => ConfirmingRestart = null;

    [RelayCommand(CanExecute = nameof(CanStopServer))]
    private void AskStopServer()
    {
        ConfirmingRestart = null;
        ConfirmingServerStop = true;
    }

    [RelayCommand]
    private void CancelStopServer() => ConfirmingServerStop = false;

    // Off the UI thread, as a restart: stopping waits on the process to go.
    [RelayCommand]
    private async Task ConfirmStopServerAsync()
    {
        if (!ConfirmingServerStop) return;

        ConfirmingServerStop = false;
        _launcherWait?.Cancel();
        IsRestarting = true;
        _poll.Stop();

        try
        {
            var installPath = AppServices.SptEnvironment.InstallPath;
            var result = await Task.Run(() => SptLaunchService.StopTarget(installPath, SptLaunchTarget.Server));

            HasError = result.Problem != SptLaunchProblem.None;
            Message = HasError
                ? SptLaunchProblems.Describe(result)
                : Text(Strings.Play_StoppedFormat, result.Info.ProcessName);
        }
        finally
        {
            IsRestarting = false;
            Refresh();
            _poll.Start();
        }
    }

    //
    // Off the UI thread: stopping waits on a process to actually go, up to five seconds twice over,
    // and a window that stops redrawing while it happens reads as the app having hung on a button
    // whose whole job is killing something.
    //
    [RelayCommand]
    private async Task ConfirmRestartAsync()
    {
        if (ConfirmingRestart is not { } target) return;

        ConfirmingRestart = null;
        IsRestarting = true;

        // A wait for the server before the restart is about a process that is going.
        _launcherWait?.Cancel();
        var hidden = false;
        var watchRestart = false;
        IReadOnlySet<int>? portsAfterRestart = null;

        // The poll would otherwise redraw the card mid-stop and offer a Start for the gap between
        // the process going and the new one appearing.
        _poll.Stop();

        try
        {
            var installPath = AppServices.SptEnvironment.InstallPath;
            var headless = HeadlessOverride();

            hidden = new SettingsService().Load().HideServerWindow;
            var result = await Task.Run(() => SptLaunchService.Restart(installPath, target, headless, hidden));

            HasError = !result.Started;
            Message = result.Started
                ? Text(Strings.Play_RestartedFormat, result.Info.ProcessName)
                : SptLaunchProblems.Describe(result);

            watchRestart = result.Started && hidden && target == SptLaunchTarget.Server;
            if (watchRestart)
            {
                portsAfterRestart = SptServerReadiness.PortsFor(Server?.ExePath)
                    .Where(p => !SptServerReadiness.IsListening(new HashSet<int> { p }))
                    .ToHashSet();
            }
        }
        finally
        {
            IsRestarting = false;
            Refresh();
            _poll.Start();
        }

        // With no window, the log is where it says how the restart went, and a start that fails is
        // reported rather than just gone. Its ports were freed by the stop; one answering already,
        // a moment after the start (SPT takes seconds to open them), is held by something else.
        if (watchRestart)
        {
            ShowServerLog = true;
            _portsBeforeStart = portsAfterRestart;
            await StartLauncherWhenServerIsUpAsync(openLauncher: false);
        }
    }

    private static string? HeadlessOverride() => new SettingsService().Load().HeadlessLauncherPath;

    private void OnEnvironmentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SptEnvironmentViewModel.InstallPath)) Refresh();
    }
}
