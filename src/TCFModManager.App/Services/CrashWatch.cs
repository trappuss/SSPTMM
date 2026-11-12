using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Fork (1.4.7): says so when the SPT server or the game crashes, with the way to Diagnose logs
// beside it - a bar at the top of the window (MainWindow.xaml), and a Windows notification when
// the window is not the one in front. Diagnose logs is under Tools, where someone who has just
// lost a raid has no reason to look.
//
// The server: SPT logs its giving up as Critical, so its log is what is watched - from where it
// ended when the app started, so an old crash is not news. That also covers a server that fails to
// start, and one started outside this app. Not while Find the broken mod runs: it starts a server
// that is expected to fail.
//
// The game: its exit code (CrashSigns.IsCrashExitCode), unless this app closed it.
//
public sealed partial class CrashWatch : LocalizedViewModel
{
    // HUNCH: often enough to feel prompt, seldom enough to cost nothing.
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(3);

    // HUNCH: how long after this app closes the game its ending is not counted.
    private static readonly TimeSpan ClosedByAppFor = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _timer = new() { Interval = Every };

    private string? _install;
    private Dictionary<string, long> _logs = [];
    private Process? _game;
    private DateTime _gameClosedByAppUntil = DateTime.MinValue;
    private bool _checking;
    private bool _serverCrashed;
    private bool _serverUp;

    [ObservableProperty]
    private bool _isOpen;

    public string Title => _serverCrashed ? Strings.Crash_ServerTitle : Strings.Crash_GameTitle;

    // Called once the main window has loaded.
    public void Start()
    {
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
    }

    /// <summary>This app is about to close the game: its ending is no crash.</summary>
    public void GameClosedByApp() => _gameClosedByAppUntil = DateTime.UtcNow + ClosedByAppFor;

    /// <summary>The server went down before it was up - the Play page saw it.</summary>
    public void ServerStopped() => Raise(server: true);

    private async Task CheckAsync()
    {
        if (_checking) return;

        var install = AppServices.SptEnvironment.InstallPath;
        var searching = AppServices.BrokenModFinder.IsRunning;

        _checking = true;
        try
        {
            var (serverLine, gameCode) = await Task.Run(() =>
                Look(install, searching, GameCloser.SparesWindowless(new SettingsService().Load(), install)));

            if (serverLine is not null)
            {
                AppLog.Info("Crash", $"the server logged: {serverLine}");
                Raise(server: true);
            }
            else if (gameCode is not null && DateTime.UtcNow > _gameClosedByAppUntil)
            {
                Raise(server: false);
            }
        }
        catch (Exception ex)
        {
            // A watcher in the background: nothing it trips over is worth the app.
            AppLog.Debug("Crash", $"couldn't look: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }

    // Off the UI thread. The server's Critical line since the last look, and the game's exit code
    // when it has just ended on a fault.
    private (string? ServerLine, int? GameCode) Look(string? install, bool searching, bool sparesWindowless)
    {
        if (string.IsNullOrWhiteSpace(install))
        {
            _install = null;
            return (null, null);
        }

        // Another install, or the first look: from here on.
        if (!string.Equals(install, _install, StringComparison.OrdinalIgnoreCase))
        {
            _install = install;
            _logs = ServerTrial.LogLengths(install);
            _game?.Dispose();
            _game = null;
            _serverUp = false;
            return (null, null);
        }

        string? serverLine = null;
        var now = ServerTrial.LogLengths(install);
        if (searching)
        {
            _logs = now;
        }
        else if (now.Any(file => file.Value != _logs.GetValueOrDefault(file.Key)))
        {
            // A file that got shorter (Find the broken mod takes its own lines back out) has nothing new.
            var since = now.ToDictionary(file => file.Key, file => Math.Min(file.Value, _logs.GetValueOrDefault(file.Key)), StringComparer.OrdinalIgnoreCase);
            serverLine = CrashSigns.ServerCrash(ServerTrial.ErrorsSince(install, since))?.Message;
            _logs = now;
        }

        // When the server is first seen up: Find the broken mod tries what changed since then first.
        var server = SptLaunchService.Describe(install, SptLaunchTarget.Server);
        var up = !searching && server.IsRunning && SptServerReadiness.IsListening(SptServerReadiness.PortsFor(server.ExePath));
        if (up && !_serverUp) new ServerUpLog().Note(install, DateTimeOffset.Now);
        _serverUp = up;

        int? gameCode = null;
        if (_game is null)
        {
            _game = SptLaunchService.WatchGame(install, sparesWindowless);
        }
        else if (_game.HasExited)
        {
            var code = _game.ExitCode;
            _game.Dispose();
            _game = null;
            AppLog.Info("Crash", $"the game ended with code 0x{code:X8}");
            if (CrashSigns.IsCrashExitCode(code)) gameCode = code;
        }

        return (serverLine, gameCode);
    }

    private void Raise(bool server)
    {
        // One at a time: the server going takes the game with it, and the first is the cause.
        if (IsOpen) return;

        AppLog.Info("Crash", server ? "suggesting Diagnose logs for the server" : "suggesting Diagnose logs for the game");

        _serverCrashed = server;
        OnPropertyChanged(nameof(Title));
        IsOpen = true;

        if (Application.Current?.MainWindow is not { IsActive: true })
            UpdateToasts.ShowMessage("crash", Title, Strings.Crash_Message);
    }

    /// <summary>The bar's button: Diagnose logs, and the bar away.</summary>
    public void OpenDiagnose()
    {
        IsOpen = false;
        AppNavigation.Navigate(typeof(DiagnosePage));
    }
}
