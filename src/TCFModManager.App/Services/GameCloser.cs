using System.Windows.Threading;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Fork (SSPTMM): closes the game when the SPT server stops, if that is ticked on the Play page
// (AppSettings.CloseGameWithServer, off by default).
//
// Watched from here rather than from the Play page, whose poll only runs while it is on screen: the
// server stops however it stops - Stop server, its own window closed, a crash - and the page is
// rarely the one showing while somebody plays.
//
// "Stopped" means it was seen running and then seen down on two checks in a row, a second apart,
// so a server that blinks out and back is not taken for one that stopped. (Three seconds apart
// before 1.1.0: with the game's own wait added, the game stayed up some fifteen seconds after the
// server and read as not closed at all.)
//
// The server is gone by then, so the game can't finish quitting - see SptLaunchService.CloseGame -
// and gets ServerGoneGrace before it is killed. Stop server on the Play page doesn't come here when
// this is ticked: it closes the game first, while the server can still answer it. A restart started from
// this app holds the watch off while it runs (HoldOff), since its moment of being down is meant.
//
public static class GameCloser
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>How long a game whose server is gone gets to close before it is killed.</summary>
    public static readonly TimeSpan ServerGoneGrace = TimeSpan.FromSeconds(2);

    private const int DownChecksToCount = 2;

    private static bool _serverWasUp;

    // The install the watch is about: switching installs starts it again, so one install's server
    // stopping never closes another's game, and another's server being down doesn't read as a stop.
    private static string? _watchedInstall;
    private static int _downChecks;
    private static DateTime _holdUntil = DateTime.MinValue;
    private static bool _closing;
    private static bool _checking;

    /// <summary>Raised on the UI thread with what happened, for the Play page to show.</summary>
    public static event EventHandler<GameClosedEventArgs>? Closed;

    public static bool IsEnabled => new SettingsService().Load().CloseGameWithServer;

    // Called once the main window has loaded.
    public static void Start()
    {
        Timer.Tick += async (_, _) => await CheckAsync();
        if (IsEnabled) Timer.Start();
    }

    public static void SetEnabled(bool enabled)
    {
        var service = new SettingsService();
        var settings = service.Load();
        if (settings.CloseGameWithServer != enabled)
        {
            settings.CloseGameWithServer = enabled;
            service.Save(settings);
        }

        _serverWasUp = false;
        _downChecks = 0;

        if (enabled) Timer.Start();
        else Timer.Stop();
    }

    /// <summary>Not to count the server being down for this long - a restart from this app.</summary>
    public static void HoldOff(TimeSpan time) => _holdUntil = DateTime.UtcNow + time;

    /// <summary>Lets the watch count again once a restart has finished.</summary>
    public static void EndHold() => _holdUntil = DateTime.MinValue;

    //
    // Whether closing the game should leave a windowless one alone: this machine runs a Fika headless
    // client, which is EscapeFromTarkov.exe too (see SptLaunchService.CloseGame).
    //
    public static bool SparesWindowless(AppSettings settings, string? installPath) =>
        settings.Roles.HasFlag(InstallRoles.Headless)
        || SptLaunchService.Describe(installPath, SptLaunchTarget.Headless, settings.HeadlessLauncherPath).Exists;

    // Once a second, so the process look-up runs off the UI thread; a look-up still running when the
    // next tick comes is let finish rather than doubled.
    private static async Task CheckAsync()
    {
        if (_closing || _checking) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (!string.Equals(installPath, _watchedInstall, StringComparison.OrdinalIgnoreCase))
        {
            _watchedInstall = installPath;
            _serverWasUp = false;
            _downChecks = 0;
        }

        bool up;
        _checking = true;
        try
        {
            up = await Task.Run(() => SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning);
        }
        finally
        {
            _checking = false;
        }

        // Switched install or turned off while the look-up ran: that answer is about something else.
        if (!Timer.IsEnabled || !string.Equals(installPath, _watchedInstall, StringComparison.OrdinalIgnoreCase)) return;

        if (up)
        {
            _serverWasUp = true;
            _downChecks = 0;
            return;
        }

        if (!_serverWasUp) return;

        if (DateTime.UtcNow < _holdUntil)
        {
            _downChecks = 0;
            return;
        }

        if (++_downChecks < DownChecksToCount) return;

        _serverWasUp = false;
        _downChecks = 0;
        _ = CloseAsync(installPath);
    }

    private static async Task CloseAsync(string? installPath)
    {
        var settings = new SettingsService().Load();
        var spares = SparesWindowless(settings, installPath);

        if (!SptLaunchService.IsGameRunning(installPath, spares)) return;

        _closing = true;
        try
        {
            AppLog.Info("Launch", "the server stopped; closing the game, as ticked on the Play page");
            var result = await Task.Run(() => SptLaunchService.CloseGame(installPath, spares, ServerGoneGrace));

            var closed = result.Problem is SptLaunchProblem.None or SptLaunchProblem.NotRunning;
            if (closed)
                AppLog.Info("Launch", result.Problem == SptLaunchProblem.None ? $"the game is closed ({result.Stopped})" : "the game had already gone");
            else
                AppLog.Warn("Launch", $"the game could not be closed: {result.Problem}{(result.Error is null ? "" : " - " + result.Error.Message)}");

            Closed?.Invoke(null, new GameClosedEventArgs(
                closed ? Localization.Strings.Play_GameClosedWithServer : SptLaunchProblems.Describe(result),
                IsError: !closed));
        }
        finally
        {
            _closing = false;
        }
    }
}

/// <summary>What GameCloser did: the line for the Play page, and whether it is a problem.</summary>
public sealed record GameClosedEventArgs(string Message, bool IsError);
