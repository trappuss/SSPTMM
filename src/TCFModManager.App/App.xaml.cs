using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;

namespace TCFModManager.App;

public partial class App : Application
{
    // Set when another copy is already running from this folder, so this one leaves no trace.
    private bool _secondCopy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // First, so nothing that follows - building every service, reading settings - can fail
        // without a line in the log. The off-thread handler flushes: the process ends as it returns,
        // before the log's own writer would get to the line that says why.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppLog.Error("App", "Unhandled exception", args.ExceptionObject as Exception);
            AppLog.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("App", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        //
        // First, before anything reads or writes Data\. A second launch from the same folder asks
        // the running copy to show its window and goes no further (D11).
        //
        // The window is created at the end of this method rather than through StartupUri, which
        // would open it even for a copy that is on its way out.
        //
        if (!SingleInstance.Claim(() => Dispatcher.BeginInvoke(ShowMainWindow)))
        {
            _secondCopy = true;
            AppLog.Flush();
            Shutdown();
            return;
        }

        AppLog.Start($"{AppVersion.Current}, SPT install: {AppServices.SptEnvironment.InstallPath ?? "(not set)"}");

        // Fork: the per-install record lists the Steam Workshop fork kept before 1.19, moved into
        // 1.19's single list before anything reads it - see ForkInstallRecordsMigration.
        try
        {
            ForkInstallRecordsMigration.Run(AppPaths.DataDirectory, new SettingsService().Load().SptInstallPath);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Records", "couldn't move the fork's install records over", ex);
        }

        // Fork (1.3.0): an install the app was closed in the middle of goes on record before anything
        // reads the records - see InstallJournal. Said once the window is up.
        try
        {
            _cutOffInstalls = InstallJournal.RecoverAll(new ModInstallManifestService());
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Install", "couldn't check for an install that did not finish", ex);
        }

        // Before the theme and before any string is read, so the first frame is drawn in the right
        // language rather than re-read a moment later.
        AppLanguage.ApplyStored();

        // Subscribes the source every {loc:Str} binding reads from, so a language chosen before any
        // page has been opened still reaches the bindings made afterwards.
        _ = LocalizationService.Instance;

        ApplyElementLanguage();

        // Before the main window exists, so it is painted in the right theme rather than repainted a
        // moment after it opens. Following the OS needs a real window and is set up in MainWindow.
        AppTheme.ApplyStored();

        // The Steam font everywhere, including the trees a window cannot hand it to.
        ApplySteamFont();

        // Wheel scrolling that glides, across every page - see Behaviors/SmoothScrolling.
        Behaviors.SmoothScrolling.Enabled = new Core.Services.SettingsService().Load().SmoothScrolling;
        Behaviors.SmoothScrolling.Register();

        // Frame-rate and description timings in the log, only when asked for - see PerfProbe.
        Services.PerfProbe.Start();

        // TEMPORARY, ADDED IN v1.5.0 - DELETE WHEN THE APP LEAVES BETA, along with the method
        // itself. Carries a pre-v1.5.0 LegacyConfigs folder from beside the exe into Data\. A no-op
        // on every launch after the first, and on any install that never had one.
        AppPaths.MigrateLegacyConfigsFolder();

        // TEMPORARY, ADDED IN v1.12.0 - DELETE WHEN THE APP LEAVES BETA, along with the method
        // itself. Carries a Server Map key and published list out of TCFModManager\ServerMap\config\
        // and into Data\ServerMap\, so a hand-deploy that replaces TCFModManager\ stops taking the
        // operator's key with it. A no-op unless this machine runs the server mod.
        ServerMapConfigFolder.MigrateLegacyFolder(new SettingsService().Load().SptInstallPath);

        // So the install buttons know from the first frame whether they are skipping mod pages.
        AppServices.ModPageGate.Refresh();

        // Before the window is built, so the sidebar is drawn with the right items rather than
        // gaining one a moment after it opens.
        AppServices.FootprintGate.Refresh();
        AppServices.Appearance.Refresh();

        // Reports how a self-update went (the script doing the swap runs after the previous process
        // is gone, so its own log is the only record of it) and clears out the staged files.
        AppUpdateInstaller.SweepAfterStartup();

        // Removed mods whose time is up under Keep removed mods are deleted from the install's holding
        // folder (D27) - off the UI thread, since a large held mod takes a moment to delete.
        var pruneInstall = SptInstallationService.ToGameRoot(new SettingsService().Load().SptInstallPath);
        if (!string.IsNullOrWhiteSpace(pruneInstall))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var pruned = new RemovedMods().Prune(pruneInstall, DateTimeOffset.Now);
                    if (pruned > 0) AppLog.Info("Remove", $"cleared {pruned} held removal(s) past Keep removed mods");
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn("Remove", $"couldn't clear old removals: {ex.Message}");
                }
            });
        }

        // A data file found damaged or unreadable - now or later - is said, once the window is up.
        SafeFile.ProblemFound += (_, _) => Dispatcher.BeginInvoke(ReportDataProblems, DispatcherPriority.ApplicationIdle);

        // Signing out or shutting Windows down closes the window like anything else, and that close
        // has to end the app rather than hide it in the tray.
        SessionEnding += (_, _) => AppTray.MarkQuitting();

        // Before the window, so a launch that came from clicking a notification opens on Installed.
        UpdateToasts.Initialize(e.Args, new SettingsService().Load().UpdateNotifications.Enabled);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (_cutOffInstalls.Count > 0)
            Dispatcher.BeginInvoke(ReportCutOffInstalls, DispatcherPriority.ApplicationIdle);

        // The first check is one interval from now, never at launch (R2). A no-op while it's off.
        AppServices.UpdateWatcher.Start();

        // A no-op unless the page is on and the user agreed to report to this server (D9).
        AppServices.ServerMapReporter.Start();
    }

    // Another launch of the exe from this folder asked for the window.
    private void ShowMainWindow()
    {
        if (MainWindow is { } window) WindowActivation.BringForward(window);
    }

    private static readonly HashSet<string> ReportedData = new(StringComparer.OrdinalIgnoreCase);

    private List<InstallJournal.Recovered> _cutOffInstalls = [];

    private void ReportCutOffInstalls()
    {
        if (MainWindow is not { IsLoaded: true } window || _cutOffInstalls.Count == 0) return;

        var names = string.Join(", ", _cutOffInstalls.Select(r => string.Join(" ", r.Name, r.Version)));
        _cutOffInstalls = [];
        _ = window;
        TCFModManager.App.Views.SteamMessageBox.Show(LocalizationService.Text(Strings.App_InstallCutOffFormat, names),
            Strings.App_InstallCutOffTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    //
    // What SafeFile found: a settings file, the collections or the install records damaged by a
    // crash or a power cut. Said plainly, with where the damaged copy was kept - nothing was thrown
    // away - rather than the app quietly starting over.
    //
    public static void ReportDataProblems()
    {
        if (Current?.MainWindow is not { IsLoaded: true }) return;

        foreach (var problem in SafeFile.Problems)
        {
            if (!ReportedData.Add(problem.Path)) continue;

            var name = System.IO.Path.GetFileName(problem.Path);
            var kept = problem.KeptAs ?? Strings.App_DataNotKept;
            var body = LocalizationService.Text(Strings.App_DataDamagedFormat, name, kept);

            TCFModManager.App.Views.SteamMessageBox.Show(body, Strings.App_DataProblemTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    //
    // WPF gives every element a Language of en-US regardless of what Windows is set to, and that is
    // what a StringFormat binding reads for its dates and numbers. Without this, a machine set to
    // en-GB or de-DE still renders every bound date in the US order - and the app's own rule that
    // dates and numbers follow the regional setting would be silently untrue everywhere in XAML.
    //
    // Reads CurrentCulture, the regional setting, not CurrentUICulture: the chosen language moves
    // the text and nothing else.
    //
    private static void ApplyElementLanguage()
    {
        try
        {
            var tag = CultureInfo.CurrentCulture.IetfLanguageTag;
            if (string.IsNullOrWhiteSpace(tag)) return;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(tag)));
        }
        catch (Exception ex)
        {
            // Dates in the wrong order are worth a line in the log. They are not worth refusing to
            // start over.
            AppLog.Warn("Language", $"couldn't set the element language: {ex.Message}");
        }
    }

    //
    // Steam's pages are set in Motiva Sans, which Valve licenses and nobody else may use, falling
    // back to Noto Sans - so Noto Sans is what ships (Themes/Fonts, SIL OFL) and what this applies.
    //
    // Default-value overrides rather than a FontFamily on each window, because tooltips, context
    // menus and popups are separate visual trees that inherit nothing from the window they belong
    // to. TextElement and TextBlock cover every run of text wherever it sits; Window, ToolTip and
    // ContextMenu cover the controls, which take their font from those roots by inheritance.
    //
    // Each override stands alone: WPF refuses a second override for a type, and one refusal must not
    // cost the rest.
    //
    private void ApplySteamFont()
    {
        if (TryFindResource("SteamFont") is not System.Windows.Media.FontFamily font)
        {
            AppLog.Warn("Theme", "SteamFont resource missing - keeping the system font");
            return;
        }

        void Override(DependencyProperty property, Type type)
        {
            try
            {
                property.OverrideMetadata(type, new FrameworkPropertyMetadata(font));
            }
            catch (Exception ex)
            {
                AppLog.Warn("Theme", $"couldn't set the Steam font on {type.Name}: {ex.Message}");
            }
        }

        Override(System.Windows.Documents.TextElement.FontFamilyProperty, typeof(System.Windows.Documents.TextElement));
        Override(System.Windows.Controls.TextBlock.FontFamilyProperty, typeof(System.Windows.Controls.TextBlock));
        Override(System.Windows.Controls.Control.FontFamilyProperty, typeof(Window));
        Override(System.Windows.Controls.Control.FontFamilyProperty, typeof(System.Windows.Controls.ToolTip));
        Override(System.Windows.Controls.Control.FontFamilyProperty, typeof(System.Windows.Controls.ContextMenu));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_secondCopy)
        {
            base.OnExit(e);
            return;
        }

        AppServices.UpdateWatcher.Stop();
        AppServices.ServerMapReporter.Stop();
        AppTray.Dispose();
        UpdateToasts.RemoveMessages();
        UpdateToasts.UnregisterIfOff(new SettingsService().Load().UpdateNotifications.Enabled); // Fork (1.2.0)
        DependencyBadgeLoader.Flush();
        AppLog.Info("App", "Shutting down");
        AppLog.Flush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("App", "Unhandled UI exception", e.Exception);

        //
        // Keyed like everything else, and safe to be: Get falls back to the key itself rather than
        // throwing, so even a failure inside the language stack leaves a readable dialog.
        //
        MessageBox.Show(
            LocalizationService.Text(Strings.App_CrashBodyFormat, e.Exception, AppLog.CurrentFile),
            Strings.App_CrashTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
