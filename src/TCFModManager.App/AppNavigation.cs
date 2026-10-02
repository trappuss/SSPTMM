using System.Windows;
using TCFModManager.App.Help;
using TCFModManager.App.Views;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

//
// Getting to a page from somewhere else: an update notification's Open (§6), which goes to
// Installed with the Show filter on "Updates available", and Review and install on the Play page
// and the Server map page, which goes to Mod lists with a server's list selected and previewed.
//
// The request can arrive before there is anywhere to go: a click on a toast after the app has
// quit starts it, and the toast's arguments can land before the main window has loaded. So the
// request is held as a flag - the window opens on Installed when it's set, and the Installed page
// takes it and sets its filter, whether that page already exists or is built by this navigation.
//
//
// Fork: also the one place that knows which page is on screen for the Steam layout's tab strips -
// the hub tabs across the top of the window and the Workshop sub-navigation - which live in
// different places and redraw their current tab whenever the page changes (Current, Navigated), and
// the Workshop pages laid over the page area, which close when anything navigates
// (CloseItemPageRequested). Merged with the original's class of the same name in 1.19.
//
public static class AppNavigation
{
    private static NavigationView? _navigation;

    private static bool _showUpdatesPending;

    private static Guid? _reviewListPending;

    // Raised on the UI thread when an open Installed page should switch its filter to updates.
    public static event EventHandler? ShowUpdatesRequested;

    // True when the window should open on Installed rather than Browse.
    public static bool StartOnInstalled => _showUpdatesPending;

    // The page on screen, for the Help "?" and F1 - kept from Navigated rather than read from
    // SelectedItem, which a footer item or a Navigate(type) call doesn't always move.
    private static Type? _currentPage;

    private static string? _helpSectionPending;

    private static string? _helpTopicPending;

    // Raised on the UI thread when Help should open on a section - heard by a Help page that is
    // already on screen, where navigating to it again does nothing.
    public static event EventHandler? HelpRequested;

    // Called once the main window's navigation exists (MainWindow's Loaded).
    public static void Attach(NavigationView navigation)
    {
        _navigation = navigation;
        navigation.Navigated += (_, e) =>
        {
            _currentPage = e.Page?.GetType();
            if (_currentPage is { } page) ReportNavigated(page);
        };
    }

    // ------------------------------------------------------------------ fork: the Steam tab strips

    /// <summary>The page type currently on screen, or null before the first navigation.</summary>
    public static Type? Current { get; private set; }

    /// <summary>Raised after every navigation, with the new page's type.</summary>
    public static event EventHandler<Type>? Navigated;

    /// <summary>Raised when something asks for the Workshop pages laid over the page area (item,
    /// collection, author) to close - every navigation does, since going to a page should land on
    /// that page and not on an item left open over it.</summary>
    public static event EventHandler? CloseItemPageRequested;

    private static void ReportNavigated(Type pageType)
    {
        Current = pageType;
        Navigated?.Invoke(null, pageType);
    }

    // The pages that belong to the Workshop: the hub's Workshop tab stays lit on each of them, and
    // the sub-navigation strip is shown for them.
    public static bool IsWorkshopPage(Type? pageType) =>
        pageType == typeof(WorkshopHomePage)
        || pageType == typeof(BrowsePage)
        || pageType == typeof(CollectionsBrowsePage)
        || pageType == typeof(InstalledPage)
        || pageType == typeof(ModListsPage)
        || pageType == typeof(FollowedAuthorsPage)
        || pageType == typeof(FavoriteCollectionsPage);

    // ------------------------------------------------------------------

    //
    // The "?" in the title bar and F1: Help, at the section for the page that was showing. From
    // Help itself there is nowhere better to go, so it stays put.
    //
    public static void ShowHelpForCurrentPage()
    {
        if (_currentPage == typeof(HelpPage)) return;
        ShowHelp(HelpCatalog.SectionIdFor(_currentPage));
    }

    // Any page by type - the Open button on a Help topic, and every tab and link in the Steam layout.
    public static void Navigate(Type page)
    {
        CloseItemPageRequested?.Invoke(null, EventArgs.Empty);

        // Navigating to the page that is already showing is a no-op inside NavigationView, and
        // raises nothing - report it anyway so a tab strip that was clicked re-checks its own tab.
        if (Current == page)
        {
            Navigated?.Invoke(null, page);
            return;
        }

        _navigation?.Navigate(page);
    }

    // Help at one section - Getting started from the no-install banner, for one - or at one topic
    // in it, for a notice that has an answer.
    public static void ShowHelp(string sectionId, string? topicId = null)
    {
        _helpSectionPending = sectionId;
        _helpTopicPending = topicId;
        HelpRequested?.Invoke(null, EventArgs.Empty);
        Navigate(typeof(HelpPage));
    }

    // Consumed by the Help page: the section to open on, and the topic if one was named, once per request.
    public static (string SectionId, string? TopicId)? TakeHelpRequest()
    {
        if (_helpSectionPending is not { } section) return null;

        var topic = _helpTopicPending;
        _helpSectionPending = null;
        _helpTopicPending = null;
        return (section, topic);
    }

    //
    // Must be called on the UI thread. Sets the request first, so an Installed page built by the
    // Navigate below reads it in its constructor; an existing one hears the event instead.
    //
    public static void ShowInstalledUpdates()
    {
        _showUpdatesPending = true;
        ShowUpdatesRequested?.Invoke(null, EventArgs.Empty);

        if (Application.Current?.MainWindow is { } window) WindowActivation.BringForward(window);

        Navigate(typeof(InstalledPage));
    }

    //
    // Opens Mod lists on this list with its Preview already worked out. Stops there on purpose:
    // a server's list is never applied without the person seeing what it would do and pressing
    // Apply themselves.
    //
    public static void ReviewList(Guid listId)
    {
        _reviewListPending = listId;
        Navigate(typeof(ModListsPage));
    }

    // Consumed by the Mod lists page when it loads: the list to review, once per request.
    public static Guid? TakeReviewList()
    {
        var pending = _reviewListPending;
        _reviewListPending = null;
        return pending;
    }

    // Consumed by the Installed page: true once per request.
    public static bool TakeShowUpdates()
    {
        var pending = _showUpdatesPending;
        _showUpdatesPending = false;
        return pending;
    }
}
