using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM, experimental): Options' "Start the game from SSPTMM" card - Play on the Play page
// starts the game itself rather than opening SPT's launcher (SptDirectLaunch).
//
public partial class OptionsViewModel
{
    [ObservableProperty]
    private bool _directLaunch;

    partial void OnDirectLaunchChanged(bool value)
    {
        OnPropertyChanged(nameof(DirectLaunchReplacesLauncher));
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.DirectLaunch = value;
        _settings.Save(settings);

        AppLog.Info("DirectLaunch", value ? "Play starts the game from SSPTMM (experimental)" : "Play opens SPT's launcher");
    }

    // Start server's "also open the SPT launcher" has no part while Play starts the game itself.
    public bool DirectLaunchReplacesLauncher =>
        DirectLaunch && SptDirectLaunch.Support(SptEnvironment.InstalledVersion) != DirectLaunchSupport.Unsupported;

    [ObservableProperty]
    private bool _minimizeWhilePlaying;

    partial void OnMinimizeWhilePlayingChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.MinimizeWhilePlaying = value;
        _settings.Save(settings);
    }

    // What this install's SPT version allows, said on the card under the switch.
    public string DirectLaunchSupportText => SptDirectLaunch.Support(SptEnvironment.InstalledVersion) switch
    {
        DirectLaunchSupport.Supported => LocalizationService.Text(Strings.Options_DirectLaunchSupportedFormat, SptEnvironment.InstalledVersion),
        DirectLaunchSupport.NewerUntested => LocalizationService.Text(Strings.Options_DirectLaunchUntestedFormat, SptEnvironment.InstalledVersion, SptDirectLaunch.TestedUpTo),
        _ when string.IsNullOrWhiteSpace(SptEnvironment.InstalledVersion) => Strings.Options_DirectLaunchNoInstall,
        _ => LocalizationService.Text(Strings.Options_DirectLaunchUnsupportedFormat, SptEnvironment.InstalledVersion),
    };

    public bool DirectLaunchSupportIsCaution => SptDirectLaunch.Support(SptEnvironment.InstalledVersion) != DirectLaunchSupport.Supported;

    private bool _directLaunchHooked;

    // Called from the constructor's end (see OptionsViewModel): the note follows the install.
    private void HookDirectLaunch()
    {
        if (_directLaunchHooked) return;
        _directLaunchHooked = true;

        SptEnvironment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SptEnvironmentViewModel.InstalledVersion)) return;
            OnPropertyChanged(nameof(DirectLaunchSupportText));
            OnPropertyChanged(nameof(DirectLaunchSupportIsCaution));
            OnPropertyChanged(nameof(DirectLaunchReplacesLauncher));
        };
    }
}
