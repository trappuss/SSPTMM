using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class OptionsViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Suppresses the save while the dropdown is being set to what is already stored, so opening the
    // page doesn't count as choosing a theme.
    private readonly bool _loaded;

    // Every write does its own Load first, so this never fights the other things that save settings.
    private readonly SettingsService _settings = new();

    // Guards the toggle being put back after the user declines the warning, so restoring it doesn't
    // run the warning a second time.
    private bool _revertingSkip;

    // Same job for the two install-role switches, which are written back after the setup prompt
    // answers them - without this, filling them in would count as the user flipping them.
    private bool _settingInstallRole;

    public SptEnvironmentViewModel SptEnvironment => AppServices.SptEnvironment;

    [ObservableProperty]
    private string? _installPathInput;

    public IReadOnlyList<ThemeOptionItem> ThemeOptions { get; } =
    [
        new(nameof(Strings.Options_ThemeFollowSystem), ThemePreference.FollowSystem),
        new(nameof(Strings.Options_ThemeLight), ThemePreference.Light),
        new(nameof(Strings.Options_ThemeDark), ThemePreference.Dark),
    ];

    // Applied and saved the moment it changes - there is nothing here to confirm, and watching the
    // theme change as you pick it is the point.
    [ObservableProperty]
    private ThemeOptionItem _selectedTheme;

    //
    // Every language this build has resources for, behind a System default entry that follows
    // Windows. One entry per language rather than a list of every language that exists: a name in
    // the list the app cannot actually read in is an offer it cannot keep.
    //
    public IReadOnlyList<LanguageOptionItem> LanguageOptions { get; } =
        [new(null), .. AppLanguage.Available.Select(c => new LanguageOptionItem(c))];

    // Applied and saved the moment it changes, the same as the theme dropdown.
    [ObservableProperty]
    private LanguageOptionItem _selectedLanguage;

    //
    // Who translated the language now being read. Empty in English, and the row is collapsed when
    // it is empty, so nothing appears until a translation carries a name.
    //
    // Read fresh rather than stored: LocalizedViewModel re-raises every property when the language
    // changes, so picking a language relabels this line along with the rest of the page.
    //
    public string TranslationCredit => Strings.Meta_TranslationCredit;

    // Turning this on is confirmed first - see the warning in OnSkipModPageConfirmationChanged.
    [ObservableProperty]
    private bool _skipModPageConfirmation;

    // The switch's own tooltip, and the install buttons' - one description of what the setting
    // currently means, shared rather than restated here.
    public ModPageGateViewModel ModPageGate => AppServices.ModPageGate;

    //
    // How long removed mods stay in the install's holding folder (R11, D27), and the button that
    // clears it now. The label carries the size held, so the button says what it would free.
    //
    public IReadOnlyList<RemovedModsRetentionItem> RemovedModsRetentionOptions { get; } =
    [
        new(nameof(Strings.Options_RemovedModsDeleteStraightAway), RemovedModsRetention.DeleteStraightAway),
        new(nameof(Strings.Options_RemovedModsOneDay), RemovedModsRetention.OneDay),
        new(nameof(Strings.Options_RemovedModsSevenDays), RemovedModsRetention.SevenDays),
        new(nameof(Strings.Options_RemovedModsFourteenDays), RemovedModsRetention.FourteenDays),
        new(nameof(Strings.Options_RemovedModsThirtyDays), RemovedModsRetention.ThirtyDays),
        new(nameof(Strings.Options_RemovedModsUntilCleared), RemovedModsRetention.UntilCleared),
    ];

    // The same words the dropdown uses, for the removal confirmation's "kept for 14 days".
    public static string RetentionLabel(RemovedModsRetention value) => value switch
    {
        RemovedModsRetention.DeleteStraightAway => Strings.Options_RemovedModsDeleteStraightAway,
        RemovedModsRetention.OneDay => Strings.Options_RemovedModsOneDay,
        RemovedModsRetention.SevenDays => Strings.Options_RemovedModsSevenDays,
        RemovedModsRetention.ThirtyDays => Strings.Options_RemovedModsThirtyDays,
        RemovedModsRetention.UntilCleared => Strings.Options_RemovedModsUntilCleared,
        _ => Strings.Options_RemovedModsFourteenDays,
    };

    [ObservableProperty]
    private RemovedModsRetentionItem _selectedRemovedModsRetention;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearRemovedModsCommand))]
    private long _removedModsBytes;

    [ObservableProperty]
    private string _removedModsStatus = string.Empty;

    public string ClearRemovedModsLabel => RemovedModsBytes > 0
        ? Text(Strings.Options_RemovedModsClearFormat, DownloadQueueItemViewModel.SizeLabel(RemovedModsBytes))
        : Strings.Options_RemovedModsNothingToClear;

    partial void OnRemovedModsBytesChanged(long value) => OnPropertyChanged(nameof(ClearRemovedModsLabel));

    partial void OnSelectedRemovedModsRetentionChanged(RemovedModsRetentionItem value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.RemovedModsRetention = value.Value;
        _settings.Save(settings);

        AppLog.Info("Remove", $"keep removed mods set to {value.Value}");
    }

    // Also run each time the Options page opens: removals and Undo on the Installed page change it.
    public void RefreshRemovedModsSize()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        RemovedModsBytes = string.IsNullOrWhiteSpace(installPath) ? 0 : RemovedMods.Size(installPath);
    }

    private bool CanClearRemovedMods() => RemovedModsBytes > 0;

    // Deletes everything held for this install, after saying how much and that it ends every Undo.
    [RelayCommand(CanExecute = nameof(CanClearRemovedMods))]
    private void ClearRemovedMods()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var size = DownloadQueueItemViewModel.SizeLabel(RemovedModsBytes);

        var answer = MessageBox.Show(
            Text(Strings.Options_RemovedModsClearConfirmFormat, size, installPath),
            Strings.Options_RemovedModsClearTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes) return;

        RemovedMods.Clear(installPath);
        RefreshRemovedModsSize();
        RemovedModsStatus = Text(Strings.Options_RemovedModsClearedFormat, size);
    }

    //
    // Monitor mode: what the install buttons do, and where a download-only save goes. The mode is
    // applied and saved the moment it changes, and every install button re-reads it through
    // ModPageGate.
    //
    public IReadOnlyList<InstallModeItem> InstallModeOptions { get; } =
    [
        new(nameof(Strings.Options_MonitorModeInstall), InstallMode.Install),
        new(nameof(Strings.Options_MonitorModeDownloadOnly), InstallMode.DownloadOnly),
    ];

    [ObservableProperty]
    private InstallModeItem _selectedInstallMode;

    // What a scan does when it finds a downloaded mod installed (R2): ask, or just note it.
    public IReadOnlyList<DownloadConfirmationItem> DownloadConfirmationOptions { get; } =
    [
        new(nameof(Strings.Options_MonitorConfirmAsk), DownloadConfirmation.Ask),
        new(nameof(Strings.Options_MonitorConfirmQuiet), DownloadConfirmation.MarkQuietly),
    ];

    [ObservableProperty]
    private DownloadConfirmationItem _selectedDownloadConfirmation;

    // Whether a mod list's downloads go into a subfolder named after the list (§9).
    [ObservableProperty]
    private bool _downloadListSubfolders;

    // Empty means the Windows Downloads folder, which the placeholder names.
    [ObservableProperty]
    private string _downloadFolderInput = string.Empty;

    public string DownloadFolderPlaceholder =>
        Text(Strings.Options_MonitorFolderPlaceholderFormat, DownloadFolders.Default());

    //
    // Update notifications (§8): a Windows notification when an installed mod has a new release,
    // checked on a timer while the app runs. Both take effect the moment they change - switching it
    // on starts the timer and takes the baseline, no restart.
    //
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesNowCommand))]
    private bool _updateNotificationsEnabled;

    // Closing the window hides it to the tray instead (§8a). Greyed out while notifications are off,
    // and ignored then too (D8) - see UpdateNotificationSettings.KeepsRunningInTray.
    [ObservableProperty]
    private bool _keepRunningInTray;

    // What the last Check now came to, under the button. Empty until it has been pressed.
    [ObservableProperty]
    private string _updateCheckStatus = string.Empty;

    public IReadOnlyList<UpdateIntervalItem> UpdateIntervalOptions { get; } =
    [
        new(nameof(Strings.Options_UpdateInterval30Minutes), UpdateCheckInterval.ThirtyMinutes),
        new(nameof(Strings.Options_UpdateInterval1Hour), UpdateCheckInterval.OneHour),
        new(nameof(Strings.Options_UpdateInterval3Hours), UpdateCheckInterval.ThreeHours),
        new(nameof(Strings.Options_UpdateInterval6Hours), UpdateCheckInterval.SixHours),
        new(nameof(Strings.Options_UpdateInterval12Hours), UpdateCheckInterval.TwelveHours),
    ];

    [ObservableProperty]
    private UpdateIntervalItem _selectedUpdateInterval;

    // Whether the Mod footprint page is in the sidebar. Off by default - see AppSettings.
    [ObservableProperty]
    private bool _showModFootprintPage;

    // Whether Start server also opens the launcher once the server is up - see AppSettings.
    [ObservableProperty]
    private bool _startLauncherAfterServer;

    partial void OnStartLauncherAfterServerChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.StartLauncherAfterServer = value;
        _settings.Save(settings);

        AppLog.Info("Launch", value ? "launcher follows the server" : "launcher no longer follows the server");
    }

    // Whether Start server runs it with no console window - see AppSettings.
    [ObservableProperty]
    private bool _hideServerWindow;

    partial void OnHideServerWindowChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.HideServerWindow = value;
        _settings.Save(settings);

        AppLog.Info("Launch", value ? "the server starts without a window" : "the server starts in its window");
    }

    // Whether the mouse wheel glides or jumps - see AppSettings and Behaviors/SmoothScrolling.
    [ObservableProperty]
    private bool _smoothScrolling;

    partial void OnSmoothScrollingChanged(bool value)
    {
        if (!_loaded) return;

        Behaviors.SmoothScrolling.Enabled = value;

        var settings = _settings.Load();
        settings.SmoothScrolling = value;
        _settings.Save(settings);

        AppLog.Info("Options", value ? "smooth scrolling on" : "smooth scrolling off");
    }

    // Whether removing an item asks first - see AppSettings.ConfirmUnsubscribe. A plain property
    // rather than [ObservableProperty]: the question's "Don't ask again" changes the stored value
    // while this page may be open, and Reload puts it on the switch without saving it again.
    private bool _confirmUnsubscribe;

    public bool ConfirmUnsubscribe
    {
        get => _confirmUnsubscribe;
        set
        {
            if (!SetProperty(ref _confirmUnsubscribe, value) || !_loaded) return;

            var settings = _settings.Load();
            settings.ConfirmUnsubscribe = value;
            _settings.Save(settings);

            AppLog.Info("Options", value ? "unsubscribe question on" : "unsubscribe question off");
        }
    }

    /// <summary>Settings another part of the app can change while this page is open, read again,
    /// and what the kept downloads take up now.</summary>
    public void Reload()
    {
        SetProperty(ref _confirmUnsubscribe, _settings.Load().ConfirmUnsubscribe, nameof(ConfirmUnsubscribe));
        RefreshKeptDownloads();
    }

    // Whether downloaded archives are kept - see AppSettings.KeepDownloads.
    [ObservableProperty]
    private bool _keepDownloads;

    partial void OnKeepDownloadsChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.KeepDownloads = value;
        _settings.Save(settings);

        AppLog.Info("Options", value ? "downloads kept" : "downloads not kept");
    }

    // "Kept now: 85.3 MB".
    [ObservableProperty]
    private string _keptDownloadsLabel = string.Empty;

    private async void RefreshKeptDownloads()
    {
        try
        {
            var bytes = await Task.Run(AppServices.DownloadQueue.KeptDownloadsSize);
            KeptDownloadsLabel = Text(Strings.Options_DownloadsSizeFormat, DownloadQueueItemViewModel.SizeLabel(bytes));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Options", $"couldn't size the kept downloads: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearKeptDownloads()
    {
        AppServices.DownloadQueue.ClearKeptDownloads();
        RefreshKeptDownloads();
    }

    // Same arrangement as ModPageGate: one description of the setting, shared with the nav item.
    public FootprintGateViewModel FootprintGate => AppServices.FootprintGate;

    //
    // How the window opens. Applied and saved the moment it changes, the same as the theme
    // dropdown - and applied to the window that is already open, so the setting does something now
    // rather than at the next launch.
    //
    public IReadOnlyList<WindowStartupItem> WindowStartupOptions { get; } =
    [
        new(nameof(Strings.Options_WindowRemember), WindowStartupMode.Remember),
        new(nameof(Strings.Options_WindowMaximised), WindowStartupMode.Maximized),
        new(nameof(Strings.Options_WindowCustom), WindowStartupMode.Custom),
        new(nameof(Strings.Options_WindowFullScreen), WindowStartupMode.FullScreen),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCustomSize))]
    private WindowStartupItem _selectedWindowStartup;

    /// <summary>Whether the two size boxes are relevant - they only do anything in the one mode
    /// that reads them.</summary>
    public bool ShowCustomSize => SelectedWindowStartup.Value == WindowStartupMode.Custom;

    //
    // Text rather than numbers, and applied by a button rather than as they are typed: a size box
    // that resizes the window on every keystroke resizes it to 1, then 17, then 172 on the way to
    // 1720.
    //
    [ObservableProperty]
    private string _customWidthInput = string.Empty;

    [ObservableProperty]
    private string _customHeightInput = string.Empty;

    [ObservableProperty]
    private string? _windowSizeMessage;

    //
    // Whether either page has a saved default to put back. The reset buttons show regardless and
    // simply say so - a button that appears only once you have used a feature elsewhere is one
    // nobody finds when they want it.
    //
    [ObservableProperty]
    private string _installedDefaultsDescription = string.Empty;

    [ObservableProperty]
    private string _browseDefaultsDescription = string.Empty;

    //
    // What this machine does with the install - see AppSettings.PlaysHere / RunsHeadlessClient.
    //
    // Two switches rather than one dropdown, because "dedicated headless" is just the second without
    // the first, and a machine that does both needs no value of its own.
    //
    [ObservableProperty]
    private bool _playsHere = true;

    [ObservableProperty]
    private bool _runsHeadlessClient;

    // Whether a Fika headless launcher is actually there. Drives the hint under the switches - a
    // machine with no launcher that claims to run a headless is worth mentioning, not preventing.
    [ObservableProperty]
    private bool _hasHeadlessLauncher;

    //
    // The headless launcher, named by hand, for a setup that keeps it somewhere the search will
    // never look.
    //
    // Detection looks at the top of the install folder and nowhere else, on purpose - see
    // SptLaunchService, where widening it has now gone wrong twice in opposite directions. A path
    // somebody typed is not a guess, so it is the way out for any layout, including a manager
    // sitting beside the SPT folder rather than in it.
    //
    [ObservableProperty]
    private string _headlessLauncherPath = string.Empty;

    [ObservableProperty]
    private string _installRoleDescription = string.Empty;

    // The same answer in a few words, for the collapsed row.
    [ObservableProperty]
    private string _installRoleSummary = string.Empty;

    //
    // The Server Map section binds straight to the shared connection rather than mirroring it into
    // properties here. It is not a stored setting the way the two switches above are: connecting is
    // an action with a result, and that result is the same object the sidebar and the page read.
    //
    public ServerMapGateViewModel ServerMap => AppServices.ServerMap;

    // Copies of the SPT profiles - see ProfileBackupsViewModel.
    public ProfileBackupsViewModel Profiles { get; } = new();

    public OptionsViewModel()
    {
        InstallPathInput = SptEnvironment.InstallPath;

        var stored = AppTheme.Stored;
        _selectedTheme = ThemeOptions.FirstOrDefault(t => t.Value == stored) ?? ThemeOptions[^1];

        // A stored tag this build has no resources for selects System default, which is what the
        // app is doing anyway - AppLanguage falls back to the same place.
        _selectedLanguage = LanguageOptions.FirstOrDefault(
                l => string.Equals(l.Tag, AppLanguage.Stored, StringComparison.OrdinalIgnoreCase))
            ?? LanguageOptions[0];

        var settings = _settings.Load();
        _skipModPageConfirmation = settings.SkipModPageConfirmation;
        _showModFootprintPage = settings.ShowModFootprintPage;
        _startLauncherAfterServer = settings.StartLauncherAfterServer;
        _hideServerWindow = settings.HideServerWindow;
        _smoothScrolling = settings.SmoothScrolling;
        _confirmUnsubscribe = settings.ConfirmUnsubscribe;
        _keepDownloads = settings.KeepDownloads;
        _showSubscribedItemsTab = settings.ShowSubscribedItemsTab;
        _showCollectionsTab = settings.ShowCollectionsTab;
        _backgroundDarkness = Math.Clamp(settings.BackgroundDarkness, 0, 0.9);
        _backgroundFileName = settings.BackgroundImage is { } picture ? System.IO.Path.GetFileName(picture) : null;

        _selectedInstallMode = InstallModeOptions.FirstOrDefault(o => o.Value == settings.Monitor.InstallMode)
            ?? InstallModeOptions[0];
        _selectedRemovedModsRetention =
            RemovedModsRetentionOptions.FirstOrDefault(o => o.Value == settings.RemovedModsRetention)
            ?? RemovedModsRetentionOptions[3];
        RefreshRemovedModsSize();
        _downloadFolderInput = settings.Monitor.DownloadFolder ?? string.Empty;
        _downloadListSubfolders = settings.Monitor.DownloadListSubfolders;
        _selectedDownloadConfirmation =
            DownloadConfirmationOptions.FirstOrDefault(o => o.Value == settings.Monitor.DownloadConfirmation)
            ?? DownloadConfirmationOptions[0];

        _updateNotificationsEnabled = settings.UpdateNotifications.Enabled;
        _keepRunningInTray = settings.UpdateNotifications.KeepRunningInTray;
        _selectedUpdateInterval =
            UpdateIntervalOptions.FirstOrDefault(o => o.Value == settings.UpdateNotifications.Interval)
            ?? UpdateIntervalOptions[1];

        _selectedWindowStartup = WindowStartupOptions.FirstOrDefault(o => o.Value == settings.Window.StartupMode)
            ?? WindowStartupOptions[0];
        _customWidthInput = FormatSize(settings.Window.CustomWidth);
        _customHeightInput = FormatSize(settings.Window.CustomHeight);

        RefreshPageDefaultDescriptions(settings);
        RefreshInstallRole(settings);

        _loaded = true;
    }

    partial void OnSelectedThemeChanged(ThemeOptionItem value)
    {
        if (!_loaded) return;

        AppTheme.Set(value.Value);
    }

    //
    // The open page follows immediately and nothing here has to ask it to: {loc:Str} bindings
    // re-read on their own, and every view model - including each entry in this dropdown - is told
    // to re-read its computed text by LocalizedViewModel.
    //
    partial void OnSelectedLanguageChanged(LanguageOptionItem value)
    {
        if (!_loaded) return;

        AppLanguage.Set(value.Tag);
    }

    //
    // Switching the gate off is warned about, switching it back on isn't - there is nothing to warn
    // about in choosing to read more.
    //
    partial void OnSkipModPageConfirmationChanged(bool value)
    {
        if (!_loaded || _revertingSkip) return;

        if (value && !ConfirmSkip())
        {
            _revertingSkip = true;
            SkipModPageConfirmation = false;
            _revertingSkip = false;
            return;
        }

        var settings = _settings.Load();
        settings.SkipModPageConfirmation = value;
        _settings.Save(settings);

        // Every install button's tooltip reads from this, so they all change with the switch.
        AppServices.ModPageGate.Refresh();

        AppLog.Info("ModPages", value ? "gate turned off" : "gate turned back on");
    }

    //
    // No confirmation either way. Nothing is at stake in showing or hiding a read-only page, and
    // the switch's own tooltip carries the caveat that matters.
    //
    // ------------------------------------------------------------------ tabs and background

    [ObservableProperty]
    private bool _showSubscribedItemsTab;

    [ObservableProperty]
    private bool _showCollectionsTab;

    // How far the picture is darkened, 0 to 0.9.
    [ObservableProperty]
    private double _backgroundDarkness;

    // The picture in use, by its file name; null for the Steam grid.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackgroundImage))]
    [NotifyCanExecuteChangedFor(nameof(UseGridBackgroundCommand))]
    private string? _backgroundFileName;

    public bool HasBackgroundImage => BackgroundFileName is not null;

    partial void OnShowSubscribedItemsTabChanged(bool value) => SaveAppearance(s => s.ShowSubscribedItemsTab = value);

    partial void OnShowCollectionsTabChanged(bool value) => SaveAppearance(s => s.ShowCollectionsTab = value);

    partial void OnBackgroundDarknessChanged(double value) => SaveAppearance(s => s.BackgroundDarkness = Math.Clamp(value, 0, 0.9));

    private void SaveAppearance(Action<AppSettings> change)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        change(settings);
        _settings.Save(settings);
        AppServices.Appearance.Refresh();
    }

    //
    // A picture of the user's own behind the window. Copied into Data\Background under a new name
    // each time (the window may still hold the previous one open), the older copies deleted, so
    // the setting never points at a file that can be moved or deleted from under it.
    //
    [RelayCommand]
    private void ChooseBackground()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Options_BackgroundChooseTitle,
            Filter = $"{Strings.Options_BackgroundPictures}|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|{Strings.ModListFile_AllFiles}|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var folder = AppearanceViewModel.BackgroundDirectory;
            System.IO.Directory.CreateDirectory(folder);

            var copy = System.IO.Path.Combine(folder, $"background-{DateTime.UtcNow:yyyyMMddHHmmss}{System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant()}");
            System.IO.File.Copy(dialog.FileName, copy, overwrite: true);

            SaveAppearance(s => s.BackgroundImage = copy);
            if (!AppServices.Appearance.HasBackgroundImage)
            {
                // Not a picture this machine can read: back to the grid, and said.
                SaveAppearance(s => s.BackgroundImage = null);
                TryDelete(copy);
                BackgroundStatus = Strings.Options_BackgroundUnreadable;
                return;
            }

            BackgroundFileName = System.IO.Path.GetFileName(dialog.FileName);
            BackgroundStatus = null;
            DeleteOldBackgrounds(keep: copy);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Appearance", $"background picture could not be copied: {ex.Message}");
            BackgroundStatus = LocalizationService.Text(Strings.Options_BackgroundCopyFailedFormat, ex.Message);
        }
    }

    private bool CanUseGrid() => HasBackgroundImage;

    [RelayCommand(CanExecute = nameof(CanUseGrid))]
    private void UseGridBackground()
    {
        SaveAppearance(s => s.BackgroundImage = null);
        BackgroundFileName = null;
        BackgroundStatus = null;
        DeleteOldBackgrounds(keep: null);
    }

    [ObservableProperty]
    private string? _backgroundStatus;

    private static void DeleteOldBackgrounds(string? keep)
    {
        try
        {
            var folder = AppearanceViewModel.BackgroundDirectory;
            if (!System.IO.Directory.Exists(folder)) return;

            foreach (var file in System.IO.Directory.EnumerateFiles(folder, "background-*"))
            {
                if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)) TryDelete(file);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Appearance", $"old background pictures not tidied: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // In use by the window still; the next change tidies it.
            AppLog.Debug("Appearance", $"{path} not deleted yet: {ex.Message}");
        }
    }

    partial void OnShowModFootprintPageChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.ShowModFootprintPage = value;
        _settings.Save(settings);

        // Moves the nav item now rather than at the next launch.
        AppServices.FootprintGate.Refresh();

        AppLog.Info("Footprint", value ? "page shown" : "page hidden");
    }

    partial void OnSelectedInstallModeChanged(InstallModeItem value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.Monitor.InstallMode = value.Value;
        _settings.Save(settings);

        AppServices.ModPageGate.Refresh();

        AppLog.Info("Monitor", $"install mode set to {value.Value}");
    }

    partial void OnDownloadListSubfoldersChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.Monitor.DownloadListSubfolders = value;
        _settings.Save(settings);

        AppLog.Info("Monitor", value ? "list downloads go into subfolders" : "list downloads go into the folder itself");
    }

    partial void OnSelectedDownloadConfirmationChanged(DownloadConfirmationItem value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.Monitor.DownloadConfirmation = value.Value;
        _settings.Save(settings);

        AppLog.Info("Monitor", $"download confirmation set to {value.Value}");
    }

    partial void OnUpdateNotificationsEnabledChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.UpdateNotifications.Enabled = value;
        _settings.Save(settings);

        AppLog.Info("Updates", value ? "notifications switched on" : "notifications switched off");

        // Off keeps update_notifications.json, so switching back on doesn't announce the same
        // versions again (§8). On takes a fresh baseline (D6).
        if (value) AppServices.UpdateWatcher.SwitchedOn();
        else AppServices.UpdateWatcher.Stop();
    }

    //
    // Runs a check now rather than at the next tick. It counts like any other check - the first
    // after switching on is the baseline - so it doubles as the quick way to try the feature out.
    //
    [RelayCommand(CanExecute = nameof(UpdateNotificationsEnabled))]
    private async Task CheckUpdatesNowAsync()
    {
        UpdateCheckStatus = Strings.Options_UpdateCheckChecking;

        var outcome = await AppServices.UpdateWatcher.CheckNowAsync();

        var message = UpdateCheckWording.Describe(outcome);

        UpdateCheckStatus = Text(Strings.Options_UpdateCheckTimeFormat, message, DateTime.Now);
    }

    partial void OnKeepRunningInTrayChanged(bool value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.UpdateNotifications.KeepRunningInTray = value;
        _settings.Save(settings);

        AppLog.Info("Tray", value ? "closing the window will hide it to the tray" : "closing the window will quit");
    }

    partial void OnSelectedUpdateIntervalChanged(UpdateIntervalItem value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.UpdateNotifications.Interval = value.Value;
        _settings.Save(settings);

        AppLog.Info("Updates", $"check interval set to {value.Value}");

        // Restarts the timer on the new interval; does nothing while notifications are off.
        AppServices.UpdateWatcher.Start();
    }

    //
    // Saved as typed, on leaving the box. A folder that doesn't exist is not refused here: it may be
    // a drive that isn't plugged in yet, and the download that needs it says so if it still isn't.
    //
    partial void OnDownloadFolderInputChanged(string value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.Monitor.DownloadFolder = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _settings.Save(settings);

        AppLog.Info("Monitor", $"download folder set to \"{settings.Monitor.DownloadFolder}\"");
    }

    [RelayCommand]
    private void BrowseDownloadFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Options_MonitorFolderPickerTitle,
            InitialDirectory = DownloadFolders.Resolve(DownloadFolderInput),
        };

        if (dialog.ShowDialog() == true) DownloadFolderInput = dialog.FolderName;
    }

    // Back to null rather than the resolved path, so a Downloads folder Windows later moves is followed.
    [RelayCommand]
    private void ResetDownloadFolder() => DownloadFolderInput = string.Empty;

    //
    // Deliberately blunt, and defaulting to No. The gate is the one thing standing between someone
    // and installing a mod whose page says it needs a specific load order, a dependency this app
    // can't see, or a version of SPT they aren't running - and the app genuinely cannot tell them
    // which mods those are.
    //
    private static bool ConfirmSkip() =>
        MessageBox.Show(
            Strings.Options_SkipGateBody,
            Strings.Options_SkipGateTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    partial void OnSelectedWindowStartupChanged(WindowStartupItem value)
    {
        if (!_loaded) return;

        var settings = _settings.Load();
        settings.Window.StartupMode = value.Value;
        _settings.Save(settings);

        // Remember deliberately does nothing to the open window - see WindowLayout.ApplyModeNow.
        WindowLayout.ApplyModeNow(value.Value, settings.Window);

        WindowSizeMessage = value.Value switch
        {
            WindowStartupMode.Remember => Strings.Options_WindowRememberNote,
            WindowStartupMode.Maximized => Strings.Options_WindowMaximisedNote,
            WindowStartupMode.Custom => Strings.Options_WindowCustomNote,
            _ => Strings.Options_WindowFullScreenNote,
        };

        AppLog.Info("Window", $"startup mode set to {value.Value}");
    }

    //
    // Saves and applies the two size boxes together. Anything that isn't a number, or is smaller
    // than the window's own minimum, is clamped rather than refused - the window would ignore it
    // anyway, and a size box that silently rejects what you typed is worse than one that corrects
    // it in front of you.
    //
    [RelayCommand]
    private void ApplyWindowSize()
    {
        var settings = _settings.Load();

        settings.Window.CustomWidth = ParseSize(CustomWidthInput, settings.Window.CustomWidth, WindowSettings.MinimumWidth);
        settings.Window.CustomHeight = ParseSize(CustomHeightInput, settings.Window.CustomHeight, WindowSettings.MinimumHeight);
        _settings.Save(settings);

        // Written back, so a clamped or unparseable entry is visibly corrected rather than left
        // sitting in the box disagreeing with what was saved.
        CustomWidthInput = FormatSize(settings.Window.CustomWidth);
        CustomHeightInput = FormatSize(settings.Window.CustomHeight);

        WindowLayout.ApplyCustomSizeNow(settings.Window);

        WindowSizeMessage = Text(
            Strings.Options_WindowSizeSavedFormat,
            CustomWidthInput,
            CustomHeightInput);
    }

    /// <summary>Fills the two boxes from the window as it is right now, so a size can be chosen by
    /// dragging the window rather than by guessing two numbers.</summary>
    [RelayCommand]
    private void UseCurrentWindowSize()
    {
        if (WindowLayout.CurrentSize() is not { } size)
        {
            WindowSizeMessage = Strings.Options_WindowSizeRestoreFirst;
            return;
        }

        CustomWidthInput = FormatSize(size.Width);
        CustomHeightInput = FormatSize(size.Height);
        ApplyWindowSize();
    }

    private static double ParseSize(string? input, double current, double minimum) =>
        double.TryParse(input?.Trim(), out var value) && double.IsFinite(value)
            ? Math.Max(value, minimum)
            : Math.Max(current, minimum);

    private static string FormatSize(double value) => ((int)Math.Round(value)).ToString();

    //
    // The two page defaults are cleared here rather than on the pages themselves: the page has a
    // button that saves one, and the place to undo a setting is where the rest of the settings are.
    //
    [RelayCommand]
    private void ResetInstalledDefaults()
    {
        var settings = _settings.Load();
        settings.InstalledDefaults = null;
        _settings.Save(settings);

        RefreshPageDefaultDescriptions(settings);
        AppLog.Info("Installed", "cleared the saved page default");
    }

    [RelayCommand]
    private void ResetBrowseDefaults()
    {
        var settings = _settings.Load();
        settings.BrowseDefaults = null;
        _settings.Save(settings);

        RefreshPageDefaultDescriptions(settings);
        AppLog.Info("Browse", "cleared the saved page default");
    }

    //
    // Both pages read their default once, when they are first built, and both are kept alive for
    // the rest of the session - so this says plainly that clearing one lands at the next launch
    // rather than pretending it takes effect now.
    //
    private void RefreshPageDefaultDescriptions(AppSettings settings)
    {
        InstalledDefaultsDescription = settings.InstalledDefaults is null
            ? Strings.Options_InstalledNoDefault
            : Strings.Options_InstalledHasDefault;

        BrowseDefaultsDescription = settings.BrowseDefaults is null
            ? Strings.Options_BrowseNoDefault
            : Strings.Options_BrowseHasDefault;
    }

    //
    // Neither switch is confirmed. Nothing on disk moves either way - the roles only decide which
    // entries of a list a SERVER hands you are this machine's to install, and the next preview shows
    // exactly what that came to before anything is applied.
    //
    partial void OnPlaysHereChanged(bool value) => SaveInstallRole();

    //
    // Saved as it is typed, like the switches beside it. Blank clears it back to detection rather
    // than storing an empty string, so a cleared box and a machine that never had one look the same
    // in settings.json.
    //
    partial void OnHeadlessLauncherPathChanged(string value)
    {
        if (!_loaded || _settingInstallRole) return;

        var settings = _settings.Load();
        settings.HeadlessLauncherPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _settings.Save(settings);

        RefreshInstallRole(settings);

        AppLog.Info("InstallRole", $"headless launcher path set to \"{settings.HeadlessLauncherPath}\"");
    }

    //
    // Points at the exe itself rather than a folder: the whole reason this box exists is that the
    // folder is not one the app can work the name out from.
    //
    [RelayCommand]
    private void BrowseHeadlessLauncher()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.Options_HeadlessPickerTitle,
            Filter = Strings.Options_HeadlessPickerFilter,
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(HeadlessLauncherPath))
        {
            try
            {
                dialog.InitialDirectory = Path.GetDirectoryName(HeadlessLauncherPath);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                // A stored path that is not a path any more. The dialog opens wherever it likes.
            }
        }

        if (dialog.ShowDialog() == true) HeadlessLauncherPath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearHeadlessLauncher() => HeadlessLauncherPath = string.Empty;

    partial void OnRunsHeadlessClientChanged(bool value) => SaveInstallRole();

    private void SaveInstallRole()
    {
        if (!_loaded || _settingInstallRole) return;

        var settings = _settings.Load();
        settings.PlaysHere = PlaysHere;
        settings.RunsHeadlessClient = RunsHeadlessClient;
        _settings.Save(settings);

        RefreshInstallRoleDescription();

        AppLog.Info("InstallRole", $"playsHere={PlaysHere} headless={RunsHeadlessClient}");
    }

    //
    // Reads the two switches back out of settings, and re-checks whether the folder actually has a
    // headless launcher in it.
    //
    // Written through the guard rather than to the backing fields, so the page updates without the
    // assignment being mistaken for the user answering.
    //
    private void RefreshInstallRole(AppSettings settings)
    {
        _settingInstallRole = true;

        var roles = settings.Roles;
        PlaysHere = roles.HasFlag(InstallRoles.Player);
        RunsHeadlessClient = roles.HasFlag(InstallRoles.Headless);
        HeadlessLauncherPath = settings.HeadlessLauncherPath ?? string.Empty;

        //
        // The named path counts as having one, and it stands on its own: a machine can name a
        // launcher outside the install folder, which is exactly the case the box was added for.
        //
        HasHeadlessLauncher = !string.IsNullOrWhiteSpace(SptEnvironment.InstallPath)
            && SptLaunchService.TryFindHeadlessLauncherExe(
                SptEnvironment.InstallPath!, out _, settings.HeadlessLauncherPath);

        _settingInstallRole = false;

        RefreshInstallRoleDescription();
    }

    // The two role lines and the page-default lines are stored strings, so a language change has to
    // compose them again rather than just re-raise them.
    protected internal override void RefreshText()
    {
        RefreshInstallRoleDescription();
        RefreshPageDefaultDescriptions(_settings.Load());
        base.RefreshText();
    }

    private void RefreshInstallRoleDescription()
    {
        InstallRoleDescription = (PlaysHere, RunsHeadlessClient) switch
        {
            (false, true) => Strings.Options_RoleHeadless,
            (true, true) => Strings.Options_RolePlaysAndHosts,
            (false, false) => Strings.Options_RoleNeither,
            _ => Strings.Options_RolePlayer,
        };

        InstallRoleSummary = (PlaysHere, RunsHeadlessClient) switch
        {
            (false, true) => Strings.Options_RoleSummaryHeadless,
            (true, true) => Strings.Options_RoleSummaryPlaysAndHosts,
            (false, false) => Strings.Options_RoleSummaryNeither,
            _ => Strings.Options_RoleSummaryPlayer,
        };
    }

    //
    // Puts the question once, when an install folder turns out to hold a headless launcher.
    //
    // Only when it has one and only when nobody has answered yet, so an ordinary install never meets
    // this and answering it once is the end of it. "Ask me later" stores nothing, which leaves the
    // machine reading as a player - the answer that installs everything - and brings the question
    // back next time the folder is set.
    //
    private void PromptForInstallRoleIfNeeded()
    {
        var installPath = SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var settings = _settings.Load();
        if (settings.InstallRolesAnswered) return;

        if (!SptLaunchService.TryFindHeadlessLauncherExe(
                installPath!, out var launcher, settings.HeadlessLauncherPath))
        {
            return;
        }

        var choice = InstallRoleWindow.Ask(launcher);
        if (choice == InstallRoleChoice.AskLater) return;

        settings.PlaysHere = choice == InstallRoleChoice.PlaysHereToo;

        // Yes either way: the launcher on disk is what asked the question, and it is the half of it
        // that does not need a person.
        settings.RunsHeadlessClient = true;

        _settings.Save(settings);

        RefreshInstallRole(settings);

        AppLog.Info("InstallRole", $"answered at setup: {choice}");
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = Strings.Options_InstallPathPickerTitle };
        if (!string.IsNullOrWhiteSpace(InstallPathInput)) dialog.InitialDirectory = InstallPathInput;

        if (dialog.ShowDialog() == true)
        {
            InstallPathInput = dialog.FolderName;
            Save();
        }
    }

    [RelayCommand]
    private void Save()
    {
        SptEnvironment.SetInstallPath(string.IsNullOrWhiteSpace(InstallPathInput) ? null : InstallPathInput.Trim());
        InstallPathInput = SptEnvironment.InstallPath;

        //
        // Setting the folder is this app's setup step - there is no first-run wizard - so it is
        // where the machine gets asked what it is. After SetInstallPath, because the question is
        // about the folder that was just chosen.
        //
        PromptForInstallRoleIfNeeded();

        RefreshInstallRole(_settings.Load());
    }
}
