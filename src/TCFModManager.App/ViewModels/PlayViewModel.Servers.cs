using System.Net.Http;
using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public enum PlayServerSource
{
    ThisPc,

    // Added in SPT's launcher, read from its settings.
    Launcher,

    // Added on the Play card.
    Added,
}

/// <summary>One server in the Play card's server picker.</summary>
public sealed class PlayServerItem(string? address, string name, PlayServerSource source)
{
    // "host:port"; null for this PC's own server.
    public string? Address { get; } = address;

    public string Name { get; } = name;

    public PlayServerSource Source { get; } = source;

    public bool IsThisPc => Address is null;

    public string Title => IsThisPc ? Strings.Play_ServerThisPc : Name;

    public string Detail => Source switch
    {
        PlayServerSource.ThisPc => Strings.Play_ServerThisPcDetail,
        PlayServerSource.Launcher => LocalizationService.Text(Strings.Play_ServerFromLauncherFormat, Address),
        _ => Address ?? string.Empty,
    };

    public override string ToString() => Title;

    public bool SameAs(PlayServerItem other) => Address == other.Address && Name == other.Name && Source == other.Source;
}

//
// Fork (1.4.3): Play on another PC's server - a Fika host, or a friend's SPT server. The game,
// its patches, bundles and clean-up are this PC's as ever (SPT's launcher does the same for any
// server it is pointed at); what changes is the server: it is not started from here, it is reached
// through a pinned certificate (SptLauncherApi.ForRemote), and the profiles are the ones on it.
// The servers added in SPT's launcher are offered beside the ones added here.
//
public partial class PlayViewModel
{
    public ObservableCollection<PlayServerItem> PlayServers { get; } = [];

    // Only when there is more than this PC's to pick from; ... > Add a server... adds one.
    public bool HasServerChoice => IsDirectLaunchOn && PlayServers.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteServer))]
    [NotifyPropertyChangedFor(nameof(IsLocalServer))]
    [NotifyPropertyChangedFor(nameof(RemoteServerNote))]
    [NotifyPropertyChangedFor(nameof(WipeWarning))]
    [NotifyPropertyChangedFor(nameof(DirectSummary))]
    private PlayServerItem? _selectedPlayServer;

    public bool IsRemoteServer => SelectedPlayServer?.Address is not null;

    // Close the game when the server stops watches this PC's server only.
    public bool IsLocalServer => !IsRemoteServer;

    // Another PC's profiles count as none only once that server has answered with none.
    private bool _remoteProfilesRead;

    public string? RemoteServerNote => SelectedPlayServer is { Address: { } address } server
        ? Text(Strings.Play_RemoteNoteFormat, server.Title, address)
        : null;

    // The server whose certificate changed: Trust the new certificate takes the one it showed.
    [ObservableProperty]
    private bool _canTrustNewCertificate;

    private (string Address, string Thumbprint)? _offeredCertificate;

    private bool _readingServers;

    // The server whose profiles are in Profiles, while it is another PC's.
    private string? _remoteProfilesFor;

    private void RefreshServers(AppSettings settings, string? serverRoot)
    {
        var found = new List<PlayServerItem> { new(null, string.Empty, PlayServerSource.ThisPc) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Options > Play with Fika off (PlayViewModel.Fika): this PC's server only. The servers
        // and the one picked stay in the settings for when it is on again.
        var others = OtherServersOn;

        void Add(string name, string? text, PlayServerSource source)
        {
            if (!SptLauncherApi.TryNormalizeAddress(text, out var address) || SptLauncherApi.IsLocalAddress(address) || !seen.Add(address)) return;
            found.Add(new PlayServerItem(address, string.IsNullOrWhiteSpace(name) ? address : name.Trim(), source));
        }

        if (others && serverRoot is not null)
        {
            foreach (var server in SptLauncherSettings.Read(serverRoot).Servers) Add(server.Name, server.Address, PlayServerSource.Launcher);
        }

        if (others)
        {
            foreach (var server in settings.PlayServers) Add(server.Name, server.Address, PlayServerSource.Added);
        }

        var wanted = others ? settings.PlayServerAddress : null;
        var before = SelectedPlayServer?.Address;
        if (found.Count == PlayServers.Count && found.Zip(PlayServers).All(pair => pair.First.SameAs(pair.Second))
            && SelectedPlayServer?.Address == wanted)
        {
            return;
        }

        _readingServers = true;
        try
        {
            PlayServers.Clear();
            foreach (var item in found) PlayServers.Add(item);
            SelectedPlayServer = PlayServers.FirstOrDefault(s => string.Equals(s.Address, wanted, StringComparison.OrdinalIgnoreCase)) ?? PlayServers[0];
        }
        finally
        {
            _readingServers = false;
            OnPropertyChanged(nameof(HasServerChoice));
        }

        if (SelectedPlayServer?.Address != before) ForgetProfiles();
    }

    partial void OnSelectedPlayServerChanged(PlayServerItem? value)
    {
        OnPropertyChanged(nameof(CanRemovePlayServer));
        if (_readingServers || value is null) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.PlayServerAddress = value.Address;
        service.Save(settings);
        AppLog.Info("DirectLaunch", $"Play's server: {value.Address ?? "this PC"}");

        ForgetProfiles();
        Refresh();
    }

    // The other server's profiles are not this one's.
    private void ForgetProfiles()
    {
        _remoteProfilesFor = null;
        _remoteProfilesRead = false;
        _serverModsPreview = null;
        ServerModsDiffer = false;
        ServerModsNote = null;
        _readingProfile = true;
        try
        {
            Profiles.Clear();
            SelectedProfile = null;
        }
        finally
        {
            _readingProfile = false;
        }

        ShowNewProfile = false;
        ConfirmingDeleteProfile = false;
        WipeNextStart = false;
        PlayStatus = null;
        OnPropertyChanged(nameof(HasNoProfiles));
    }

    // From RefreshDirect while another PC's server is picked: its profiles, asked once.
    private void RefreshRemoteProfiles()
    {
        if (SelectedPlayServer?.Address is not { } address || _remoteProfilesFor == address || IsPlaying) return;
        _ = LoadRemoteProfilesAsync();
    }

    [RelayCommand]
    private async Task LoadRemoteProfilesAsync()
    {
        if (SelectedPlayServer?.Address is not { } address) return;
        _remoteProfilesFor = address;

        await WithServerAsync(async (api, ct) =>
        {
            var found = (await api.ProfilesAsync(ct)).Select(p => new PlayProfileItem(p)).ToList();
            if (SelectedPlayServer?.Address != address) return;

            _remoteProfilesRead = true;
            ShowProfiles(found, SelectedProfile?.Profile.ProfileId ?? new SettingsService().Load().DirectLaunchProfileId);
            OnPropertyChanged(nameof(HasNoProfiles));
            AppLog.Info("DirectLaunch", $"{found.Count} profile(s) on {address}");
        });

        // Fork (1.4.7): then its mods against the ones here and a copy of the profile - by
        // themselves, so that neither holds Play back nor is taken for the profiles failing.
        if (_remoteProfilesRead) _ = RecheckServerModsAsync();
    }

    //
    // Fork (1.4.7): another PC's server's mods against the ones here. The server mods it has loaded
    // become a collection (HostMods), and the Collections page's preview says what applying it
    // would do; nothing is installed from here.
    //
    // Asked without being asked for when the server's profiles are read and each time the page is
    // shown, so a mismatch is said before Play is pressed, not found in the game. The collection is
    // kept only once Review is pressed.
    //
    [ObservableProperty]
    private string? _serverModsNote;

    // Something to install, update or turn on: Review and install is offered.
    [ObservableProperty]
    private bool _serverModsDiffer;

    private ModListPreview? _serverModsPreview;

    // The server's mods at the last look, as HostMods.Key names them - kept with the collection.
    private (string Address, List<string> Keys)? _serverModsSeen;

    // The menu's "Compare mods with this server": the same, said aloud, and straight to the preview.
    [RelayCommand]
    private async Task CompareServerModsAsync()
    {
        if (SelectedPlayServer is not { Address: { } address } server) return;

        if (AppServices.ModCache.AllMods.Count == 0)
        {
            PlayStatus = Strings.Play_RemoteModsCatalogLoading;
            return;
        }

        await WithServerAsync(async (api, ct) =>
        {
            if (!await CheckServerModsAsync(api, server, address, ct))
            {
                PlayStatus = Text(Strings.Play_RemoteNoModsFormat, server.Title);
                return;
            }

            if (ServerModsDiffer) ReviewServerMods();
        });
    }

    // False when the server has no mods to say (none loaded, or an SPT that is not asked this way).
    private async Task<bool> CheckServerModsAsync(SptLauncherApi api, PlayServerItem server, string address, CancellationToken ct)
    {
        var mods = await api.ModsAsync(ct);
        if (SelectedPlayServer?.Address != address) return true;

        AppLog.Info("DirectLaunch", $"{mods.Count} server mod(s) loaded on {address}");

        _serverModsPreview = null;
        ServerModsDiffer = false;
        ServerModsNote = null;
        if (mods.Count == 0) return false;

        // Fika's own server mod (ModGuid "Fika", named "server") is said by itself: a player needs
        // Fika's plugin, not that, and the two halves are versioned apart.
        var hostFika = mods.FirstOrDefault(m => m.ModGuid.Equals("Fika", StringComparison.OrdinalIgnoreCase)
            || m.ModGuid.StartsWith("com.fika", StringComparison.OrdinalIgnoreCase));
        if (hostFika is not null) mods = [.. mods.Where(m => !ReferenceEquals(m, hostFika))];

        var preview = await AppServices.ModListWorkflow.FromServerAsync(
            Text(Strings.Play_RemoteModsListNameFormat, server.Title), address, mods);
        if (preview is null || SelectedPlayServer?.Address != address) return true;

        _serverModsSeen = (address, [.. mods.Select(HostMods.Key)]);

        string? fika = null;
        if (hostFika is not null)
        {
            var plugin = preview.Install.Candidates.SelectMany(c => c.Entries).FirstOrDefault(e =>
                e.Target == InstalledModTarget.Client && !e.IsDisabled
                && e.AllGuids.Any(g => g.Equals("com.fika.core", StringComparison.OrdinalIgnoreCase)));
            fika = plugin is null
                ? Text(Strings.Play_RemoteFikaMissingFormat, server.Title)
                : Text(Strings.Play_RemoteFikaVersionsFormat, hostFika.Version, plugin.Version ?? "?");
        }

        // A server that hands out its own list through Server Map is checked against that list, by
        // the check above the launch buttons: it names the host's game mods too, which a server
        // cannot. Only Fika's line is said here, and no second collection is made for it.
        if (ServerMapCovers(address))
        {
            ServerModsNote = string.Join(" ", new[] { Text(Strings.Play_RemoteModsServerMapFormat, server.Title), fika }
                .Where(part => part is not null));
            return true;
        }

        var plan = preview.Plan;
        int install = plan.Install.Count(), update = plan.Update.Count(), enable = plan.Enable.Count();
        var byHand = plan.Manual.Select(a => a.Name).ToList();
        AppLog.Info("DirectLaunch", $"against {address}: {install} to install, {update} to update, {enable} to turn on, "
            + $"{byHand.Count} not on the catalog, {plan.Keep.Count()} the same");

        var manual = byHand.Count > 0 ? Text(Strings.Play_RemoteModsManualFormat, server.Title, TextLists.Join(byHand)) : null;

        if (plan.IsNoOp)
        {
            // A server with Fika and nothing else has no mods to match: only Fika's line is said.
            var match = manual ?? (mods.Count > 0 ? Text(Strings.Play_RemoteModsMatchFormat, server.Title, mods.Count) : null);
            ServerModsNote = string.Join(" ", new[] { match, fika }.Where(part => part is not null)) is { Length: > 0 } note ? note : null;
            return true;
        }

        _serverModsPreview = preview;
        ServerModsDiffer = true;
        ServerModsNote = string.Join(" ", new[] { Text(Strings.Play_RemoteModsDifferFormat, server.Title, install, update, enable), manual, fika }
            .Where(part => part is not null));
        return true;
    }

    // Fork (1.4.7): whether Server Map is set to this server and has its list.
    private static bool ServerMapCovers(string address)
    {
        var gate = AppServices.ServerMap;
        return gate.IsPageEnabled
            && (gate.List is not null || AppServices.ModLists.GetActiveServer() is not null)
            && SptLauncherApi.IsSameServer(gate.HostInput, gate.PortInput, address);
    }

    // Quietly: after the profiles, and when the page comes back - from applying the collection, say.
    private async Task RecheckServerModsAsync()
    {
        if (SelectedPlayServer is not { Address: { } address } server || IsPlaying) return;

        try
        {
            if (CreateApi() is not { } created) return;

            using var api = created;
            if (AppServices.ModCache.AllMods.Count > 0) await CheckServerModsAsync(api, server, address, CancellationToken.None);
            await CopyRemoteProfileAsync(api, address, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Nobody waits on this: whatever a server far away answers with is logged, not thrown.
            AppLog.Debug("DirectLaunch", $"couldn't compare mods with {address}: {ex.Message}");
        }
    }

    //
    // Fork (1.4.7): the picked profile, copied here from a Fika server (RemoteProfileCopies) - when
    // the server's profiles are read, when Play is shown, and a little after the game closes. Said
    // nowhere: a server without Fika has nothing to give, and that is not news.
    //
    private async Task CopyRemoteProfileAsync(SptLauncherApi api, string address, CancellationToken ct)
    {
        if (SelectedProfile?.Profile.ProfileId is not { Length: > 0 } id) return;

        try
        {
            if (await api.FikaProfileAsync(id, ct) is not { } json) return;

            var path = await Task.Run(() => AppServices.RemoteProfiles.Save(address, id, json, DateTimeOffset.Now), ct);
            if (path is not null) AppLog.Info("DirectLaunch", $"kept a copy of profile {id} from {address}: {path}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
            or InvalidDataException or System.Text.Json.JsonException)
        {
            AppLog.Debug("DirectLaunch", $"couldn't copy the profile from {address}: {ex.Message}");
        }
    }

    // The game has closed on another PC's server: what the raid left in the profile, once it is saved.
    private async Task CopyRemoteProfileAfterGameAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5)); // HUNCH: for the server to have written it

        try
        {
            if (SelectedPlayServer?.Address is not { } address || CreateApi() is not { } created) return;

            using var api = created;
            await CopyRemoteProfileAsync(api, address, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppLog.Debug("DirectLaunch", $"couldn't copy the profile after the game: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenRemoteProfileCopies()
    {
        if (SelectedPlayServer?.Address is not { } address) return;

        var folder = AppServices.RemoteProfiles.FolderFor(address);
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("DirectLaunch", $"couldn't open {folder}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ReviewServerMods()
    {
        if (_serverModsPreview is not { } preview) return;

        AppServices.ModLists.Add(preview.List);

        // What the collection was made from: an entry taken out of it later is then known as taken out.
        if (_serverModsSeen is { } seen)
        {
            var service = new SettingsService();
            var settings = service.Load();
            settings.PlayServerModsSeen[seen.Address] = seen.Keys;
            service.Save(settings);
        }

        AppNavigation.ReviewList(preview.List.Id);
    }

    // The api for the server picked: this PC's at its http.json address, or another PC's, pinned.
    private SptLauncherApi? CreateApi()
    {
        if (SelectedPlayServer?.Address is { } address)
        {
            var pins = new SettingsService().Load().PlayServerPins;
            return SptLauncherApi.ForRemote(address, pins.GetValueOrDefault(address));
        }

        return Server?.ExePath is { } exe ? new SptLauncherApi(SptDirectLaunch.ServerAddress(exe)) : null;
    }

    //
    // Another PC's server is not started from here: it answers, or Play says why not. A few tries,
    // for a server on the far side of the internet; none after a certificate it did not show before.
    // Three tries 1.5s apart, each with PingAsync's 3s, is HUNCH: not tried against a host far away.
    //
    private async Task<bool> ReachRemoteAsync(SptLauncherApi api, CancellationToken ct)
    {
        var name = SelectedPlayServer?.Title ?? api.Address;
        PlayStatus = Text(Strings.Play_RemoteConnectingFormat, name);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (await PingAsync(api, ct))
            {
                RememberCertificate(api);
                return true;
            }

            if (api.CertificateChanged) break;
            await Task.Delay(1500, ct);
        }

        if (api.CertificateChanged && api.PresentedThumbprint is { } presented)
        {
            AppLog.Warn("DirectLaunch", $"{api.Address} showed certificate {presented}, not the one pinned - not connected");
            Fail(Text(Strings.Play_RemoteCertificateChangedFormat, name));
            _offeredCertificate = (api.Address, presented);
            CanTrustNewCertificate = true;
            return false;
        }

        Fail(Text(Strings.Play_RemoteNoAnswerFormat, name, api.Address));
        return false;
    }

    // Trust on first use: the certificate the server showed the first time it answered.
    private static void RememberCertificate(SptLauncherApi api)
    {
        if (api.PresentedThumbprint is not { } thumbprint) return;

        var service = new SettingsService();
        var settings = service.Load();
        if (settings.PlayServerPins.ContainsKey(api.Address)) return;

        settings.PlayServerPins[api.Address] = thumbprint;
        service.Save(settings);
        AppLog.Info("DirectLaunch", $"{api.Address}: certificate {thumbprint} pinned on first use");
    }

    [RelayCommand]
    private void TrustNewCertificate()
    {
        if (_offeredCertificate is not { } offered) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.PlayServerPins[offered.Address] = offered.Thumbprint;
        service.Save(settings);
        AppLog.Info("DirectLaunch", $"{offered.Address}: certificate {offered.Thumbprint} trusted in place of the one pinned");

        _offeredCertificate = null;
        PlayStatusIsError = false;
        PlayStatus = Strings.Play_RemoteCertificateTrusted;
        _remoteProfilesFor = null;
        if (Profiles.Count == 0) RefreshRemoteProfiles();
    }

    // ... > Add a server...: a name and an address, kept in the settings and picked.
    [RelayCommand]
    private void AddPlayServer()
    {
        if (AskServer() is not { } server) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.PlayServers.RemoveAll(s => string.Equals(s.Address, server.Address, StringComparison.OrdinalIgnoreCase));
        settings.PlayServers.Add(server);
        settings.PlayServerAddress = server.Address;
        service.Save(settings);
        AppLog.Info("DirectLaunch", $"server {server.Address} added");

        Refresh();
        PlayStatusIsError = false;
        PlayStatus = Text(Strings.Play_RemoteAddedFormat, server.Name);
    }

    public bool CanRemovePlayServer => SelectedPlayServer?.Source == PlayServerSource.Added && !IsPlaying;

    [RelayCommand]
    private void RemovePlayServer()
    {
        if (!CanRemovePlayServer || SelectedPlayServer?.Address is not { } address) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.PlayServers.RemoveAll(s => string.Equals(s.Address, address, StringComparison.OrdinalIgnoreCase));
        settings.PlayServerPins.Remove(address);
        settings.PlayServerAddress = null;
        service.Save(settings);
        AppLog.Info("DirectLaunch", $"server {address} removed");

        SelectedPlayServer = PlayServers[0];
    }

    //
    // Fork (1.4.7): "Join from an invite..." - the text a host copied (FikaInviteText), pasted. Its
    // addresses are tried in turn and the first that answers is added and picked; the rest follows
    // from picking a server: its profiles, and its mods against the ones here.
    //
    [RelayCommand]
    private async Task JoinFromInviteAsync()
    {
        if (AskInvite() is not { } text) return;

        var invite = FikaInviteText.Parse(text);
        if (invite.Addresses.Count == 0)
        {
            PlayStatusIsError = true;
            PlayStatus = Strings.Play_InviteNoAddress;
            return;
        }

        PlayStatusIsError = false;
        PlayStatus = Strings.Play_InviteTrying;

        var pins = new SettingsService().Load().PlayServerPins;
        string? reached = null;
        foreach (var address in invite.Addresses)
        {
            try
            {
                using var api = SptLauncherApi.ForRemote(address, pins.GetValueOrDefault(address));
                if (!await PingAsync(api, CancellationToken.None)) continue;

                RememberCertificate(api);
                reached = address;
                break;
            }
            catch (Exception ex) when (ex is IOException or UriFormatException or ArgumentException or InvalidDataException)
            {
                AppLog.Debug("DirectLaunch", $"invite: {address} didn't answer: {ex.Message}");
            }
        }

        var chosen = reached ?? invite.Addresses[0];
        var service = new SettingsService();
        var settings = service.Load();
        if (!settings.PlayServers.Any(s => string.Equals(s.Address, chosen, StringComparison.OrdinalIgnoreCase)))
            settings.PlayServers.Add(new PlayServerEntry { Name = chosen, Address = chosen });
        settings.PlayServerAddress = chosen;
        settings.FikaOptions = true; // other PCs' servers are offered from here on, Fika installed or not
        service.Save(settings);
        AppLog.Info("DirectLaunch", $"invite: {invite.Addresses.Count} address(es), {(reached is null ? "none answered" : reached + " answered")}; SPT {invite.Spt ?? "?"}, Fika {invite.Fika ?? "?"}");

        Refresh();

        var here = AppServices.SptEnvironment.InstalledVersion;
        var differs = invite.Spt is { } theirs && here is { Length: > 0 }
            && !here.StartsWith(theirs, StringComparison.OrdinalIgnoreCase) && !theirs.StartsWith(here, StringComparison.OrdinalIgnoreCase);

        PlayStatusIsError = reached is null || differs;
        PlayStatus = string.Join(" ", new[]
        {
            reached is null ? Text(Strings.Play_InviteNotReachedFormat, chosen) : Text(Strings.Play_RemoteAddedFormat, chosen),
            differs ? Text(Strings.Play_InviteSptDiffersFormat, invite.Spt, here) : null,
        }.Where(part => part is not null));

        // The host's own mods, when the invite carries them: every mod, the game-only ones too.
        if (ModListShareCode.Contains(text) && await CollectionSharing.AddFromCodeAsync(text) is { } received)
            AppNavigation.ReviewList(received.List.Id);
    }

    private static string? AskInvite()
    {
        var box = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MaxHeight = 240,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 6, 0, 0),
        };
        box.Loaded += (_, _) => box.Focus();

        var answer = SteamDialog.Show(
            Strings.Play_InviteTitle,
            new StackPanel { Width = 420, Children = { new TextBlock { Text = Strings.Play_InviteHint }, box } },
            new SteamDialogChoice(Strings.Play_InviteJoinButton, SteamDialogButton.Green),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        return answer == 0 && box.Text.Trim() is { Length: > 0 } text ? text : null;
    }

    private static PlayServerEntry? AskServer()
    {
        var name = string.Empty;
        var address = string.Empty;
        var wrong = false;
        while (true)
        {
            // Made again for each try: a dialog's content can't be handed on to the next one.
            var nameBox = Field(Strings.Play_RemoteAddName, name);
            var addressBox = Field(Strings.Play_RemoteAddAddress, address);
            addressBox.Margin = new Thickness(0, 8, 0, 0);
            nameBox.Loaded += (_, _) => nameBox.Focus();
            var invalid = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Text = Strings.Play_RemoteAddInvalid,
                Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("SystemFillColorCautionBrush"),
                Visibility = wrong ? Visibility.Visible : Visibility.Collapsed,
            };

            var answer = SteamDialog.Show(
                Strings.Play_RemoteAddTitle,
                new StackPanel { Width = 360, Children = { nameBox, addressBox, invalid } },
                new SteamDialogChoice(Strings.Common_Save, SteamDialogButton.Green, IsDefault: true),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
            if (answer != 0) return null;

            name = nameBox.Text.Trim();
            address = addressBox.Text.Trim();
            if (SptLauncherApi.TryNormalizeAddress(address, out var normal) && !SptLauncherApi.IsLocalAddress(normal))
                return new PlayServerEntry { Name = name.Length > 0 ? name : normal, Address = normal };

            wrong = true;
        }
    }

    private static TextBox Field(string hint, string text) => new()
    {
        Style = (Style)Application.Current.FindResource("SteamSearchField"),
        Tag = hint,
        Text = text,
        Height = 34,
        MaxLength = 200,
    };
}
