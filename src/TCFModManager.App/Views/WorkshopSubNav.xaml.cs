using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TCFModManager.App.Services;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// Home / Browse / Your Items under the Workshop banner. See the header of the XAML.
public partial class WorkshopSubNav : UserControl
{
    // How long the pointer can be off both a tab and its menu before the menu closes: long enough
    // to cross from one to the other, short enough that leaving feels like leaving.
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _closeTimer;

    public WorkshopSubNav()
    {
        InitializeComponent();

        _closeTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = CloseDelay };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            CloseMenusNotUnderPointer();
        };

        // Subscribed while on screen only: a BrowsePage and MainWindow each carry one of these, and
        // a strip that has been unloaded has nothing to redraw.
        Loaded += (_, _) =>
        {
            AppNavigation.Navigated += OnNavigated;
            SyncToPage(AppNavigation.Current);

            // A menu that stays open by itself would float over other windows once this one is
            // left - alt-tab with the pointer still on it.
            _window = Window.GetWindow(this);
            if (_window is not null) _window.Deactivated += Window_Deactivated;
        };
        Unloaded += (_, _) =>
        {
            AppNavigation.Navigated -= OnNavigated;
            if (_window is not null) _window.Deactivated -= Window_Deactivated;
            _window = null;
            _closeTimer.Stop();
            BrowsePopup.IsOpen = false;
            YourItemsPopup.IsOpen = false;
        };
    }

    private void OnNavigated(object? sender, Type pageType) => SyncToPage(pageType);

    private Window? _window;

    private void Window_Deactivated(object? sender, EventArgs e) => CloseMenus();

    // Lights the entry for the page on screen. Browse stands for both browse pages, Your Items for
    // every page in its menu.
    private void SyncToPage(Type? pageType)
    {
        HomeTab.IsChecked = pageType == typeof(WorkshopHomePage);
        BrowseTab.IsChecked = pageType == typeof(BrowsePage) || pageType == typeof(CollectionsBrowsePage);
        YourItemsTab.IsChecked = pageType == typeof(InstalledPage) || pageType == typeof(ModListsPage) || pageType == typeof(YourCollectionsPage)
            || pageType == typeof(FollowedAuthorsPage) || pageType == typeof(FavoriteCollectionsPage);
    }

    // ------------------------------------------------------------------ the menus

    private (Popup Popup, FrameworkElement Tab, FrameworkElement Panel)[] Menus =>
    [
        (BrowsePopup, BrowseTab, BrowsePanel),
        (YourItemsPopup, YourItemsTab, YourItemsPanel),
    ];

    // Under the pointer, a tab's menu opens - and any other closes.
    private void Menu_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _closeTimer.Stop();

        foreach (var (popup, tab, _) in Menus)
        {
            var mine = ReferenceEquals(tab, sender);
            if (mine && !popup.IsOpen) popup.StaysOpen = true;
            popup.IsOpen = mine;
        }
    }

    private void Menu_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => _closeTimer.Start();

    private void CloseMenusNotUnderPointer()
    {
        foreach (var (popup, tab, panel) in Menus)
        {
            if (popup.IsOpen && !tab.IsMouseOver && !panel.IsMouseOver) popup.IsOpen = false;
        }
    }

    private void CloseMenus()
    {
        _closeTimer.Stop();
        foreach (var (popup, _, _) in Menus) popup.IsOpen = false;
    }

    private void Popup_Closed(object? sender, EventArgs e) => SyncToPage(AppNavigation.Current);

    // ------------------------------------------------------------------ the entries

    private void HomeTab_Click(object sender, RoutedEventArgs e) => Go(typeof(WorkshopHomePage));

    private void BrowseTab_Click(object sender, RoutedEventArgs e)
    {
        CloseMenus();
        Go(typeof(BrowsePage));
    }

    // Browse > an items sort order: the items page, sorted that way.
    private void BrowseMods_Click(object sender, RoutedEventArgs e)
    {
        CloseMenus();
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ModSortOrder>(tag, out var order))
            AppServices.Browse.ShowSortedBy(order);

        Go(typeof(BrowsePage));
    }

    private void BrowseCollections_Click(object sender, RoutedEventArgs e)
    {
        CloseMenus();
        Go(typeof(CollectionsBrowsePage));
    }

    // A click opens the menu as well as the pointer does - on a touch screen there is no pointer -
    // and changes no page, so the tab goes back to saying whether one of its own pages is showing.
    //
    // Opened this way (the keyboard, a touch) the pointer may never leave anything, so the menu
    // closes the ordinary way instead: a click anywhere else. Under the pointer it stays open by
    // itself and closes when the pointer leaves - see Menu_MouseEnter.
    //
    private void YourItemsTab_Click(object sender, RoutedEventArgs e)
    {
        SyncToPage(AppNavigation.Current);
        _closeTimer.Stop();
        BrowsePopup.IsOpen = false;

        if (!YourItemsTab.IsMouseOver)
        {
            YourItemsPopup.IsOpen = false;
            YourItemsPopup.StaysOpen = false;
        }

        YourItemsPopup.IsOpen = true;
    }

    private void SubscribedItems_Click(object sender, RoutedEventArgs e)
    {
        CloseMenus();
        Go(typeof(InstalledPage));
    }

    private void YourCollections_Click(object sender, RoutedEventArgs e)
    {
        CloseMenus();
        // Fork: your collections as Steam lists them; the form that edits one is its Manage.
        Go(typeof(YourCollectionsPage));
    }

    private void FollowedAuthors_Click(object sender, RoutedEventArgs e)
    {
        CloseMenus();
        Go(typeof(FollowedAuthorsPage));
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        CloseMenus();
        Go(typeof(FavoriteCollectionsPage));
    }

    private void Go(Type pageType)
    {
        AppNavigation.Navigate(pageType);

        // A click toggles the button before anything navigates; put it back to what the page says.
        SyncToPage(AppNavigation.Current);
    }
}
