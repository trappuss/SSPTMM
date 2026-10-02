using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;
using Wpf.Ui.Tray.Controls;

namespace TCFModManager.App;

//
// Running in the tray (§8a, R9): with the setting on, closing the window hides it and leaves an
// icon in the notification area, so update checks carry on.
//
// The icon exists only while the window is hidden (D9). It is made in code rather than placed in
// MainWindow's XAML because WPF-UI's NotifyIcon registers itself the first time it renders - in the
// window, that would put it in the tray from launch.
//
// Its menu is Open, Check for updates now and Quit (D10). Quit is the only way out while the
// setting is on, so it is also what everything else that ends the app goes through (Quit below).
//
internal static class AppTray
{
    private static NotifyIcon? _icon;

    //
    // Set once the app is really ending - Quit, a self-update or Windows signing out - so closing
    // the window then isn't turned into hiding it.
    //
    public static bool IsQuitting { get; private set; }

    public static void MarkQuitting() => IsQuitting = true;

    // Ends the app, whatever the tray setting says.
    public static void Quit()
    {
        IsQuitting = true;
        Application.Current.Shutdown();
    }

    // Read at the moment the window is closed, so the switch in Options applies straight away.
    public static bool HidesOnClose() =>
        !IsQuitting && new SettingsService().Load().UpdateNotifications.KeepsRunningInTray;

    public static void HideToTray(Window window)
    {
        window.Hide();

        try
        {
            _icon ??= Build();

            // Rebuilt on every hide rather than once, so it's in whatever language the app is now in.
            _icon.TooltipText = Strings.Tray_ToolTip;
            _icon.Menu = BuildMenu();
            _icon.Register();
        }
        catch (Exception ex)
        {
            //
            // A window hidden with no icon to bring it back would leave the app running where nobody
            // can reach it but a second launch of the exe. Better to leave the window where it was.
            //
            AppLog.Error("Tray", "couldn't put the icon in the tray - showing the window again", ex);
            window.Show();
            return;
        }

        // Register reports failure by leaving this false rather than by throwing. Same way out as above.
        if (!_icon.IsRegistered)
        {
            AppLog.Error("Tray", "the shell refused the tray icon - showing the window again");
            window.Show();
            return;
        }

        AppLog.Info("Tray", "window hidden to the tray");
        ShowNoticeOnce();
    }

    //
    // Called whenever the window becomes visible, however that happened - the menu, a click on the
    // icon or on a notification, or launching the exe again (D11).
    //
    public static void OnWindowShown()
    {
        if (_icon?.IsRegistered != true) return;

        _icon.Unregister();
        AppLog.Info("Tray", "window shown again");
    }

    public static void Open()
    {
        if (Application.Current?.MainWindow is { } window) WindowActivation.BringForward(window);
    }

    // On exit, so no dead icon is left in the tray until the mouse passes over it.
    public static void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    private static NotifyIcon Build()
    {
        var icon = new NotifyIcon
        {
            // The exe's own icon is used when none is set.
            FocusOnLeftClick = false,
            MenuOnRightClick = true,
        };

        //
        // A single left click opens the window. D10 said double-click, but a double-click starts with
        // a single one, and a single click is what Windows 11's own tray icons answer to.
        //
        icon.LeftClick += OnIconClicked;

        return icon;
    }

    // The library's delegate marks the sender [NotNull], which a lambda can't match without a warning.
    private static void OnIconClicked([NotNull] NotifyIcon sender, RoutedEventArgs e) => Open();

    private static ContextMenu BuildMenu()
    {
        var open = new MenuItem { Header = Strings.Tray_Open, FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => Open();

        var check = new MenuItem { Header = Strings.Tray_CheckNow };
        check.Click += async (_, _) => await CheckNowAsync();

        var quit = new MenuItem { Header = Strings.Tray_Quit };
        quit.Click += (_, _) =>
        {
            // Fork: asked first while a mod's files are being placed, as closing the window asks.
            if (!MainWindow.MayStopAnInstall(Application.Current?.MainWindow)) return;

            AppLog.Info("Tray", "quit from the tray menu");
            Quit();
        };

        var menu = new ContextMenu();
        menu.Items.Add(open);
        menu.Items.Add(check);
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    //
    // Nothing on screen to say what a check found, so it answers with a notification - unless it
    // announced updates, in which case that notification has already said it.
    //
    private static async Task CheckNowAsync()
    {
        var outcome = await AppServices.UpdateWatcher.CheckNowAsync();

        if (outcome is { Result: UpdateCheckResult.Checked, Announced: > 0 }) return;

        UpdateToasts.ShowMessage("check", Strings.Tray_CheckResultTitle, UpdateCheckWording.Describe(outcome));
    }

    // D9: the first time the window goes to the tray, say so - once, ever.
    private static void ShowNoticeOnce()
    {
        var settings = new SettingsService();
        var stored = settings.Load();
        if (stored.UpdateNotifications.TrayNoticeShown) return;

        UpdateToasts.ShowMessage("tray", Strings.Tray_NoticeTitle, Strings.Tray_NoticeBody);

        stored.UpdateNotifications.TrayNoticeShown = true;
        settings.Save(stored);
    }
}
