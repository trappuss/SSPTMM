using System.IO;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Tells the server this machine is here, once a minute, for as long as the app runs - window open
// or hidden in the tray (R1).
//
// Only ever runs when all four hold: the Server map page is switched on, an address is set, the
// server's certificate is pinned (so the machine has actually connected to it once), and the user
// said yes to reporting to THAT server (D9). Start() re-checks all four every time it is called, so
// every place that changes one of them just calls Start() again.
//
// The inventory is re-read whenever the install looks different from the last read - a mod folder
// added, removed, disabled or enabled, or the install record rewritten - and at least every five
// minutes regardless. A heartbeat in between sends only the hash of it; the full list goes when it
// changed or the server asks for it.
//
internal sealed class ServerMapReporter
{
    private static readonly TimeSpan InventoryMaxAge = TimeSpan.FromMinutes(5);

    private readonly SettingsService _settings = new();

    private CancellationTokenSource? _loop;
    private List<ReportedMod>? _inventory;
    private DateTimeOffset _inventoryReadAt;
    private string? _installStamp;

    // The loop whose report is on its way, if any. Per loop rather than a flag, so a report still
    // winding down from a stopped loop never holds up the first report of the one that replaced it.
    private CancellationTokenSource? _reportingFor;
    private string? _sentHash;
    private string? _sentTo;
    private bool _serverWantsInventory;
    private ServerMapProblem? _lastLogged;

    // Raised on the UI thread after every attempt, successful or not.
    public event EventHandler? Reported;

    public bool IsRunning => _loop is not null;

    public ServerMapReportResult? LastResult { get; private set; }

    public DateTimeOffset? LastReportedAt { get; private set; }

    public ServerMapReporter()
    {
        AppServices.DownloadQueue.ItemInstalled += (_, _) => InventoryChanged();
    }

    // Must be called on the UI thread.
    public void Start()
    {
        Stop();

        var map = _settings.Load().ServerMap;
        var endpoint = map.ToEndpoint();

        if (!map.ShowPage || !map.IsConfigured || !endpoint.HasPin) return;
        if (ServerMapReporting.ConsentFor(map, endpoint) != ReportConsentState.Allowed) return;

        var server = ServerMapReporting.ServerKey(endpoint);
        if (!string.Equals(server, _sentTo, StringComparison.OrdinalIgnoreCase))
        {
            _sentHash = null;
            _sentTo = server;

            // What was said about the last server is not true of this one.
            LastResult = null;
            LastReportedAt = null;
        }

        _loop = new CancellationTokenSource();
        _ = RunAsync(_loop, _loop.Token);

        AppLog.Info("ServerMap", $"reporting to {endpoint.Host}:{endpoint.Port}");
    }

    public void Stop()
    {
        if (_loop is null) return;

        _loop.Cancel();
        _loop.Dispose();
        _loop = null;

        AppLog.Info("ServerMap", "reporting stopped");
    }

    // The next report reads the install again rather than trusting the last read.
    public void InventoryChanged() => _inventory = null;

    //
    // Reports straight away rather than at the next tick - the map page's Refresh, so what it shows
    // for this machine is never older than what the person pressing it can see on their own screen.
    // Does nothing when reporting is off, or while a report is already on its way.
    //
    public async Task ReportNowAsync()
    {
        if (_loop is not { } loop) return;

        try
        {
            await ReportOnceAsync(loop, loop.Token);
        }
        catch (OperationCanceledException)
        {
            // Stopped while it was on its way.
        }
    }

    //
    // Takes this machine off the map of the server it is configured for. Best effort: a server that
    // is down forgets the machine on its own after 30 days, and shows it as away long before that.
    //
    public async Task<ServerMapProblem> WithdrawAsync(ServerMapEndpoint endpoint)
    {
        var clientId = _settings.Load().ServerMap.ClientId;
        if (string.IsNullOrWhiteSpace(clientId)) return ServerMapProblem.None;

        using var client = ServerMapClient.TryCreate(endpoint);
        if (client is null) return ServerMapProblem.InvalidAddress;

        var problem = await client.WithdrawAsync(clientId);

        AppLog.Info("ServerMap", problem == ServerMapProblem.None
            ? $"withdrew this machine from {endpoint.Host}:{endpoint.Port}"
            : $"couldn't withdraw from {endpoint.Host}:{endpoint.Port} - {problem}");

        _sentHash = null;
        return problem;
    }

    private async Task RunAsync(CancellationTokenSource loop, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var interval = await ReportOnceAsync(loop, ct);
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopped, or restarted for a new server.
        }
    }

    //
    // loop is the loop this report belongs to, and ct its token - taken by the caller, since the
    // token of a loop that has been stopped and disposed can no longer be read from it.
    //
    private async Task<int> ReportOnceAsync(CancellationTokenSource loop, CancellationToken ct)
    {
        // The timer and the Refresh button can land together; one report is enough.
        if (ReferenceEquals(_reportingFor, loop)) return ServerMapMachines.DefaultIntervalSeconds;
        _reportingFor = loop;

        try
        {
            var installPath = AppServices.SptEnvironment.InstallPath;

            if (string.IsNullOrWhiteSpace(installPath)) return ServerMapMachines.DefaultIntervalSeconds;

            await RefreshInventoryAsync(installPath);
            if (_inventory is null) return ServerMapMachines.DefaultIntervalSeconds;

            var hash = ServerMapMachines.HashOf(_inventory);
            var includeInventory = _serverWantsInventory || !string.Equals(hash, _sentHash, StringComparison.Ordinal);

            // A process walk, once a minute - off the UI thread.
            var gameRunning = await Task.Run(() => ModInstallService.RunningBlockers(installPath)
                .Contains("EscapeFromTarkov.exe", StringComparer.OrdinalIgnoreCase), ct);

            //
            // The id is made the first time this install reports and must survive to the next
            // launch, or every restart would add the same machine to the map again.
            //
            // Loaded here, after the slow reads above, and saved with nothing awaited in between:
            // a copy loaded before them would put back anything changed in Options meanwhile.
            //
            var settings = _settings.Load();
            var idBefore = settings.ServerMap.ClientId;
            ServerMapReporting.EnsureClientId(settings.ServerMap);
            if (idBefore != settings.ServerMap.ClientId) _settings.Save(settings);

            var report = ServerMapReporting.Build(
                settings.ServerMap,
                settings.Roles,
                hosts: ServerMapReporting.HostsServerMap(installPath),
                gameRunning,
                AppServices.SptEnvironment.InstalledVersion,
                AppVersion.Current,
                _inventory,
                includeInventory);

            using var client = ServerMapClient.TryCreate(settings.ServerMap.ToEndpoint());
            if (client is null) return ServerMapMachines.DefaultIntervalSeconds;

            var result = await client.ReportAsync(report, ct);

            // Answered just as the loop was stopped: it belongs to a server or a choice that has
            // since changed, so it is not recorded, logged or announced.
            ct.ThrowIfCancellationRequested();

            LastResult = result;

            if (result.Succeeded)
            {
                LastReportedAt = DateTimeOffset.Now;
                if (includeInventory) _sentHash = hash;
                _serverWantsInventory = result.Resend;
            }

            LogOnChange(result.Problem);
            Reported?.Invoke(this, EventArgs.Empty);

            // Nothing will change until the server mod is updated; the next connect restarts this.
            // Only this report's own loop - never one started since.
            if (result.Problem == ServerMapProblem.MapUnsupported && ReferenceEquals(_loop, loop)) Stop();

            return result.Succeeded ? result.IntervalSeconds : ServerMapMachines.DefaultIntervalSeconds;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A report that fails for any other reason must not take the loop down with it.
            AppLog.Error("ServerMap", "report failed", ex);
            return ServerMapMachines.DefaultIntervalSeconds;
        }
        finally
        {
            if (ReferenceEquals(_reportingFor, loop)) _reportingFor = null;
        }
    }

    private async Task RefreshInventoryAsync(string installPath)
    {
        var stamp = await Task.Run(() => InstallStamp(installPath));

        if (_inventory is not null
            && DateTimeOffset.Now - _inventoryReadAt < InventoryMaxAge
            && string.Equals(stamp, _installStamp, StringComparison.Ordinal))
        {
            return;
        }

        // An install or a list apply in progress would be read half-placed. The last read stands.
        if (AppServices.DownloadQueue.Items.Any(i => !i.IsFinished)) return;

        var install = await AppServices.ModListWorkflow.ReadInstallAsync();
        if (install is null) return;

        _inventory = ServerMapMachines.InventoryOf(install.Candidates);
        _inventoryReadAt = DateTimeOffset.Now;
        _installStamp = stamp;
    }

    //
    // A cheap fingerprint of the install, taken every heartbeat: when each mod container and its
    // ".disabled" sibling was last written, and when the install record was. A folder's timestamp
    // moves whenever something is added to it, removed from it or moved out of it - so a Remove,
    // a disable, an enable, a list apply or a mod dropped in by hand all change this, and the full
    // re-read (a scan and a catalog match, seconds on a big install) only runs when one did.
    //
    // A file overwritten inside an existing mod folder does not move the container's timestamp.
    // The app's own updates rewrite the install record, which does; a hand update is caught by the
    // five-minute re-read.
    //
    private static string InstallStamp(string installPath)
    {
        var parts = new List<string>();

        try
        {
            var containers = DisabledModPaths.ClientContainers(installPath)
                .Concat(DisabledModPaths.ServerContainers(installPath));

            foreach (var container in containers)
            {
                foreach (var folder in new[] { container, DisabledModPaths.Disabled(container) })
                {
                    parts.Add(Directory.Exists(folder)
                        ? Directory.GetLastWriteTimeUtc(folder).Ticks.ToString()
                        : "-");
                }
            }

            var manifest = Path.Combine(AppPaths.DataDirectory, "installed-mods.json");
            parts.Add(File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest).Ticks.ToString() : "-");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable reads as changed, so the next heartbeat re-reads rather than trusting it.
            return Guid.NewGuid().ToString();
        }

        return string.Join("|", parts);
    }

    private void LogOnChange(ServerMapProblem problem)
    {
        if (_lastLogged == problem) return;
        _lastLogged = problem;

        if (problem == ServerMapProblem.None) AppLog.Info("ServerMap", "reporting");
        else AppLog.Warn("ServerMap", $"report not accepted - {problem}");
    }
}
