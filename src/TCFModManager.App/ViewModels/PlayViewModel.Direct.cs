using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

/// <summary>One profile in the Play card's picker.</summary>
public sealed class PlayProfileItem(SptMiniProfile profile)
{
    public SptMiniProfile Profile { get; } = profile;

    public string Title => Profile.Nickname.Length > 0 ? Profile.Nickname : Profile.Username;

    public string Detail => Profile.Wipe || Profile.Nickname.Length == 0
        ? LocalizationService.Text(Strings.Play_ProfileNewFormat, Profile.Username, Profile.Edition)
        : LocalizationService.Text(Strings.Play_ProfileDetailFormat, Profile.Level, Profile.Side.ToUpperInvariant(), Profile.Edition);

    // For screen readers and anything that shows the item as text.
    public override string ToString() => Title;

    // Same profile, same details: the picker is left alone (no flicker, no lost selection).
    public bool SameAs(PlayProfileItem other) => Profile == other.Profile;
}

//
// Fork (SSPTMM): Play on the launcher's card - the server, the profile, SPT's
// launcher steps and the game, with SPT's launcher never opened (SptDirectLaunch). Switched on in
// Options; on an SPT version it was not made for the card is the launcher's, as before.
//
public partial class PlayViewModel
{
    public ObservableCollection<PlayProfileItem> Profiles { get; } = [];

    public bool HasNoProfiles => IsDirectLaunchOn && Profiles.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(WipeWarning))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private PlayProfileItem? _selectedProfile;

    // The direct start is on, and this SPT version takes it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LauncherCardTitle))]
    [NotifyPropertyChangedFor(nameof(LauncherCardSummary))]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyPropertyChangedFor(nameof(HasNoProfiles))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    private bool _isDirectLaunchOn;

    // On in Options, but not for this version: said on the launcher's card.
    [ObservableProperty]
    private string? _directLaunchUnavailable;

    // A later 4.1 than the ones read: allowed, and said.
    [ObservableProperty]
    private string? _directLaunchUntested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelPlayCommand))]
    private bool _isPlaying;

    [ObservableProperty]
    private string? _playStatus;

    [ObservableProperty]
    private bool _playStatusIsError;

    // 0-100 while bundles download; null shows the bar running.
    [ObservableProperty]
    private double? _playProgress;

    // The next Play wipes the profile first (a new character, from the edition's start).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WipeWarning))]
    private bool _wipeNextStart;

    public string? WipeWarning => WipeNextStart && SelectedProfile is { } p
        ? LocalizationService.Text(Strings.Play_WipeArmedFormat, p.Title)
        : null;

    public string LauncherCardTitle => IsDirectLaunchOn ? Strings.Play_DirectHeader : Strings.Play_LauncherHeader;

    public string LauncherCardSummary => IsDirectLaunchOn ? Strings.Play_DirectSummary : Strings.Play_LauncherSummary;

    public bool CanPlay => IsDirectLaunchOn && !IsPlaying && SelectedProfile is not null && !IsGameRunning && Server?.Exists == true;

    private CancellationTokenSource? _playCts;
    private bool _readingProfile;
    private WindowState? _stateBeforeGame;

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPlay));
        PlayCommand.NotifyCanExecuteChanged();
        if (value || IsPlaying) return;

        // The game has gone: "Playing as ..." no longer holds, and the window comes back if Play hid it.
        if (!PlayStatusIsError) PlayStatus = null;
        if (_stateBeforeGame is { } before && Application.Current?.MainWindow is { WindowState: WindowState.Minimized } window)
            window.WindowState = before;
        _stateBeforeGame = null;
    }

    partial void OnSelectedProfileChanged(PlayProfileItem? value)
    {
        WipeNextStart = false;
        ConfirmingDeleteProfile = false;
        if (_readingProfile || value is null) return;

        var settings = new SettingsService().Load();
        if (settings.DirectLaunchProfileId == value.Profile.ProfileId) return;
        settings.DirectLaunchProfileId = value.Profile.ProfileId;
        new SettingsService().Save(settings);
    }

    // From Refresh: whether the card is the direct one, and the profiles on disk.
    private void RefreshDirect(string? installPath, AppSettings settings)
    {
        var support = SptDirectLaunch.Support(AppServices.SptEnvironment.InstalledVersion);
        IsDirectLaunchOn = settings.DirectLaunch && support != DirectLaunchSupport.Unsupported && Server?.Exists == true;

        // Said only for an install whose SPT version is known and is not one this can start. The
        // setting is on by default now, so this is no longer somebody's own choice not working:
        // with no install, or no server found, the page already says that, and this said
        // "SPT ?" beside it.
        DirectLaunchUnavailable = settings.DirectLaunch
            && support == DirectLaunchSupport.Unsupported
            && Server?.Exists == true
            && AppServices.SptEnvironment.InstalledVersion is { Length: > 0 } installedVersion
                ? Text(Strings.Play_DirectUnavailableFormat, installedVersion)
                : null;
        DirectLaunchUntested = IsDirectLaunchOn && support == DirectLaunchSupport.NewerUntested
            ? Text(Strings.Play_DirectUntestedFormat, AppServices.SptEnvironment.InstalledVersion, SptDirectLaunch.TestedUpTo)
            : null;

        if (!IsDirectLaunchOn) return;

        var found = SptLocalProfiles.Read(installPath).Select(p => new PlayProfileItem(p)).ToList();
        if (found.Count == Profiles.Count && found.Zip(Profiles).All(pair => pair.First.SameAs(pair.Second))) return;

        var keep = SelectedProfile?.Profile.ProfileId ?? settings.DirectLaunchProfileId ?? LauncherPreferredProfile(installPath);

        _readingProfile = true;
        try
        {
            Profiles.Clear();
            foreach (var item in found) Profiles.Add(item);
            SelectedProfile = Profiles.FirstOrDefault(p => p.Profile.ProfileId == keep) ?? Profiles.FirstOrDefault();
        }
        finally
        {
            _readingProfile = false;
            OnPropertyChanged(nameof(HasNoProfiles));
        }
    }

    private static string? LauncherPreferredProfile(string? installPath) =>
        ServerRootOf(installPath) is { } root ? SptLauncherSettings.Read(root).PreferredProfileId : null;

    private static string? ServerRootOf(string? installPath) =>
        SptInstallationService.TryGetServerRoot(installPath, out var relative)
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(installPath!, relative))
            : null;

    // ---- Play ----------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (SelectedProfile is not { } item) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        var gameRoot = SptInstallationService.ToGameRoot(installPath);
        if (gameRoot is null || ServerRootOf(installPath) is not { } serverRoot || Server?.ExePath is not { } serverExe) return;

        _playCts?.Dispose();
        var cts = _playCts = new CancellationTokenSource();
        IsPlaying = true;
        PlayStatusIsError = false;
        PlayProgress = null;
        var gameStarted = false;
        ConfirmingDeleteProfile = false;
        ShowNewProfile = false;

        try
        {
            using var api = new SptLauncherApi(SptDirectLaunch.ServerAddress(serverExe));

            if (!await EnsureServerAsync(api, installPath, cts.Token)) return;

            var wipe = WipeNextStart;
            if (wipe && !AppServices.ProfileBackups.EnsureBackupBefore(installPath!, ProfileBackups.BeforeWipe))
            {
                Fail(Strings.Play_DirectBackupFailed);
                return;
            }

            var launcherSettings = SptLauncherSettings.Read(serverRoot);
            var launch = new SptDirectLaunch(gameRoot, serverRoot, api);
            var progress = new Progress<DirectLaunchProgress>(ShowProgress);

            var result = await launch.RunAsync(item.Profile, new DirectLaunchOptions
            {
                Wipe = wipe,
                ClearCache = launcherSettings.ClearCacheOnLaunch,
                ExcludeFromCleanup = launcherSettings.ExcludeFromCleanup,
                KeepGameLogsIn = LogFiles.KeptGameLogsFolder(gameRoot),
            }, progress, cts.Token);

            if (!result.Started)
            {
                Fail(DescribeProblem(result));
                return;
            }

            WipeNextStart = false;
            gameStarted = true;
            PlayProgress = null;
            PlayStatus = Text(Strings.Play_DirectStartingGameFormat, item.Title);

            // The game takes a few seconds to show up; watched for a minute and a half.
            var settings = new SettingsService().Load();
            for (var i = 0; i < 90 && !cts.IsCancellationRequested; i++)
            {
                if (SptLaunchService.IsGameRunning(installPath, GameCloser.SparesWindowless(settings, installPath))) break;
                await Task.Delay(1000, cts.Token);
            }

            Refresh();
            PlayStatus = IsGameRunning ? Text(Strings.Play_DirectPlayingFormat, item.Title) : Strings.Play_DirectGameNotSeen;

            if (IsGameRunning && settings.MinimizeWhilePlaying && Application.Current?.MainWindow is { } window)
            {
                _stateBeforeGame = window.WindowState;
                window.WindowState = WindowState.Minimized;
            }
        }
        catch (OperationCanceledException)
        {
            // Once the game was started, Cancel only stops the watching for it.
            PlayStatus = gameStarted ? Text(Strings.Play_DirectStartingGameFormat, item.Title) : Strings.Play_DirectCancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            // Something on disk or in SPT's config SSPTMM couldn't use: said on the card, not as a crash.
            AppLog.Warn("DirectLaunch", $"Play stopped: {ex}");
            Fail(Text(Strings.Play_DirectErrorFormat, ex.Message));
        }
        finally
        {
            IsPlaying = false;
            PlayProgress = null;
            if (ReferenceEquals(_playCts, cts)) _playCts = null;
            cts.Dispose();
            Refresh();
        }
    }

    private bool CanCancelPlay() => IsPlaying;

    [RelayCommand(CanExecute = nameof(CanCancelPlay))]
    private void CancelPlay() => _playCts?.Cancel();

    private void Fail(string text)
    {
        PlayStatusIsError = true;
        PlayStatus = text;
        AppLog.Warn("DirectLaunch", text);
    }

    private void ShowProgress(DirectLaunchProgress p)
    {
        PlayProgress = null;
        PlayStatus = p.Stage switch
        {
            DirectLaunchStage.CheckingVersion => Strings.Play_DirectStageChecking,
            DirectLaunchStage.CleaningUp => Strings.Play_DirectStageCleaning,
            DirectLaunchStage.Wiping => Strings.Play_DirectStageWiping,
            DirectLaunchStage.ClearingCache => Strings.Play_DirectStageCache,
            DirectLaunchStage.Patching => Strings.Play_DirectStagePatching,
            DirectLaunchStage.Bundles => Text(Strings.Play_DirectStageBundlesFormat, p.Done, p.Total, p.Bytes / (1024d * 1024d), p.TotalBytes / (1024d * 1024d)),
            _ => Strings.Play_DirectStageStarting,
        };

        if (p.Stage == DirectLaunchStage.Bundles && p.TotalBytes > 0) PlayProgress = Math.Clamp(100d * p.Bytes / p.TotalBytes, 0, 100);
    }

    private static string DescribeProblem(DirectLaunchResult result) => result.Problem switch
    {
        DirectLaunchProblem.NoGameExe => Strings.Play_DirectNoGame,
        DirectLaunchProblem.ServerUnreachable => Text(Strings.Play_DirectServerUnreachableFormat, result.Error?.Message),
        DirectLaunchProblem.CoreDllMissing => Strings.Play_DirectNoCoreDll,
        DirectLaunchProblem.VersionMismatch => Text(Strings.Play_DirectVersionMismatchFormat, result.ServerVersion, result.DllVersion),
        DirectLaunchProblem.NoSuchProfile => Strings.Play_DirectNoSuchProfile,
        DirectLaunchProblem.WipeRefused => Strings.Play_DirectWipeRefused,
        DirectLaunchProblem.ProfileInvalid => Strings.Play_DirectProfileInvalid,
        DirectLaunchProblem.PatchFailed => Text(Strings.Play_DirectPatchFailedFormat, result.Detail, result.Error?.Message),
        DirectLaunchProblem.BundlesFailed when result.Detail?.Split('|', 2) is [var count, var name] =>
            Text(Strings.Play_DirectBundlesFailedFormat, count, name),
        DirectLaunchProblem.StartFailed => Text(Strings.Play_DirectStartFailedFormat, result.Error?.Message),
        _ => Strings.Play_DirectFailed,
    };

    //
    // The server answering the launcher routes - started first when it is not running, with or
    // without its window as Options has it, and waited for (a modded server takes a while to load).
    //
    private async Task<bool> EnsureServerAsync(SptLauncherApi api, string? installPath, CancellationToken ct)
    {
        if (await PingAsync(api, ct)) return true;

        if (!SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning)
        {
            PlayStatus = Strings.Play_DirectStartingServer;
            var hidden = new SettingsService().Load().HideServerWindow;
            var started = SptLaunchService.Launch(installPath, SptLaunchTarget.Server, HeadlessOverride(), hidden);
            Refresh();
            if (!started.Started)
            {
                Fail(SptLaunchProblems.Describe(started));
                return false;
            }
        }

        var since = DateTime.UtcNow;
        while (DateTime.UtcNow - since < TimeSpan.FromMinutes(3))
        {
            PlayStatus = Text(Strings.Play_DirectWaitingForServerFormat, (int)(DateTime.UtcNow - since).TotalSeconds);
            await Task.Delay(1000, ct);

            if (await PingAsync(api, ct)) return true;

            // The first seconds are given regardless: the new process may not be listed yet.
            if (DateTime.UtcNow - since > TimeSpan.FromSeconds(15)
                && !SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning)
            {
                Fail(Strings.Play_DirectServerStopped);
                ShowServerLog = true;
                return false;
            }
        }

        Fail(Text(Strings.Play_DirectServerTimeoutFormat, api.Address));
        ShowServerLog = true;
        return false;
    }

    private static async Task<bool> PingAsync(SptLauncherApi api, CancellationToken ct)
    {
        using var once = CancellationTokenSource.CreateLinkedTokenSource(ct);
        once.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            return await api.PingAsync(once.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ---- the card's menu -----------------------------------------------------------------------

    [RelayCommand]
    private void ToggleWipe()
    {
        if (SelectedProfile is null) return;
        WipeNextStart = !WipeNextStart;
    }

    [RelayCommand]
    private void ClearGameCacheNow()
    {
        if (ServerRootOf(AppServices.SptEnvironment.InstallPath) is not { } root) return;
        if (IsGameRunning)
        {
            Fail(Strings.Play_DirectCacheWhileRunning);
            return;
        }

        SptDirectLaunch.ClearCache(root);
        PlayStatusIsError = false;
        PlayStatus = Strings.Play_DirectCacheCleared;
    }

    // New profile: a name and an edition (the server's list), created on the server.
    [ObservableProperty]
    private bool _showNewProfile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private string _newProfileName = "";

    public ObservableCollection<KeyValuePair<string, string>> Editions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateProfileCommand))]
    private KeyValuePair<string, string>? _selectedEdition;

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (IsPlaying) return;
        ConfirmingDeleteProfile = false;

        await WithServerAsync(async (api, ct) =>
        {
            var types = await api.TypesAsync(ct);
            Editions.Clear();
            foreach (var type in types.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)) Editions.Add(type);
            SelectedEdition = Editions.Count == 0
                ? null
                : Editions.FirstOrDefault(e => e.Key.Equals("Standard", StringComparison.OrdinalIgnoreCase)) is { Key: not null } standard
                    ? standard
                    : Editions[0];
            NewProfileName = "";
            ShowNewProfile = true;
            PlayStatus = null;
        });
    }

    private bool CanCreateProfile() => !string.IsNullOrWhiteSpace(NewProfileName) && SelectedEdition is not null;

    [RelayCommand(CanExecute = nameof(CanCreateProfile))]
    private async Task CreateProfileAsync()
    {
        if (SelectedEdition is not { } edition) return;
        var name = NewProfileName.Trim();

        await WithServerAsync(async (api, ct) =>
        {
            if (!await api.RegisterAsync(name, edition.Key, ct))
            {
                Fail(Text(Strings.Play_DirectProfileNotCreatedFormat, name));
                return;
            }

            ShowNewProfile = false;
            Refresh();
            var made = SptLocalProfiles.Read(AppServices.SptEnvironment.InstallPath).FirstOrDefault(p => p.Username == name);
            if (made is not null) SelectedProfile = Profiles.FirstOrDefault(p => p.Profile.ProfileId == made.ProfileId) ?? SelectedProfile;
            PlayStatusIsError = false;
            PlayStatus = Text(Strings.Play_DirectProfileCreatedFormat, name);
        });
    }

    [RelayCommand]
    private void CancelNewProfile() => ShowNewProfile = false;

    // Delete: asked on the card, a copy of the profiles taken first.
    [ObservableProperty]
    private bool _confirmingDeleteProfile;

    public string? DeleteProfileWarning => SelectedProfile is { } p ? Text(Strings.Play_DirectDeleteWarningFormat, p.Title) : null;

    [RelayCommand]
    private void AskDeleteProfile()
    {
        if (SelectedProfile is null || IsPlaying) return;
        ShowNewProfile = false;
        PlayStatus = null;
        OnPropertyChanged(nameof(DeleteProfileWarning));
        ConfirmingDeleteProfile = true;
    }

    [RelayCommand]
    private void CancelDeleteProfile() => ConfirmingDeleteProfile = false;

    [RelayCommand]
    private async Task ConfirmDeleteProfileAsync()
    {
        if (!ConfirmingDeleteProfile || SelectedProfile is not { } item) return;
        ConfirmingDeleteProfile = false;

        if (IsGameRunning)
        {
            Fail(Strings.Play_DirectDeleteWhileRunning);
            return;
        }

        await WithServerAsync(async (api, ct) =>
        {
            if (!AppServices.ProfileBackups.EnsureBackupBefore(AppServices.SptEnvironment.InstallPath!, ProfileBackups.BeforeProfileDelete))
            {
                Fail(Strings.Play_DirectBackupFailed);
                return;
            }

            if (!await api.RemoveAsync(item.Profile.Username, ct))
            {
                Fail(Text(Strings.Play_DirectProfileNotDeletedFormat, item.Title));
                return;
            }

            AppLog.Info("DirectLaunch", $"profile {item.Profile.Username} deleted (a copy was kept)");
            Refresh();
            PlayStatusIsError = false;
            PlayStatus = Text(Strings.Play_DirectProfileDeletedFormat, item.Title);
        });
    }

    // Runs an action against the server, starting it first when it is down.
    private async Task WithServerAsync(Func<SptLauncherApi, CancellationToken, Task> action)
    {
        if (Server?.ExePath is not { } exe) return;

        IsPlaying = true;
        PlayStatusIsError = false;
        var cts = _playCts = new CancellationTokenSource();
        try
        {
            using var api = new SptLauncherApi(SptDirectLaunch.ServerAddress(exe));
            if (!await EnsureServerAsync(api, AppServices.SptEnvironment.InstallPath, cts.Token)) return;
            PlayStatus = null;
            await action(api, cts.Token);
        }
        catch (OperationCanceledException)
        {
            PlayStatus = Strings.Play_DirectCancelled;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            Fail(Text(Strings.Play_DirectServerUnreachableFormat, ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or UriFormatException)
        {
            AppLog.Warn("DirectLaunch", $"profile action stopped: {ex}");
            Fail(Text(Strings.Play_DirectErrorFormat, ex.Message));
        }
        finally
        {
            IsPlaying = false;
            if (ReferenceEquals(_playCts, cts)) _playCts = null;
            cts.Dispose();
        }
    }
}
