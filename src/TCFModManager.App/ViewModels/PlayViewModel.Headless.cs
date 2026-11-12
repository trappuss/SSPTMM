using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (1.4.7): "Start with the server" on the headless card - Start server then starts the Fika
// headless client too, once the server listens. A host with a headless client otherwise starts one,
// waits, and starts the other. Only from Start server, and only once per start: a headless client
// that goes down later is not brought back, since why it went is not known here.
//
public partial class PlayViewModel
{
    [ObservableProperty]
    private bool _startHeadlessWithServer;

    private bool _readingHeadlessSetting;

    private void RefreshHeadlessSetting(TCFModManager.Core.Models.AppSettings settings)
    {
        _readingHeadlessSetting = true;
        StartHeadlessWithServer = settings.StartHeadlessWithServer;
        _readingHeadlessSetting = false;
    }

    partial void OnStartHeadlessWithServerChanged(bool value)
    {
        if (_readingHeadlessSetting) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.StartHeadlessWithServer = value;
        service.Save(settings);
    }

    // From Start server. Given as long as Find the broken mod gives a server to come up.
    private async Task StartHeadlessWhenServerIsUpAsync()
    {
        var install = AppServices.SptEnvironment.InstallPath;
        var ports = SptServerReadiness.PortsFor(Server?.ExePath);
        var until = DateTime.UtcNow + ServerTrial.StartTimeout;

        await Task.Delay(2000); // the new process may not be listed yet
        while (DateTime.UtcNow < until)
        {
            if (!SptLaunchService.Describe(install, SptLaunchTarget.Server).IsRunning) return;

            if (SptServerReadiness.IsListening(ports))
            {
                if (Headless is { CanLaunch: true, IsRunning: false })
                {
                    AppLog.Info("Launch", "the server is up: starting the headless client with it");
                    StartHeadless();
                }

                return;
            }

            await Task.Delay(1000);
        }
    }
}
