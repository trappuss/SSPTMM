using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Services;

namespace TCFModManager.App.Views;

// Home / Browse / Your Items under the Workshop banner. See the header of the XAML.
public partial class WorkshopSubNav : UserControl
{
    public WorkshopSubNav()
    {
        InitializeComponent();

        // Subscribed while on screen only: a BrowsePage and MainWindow each carry one of these, and
        // a strip that has been unloaded has nothing to redraw.
        Loaded += (_, _) =>
        {
            AppNavigation.Navigated += OnNavigated;
            SyncToPage(AppNavigation.Current);
        };
        Unloaded += (_, _) => AppNavigation.Navigated -= OnNavigated;
    }

    private void OnNavigated(object? sender, Type pageType) => SyncToPage(pageType);

    // Lights the entry for the page on screen. Your Items stands for both pages in its menu.
    private void SyncToPage(Type? pageType)
    {
        HomeTab.IsChecked = pageType == typeof(WorkshopHomePage);
        BrowseTab.IsChecked = pageType == typeof(BrowsePage);
        YourItemsTab.IsChecked = pageType == typeof(InstalledPage) || pageType == typeof(ModListsPage)
            || pageType == typeof(FollowedAuthorsPage);
    }

    private void HomeTab_Click(object sender, RoutedEventArgs e) => Go(typeof(WorkshopHomePage));

    private void BrowseTab_Click(object sender, RoutedEventArgs e) => Go(typeof(BrowsePage));

    // Opens the menu; the click itself changes no page, so the tab goes back to saying whether one
    // of its own pages is showing.
    private void YourItemsTab_Click(object sender, RoutedEventArgs e)
    {
        SyncToPage(AppNavigation.Current);
        YourItemsPopup.IsOpen = true;
    }

    private void YourItemsPopup_Closed(object? sender, EventArgs e) => SyncToPage(AppNavigation.Current);

    private void SubscribedItems_Click(object sender, RoutedEventArgs e)
    {
        YourItemsPopup.IsOpen = false;
        Go(typeof(InstalledPage));
    }

    private void YourCollections_Click(object sender, RoutedEventArgs e)
    {
        YourItemsPopup.IsOpen = false;
        Go(typeof(ModListsPage));
    }

    private void FollowedAuthors_Click(object sender, RoutedEventArgs e)
    {
        YourItemsPopup.IsOpen = false;
        Go(typeof(FollowedAuthorsPage));
    }

    private void Go(Type pageType)
    {
        AppNavigation.Navigate(pageType);

        // A click toggles the button before anything navigates; put it back to what the page says.
        SyncToPage(AppNavigation.Current);
    }
}
