using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

//
// The Server Map mod on the App update page. It is installed by hand on the machine running the
// server, so the app can only say that it is behind - never update it.
//
// Two ways to know what is running:
//   - this machine runs the server: the payload and stub on disk, which also catches a stub left
//     older than its payload (LAN-only then refuses everyone);
//   - this machine joins one: the version that server's /hello reports. Its operator has to update
//     it, so the page says who to ask.
//
public partial class AppUpdateViewModel
{
    private readonly ServerMapModUpdateService _serverMapMod = new(AppServices.SpModApi);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerMapLatestVersion))]
    private ServerMapModRelease? _serverMapRelease;

    [ObservableProperty]
    private InstalledServerMapMod? _serverMapInstalled;

    [ObservableProperty]
    private bool _serverMapCheckFailed;

    private bool _serverMapHooked;

    // Which SPT line's addon the check and the link use. Set with the files on disk.
    private ServerMapSptLine _serverMapLine = ServerMapSptLine.Spt41;

    public string? ServerMapLatestVersion => ServerMapRelease?.LatestVersion;

    // The version the connected server reports. Only used when this machine runs no server itself:
    // on the server's own machine the files on disk are the better answer, and usually the same one.
    // The mod reports its informational version, which can carry a "+commit" suffix; that is dropped.
    private string? ConnectedServerMapVersion =>
        ServerMapInstalled is null && AppServices.ServerMap.IsConnected
        && AppServices.ServerMap.Probe?.Hello?.ModVersion is { Length: > 0 } reported
            ? reported.Split('+')[0].Trim()
            : null;

    public bool ShowServerMapMod => ServerMapInstalled is not null || ConnectedServerMapVersion is not null;

    public bool HasServerMapLocal => ServerMapInstalled is not null;

    public bool HasServerMapConnected => ConnectedServerMapVersion is not null;

    public string ServerMapLocalVersion => ServerMapInstalled?.PayloadVersion ?? Strings.Common_Unknown;

    public string ServerMapConnectedVersion => ConnectedServerMapVersion ?? Strings.Common_Unknown;

    public bool ServerMapLocalBehind =>
        ServerMapInstalled is { } installed && ServerMapModVersions.IsBehind(installed.PayloadVersion, ServerMapLatestVersion);

    public bool ServerMapStubBehind => ServerMapInstalled?.StubBehindPayload == true;

    public bool ServerMapConnectedBehind => ServerMapModVersions.IsBehind(ConnectedServerMapVersion, ServerMapLatestVersion);

    // What lights the sidebar badge alongside an app update.
    public bool ServerMapModBehind => ServerMapLocalBehind || ServerMapStubBehind || ServerMapConnectedBehind;

    public bool ShowServerMapUpToDate =>
        ShowServerMapMod && ServerMapRelease is not null && !ServerMapModBehind;

    public bool ShowUpdateBadge => UpdateAvailable || ServerMapModBehind;

    public string ServerMapLocalBehindMessage =>
        Text(Strings.AppUpdate_ServerMapLocalBehindFormat, ServerMapLatestVersion, ServerMapInstalled?.PayloadVersion);

    public string ServerMapStubBehindMessage =>
        Text(Strings.AppUpdate_ServerMapStubBehindFormat, ServerMapInstalled?.StubVersion, ServerMapInstalled?.PayloadVersion);

    public string ServerMapConnectedBehindMessage =>
        Text(Strings.AppUpdate_ServerMapConnectedBehindFormat, ConnectedServerMapVersion, ServerMapLatestVersion);

    [RelayCommand]
    private void OpenServerMapModPage() =>
        Process.Start(new ProcessStartInfo(ServerMapAddon.PageUrl(_serverMapLine)) { UseShellExecute = true });

    //
    // The files on disk, read again. Cheap, so the page does it every time it is shown, and again
    // whenever the install folder changes - the sidebar badge reads from this too.
    //
    // A different install folder can mean a different SPT line, and each line has its own listing.
    // The release already fetched is then for the other line and says nothing about this one, so it
    // is dropped and the new line's asked for - otherwise the page compares against the wrong mod
    // until the next Check. Only once a check has run: before that, the startup check is on its way.
    //
    public void RefreshServerMapInstall()
    {
        if (!ReadServerMapInstall()) return;
        if (ServerMapRelease is null && !ServerMapCheckFailed) return;

        ServerMapRelease = null;
        ServerMapCheckFailed = false;
        _ = FetchServerMapReleaseAsync();
    }

    // True when the SPT line changed.
    private bool ReadServerMapInstall()
    {
        HookServerMap();

        // The environment's path, not settings.json: it changes before the new path is saved.
        var installPath = AppServices.SptEnvironment.InstallPath;
        var installed = ServerMapModVersions.FindInstalled(installPath);
        var line = ServerMapModVersions.LineFor(installed, installPath);

        var changed = line != _serverMapLine;
        _serverMapLine = line;
        ServerMapInstalled = installed;

        return changed;
    }

    private async Task CheckServerMapModAsync()
    {
        ReadServerMapInstall();
        await FetchServerMapReleaseAsync().ConfigureAwait(true);
    }

    //
    // Asks sp-mod.com for the newest Server Map mod. Its own failure stays its own: the app's check
    // above has already said whether sp-mod.com could be reached at all.
    //
    // An answer for a line that is no longer this machine's - the folder changed while it was on its
    // way - is thrown away; the fetch for the new line is what counts.
    //
    private async Task FetchServerMapReleaseAsync()
    {
        var line = _serverMapLine;

        try
        {
            var release = await _serverMapMod.LatestAsync(line).ConfigureAwait(true);
            if (line != _serverMapLine) return;

            ServerMapRelease = release;
            ServerMapCheckFailed = false;
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException or OperationCanceledException)
        {
            if (line != _serverMapLine) return;

            ServerMapCheckFailed = true;
            AppLog.Warn("ServerMapMod", $"couldn't check for a newer Server Map mod: {ex.Message}");
        }
    }

    partial void OnServerMapReleaseChanged(ServerMapModRelease? value) => NotifyServerMap();

    partial void OnServerMapInstalledChanged(InstalledServerMapMod? value) => NotifyServerMap();

    //
    // Subscribed on first use, not in the constructor: this object is built before the Server Map
    // connection in AppServices.
    //
    private void HookServerMap()
    {
        if (_serverMapHooked) return;
        _serverMapHooked = true;

        AppServices.ServerMap.PropertyChanged += OnServerMapConnectionChanged;

        AppServices.SptEnvironment.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SptEnvironmentViewModel.InstallPath)) RefreshServerMapInstall();
        };
    }

    private void OnServerMapConnectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServerMapGateViewModel.Probe) or nameof(ServerMapGateViewModel.IsConnected))
            NotifyServerMap();
    }

    private void NotifyServerMap()
    {
        OnPropertyChanged(nameof(ShowServerMapMod));
        OnPropertyChanged(nameof(HasServerMapLocal));
        OnPropertyChanged(nameof(HasServerMapConnected));
        OnPropertyChanged(nameof(ServerMapLocalVersion));
        OnPropertyChanged(nameof(ServerMapConnectedVersion));
        OnPropertyChanged(nameof(ServerMapLocalBehind));
        OnPropertyChanged(nameof(ServerMapStubBehind));
        OnPropertyChanged(nameof(ServerMapConnectedBehind));
        OnPropertyChanged(nameof(ServerMapModBehind));
        OnPropertyChanged(nameof(ShowServerMapUpToDate));
        OnPropertyChanged(nameof(ShowUpdateBadge));
        OnPropertyChanged(nameof(BadgeSeverity));
        OnPropertyChanged(nameof(ServerMapLocalBehindMessage));
        OnPropertyChanged(nameof(ServerMapStubBehindMessage));
        OnPropertyChanged(nameof(ServerMapConnectedBehindMessage));
    }
}
