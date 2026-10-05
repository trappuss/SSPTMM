using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM): the game on the launcher's card - Close game while it runs, asked about on the card
// first as Stop server is (a raid in progress is lost), and the tick box that closes it when the
// server stops (GameCloser).
//
public partial class PlayViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloseGame))]
    private bool _isGameRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCloseGame))]
    private bool _isClosingGame;

    [ObservableProperty]
    private bool _confirmingCloseGame;

    // Set from settings without saving them back - see OnCloseGameWithServerChanged.
    private bool _readingCloseGameSetting;

    [ObservableProperty]
    private bool _closeGameWithServer;

    public bool CanCloseGame => IsGameRunning && !IsClosingGame;

    private bool _gameCloserHooked;

    // From Refresh: whether the game is up, and the tick box as settings have it.
    private void RefreshGame(string? installPath, AppSettings settings)
    {
        if (!_gameCloserHooked)
        {
            _gameCloserHooked = true;
            GameCloser.Closed += (_, e) =>
            {
                HasError = e.IsError;
                Message = e.Message;
                Refresh();
            };
        }

        IsGameRunning = SptLaunchService.IsGameRunning(installPath, GameCloser.SparesWindowless(settings, installPath));
        if (!IsGameRunning) ConfirmingCloseGame = false;

        _readingCloseGameSetting = true;
        CloseGameWithServer = settings.CloseGameWithServer;
        _readingCloseGameSetting = false;

        UpdateToolStates(); // the Mod tools' notes follow the server
    }

    partial void OnCloseGameWithServerChanged(bool value)
    {
        if (!_readingCloseGameSetting) GameCloser.SetEnabled(value);
    }

    // Stop server with "Close the game when the server stops" ticked: the game first. Null when that
    // isn't ticked or no game is running.
    private static async Task<SptLaunchResult?> CloseGameBeforeServerAsync(string? installPath)
    {
        var settings = new SettingsService().Load();
        if (!settings.CloseGameWithServer) return null;

        var spares = GameCloser.SparesWindowless(settings, installPath);
        if (!SptLaunchService.IsGameRunning(installPath, spares)) return null;

        AppLog.Info("Launch", "stopping the server; closing the game first, as ticked on the Play page");
        var result = await Task.Run(() => SptLaunchService.CloseGame(installPath, spares));
        if (result.Problem is not (SptLaunchProblem.None or SptLaunchProblem.NotRunning))
            AppLog.Warn("Launch", $"the game could not be closed: {result.Problem}{(result.Error is null ? "" : " - " + result.Error.Message)}");
        return result;
    }

    [RelayCommand]
    private void AskCloseGame()
    {
        if (!CanCloseGame) return;
        ConfirmingCloseGame = true;
    }

    [RelayCommand]
    private void CancelCloseGame() => ConfirmingCloseGame = false;

    // Off the UI thread: closing waits for the game to go, up to five seconds twice over.
    [RelayCommand]
    private async Task ConfirmCloseGameAsync()
    {
        if (!ConfirmingCloseGame) return;

        ConfirmingCloseGame = false;
        IsClosingGame = true;
        _poll.Stop();

        try
        {
            var installPath = AppServices.SptEnvironment.InstallPath;
            var spares = GameCloser.SparesWindowless(new SettingsService().Load(), installPath);
            var result = await Task.Run(() => SptLaunchService.CloseGame(installPath, spares));

            HasError = result.Problem is not (SptLaunchProblem.None or SptLaunchProblem.NotRunning);
            Message = result.Problem switch
            {
                SptLaunchProblem.None => Strings.Play_GameClosed,
                SptLaunchProblem.NotRunning => Strings.Play_GameNotRunning,
                _ => SptLaunchProblems.Describe(result),
            };
        }
        finally
        {
            IsClosingGame = false;
            Refresh();
            _poll.Start();
        }
    }
}
