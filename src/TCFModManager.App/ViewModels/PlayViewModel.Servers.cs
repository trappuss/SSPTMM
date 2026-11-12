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
    }

    //
    // Fork (1.4.7): "Compare mods with this server" - the server mods the picked server has loaded,
    // as a collection (HostMods), and the Collections page's preview of what applying it would do.
    // Nothing is installed from here. When nothing would be, it only says so and keeps no collection.
    //
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
            var mods = await api.ModsAsync(ct);
            AppLog.Info("DirectLaunch", $"{mods.Count} server mod(s) loaded on {address}");

            if (mods.Count == 0)
            {
                PlayStatus = Text(Strings.Play_RemoteNoModsFormat, server.Title);
                return;
            }

            var preview = await AppServices.ModListWorkflow.FromServerAsync(
                Text(Strings.Play_RemoteModsListNameFormat, server.Title), address, mods);
            if (preview is null)
            {
                PlayStatus = AppMessages.NoSptInstallFolder;
                return;
            }

            var plan = preview.Plan;
            AppLog.Info("DirectLaunch", $"against {address}: {plan.Install.Count()} to install, {plan.Update.Count()} to update, "
                + $"{plan.Enable.Count()} to turn on, {plan.Manual.Count()} not on the catalog, {plan.Keep.Count()} the same");

            if (plan.IsNoOp)
            {
                var byHand = plan.Manual.Select(a => a.Name).ToList();
                PlayStatus = byHand.Count == 0
                    ? Text(Strings.Play_RemoteModsMatchFormat, server.Title, mods.Count)
                    : Text(Strings.Play_RemoteModsManualFormat, server.Title, TextLists.Join(byHand));
                return;
            }

            AppServices.ModLists.Add(preview.List);
            AppNavigation.ReviewList(preview.List.Id);
        });
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
