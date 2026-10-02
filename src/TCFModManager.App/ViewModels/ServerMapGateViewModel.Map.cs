using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// The map half of the connection: who else reports to this server, whether this machine does, and
// the question that decides it (D9 - asked once per server, never assumed).
//
// Kept in its own file so the list and key handling above stays readable; it is the same shared
// object the sidebar, Options and the page already read.
//
public sealed partial class ServerMapGateViewModel
{
    public ObservableCollection<MapMachineViewModel> Machines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapMessage))]
    private string _mapMessage = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshMapNowCommand))]
    private bool _isMapBusy;

    //
    // The name this machine shows on the map. Empty means the computer's own name, which is what
    // the box shows as its placeholder - so leaving it alone is a choice, not a gap.
    //
    [ObservableProperty]
    private string? _displayNameInput = new SettingsService().Load().ServerMap.DisplayName;

    private bool _reporterHooked;

    //
    // LAN-only, for the server running on THIS machine: the server refuses every request from
    // outside its own network (Tailscale counts as inside). Read from and written to the same
    // Data\ServerMap\servermap.json the mod re-reads on every change, so it takes effect without a
    // restart. Only offered where the key file is found - there is no server here to configure
    // anywhere else.
    //
    [ObservableProperty]
    private bool _serverLanOnly;

    private bool _loadingLanOnly;

    public void RefreshServerSettings(string? sptInstallPath)
    {
        var path = ServerMapServerSettings.PathFor(sptInstallPath);

        _loadingLanOnly = true;
        ServerLanOnly = path is not null && ServerMapServerSettings.ReadLanOnly(path);
        _loadingLanOnly = false;
    }

    partial void OnServerLanOnlyChanged(bool value)
    {
        if (_loadingLanOnly) return;

        var path = ServerMapServerSettings.PathFor(_settings.Load().SptInstallPath);

        if (path is not null && ServerMapServerSettings.TryWriteLanOnly(path, value)) return;

        // Not saved, so the switch goes back to what the server is actually doing.
        KeyNotice = Strings.Options_ServerMapLanOnlyFailed;
        _loadingLanOnly = true;
        ServerLanOnly = !value;
        _loadingLanOnly = false;
    }

    // Set when reporting is switched on, so the map is read again once this machine is on it.
    private bool _refreshAfterReport;

    // A read asked for while another was on its way. Run once that one finishes rather than
    // alongside it, and never dropped: it may be the read that follows this machine's own report.
    private bool _mapRefreshQueued;

    public bool HasMapMessage => !string.IsNullOrWhiteSpace(MapMessage);

    public bool HasMachines => Machines.Count > 0;

    public string DisplayNamePlaceholder => Environment.MachineName;

    // Whether the server's mod is new enough to have a map at all (0.2.0 and later).
    public bool SupportsMap => Probe?.Hello?.Supports(ServerMapClient.MapCapability) == true;

    private ReportConsentState Consent
    {
        get
        {
            var map = _settings.Load().ServerMap;
            return ServerMapReporting.ConsentFor(map, map.ToEndpoint());
        }
    }

    //
    // The question, put on the page the first time this machine reaches a server that has a map.
    // Not a dialog: nothing is waiting on the answer, and a machine that never answers simply never
    // reports.
    //
    public bool AsksForConsent => IsConnected && SupportsMap && HasPin() && Consent == ReportConsentState.Unanswered;

    public string ConsentMessage => Text(Strings.ServerMap_ConsentMessageFormat, ServerName);

    // Reachable once the machine has connected at least once, because consent is keyed on the
    // certificate that connection recorded.
    public bool CanChooseReporting => HasPin();

    //
    // The Options switch. Two-way, and the same answer the page's question records - so turning it
    // off here withdraws the machine from the map, and turning it on is saying yes.
    //
    public bool ReportsToServer
    {
        get => Consent == ReportConsentState.Allowed;
        set
        {
            if (value == ReportsToServer) return;
            _ = SetReportingAsync(value);
        }
    }

    //
    // One line under the page's question and in Options, saying what this machine is doing now.
    //
    public string ReportingStatus
    {
        get
        {
            if (IsConnected && !SupportsMap) return Strings.ServerMap_MapUnsupported;

            switch (Consent)
            {
                case ReportConsentState.Unanswered:
                    return "";
                case ReportConsentState.Declined:
                    return Strings.ServerMap_NotReporting;
            }

            var name = ServerMapReporting.DisplayNameOf(_settings.Load().ServerMap);
            var reporter = AppServices.ServerMapReporter;

            if (reporter.LastResult is { Succeeded: false } failed && failed.Problem != ServerMapProblem.None)
                return Strings.ServerMap_ReportFailed;

            return reporter.LastReportedAt is { } at
                ? Text(Strings.ServerMap_ReportingFormat, name, at.ToString("t"))
                : Text(Strings.ServerMap_ReportingPendingFormat, name);
        }
    }

    [RelayCommand]
    private Task AllowReportingAsync() => SetReportingAsync(true);

    [RelayCommand]
    private Task DeclineReportingAsync() => SetReportingAsync(false);

    //
    // Records the answer for this server and acts on it straight away. Saying no after saying yes
    // takes the machine off the map rather than leaving a row behind that will only go stale.
    //
    private async Task SetReportingAsync(bool allow)
    {
        HookReporter();

        var settings = _settings.Load();
        var endpoint = settings.ServerMap.ToEndpoint();
        var wasAllowed = ServerMapReporting.ConsentFor(settings.ServerMap, endpoint) == ReportConsentState.Allowed;

        ServerMapReporting.SetConsent(settings.ServerMap, endpoint, allow);
        _settings.Save(settings);

        AppLog.Info("ServerMap", allow
            ? $"this machine agreed to report to {endpoint.Host}:{endpoint.Port}"
            : $"this machine declined to report to {endpoint.Host}:{endpoint.Port}");

        NotifyReporting();

        if (allow)
        {
            _refreshAfterReport = true;
            AppServices.ServerMapReporter.Start();
            return;
        }

        AppServices.ServerMapReporter.Stop();
        if (wasAllowed) await AppServices.ServerMapReporter.WithdrawAsync(endpoint);

        await RefreshMapAsync();
    }

    partial void OnDisplayNameInputChanged(string? value)
    {
        var settings = _settings.Load();
        settings.ServerMap.DisplayName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _settings.Save(settings);

        OnPropertyChanged(nameof(ReportingStatus));
    }

    partial void OnProbeChanged(ServerHelloProbe? value) => NotifyReporting();

    private bool CanRefreshMap() => !IsMapBusy;

    //
    // The Refresh button: this machine reports first, so its own row is as current as the person
    // pressing it, then the map is read. The page's timer only reads - the reporter keeps its own
    // schedule, and a second report every minute from an open page would be traffic for nothing.
    //
    [RelayCommand(CanExecute = nameof(CanRefreshMap))]
    private async Task RefreshMapNowAsync()
    {
        HookReporter();

        await AppServices.ServerMapReporter.ReportNowAsync();
        await RefreshMapAsync();
    }

    //
    // Asks the server who is on it. Called when the page opens, on a timer while it is open, after
    // this machine's own report lands, and from the Refresh button.
    //
    public async Task RefreshMapAsync()
    {
        HookReporter();

        if (!IsPageEnabled || !IsConnected)
        {
            Machines.Clear();
            MapMessage = "";
            OnPropertyChanged(nameof(HasMachines));
            return;
        }

        if (!SupportsMap)
        {
            Machines.Clear();
            MapMessage = Strings.ServerMap_MapUnsupported;
            OnPropertyChanged(nameof(HasMachines));
            return;
        }

        if (IsMapBusy)
        {
            _mapRefreshQueued = true;
            return;
        }

        IsMapBusy = true;

        try
        {
            var map = _settings.Load().ServerMap;

            using var client = ServerMapClient.TryCreate(map.ToEndpoint());
            if (client is null) return;

            var result = await client.ClientsAsync(map.ClientId);

            if (!result.Succeeded)
            {
                MapMessage = ServerMapProblems.DescribeMap(result.Problem);
                AppLog.Info("ServerMap", $"map not read - {result.Problem}");
                return;
            }

            Machines.Clear();
            foreach (var machine in result.Machines)
                Machines.Add(new MapMachineViewModel(machine, result.IntervalSeconds, List));

            MapMessage = Machines.Count == 0 ? Strings.ServerMap_MachinesEmpty : "";
        }
        finally
        {
            IsMapBusy = false;
            OnPropertyChanged(nameof(HasMachines));

            if (_mapRefreshQueued)
            {
                _mapRefreshQueued = false;
                _ = RefreshMapAsync();
            }
        }
    }

    // Everything that reads the connection, the consent or the last report, re-read together.
    private void NotifyReporting()
    {
        OnPropertyChanged(nameof(SupportsMap));
        OnPropertyChanged(nameof(AsksForConsent));
        OnPropertyChanged(nameof(ConsentMessage));
        OnPropertyChanged(nameof(CanChooseReporting));
        OnPropertyChanged(nameof(ReportsToServer));
        OnPropertyChanged(nameof(ReportingStatus));
    }

    //
    // Subscribed on first use rather than in the constructor: this object is built before the
    // reporter in AppServices, so the reporter does not exist yet when the constructor runs.
    //
    private void HookReporter()
    {
        if (_reporterHooked) return;
        _reporterHooked = true;

        AppServices.ServerMapReporter.Reported += (_, _) =>
        {
            OnPropertyChanged(nameof(ReportingStatus));

            if (!_refreshAfterReport) return;
            _refreshAfterReport = false;
            _ = RefreshMapAsync();
        };
    }

    // After a connect: the reporter re-checks whether it should be running, and the map is read.
    private async Task AfterConnectAsync()
    {
        HookReporter();
        NotifyReporting();

        AppServices.ServerMapReporter.Start();
        await RefreshMapAsync();
    }
}
