using System.Net.Http;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

using Wpf.Ui.Controls;

namespace TCFModManager.App.ViewModels;

//
// App-lifetime state for "is there a newer version of this app on sp-mod.com, and does the user
// want it". Backs both the banner in MainWindow and the App update page.
//
// The whole feature runs through sp-mod.com's public API and sp-mod.com's own download link for
// this app's listing - the same file, from the same place, as clicking Download on the mod page.
// It doesn't bypass the mod page either: the existing ReadModPageConfirmationWindow gate applies
// here exactly as it does to installing any other mod, so the page is always opened first.
//
public partial class AppUpdateViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly AppUpdateService _updates = new(AppServices.SpModApi);
    private readonly AppUpdateInstaller _installer = new(AppServices.Downloads);
    private readonly SettingsService _settings = new();

    // Fork: SSPTMM's own releases, on GitHub - see GitHubReleaseCheck. Tells, never installs.
    private readonly GitHubReleaseCheck _github = new();

    private CancellationTokenSource? _installCts;

    public string CurrentVersion => AppVersion.Current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateAvailable))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateBadge))]
    [NotifyPropertyChangedFor(nameof(ShowUpToDate))]
    [NotifyPropertyChangedFor(nameof(LatestVersion))]
    [NotifyPropertyChangedFor(nameof(Changelog))]
    [NotifyPropertyChangedFor(nameof(ChangeTitle))]
    [NotifyPropertyChangedFor(nameof(ChangeSummary))]
    [NotifyPropertyChangedFor(nameof(BannerTitle))]
    [NotifyPropertyChangedFor(nameof(BannerSeverity))]
    [NotifyPropertyChangedFor(nameof(BadgeSeverity))]
    [NotifyPropertyChangedFor(nameof(DownloadSizeText))]
    [NotifyPropertyChangedFor(nameof(PublishedText))]
    [NotifyPropertyChangedFor(nameof(ReleaseTitle))]
    [NotifyPropertyChangedFor(nameof(ReleaseDetails))]
    [NotifyPropertyChangedFor(nameof(ShowOriginalUpdateCard))]
    [NotifyPropertyChangedFor(nameof(ShowNewestRelease))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenModPageCommand))]
    private AppUpdateInfo? _update;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isChecking;

    // True once a check has run, so the page can tell "not checked yet" apart from "nothing new".
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpToDate))]
    [NotifyPropertyChangedFor(nameof(ShowNewestRelease))]
    private bool _hasChecked;

    // Why the last check couldn't complete. Null when it did.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpToDate))]
    [NotifyPropertyChangedFor(nameof(ShowNewestRelease))]
    private string? _checkError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelInstallCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _installProgress;

    [ObservableProperty]
    private string? _installStatus;

    [ObservableProperty]
    private string? _installError;

    // Drives the banner in MainWindow. Two-way bound to the InfoBar, so the user closing it is a
    // dismissal - see OnIsBannerOpenChanged.
    [ObservableProperty]
    private bool _isBannerOpen;

    public bool UpdateAvailable => Update?.IsUpdate == true;

    // Deliberately requires a completed, successful check: a page that hasn't looked yet, or whose
    // look failed, must not claim the app is up to date.
    public bool ShowUpToDate => HasChecked && CheckError is null && !UpdateAvailable;

    public string? LatestVersion => Update?.LatestVersion;

    public string? Changelog => Update?.Changelog;

    // ---- What kind of update this is ----------------------------------------------------------
    //
    // Which of the three numbers moved is the most useful thing to tell someone deciding whether to
    // update now or later, so it's stated plainly rather than left for them to infer from the
    // version string.

    public string ChangeTitle => Update?.ChangeKind switch
    {
        VersionChangeKind.Patch => Strings.AppUpdate_ChangePatch,
        VersionChangeKind.Minor => Strings.AppUpdate_ChangeMinor,
        VersionChangeKind.Major => Strings.AppUpdate_ChangeMajor,
        _ => Strings.AppUpdate_ChangeUnknown,
    };

    public string ChangeSummary => Update is null
        ? string.Empty
        : Update.ChangeKind switch
        {
            VersionChangeKind.Patch =>
                Text(Strings.AppUpdate_PatchBodyFormat, Update.LatestVersion),

            VersionChangeKind.Minor =>
                Text(Strings.AppUpdate_MinorBodyFormat, Update.LatestVersion),

            VersionChangeKind.Major =>
                Text(Strings.AppUpdate_MajorBodyFormat, Update.LatestVersion),

            _ =>
                Text(
                    Strings.AppUpdate_UnknownBodyFormat,
                    Update.LatestVersion,
                    Update.CurrentVersion),
        };

    public InfoBarSeverity BannerSeverity => Update?.ChangeKind switch
    {
        VersionChangeKind.Minor => InfoBarSeverity.Success,
        VersionChangeKind.Major => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational,
    };

    // The app's own update decides the colour when there is one; a Server Map mod that is behind
    // on its own is Caution - something to act on, on another machine or by hand.
    public InfoBadgeSeverity BadgeSeverity => !UpdateAvailable && ServerMapModBehind
        ? InfoBadgeSeverity.Caution
        : Update?.ChangeKind switch
        {
            VersionChangeKind.Minor => InfoBadgeSeverity.Success,
            VersionChangeKind.Major => InfoBadgeSeverity.Attention,
            _ => InfoBadgeSeverity.Informational,
        };

    public string BannerTitle => Text(Strings.AppUpdate_BannerTitleFormat, ChangeTitle, LatestVersion);

    public string? DownloadSizeText => Update?.DownloadSizeBytes is > 0
        ? Text(Strings.AppUpdate_DownloadSizeFormat, Update.DownloadSizeBytes.Value / (1024d * 1024d))
        : null;

    public string? PublishedText => Update?.PublishedAt is { } published
        ? Text(Strings.AppUpdate_PublishedFormat, published.ToLocalTime())
        : null;

    // ---- Checking ------------------------------------------------------------------------------

    //
    // The check MainWindow fires once on launch. Anything that goes wrong is logged and shown on
    // the About page rather than interrupting startup - not being able to reach GitHub is not a
    // reason to put a dialog in front of someone who just opened the app. The one dialog it can
    // show is the one-time question below, asked before anything is sent.
    //
    public async Task CheckOnStartupAsync()
    {
        // Lets the catalog fetch get its first requests away before adding two more. sp-mod.com
        // rate limits at the edge, and the catalog is what the user is actually waiting to see.
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(true);

        // Fork (1.2.0): only with the user's yes - asked once (SPT's mod site requires consent for
        // update checks). Check now on About is the user asking, so it always runs.
        if (!SelfMod.ChecksOriginalUpdates && !StartupCheckAllowed()) return;

        await RunCheckAsync(announce: true).ConfigureAwait(true);
    }

    // ---- Fork (1.2.0): asking before checking ---------------------------------------------------

    [ObservableProperty]
    private bool _checkForNewReleases = new SettingsService().Load().CheckForNewReleases == true;

    private bool _readingCheckSetting;

    partial void OnCheckForNewReleasesChanged(bool value)
    {
        if (_readingCheckSetting) return;

        var service = new SettingsService();
        var settings = service.Load();
        settings.CheckForNewReleases = value;
        service.Save(settings);
        AppLog.Info("AppUpdate", value ? "checks for new releases at start: on" : "checks for new releases at start: off");
    }

    //
    // The saved answer, asking for one the first time. Shown over the window, which is up by now.
    // Closing the question without answering (X, Esc) saves nothing and doesn't check: it is asked
    // again next start. The answer is written over a fresh load of the settings, not the copy read
    // before the dialog - other code (the Server Map's pin, its client id) can save while it is open.
    //
    private bool StartupCheckAllowed()
    {
        var service = new SettingsService();
        var settings = service.Load();

        if (settings.CheckForNewReleases is null)
        {
            var body = new System.Windows.Controls.TextBlock
            {
                Text = Strings.AppUpdate_ConsentBody,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                MaxWidth = 560,
            };

            var answer = SteamDialog.Show(
                Strings.AppUpdate_ConsentTitle,
                body,
                new SteamDialogChoice(Strings.AppUpdate_ConsentYes, SteamDialogButton.Green, IsDefault: true),
                new SteamDialogChoice(Strings.AppUpdate_ConsentNo, SteamDialogButton.Grey));

            if (answer < 0)
            {
                AppLog.Info("AppUpdate", "asked about checking for new releases at start: closed without an answer, asking again next start");
                return false;
            }

            settings = service.Load();
            settings.CheckForNewReleases = answer == 0;
            service.Save(settings);
            AppLog.Info("AppUpdate", $"asked about checking for new releases at start: {(answer == 0 ? "yes" : "no")}");
        }

        _readingCheckSetting = true;
        CheckForNewReleases = settings.CheckForNewReleases == true;
        _readingCheckSetting = false;

        return CheckForNewReleases;
    }

    private bool CanCheckForUpdates() => !IsChecking && !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync() => RunCheckAsync(announce: false);

    //
    // <param name="announce">Whether a found update should raise the banner. True for the automatic
    // startup check; false for the user pressing "Check now", who is already looking at the page and
    // doesn't need a banner over the top of it.</param>
    //
    private async Task RunCheckAsync(bool announce)
    {
        // SSPTMM: the original app's listing is not asked about at all - its releases are merged in,
        // never installed or announced here. SSPTMM's own releases are asked of GitHub instead, and
        // only ever shown: the dot beside Help and About, and the About page. No banner - a newer
        // release is something to pick up when convenient, not to interrupt with.
        if (!SelfMod.ChecksOriginalUpdates)
        {
            // Check now pressed during the startup check's delay: one check at a time.
            if (IsChecking) return;

            IsChecking = true;
            try
            {
                await CheckGitHubAsync().ConfigureAwait(true);
                await CheckServerMapModAsync().ConfigureAwait(true);
            }
            finally
            {
                IsChecking = false;
            }

            return;
        }

        IsChecking = true;
        CheckError = null;

        try
        {
            Update = await _updates.CheckAsync().ConfigureAwait(true);
            HasChecked = true;

            // A version the user has already closed the banner on stays closed until something
            // newer than it is published.
            var dismissed = _settings.Load().DismissedAppUpdateVersion;
            if (UpdateAvailable && announce
                && !string.Equals(dismissed, Update!.LatestVersion, StringComparison.OrdinalIgnoreCase))
                IsBannerOpen = true;
        }
        catch (SpModApiRateLimitedException)
        {
            CheckError = Strings.AppUpdate_RateLimited;
        }
        catch (SpModApiException ex)
        {
            CheckError = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            CheckError = Text(Strings.AppUpdate_UnreachableFormat, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces a request timeout as this rather than HttpRequestException.
            CheckError = Strings.AppUpdate_TimedOut;
        }
        catch (Exception ex)
        {
            CheckError = Text(Strings.AppUpdate_CheckUnexpectedFormat, ex.Message);
            AppLog.Error("AppUpdate", "update check failed", ex);
        }
        finally
        {
            IsChecking = false;
            if (CheckError is not null) AppLog.Warn("AppUpdate", CheckError);
        }

        await CheckServerMapModAsync().ConfigureAwait(true);
    }

    // Fork: the newest SSPTMM release on GitHub. A failure is said on the About page and logged,
    // never raised - not reaching GitHub is no reason to bother someone who just opened the app.
    private async Task CheckGitHubAsync()
    {
        CheckError = null;

        try
        {
            Update = await _github.CheckAsync(AppVersion.Current).ConfigureAwait(true);
            HasChecked = true;
        }
        catch (GitHubReleaseCheckException ex)
        {
            CheckError = ex.RateLimited
                ? Strings.About_CheckRateLimited
                : Text(Strings.About_CheckRefusedFormat, (int)ex.Status);
        }
        catch (HttpRequestException ex)
        {
            CheckError = Text(Strings.About_CheckUnreachableFormat, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its own timeout as this rather than HttpRequestException.
            CheckError = Strings.About_CheckTimedOut;
        }
        catch (Exception ex)
        {
            CheckError = Text(Strings.AppUpdate_CheckUnexpectedFormat, ex.Message);
            AppLog.Error("AppUpdate", "GitHub release check failed", ex);
        }
        finally
        {
            if (CheckError is not null) AppLog.Warn("AppUpdate", CheckError);
        }
    }

    // Fork: said only when a release was found and compared - not after a 404 (no release, or the
    // repository gone), a tag that isn't a version, or a running version that won't parse.
    public bool ShowNewestRelease => HasChecked && CheckError is null && Update?.ChangeKind == VersionChangeKind.None;

    // "Feature update: SSPTMM 1.1.0 is out".
    public string ReleaseTitle => Text(Strings.About_UpdateTitleFormat, ChangeTitle, LatestVersion);

    // "Published 3 October 2026 · 64 MB download", whichever of the two GitHub gave.
    public string? ReleaseDetails =>
        string.Join("  ·  ", new[] { PublishedText, DownloadSizeText }.Where(t => t is not null)) is { Length: > 0 } line
            ? line
            : null;

    // The original's update card - version guide, its mod page, Download and install. SSPTMM shows
    // its own lines in the About card instead; the What's new card below is shared.
    public bool ShowOriginalUpdateCard => UpdateAvailable && !IsFork;

    // ---- Acting on it ---------------------------------------------------------------------------

    private bool CanOpenModPage() => !string.IsNullOrWhiteSpace(Update?.ModPageUrl);

    [RelayCommand(CanExecute = nameof(CanOpenModPage))]
    private void OpenModPage()
    {
        if (Update?.ModPageUrl is not { } url) return;

        // Through OpenUrl, so a PC with no browser set logs it rather than crashing.
        OpenUrl(url);
    }

    // Fork (1.3.0): SSPTMM's own release from GitHub (GitHubReleaseCheck gives its zip's link). The
    // original's sp-mod.com listing is never checked here, so Update is always SSPTMM's.
    private bool CanInstallUpdate() => Update?.CanInstall == true && !IsInstalling;

    public bool IsFork => SelfMod.IsFork;

    // ---- About (SSPTMM) -------------------------------------------------------------------------

    public string AppName => SelfMod.AppName;

    public string VersionLine => Text(Strings.About_VersionFormat, AppVersion.Current);

    public string BasedOnLine => Text(Strings.About_BasedOnFormat, SelfMod.OriginalAuthor);

    [RelayCommand]
    private static void OpenRepository() => OpenUrl(SelfMod.RepositoryUrl);

    [RelayCommand]
    private static void OpenOriginal() => OpenUrl(SelfMod.ModPageUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("About", $"couldn't open {url}: {ex.Message}");
        }
    }

    public bool CanInstallHere => !SelfMod.IsFork;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (Update is not { CanInstall: true } update) return;

        // Fork (1.3.0): asked every time, naming what is downloaded and from where - nothing is
        // fetched or changed before a yes. (The original went through its mod page here; SSPTMM's
        // release notes are already on this page.)
        var file = Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var link)
            ? Uri.UnescapeDataString(link.Segments[^1])
            : update.LatestVersion;
        var body = new System.Windows.Controls.TextBlock
        {
            Text = Text(Strings.About_InstallConfirmBodyFormat, file, DownloadSizeText ?? Strings.About_SizeUnknown),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            MaxWidth = 560,
        };
        var answer = SteamDialog.Show(
            Text(Strings.About_InstallConfirmTitle, update.LatestVersion),
            body,
            new SteamDialogChoice(Strings.About_InstallConfirmYes, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.About_InstallConfirmNo, SteamDialogButton.Grey));
        if (answer != 0)
        {
            AppLog.Info("AppUpdate", $"install of {update.LatestVersion} declined");
            return;
        }

        AppLog.Info("AppUpdate", $"installing {update.LatestVersion} from {update.DownloadUrl}");

        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();

        IsInstalling = true;
        InstallError = null;
        InstallProgress = 0;
        InstallStatus = Text(Strings.AppUpdate_DownloadingFormat, update.LatestVersion);

        try
        {
            var progress = new Progress<double>(fraction =>
            {
                InstallProgress = fraction * 100;
                InstallStatus = fraction < 0.85
                    ? Text(
                        Strings.AppUpdate_DownloadingProgressFormat,
                        update.LatestVersion,
                        fraction / 0.85)
                    : Strings.AppUpdate_Unpacking;
            });

            await _installer.PrepareAsync(update, progress, _installCts.Token).ConfigureAwait(true);

            InstallStatus = Strings.AppUpdate_Restarting;
            AppUpdateInstaller.LaunchApplyScript();

            // The script is already waiting on this process. Shutdown (rather than Environment.Exit)
            // so App.OnExit still runs and the log is flushed before the swap happens. Through the
            // tray's Quit, so the window closing isn't taken for a close to the tray.
            AppTray.Quit();
        }
        catch (OperationCanceledException)
        {
            AppUpdateInstaller.ClearWorkingFiles();
            InstallStatus = null;
            InstallError = Strings.AppUpdate_Cancelled;
        }
        catch (AppUpdateException ex)
        {
            AppUpdateInstaller.ClearWorkingFiles();
            InstallStatus = null;
            InstallError = AppUpdateProblems.Describe(ex);

            // The reason and the sentence both go in: the reason is what a log is searched for, the
            // sentence carries the numbers behind it.
            AppLog.Error("AppUpdate", $"update could not be applied ({ex.Reason}): {InstallError}", ex);
        }
        catch (Exception ex)
        {
            AppUpdateInstaller.ClearWorkingFiles();
            InstallStatus = null;
            InstallError = AppUpdateProblems.Unexpected(ex);
            AppLog.Error("AppUpdate", "update failed", ex);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private bool CanCancelInstall() => IsInstalling;

    [RelayCommand(CanExecute = nameof(CanCancelInstall))]
    private void CancelInstall() => _installCts?.Cancel();

    // Closing the banner is how a release gets skipped. Persisted, so a bug-fix update someone has
    // decided against doesn't reappear on every launch - anything published after it still will.
    partial void OnIsBannerOpenChanged(bool value)
    {
        if (value || Update?.LatestVersion is not { } version) return;

        var settings = _settings.Load();
        if (string.Equals(settings.DismissedAppUpdateVersion, version, StringComparison.OrdinalIgnoreCase)) return;

        settings.DismissedAppUpdateVersion = version;
        _settings.Save(settings);
        AppLog.Info("AppUpdate", $"banner dismissed for {version}");
    }
}
