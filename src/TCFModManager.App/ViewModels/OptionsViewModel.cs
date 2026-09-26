using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TCFModManager.App.Localization;
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

    //
    // The Server Map section binds straight to the shared connection rather than mirroring it into
    // properties here. It is not a stored setting the way the two switches above are: connecting is
    // an action with a result, and that result is the same object the sidebar and the page read.
    //
    public ServerMapGateViewModel ServerMap => AppServices.ServerMap;

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
        _smoothScrolling = settings.SmoothScrolling;
        _confirmUnsubscribe = settings.ConfirmUnsubscribe;
        _keepDownloads = settings.KeepDownloads;

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

    private void RefreshInstallRoleDescription()
    {
        InstallRoleDescription = (PlaysHere, RunsHeadlessClient) switch
        {
            (false, true) => Strings.Options_RoleHeadless,
            (true, true) => Strings.Options_RolePlaysAndHosts,
            (false, false) => Strings.Options_RoleNeither,
            _ => Strings.Options_RolePlayer,
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
