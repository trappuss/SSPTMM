using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// Fork (1.4.3, experimental): Options > Play with Fika. On - or Automatic, with Fika found in the
// install (FikaInstall) - the Play card offers other PCs' servers (PlayViewModel.Servers), and:
//   - a Fika host is told what to give friends: this PC's addresses with the port from fika.jsonc,
//     which Fika listens on in place of http.json's, and the two ports Fika's wiki says to forward
//     (TCP for the server, UDP 25565 for whoever hosts the raid);
//   - a player on another PC's server is told when this PC's own server is running too: with Fika
//     only the host runs one, and Fika's FAQ names a stray one as a cause of friends not seeing
//     each other.
//
public partial class PlayViewModel
{
    [ObservableProperty]
    private bool _otherServersOn;

    [ObservableProperty]
    private string? _fikaHostNote;

    // The first of the addresses, for Copy address.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyFikaAddressCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyFikaInviteCommand))]
    private string? _fikaHostAddress;

    // Fika's settings say something that keeps friends out, or are not written yet.
    [ObservableProperty]
    private string? _fikaHostWarning;

    [ObservableProperty]
    private string? _fikaStrayServerNote;

    // Fork (1.4.7): Windows Firewall has no rule that lets friends in - FirewallRules.
    [ObservableProperty]
    private string? _fikaFirewallNote;

    // Read off the UI thread, and again after a minute (HUNCH): a rule Windows has just asked
    // about shows soon enough, and reading every rule is not something to do every two seconds.
    private (string Key, DateTime At)? _firewallRead;

    // HUNCH: Fika's default for the raid; the game's own setting is not read.
    private const int FikaRaidPort = 25565;

    private void RefreshFirewall(string serverExe, string? port, string? installPath)
    {
        if (!int.TryParse(port, out var serverPort))
        {
            FikaFirewallNote = null;
            return;
        }

        var key = serverExe + "|" + serverPort;
        if (_firewallRead is { } read && read.Key == key && DateTime.UtcNow - read.At < TimeSpan.FromMinutes(1)) return;
        _firewallRead = (key, DateTime.UtcNow);

        var game = SptInstallationService.ToGameRoot(installPath) is { Length: > 0 } root
            ? System.IO.Path.Combine(root, "EscapeFromTarkov.exe")
            : null;

        _ = Task.Run(() =>
        {
            var state = FirewallRules.Read();
            return (
                Server: FirewallRules.Reaches(state, serverExe, FirewallRules.Tcp, serverPort),
                Raid: game is null ? null : FirewallRules.Reaches(state, game, FirewallRules.Udp, FikaRaidPort));
        }).ContinueWith(done =>
        {
            if (done.IsFaulted || _firewallRead?.Key != key) return;

            var (server, raid) = done.Result;
            AppLog.Info("Fika", $"Windows Firewall lets in: server TCP {serverPort} {server?.ToString() ?? "?"}, raid UDP {FikaRaidPort} {raid?.ToString() ?? "?"}");

            var notes = new[]
            {
                server == false ? Text(Strings.Play_FikaFirewallServerFormat, serverPort) : null,
                raid == false ? Text(Strings.Play_FikaFirewallRaidFormat, FikaRaidPort) : null,
            }.Where(note => note is not null).ToList();
            FikaFirewallNote = notes.Count > 0 ? string.Join("\n", notes) : null;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private FikaInstall.Found _fikaFound;
    private string? _fikaFoundFor;
    private bool _lookingForFika;

    // This PC's addresses and Fika's settings, read again every so often rather than every 2s poll.
    // 15s is HUNCH: a changed fika.jsonc needs a server restart anyway.
    private (string? Root, DateTime At, IReadOnlyList<string> Addresses, FikaServerConfig? Config)? _fikaHostRead;

    // From RefreshDirect, before the servers are listed: whether they are offered at all.
    private void RefreshFika(string? installPath, AppSettings settings)
    {
        if (FikaInstall.Known(installPath) is { } known)
        {
            _fikaFound = known;
            _fikaFoundFor = installPath;
        }
        else
        {
            // Kept while it is read again, so a picked server doesn't drop out every few minutes.
            if (!string.Equals(_fikaFoundFor, installPath, StringComparison.OrdinalIgnoreCase)) _fikaFound = default;
            if (!string.IsNullOrWhiteSpace(installPath) && !_lookingForFika) _ = LookForFikaAsync(installPath);
        }

        OtherServersOn = settings.FikaOptions ?? _fikaFound.Any;
    }

    private async Task LookForFikaAsync(string installPath)
    {
        _lookingForFika = true;
        try
        {
            var found = await FikaInstall.ReadAsync(installPath);
            var changed = found != _fikaFound || !string.Equals(_fikaFoundFor, installPath, StringComparison.OrdinalIgnoreCase);
            _fikaFound = found;
            _fikaFoundFor = installPath;
            if (!changed) return;

            AppLog.Info("Fika", $"in the install: plugin {(found.Client ? "yes" : "no")}, server mod {(found.Server ? "yes" : "no")}");
            Refresh();
        }
        finally
        {
            _lookingForFika = false;
        }
    }

    // From RefreshDirect, once the server picked is known.
    private void RefreshFikaNotes(string? installPath)
    {
        FikaStrayServerNote = OtherServersOn && IsDirectLaunchOn && IsRemoteServer && IsServerRunning ? Strings.Play_FikaStrayServer : null;

        if (!OtherServersOn || !IsDirectLaunchOn || IsRemoteServer || !_fikaFound.Server || Server?.ExePath is not { } exe)
        {
            FikaHostNote = null;
            FikaHostAddress = null;
            FikaHostWarning = null;
            FikaFirewallNote = null;
            _firewallRead = null;
            return;
        }

        var root = ServerRootOf(installPath);
        if (_fikaHostRead is not { } read || read.Root != root || DateTime.UtcNow - read.At > TimeSpan.FromSeconds(15))
        {
            read = (root, DateTime.UtcNow, LocalNetworkAddresses.Find(), FikaServerConfig.Read(root));
            _fikaHostRead = read;
        }

        if (read.Config is not { } config)
        {
            FikaHostNote = null;
            FikaHostAddress = null;
            FikaHostWarning = FikaServerConfig.Find(root) is null ? Strings.Play_FikaHostNotWritten : null;
            return;
        }

        var port = config.Port?.ToString() ?? SptDirectLaunch.ServerAddress(exe).Split(':')[^1];
        var addresses = read.Addresses.Select(a => a + ":" + port).ToList();
        FikaHostAddress = addresses.FirstOrDefault();
        _fikaHostAddresses = addresses;
        _fikaHostPort = port;
        FikaHostNote = addresses.Count == 0
            ? Text(Strings.Play_FikaHostNoAddressFormat, port)
            : Text(Strings.Play_FikaHostFormat, TextLists.Join(addresses), port);
        FikaHostWarning = config.OnlyThisPc ? Text(Strings.Play_FikaHostOnlyThisPcFormat, config.Ip) : null;

        // Not worth saying while nobody can reach it anyway.
        if (config.OnlyThisPc) FikaFirewallNote = null;
        else RefreshFirewall(exe, port, installPath);
    }

    [RelayCommand(CanExecute = nameof(CanCopyFikaAddress))]
    private void CopyFikaAddress()
    {
        if (FikaHostAddress is not { } address) return;

        try
        {
            Clipboard.SetText(address);
            PlayStatusIsError = false;
            PlayStatus = Text(Strings.Play_FikaHostCopiedFormat, address);
        }
        catch (COMException ex)
        {
            AppLog.Warn("Fika", $"couldn't copy the address: {ex.Message}");
        }
    }

    private bool CanCopyFikaAddress() => FikaHostAddress is not null;

    // Fork (1.4.7): everything a friend needs, in one message - the addresses, the versions that
    // have to match, and how SSPTMM gets them the same mods (Compare mods with this server).
    private IReadOnlyList<string> _fikaHostAddresses = [];
    private string? _fikaHostPort;

    [RelayCommand(CanExecute = nameof(CanCopyFikaAddress))]
    private async Task CopyFikaInviteAsync()
    {
        if (_fikaHostAddresses.Count == 0 || AppServices.SptEnvironment.InstallPath is not { Length: > 0 } install) return;

        var fika = await Task.Run(() => InstalledModScanner.Scan(install)
            .Where(m => m.Target == InstalledModTarget.Server && !m.IsDisabled)
            .FirstOrDefault(m => m.Name.StartsWith("fika", StringComparison.OrdinalIgnoreCase)
                || m.AllGuids.Any(g => g.Contains("fika", StringComparison.OrdinalIgnoreCase)))?.Version);

        var invite = FikaInvite(_fikaHostAddresses, _fikaHostPort, AppServices.SptEnvironment.InstalledVersion, fika);

        try
        {
            Clipboard.SetText(invite);
            PlayStatusIsError = false;
            PlayStatus = Strings.Play_FikaInviteCopied;
        }
        catch (COMException ex)
        {
            AppLog.Warn("Fika", $"couldn't copy the invite: {ex.Message}");
        }
    }

    internal static string FikaInvite(IReadOnlyList<string> addresses, string? port, string? spt, string? fika)
    {
        var lines = new List<string>
        {
            Strings.Play_FikaInviteIntro,
            Text(Strings.Play_FikaInviteAddressFormat, TextLists.Join([.. addresses.Select(Labelled)])),
        };

        if (port is not null) lines.Add(Text(Strings.Play_FikaInviteOutsideFormat, port));
        if (spt is { Length: > 0 })
            lines.Add(fika is { Length: > 0 } ? Text(Strings.Play_FikaInviteVersionsFormat, spt, fika) : Text(Strings.Play_FikaInviteSptFormat, spt));
        lines.Add(Text(Strings.Play_FikaInviteHowFormat, Strings.Play_InviteJoin.TrimEnd('.', '\u2026')));

        return string.Join(Environment.NewLine, lines);
    }

    // "192.168.1.5:6969 (same network)", "100.72.1.9:6969 (Tailscale)" - which one a friend can use.
    private static string Labelled(string addressAndPort)
    {
        var address = addressAndPort.Split(':')[0];
        if (LocalNetworkAddresses.IsHomeNetwork(address)) return Text(Strings.Play_FikaAddressHomeFormat, addressAndPort);
        return LocalNetworkAddresses.IsTailscale(address) ? Text(Strings.Play_FikaAddressTailscaleFormat, addressAndPort) : addressAndPort;
    }
}
