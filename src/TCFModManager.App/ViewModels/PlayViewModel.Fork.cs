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
            GameCloser.Closed += (_, message) =>
            {
                HasError = false;
                Message = message;
                Refresh();
            };
        }

        IsGameRunning = SptLaunchService.IsGameRunning(installPath, GameCloser.SparesWindowless(settings, installPath));
        if (!IsGameRunning) ConfirmingCloseGame = false;

        _readingCloseGameSetting = true;
        CloseGameWithServer = settings.CloseGameWithServer;
        _readingCloseGameSetting = false;
    }

    partial void OnCloseGameWithServerChanged(bool value)
    {
        if (!_readingCloseGameSetting) GameCloser.SetEnabled(value);
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
