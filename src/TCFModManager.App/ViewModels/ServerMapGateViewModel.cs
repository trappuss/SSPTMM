using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Where the Server Map server is, whether it answered, and what it said.
//
// A shared singleton for the same reason FootprintGateViewModel is one: the sidebar item, the
// Options section and the page itself are three views of one thing, and flicking the switch has to
// move the nav item at that moment rather than at the next launch.
//
// It carries two separate things, and keeping them apart is the point: whether the page is SHOWN
// (a stored switch the user owns, exactly like Mod footprint) and whether the server ANSWERED (the
// last handshake). Tying the sidebar to the handshake instead was the first cut, and it was wrong -
// a page that comes and goes with a server being up is a page nobody can find on purpose.
//
public sealed partial class ServerMapGateViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Every write does its own Load first, so this never fights the other things that save settings.
    private readonly SettingsService _settings = new();

    [ObservableProperty]
    private string? _hostInput;

    //
    // A string rather than an int, because this is bound to a text box that is mid-edit for as long
    // as someone is typing in it. An int binding rejects the empty box and the half-typed value,
    // leaving the user fighting the control; parsing once, on connect, reports a bad port as a
    // problem like any other.
    //
    [ObservableProperty]
    private string _portInput = ServerMapEndpoint.DefaultPort.ToString();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ServerName))]
    [NotifyPropertyChangedFor(nameof(ServerDetail))]
    [NotifyPropertyChangedFor(nameof(HasCertificateChanged))]
    [NotifyPropertyChangedFor(nameof(KeyDescription))]
    [NotifyCanExecuteChangedFor(nameof(TrustNewCertificateCommand))]
    private ServerHelloProbe? _probe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinDescription))]
    [NotifyCanExecuteChangedFor(nameof(ForgetPinCommand))]
    private string? _pinnedThumbprint;

    //
    // The server's shared key. Saved on connect alongside the address, because the two are one
    // thing an operator hands out together and there is nothing to gain from making them two steps.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyDescription))]
    [NotifyPropertyChangedFor(nameof(CanUseLocalKey))]
    private string? _keyInput;

    //
    // The key belonging to a Server Map server running on THIS machine, when there is one. Null on
    // any machine that is not the server, which is most of them.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServerOwner))]
    [NotifyPropertyChangedFor(nameof(LocalKeyDescription))]
    [NotifyPropertyChangedFor(nameof(CanUseLocalKey))]
    [NotifyCanExecuteChangedFor(nameof(UseLocalKeyCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewLocalKeyCommand))]
    private string? _localKey;

    //
    // What just happened to this machine's key, when something did. Its own line rather than
    // StatusMessage, which describes the last handshake and is derived from the probe - rotating a
    // key is not a connection result and would be wiped by the next connect.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyNotice))]
    private string _keyNotice = "";

    // The list this server publishes, as last fetched. Also in the user's mod lists - this is the
    // page's own handle on it, not a second copy of the truth.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasList))]
    [NotifyPropertyChangedFor(nameof(ListSummary))]
    [NotifyCanExecuteChangedFor(nameof(FetchListCommand))]
    private ModList? _list;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasListStatus))]
    private string _listStatus = "";

    //
    // Whether the Server map item is in the sidebar. Off by default and stored - see
    // ServerMapSettings.
    //
    // Deliberately NOT tied to whether the server is up. The page is worth reaching when the server
    // is down too, because that is exactly when someone wants to look at it, and a sidebar item that
    // appears and disappears on its own is one nobody can rely on finding.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingToolTip))]
    private bool _isPageEnabled;

    // Suppresses the save while the constructor is putting the switch where the stored value already
    // is, so starting the app doesn't count as flicking it.
    private readonly bool _loaded;

    public ServerMapGateViewModel()
    {
        var settings = _settings.Load();
        var stored = settings.ServerMap;

        _isPageEnabled = stored.ShowPage;
        _hostInput = stored.Host;
        _portInput = stored.Port.ToString();
        _pinnedThumbprint = stored.PinnedThumbprint;
        _keyInput = stored.SharedKey;

        RefreshLocalKey(settings.SptInstallPath);

        //
        // Filled in from this machine's own server, when there is one. Someone running the app on
        // their own server should not have to go and find a file to paste back into the window in
        // front of them - but a key they typed THEMSELVES is never overwritten, because it may
        // belong to a different server they are connecting to from this machine.
        //
        if (string.IsNullOrWhiteSpace(_keyInput) && _localKey is not null) _keyInput = _localKey;

        // Whatever is in the box now, if it matches this machine's file, is ours to keep in step -
        // including across a restart, where nothing else would remember that we put it there.
        if (_localKey is not null && string.Equals(_keyInput?.Trim(), _localKey, StringComparison.Ordinal))
        {
            _autoFilledKey = _localKey;
        }

        _loaded = true;
    }

    //
    // Whether this machine is running a Server Map server. Not inferred from the address: 127.0.0.1,
    // a LAN address, a hostname and an external IP can all reach the same box, and the app cannot
    // tell. The presence of the key file is the honest test, and it is also the only one that
    // matters - it means the person at this keyboard already owns that file.
    //
    public bool IsServerOwner => LocalKey is not null;

    public bool HasKeyNotice => !string.IsNullOrWhiteSpace(KeyNotice);

    //
    // The Server Map mod's page on sp-mod.com, an addon of TCF Mod Manager - the one for this
    // machine's SPT line, since each line has its own. Blank hides the button; the ids live in Core
    // so the App update page's check links the same page.
    //
    public string AddonPageUrl
    {
        get
        {
            var installPath = _settings.Load().SptInstallPath;
            return ServerMapAddon.PageUrl(
                ServerMapModVersions.LineFor(ServerMapModVersions.FindInstalled(installPath), installPath));
        }
    }

    public bool HasAddonPage => !string.IsNullOrWhiteSpace(AddonPageUrl);

    //
    // Opens the mod's page in the default browser.
    //
    // The server half is distributed as an addon rather than bundled with the app, because it is
    // installed on a different machine from this one by a different person - an operator sets up a
    // server once, and every player joining it needs nothing but this page switched on.
    //
    [RelayCommand(CanExecute = nameof(HasAddonPage))]
    private void OpenAddonPage()
    {
        var url = AddonPageUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public bool CanUseLocalKey =>
        LocalKey is not null && !string.Equals(LocalKey, KeyInput?.Trim(), StringComparison.Ordinal);

    public string LocalKeyDescription => LocalKey is null
        ? ""
        : Text(Strings.ServerMap_LocalKeyDescriptionFormat, LocalKey);

    //
    // The key this app last put in the box on its own. Null when the box holds something the user
    // typed, which is the whole distinction this field exists to keep.
    //
    private string? _autoFilledKey;

    //
    // Re-reads this machine's key file and follows it.
    //
    // On demand rather than once at startup, because the file appears the first time the server mod
    // runs - which is usually after this app was opened - and changes again whenever the key is
    // rotated. Called when the Server map page and the Options page are shown, so an operator never
    // has to restart the app to see their own server's key, or press a button to collect it.
    //
    // The box is filled when it is empty, or when it still holds the key we put there. It is left
    // alone when it holds anything else: a machine that runs a server can perfectly well be
    // connecting to somebody else's, and silently replacing their key with the local one would be a
    // 401 they had no reason to expect.
    //
    public void RefreshLocalKey(string? sptInstallPath = null)
    {
        LocalKey = ServerMapKeyFile.TryReadLocal(sptInstallPath ?? _settings.Load().SptInstallPath);
        RefreshServerSettings(sptInstallPath ?? _settings.Load().SptInstallPath);

        if (LocalKey is null) return;

        var current = KeyInput?.Trim();

        if (!string.IsNullOrWhiteSpace(current)
            && !string.Equals(current, _autoFilledKey, StringComparison.Ordinal))
        {
            return;
        }

        KeyInput = LocalKey;
        _autoFilledKey = LocalKey;
    }

    //
    // Puts this machine's own key in the box. Offered rather than forced, because the address might
    // legitimately point at somebody else's server from a machine that also runs one.
    //
    [RelayCommand(CanExecute = nameof(CanUseLocalKey))]
    private void UseLocalKey()
    {
        if (LocalKey is not null) KeyInput = LocalKey;
    }

    //
    // Replaces this machine's server key with a new one.
    //
    // Deliberate and destructive, which is why it is a button and not something that happens on its
    // own: every copy of the old key stops working the moment this is pressed, and everybody who has
    // one has to be sent the new one. That is the entire purpose - it is what you press when a key
    // has gone somewhere it should not have.
    //
    // The server watches the file rather than caching the key for the life of its process, so this
    // takes effect on its next request. It does not need stopping.
    //
    [RelayCommand(CanExecute = nameof(IsServerOwner))]
    private void NewLocalKey()
    {
        var installPath = _settings.Load().SptInstallPath;

        if (!ServerMapKeyFile.TryRotateLocal(installPath, out var rotated))
        {
            KeyNotice = Strings.ServerMap_KeyRotateFailed;
            return;
        }

        AppLog.Info("ServerMap", "shared key rotated by the operator");

        LocalKey = rotated;

        // Into the box as well: on the server's own machine the connection this app uses is that
        // same server, so leaving it on the old key would fail the next connect for no visible
        // reason. Marked as ours, so the next refresh keeps following the file.
        KeyInput = rotated;
        _autoFilledKey = rotated;

        KeyNotice = Strings.ServerMap_KeyRotated;
    }

    //
    // The switch's tooltip. Says what the page needs to be useful, because that is the part someone
    // deciding whether to switch it on cannot tell from the name: the mod goes on the SERVER, and
    // without it this page has nothing to show.
    //
    public string SettingToolTip => IsPageEnabled
        ? Strings.Options_ServerMapToolTipOn
        : Strings.Options_ServerMapToolTipOff;

    //
    // No confirmation either way. Nothing is at stake in showing or hiding a page, and turning it
    // off does not throw away the address or the recorded certificate.
    //
    partial void OnIsPageEnabledChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.ServerMap.ShowPage = value;
        _settings.Save(settings);

        AppLog.Info("ServerMap", value ? "page shown" : "page hidden");

        // Switched off means no requests to the server, reports included.
        AppServices.ServerMapReporter.Start();
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(HostInput);

    // Emptying the box has to change the resting line under it straight away, before anything is
    // clicked - otherwise it still reads "not connected yet" for an address that no longer exists.
    partial void OnHostInputChanged(string? value)
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(StatusMessage));
    }

    public bool IsConnected => Probe?.Found == true;

    public string StatusMessage => ServerMapProblems.DescribeState(Probe, IsConfigured);

    // Named to match the shared ErrorText style in App.xaml, which binds to it.
    public bool HasError => Probe is not null && !Probe.Found;

    public string PinDescription => ServerMapProblems.DescribePin(PinnedThumbprint);

    public string KeyDescription =>
        ServerMapProblems.DescribeKey(!string.IsNullOrWhiteSpace(KeyInput), Probe?.Hello?.RequiresKey ?? false);

    public bool HasList => List is not null;

    public bool HasListStatus => !string.IsNullOrEmpty(ListStatus);

    //
    // What the held list is, in one line. Reads from the stored list rather than the handshake, so
    // it keeps saying something true after the server goes down.
    //
    //
    // One whole sentence per shape rather than a count, a plural noun and a clause concatenated:
    // where the unresolved count goes, and whether the noun agrees with it, are the translator's.
    //
    public string ListSummary
    {
        get
        {
            if (List is null) return "";

            var one = List.Entries.Count == 1;
            var unresolved = List.Unresolved.Count();

            if (unresolved == 0)
            {
                return Strings.ServerMap_ListSummary(
                    List.Entries.Count, List.Name, List.Revision, List.Entries.Count);
            }

            return one
                ? Text(
                    Strings.ServerMap_ListSummaryOneUnresolvedFormat,
                    List.Name,
                    List.Revision,
                    unresolved)
                : Text(
                    Strings.ServerMap_ListSummaryManyUnresolvedFormat,
                    List.Name,
                    List.Revision,
                    List.Entries.Count,
                    unresolved);
        }
    }

    // The one failure the user is asked to make a judgement about, rather than just told about.
    public bool HasCertificateChanged => Probe?.Problem == ServerMapProblem.CertificateRejected;

    public string ServerName => Probe?.Hello is null
        ? ""
        : string.IsNullOrWhiteSpace(Probe.Hello.ServerName) ? Probe.Endpoint.Host : Probe.Hello.ServerName!;

    //
    // The line under the server's name on the page. Says what the mod can currently do rather than
    // only what it is, because "no mod list published" is the difference between a map that is
    // working and one that has nothing to show.
    //
    public string ServerDetail
    {
        get
        {
            if (Probe?.Hello is not { } hello) return "";

            var parts = new List<string>
            {
                Text(Strings.ServerMap_EndpointFormat, Probe.Endpoint.Host, Probe.Endpoint.Port),
            };

            if (!string.IsNullOrWhiteSpace(hello.SptVersion))
                parts.Add(Text(Strings.ServerMap_DetailSptFormat, hello.SptVersion));

            if (!string.IsNullOrWhiteSpace(hello.ModVersion))
                parts.Add(Text(Strings.ServerMap_DetailModFormat, hello.ModVersion));

            parts.Add(hello.HasList
                ? Text(
                    Strings.ServerMap_DetailPublishingFormat,
                    hello.ListRevision?.ToString() ?? Strings.Common_Unknown)
                : Strings.ServerMap_DetailNoList);

            return string.Join(Strings.ServerMap_DetailSeparator, parts);
        }
    }

    //
    // Called from MainWindow once the window is up. Fire-and-forget: whether a server answers has no
    // bearing on the app opening, and a server that is off just leaves the page saying so.
    //
    public async Task ConnectOnStartupAsync()
    {
        // Switched off means switched off: no page, and no request to somebody's server either.
        if (!IsPageEnabled || !IsConfigured) return;

        await ConnectAsync();
    }

    //
    // Public so the pre-launch check can ask for a fresh handshake rather than reading whatever the
    // last one said - the point of that check is that it is current at the moment of launching.
    //
    [RelayCommand(CanExecute = nameof(CanConnect))]
    public async Task ConnectAsync()
    {
        IsBusy = true;

        try
        {
            var endpoint = SaveAndBuildEndpoint();

            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                Probe = new ServerHelloProbe { Endpoint = endpoint, Problem = ServerMapProblem.NoAddress };
                return;
            }

            using var client = ServerMapClient.TryCreate(endpoint);

            if (client is null)
            {
                Probe = new ServerHelloProbe { Endpoint = endpoint, Problem = ServerMapProblem.InvalidAddress };
                return;
            }

            var probe = await client.HelloAsync();

            //
            // Trust on first use, recorded the moment it happens. Saved before Probe is set so the
            // Options page never shows "connected, recorded its certificate" beside an empty pin.
            //
            if (probe.Found && probe.PinnedOnThisConnection && !string.IsNullOrWhiteSpace(probe.ActualThumbprint))
            {
                SavePin(probe.ActualThumbprint);
                AppLog.Info("ServerMap", $"pinned {endpoint.Host} to {ServerMapProblems.Short(probe.ActualThumbprint)}");
            }

            Probe = probe;

            AppLog.Info("ServerMap", probe.Found
                ? $"connected to {endpoint.Host}:{endpoint.Port}"
                : $"{endpoint.Host}:{endpoint.Port} - {probe.Problem}");

            if (probe.Hello is { } hello) await SyncListAsync(client, endpoint, hello, cancellationToken: default);

            await AfterConnectAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConnect() => !IsBusy;

    //
    // Fetches the published list when it is worth fetching, and stores it.
    //
    // The handshake carries the revision precisely so this can be skipped: a list that has not moved
    // since the last fetch is not asked for again. Which revision is held comes from the stored
    // lists themselves, matched on the server address they carry as their Source - there is no
    // separate record to get out of step with what is actually in the store.
    //
    // Storing is not applying. A served list lands in the user's mod lists read-only and does
    // nothing until they apply it from that page, which shows what would change first. That
    // separation is the whole reason this feature serves a list rather than files.
    //
    private async Task SyncListAsync(ServerMapClient client, ServerMapEndpoint endpoint, ServerHello hello,
        CancellationToken cancellationToken)
    {
        var held = HeldListFor(endpoint);

        if (!hello.HasList)
        {
            List = held;
            ListStatus = ServerMapProblems.DescribeList(
                new ServerMapListResult { Endpoint = endpoint, Problem = ServerMapProblem.NoList }, held);
            return;
        }

        if (held is not null
            && hello.ListRevision is { } revision
            && revision <= held.Revision
            && IsTheHeldList(hello.ListName, held.Name))
        {
            List = held;
            ListStatus = ServerMapProblems.DescribeList(
                new ServerMapListResult { Endpoint = endpoint, List = held }, held);
            return;
        }

        var result = await client.ListAsync(cancellationToken);

        var own = false;

        if (result.List is { } fetched)
        {
            //
            // Upserts by Id, so a newer revision of a list already held replaces it rather than
            // leaving two - and hands back the local list untouched when this machine is the one
            // that published it, which is why what goes on screen is the return value rather than
            // what came off the wire. See ModListStore.Add.
            //
            var stored = AppServices.ModLists.Add(fetched);
            own = stored.IsEditable;
            List = stored;

            AppLog.Info("ServerMap", own
                ? $"server is serving this machine's own list \"{stored.Name}\" revision {stored.Revision}"
                : $"fetched list \"{fetched.Name}\" revision {fetched.Revision} ({fetched.Entries.Count} entries)");
        }
        else
        {
            // A list that would not come back does not throw away the one already held.
            List = held;
            AppLog.Info("ServerMap", $"list not fetched - {result.Problem}");
        }

        ListStatus = ServerMapProblems.DescribeList(result, held, own);
    }

    //
    // Whether the list the handshake describes is the one already held, as far as it can be told.
    //
    // The handshake carries no list id, so the NAME is the only thing that separates "a different
    // list" from "a newer revision of this one" - and the revision test alone gets the first case
    // exactly backwards: a server that switches from one list at revision 5 to another at revision 1
    // leaves every client sitting on the old one, because 1 is not greater than 5. A server that
    // does not send a name at all is taken at its word on the revision, which is the behaviour that
    // shipped.
    //
    private static bool IsTheHeldList(string? published, string held) =>
        string.IsNullOrWhiteSpace(published)
        || string.Equals(published.Trim(), held.Trim(), StringComparison.OrdinalIgnoreCase);

    //
    // The newest list this install already holds from this server. Matched on Source, which a served
    // list carries as "host:port" - see ModListFile.Read.
    //
    private static ModList? HeldListFor(ServerMapEndpoint endpoint)
    {
        var source = $"{endpoint.Host}:{endpoint.Port}";

        return AppServices.ModLists.Load().Lists
            .Where(l => l.Origin == ModListOrigin.Server
                        && string.Equals(l.Source, source, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.Revision)
            .FirstOrDefault();
    }

    //
    // Fetches the list again whether or not the revision moved. For the case the automatic path
    // deliberately does not cover: the held copy was edited, deleted, or is simply doubted.
    //
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task FetchListAsync() => await FetchAndStoreAsync();

    //
    // The same fetch, callable from elsewhere, returning what ended up in the store.
    //
    // The Mod lists page needs it: a served list is refreshed from the page the list lives on, not
    // only from the map page. Both routes have to go through one implementation - two fetches that
    // store differently is how a page ends up showing a list nobody else has.
    //
    public async Task<ModList?> FetchAndStoreAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;

        try
        {
            var endpoint = SaveAndBuildEndpoint();

            using var client = ServerMapClient.TryCreate(endpoint);
            if (client is null) return null;

            var result = await client.ListAsync(cancellationToken);

            var own = false;
            ModList? stored = null;

            if (result.List is { } fetched)
            {
                stored = AppServices.ModLists.Add(fetched);
                own = stored.IsEditable;
                List = stored;
            }

            ListStatus = ServerMapProblems.DescribeList(result, HeldListFor(endpoint), own);

            return stored;
        }
        finally
        {
            IsBusy = false;
        }
    }

    //
    // Records the certificate the server is presenting now, replacing the one that was pinned, and
    // reconnects. Only reachable from the mismatch message, which names both fingerprints - this is
    // the user answering it, not a retry that quietly gives up on checking.
    //
    [RelayCommand(CanExecute = nameof(HasCertificateChanged))]
    private async Task TrustNewCertificateAsync()
    {
        if (Probe?.ActualThumbprint is not { } presented) return;

        AppLog.Info("ServerMap",
            $"certificate for {Probe.Endpoint.Host} replaced: {ServerMapProblems.Short(PinnedThumbprint)}"
            + $" -> {ServerMapProblems.Short(presented)}");

        SavePin(presented);

        await ConnectAsync();
    }

    //
    // Throws the pin away and re-arms trust-on-first-use, so the next connection records whatever
    // is there. The honest way out for someone who has moved their server rather than been attacked.
    //
    [RelayCommand(CanExecute = nameof(HasPin))]
    private void ForgetPin()
    {
        SavePin(null);
        AppLog.Info("ServerMap", "pinned certificate cleared");
    }

    private bool HasPin() => !string.IsNullOrWhiteSpace(PinnedThumbprint);

    // Persists what is in the boxes and hands back the endpoint to dial. An unparseable port is kept
    // as typed and passed through as 0, which TryGetBaseUri refuses and Options reports.
    private ServerMapEndpoint SaveAndBuildEndpoint()
    {
        var host = HostInput?.Trim();
        var port = int.TryParse(PortInput?.Trim(), out var parsed) ? parsed : 0;

        var key = KeyInput?.Trim();

        var settings = _settings.Load();
        settings.ServerMap.Host = string.IsNullOrWhiteSpace(host) ? null : host;
        settings.ServerMap.Port = port;
        settings.ServerMap.SharedKey = string.IsNullOrWhiteSpace(key) ? null : key;
        _settings.Save(settings);

        OnPropertyChanged(nameof(IsConfigured));

        return new ServerMapEndpoint(host ?? string.Empty, port, PinnedThumbprint, key);
    }

    private void SavePin(string? thumbprint)
    {
        var settings = _settings.Load();
        settings.ServerMap.PinnedThumbprint = thumbprint;
        _settings.Save(settings);

        PinnedThumbprint = thumbprint;

        // Consent is keyed on the certificate, so a new one is a server that has not been asked.
        AppServices.ServerMapReporter.Start();
        NotifyReporting();
    }
}
