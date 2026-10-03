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
// "Stopped" means it was seen running and then seen down on two checks in a row, about six seconds,
// so a server that blinks out and back is not taken for one that stopped. A restart started from
// this app holds the watch off while it runs (HoldOff), since its moment of being down is meant.
//
public static class GameCloser
{
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromSeconds(3) };

    private const int DownChecksToCount = 2;

    private static bool _serverWasUp;
    private static int _downChecks;
    private static DateTime _holdUntil = DateTime.MinValue;
    private static bool _closing;

    /// <summary>Raised on the UI thread with what happened, for the Play page to show.</summary>
    public static event EventHandler<string>? Closed;

    public static bool IsEnabled => new SettingsService().Load().CloseGameWithServer;

    // Called once the main window has loaded.
    public static void Start()
    {
        Timer.Tick += (_, _) => Check();
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

    private static void Check()
    {
        if (_closing) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        var up = SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning;

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
            var result = await Task.Run(() => SptLaunchService.CloseGame(installPath, spares));

            Closed?.Invoke(null, result.Problem == SptLaunchProblem.None
                ? Localization.Strings.Play_GameClosedWithServer
                : SptLaunchProblems.Describe(result));
        }
        finally
        {
            _closing = false;
        }
    }
}
