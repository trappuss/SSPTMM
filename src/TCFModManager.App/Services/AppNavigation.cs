using TCFModManager.App.Views;

namespace TCFModManager.App.Services;

//
// One place that knows which page is on screen and how to change it.
//
// The Steam layout moves navigation out of NavigationView's own sidebar and into two strips of
// tabs - the app hub tabs across the top of the window, and the Workshop sub-navigation that sits
// under the banner on Browse and above Subscribed Items and Collections. Those strips live in
// different places (MainWindow, and inside BrowsePage's scrolling content), so neither can reach
// MainWindow's NavigationView directly, and both need to redraw their current tab whenever the page
// changes - including when something else changed it, like the update banner's "See what's new".
//
// MainWindow attaches the one real navigation call and reports every navigation back here; the tab
// strips only ever talk to this.
//
public static class AppNavigation
{
    private static Func<Type, bool>? _navigate;

    /// <summary>The page type currently on screen, or null before the first navigation.</summary>
    public static Type? Current { get; private set; }

    /// <summary>Raised after every navigation, with the new page's type.</summary>
    public static event EventHandler<Type>? Navigated;

    /// <summary>Raised when something asks for the Workshop item page to close - the hub tabs do,
    /// since choosing a tab should always land on that tab's page and not on an item left open
    /// over it.</summary>
    public static event EventHandler? CloseItemPageRequested;

    internal static void Attach(Func<Type, bool> navigate) => _navigate = navigate;

    internal static void ReportNavigated(Type pageType)
    {
        Current = pageType;
        Navigated?.Invoke(null, pageType);
    }

    public static void Navigate(Type pageType)
    {
        CloseItemPageRequested?.Invoke(null, EventArgs.Empty);

        // Navigating to the page that is already showing is a no-op inside NavigationView, and
        // raises nothing - report it anyway so a tab strip that was clicked re-checks its own tab.
        if (Current == pageType)
        {
            Navigated?.Invoke(null, pageType);
            return;
        }

        _navigate?.Invoke(pageType);
    }

    // The pages that belong to the Workshop: the hub's Workshop tab stays lit on each of them, and
    // the sub-navigation strip is shown for them.
    public static bool IsWorkshopPage(Type? pageType) =>
        pageType == typeof(WorkshopHomePage)
        || pageType == typeof(BrowsePage)
        || pageType == typeof(InstalledPage)
        || pageType == typeof(ModListsPage);
}
