using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The Windows notification the update watcher raises, and what clicking it does (§6, R5 option A).
//
// One notification per check, however many updates it found (D7), with Open as its only button
// (R8). A newer one replaces an older one still sitting in the Notification Centre, because both
// carry the same Tag and Group.
//
// Clicking it - the body or Open - brings the window forward on Installed with the Show filter on
// "Updates available". If the app has quit since, Windows starts it with -ToastActivated and the
// toolkit hands the same arguments over once it is running (proven by the spike, §10).
//
// The tray (§8a) raises two plain messages through here as well - the one-time "still running"
// notice and the answer to "Check for updates now". Clicking either just shows the window. They
// are taken out of the Notification Centre when the app quits, so the only notification that can
// start the app is an update one - which is what lets a -ToastActivated launch open on Installed
// before its arguments have even arrived.
//
internal static class UpdateToasts
{
    private const string Tag = "updates";
    private const string Group = "tcfmm";

    private const string PageArgument = "page";
    private const string InstalledPage = "installed";

    private const string ActionArgument = "action";
    private const string OpenAction = "open";

    // The plain messages' group, cleared on exit. Each message kind has its own tag, so a new one
    // replaces the last of its kind.
    private const string MessageGroup = "tcfmm-app";

    // How many updates are named in the text before the rest become "and N more".
    private const int NamedInText = 2;

    private static bool _listening;

    //
    // Called once at startup. The toolkit is only touched when the feature is on or this launch
    // came from a notification: its first use registers the app with Windows under HKCU, which an
    // install that never switched notifications on has no reason to get.
    //
    public static void Initialize(IReadOnlyList<string> args, bool enabled)
    {
        var launchedByToast = args.Any(a => string.Equals(a, "-ToastActivated", StringComparison.OrdinalIgnoreCase));

        if (launchedByToast)
        {
            // Before the window exists, so it opens on Installed rather than flashing Browse first.
            AppLog.Info("Updates", "started by a click on an update notification");
            AppNavigation.ShowInstalledUpdates();
        }

        if (enabled || launchedByToast) Listen();
    }

    // Also called when the feature is switched on, so a notification raised this session is heard.
    public static void Listen()
    {
        if (_listening) return;

        try
        {
            ToastNotificationManagerCompat.OnActivated += OnActivated;
            _listening = true;
        }
        catch (Exception ex)
        {
            // An old Windows build or a locked-down registry. The watcher still runs and logs what it
            // finds; the Installed page shows the same updates.
            AppLog.Warn("Updates", $"couldn't register for notification clicks: {ex.Message}");
        }
    }

    public static void Show(IReadOnlyList<UpdateCandidate> updates)
    {
        if (updates.Count == 0) return;

        try
        {
            Listen();

            new ToastContentBuilder()
                .AddArgument(PageArgument, InstalledPage)
                .AddText(Strings.UpdateToast_Title(updates.Count, updates.Count))
                .AddText(Body(updates))
                .AddButton(new ToastButton()
                    .SetContent(Strings.UpdateToast_Open)
                    .AddArgument(PageArgument, InstalledPage))
                .Show(toast =>
                {
                    toast.Tag = Tag;
                    toast.Group = Group;
                });
        }
        catch (Exception ex)
        {
            // Notifications turned off for the app in Windows Settings don't land here - Windows
            // accepts the toast and drops it. This is the toolkit itself failing.
            AppLog.Warn("Updates", $"couldn't show the notification: {ex.Message}");
        }
    }

    // "SAIN 4.5.2", "SAIN 4.5.2 and UI Fixes 6.0.2", "SAIN 4.5.2, UI Fixes 6.0.2 and 1 more".
    internal static string Body(IReadOnlyList<UpdateCandidate> updates)
    {
        var named = Math.Min(updates.Count, NamedInText);

        var parts = updates
            .Take(named)
            .Select(u => LocalizationService.Text(Strings.UpdateToast_ItemFormat, u.Name, u.Version))
            .ToList();

        var rest = updates.Count - named;
        if (rest > 0) parts.Add(Strings.UpdateToast_More(rest, rest));

        return TextLists.Join(parts);
    }

    //
    // A notification that says something and, clicked, shows the window. <paramref name="tag"/>
    // names the kind, so a newer message of the same kind replaces the older one.
    //
    public static void ShowMessage(string tag, string title, string body)
    {
        try
        {
            Listen();

            new ToastContentBuilder()
                .AddArgument(ActionArgument, OpenAction)
                .AddText(title)
                .AddText(body)
                .Show(toast =>
                {
                    toast.Tag = tag;
                    toast.Group = MessageGroup;
                });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Updates", $"couldn't show the notification: {ex.Message}");
        }
    }

    // On exit: takes the plain messages back out of the Notification Centre (see the top).
    public static void RemoveMessages()
    {
        if (!_listening) return;

        try
        {
            ToastNotificationManagerCompat.History.RemoveGroup(MessageGroup);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Updates", $"couldn't clear the app's notifications: {ex.Message}");
        }
    }

    // Raised on a thread-pool thread, whether the app was running or has just been started by it.
    private static void OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        var dispatcher = Application.Current?.Dispatcher;

        if (args.TryGetValue(PageArgument, out var page) && page == InstalledPage)
        {
            AppLog.Info("Updates", "notification clicked - opening Installed on the updates");
            dispatcher?.BeginInvoke(AppNavigation.ShowInstalledUpdates);
        }
        else if (args.TryGetValue(ActionArgument, out var action) && action == OpenAction)
        {
            dispatcher?.BeginInvoke(AppTray.Open);
        }
    }
}
