using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.SpModApi;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // Before the window is shown, not on Loaded: WindowStartupLocation reads Width/Height while
        // it decides where to put the window, and by Loaded it has already decided. This also wires
        // F11/Escape and records the window's position on close - see WindowLayout.
        WindowLayout.Attach(this, RootTitleBar);

        // The hub tabs and the Workshop strips navigate through AppNavigation; this is the one real
        // navigation call behind it, and every navigation - whoever asked for it - is reported back.
        AppNavigation.Attach(pageType => RootNavigationView.Navigate(pageType));
        RootNavigationView.Navigated += (_, args) => AppNavigation.ReportNavigated(args.Page.GetType());
        AppNavigation.Navigated += (_, pageType) => SyncHeader(pageType);
        AppNavigation.CloseItemPageRequested += (_, _) =>
        {
            QuickView.Close();
            // The collection first, so the item page closing does not bring it back.
            _collectionUnderItem = false;
            CollectionPage.Close();
            ItemPage.Close();
        };

        Loaded += (_, _) =>
        {
            // The theme itself was applied at startup. This hooks up repainting the chrome when the
            // theme changes.
            AppTheme.Attach(this);

            // Steam's Workshop tab opens on the Workshop's front page.
            AppNavigation.Navigate(typeof(WorkshopHomePage));

            // Fire-and-forget: whether a newer build of this app exists on sp-mod.com has no
            // bearing on the window opening, and a failed check just leaves the banner down.
            _ = AppServices.AppUpdate.CheckOnStartupAsync();

            // Same arrangement, same reason: a server that is off, unreachable or simply not
            // configured just leaves the Server Map tab without anything to show, so nothing here is
            // worth holding the window open for.
            _ = AppServices.ServerMap.ConnectOnStartupAsync();
        };

        // A mod opened from anywhere opens as its Workshop item page, over the page it came from.
        AppServices.ModDetailsOverlay.Requested += (_, request) =>
        {
            QuickView.Close();

            // An item opened from a collection's page goes over it; closing the item brings it back.
            if (CollectionPage.Visibility == Visibility.Visible)
            {
                _collectionUnderItem = true;
                CollectionPage.Hide();
            }

            ItemPage.Show(request);
            PerfProbe.ItemPage(opened: true);
        };

        ItemPage.Closed += (_, _) =>
        {
            PerfProbe.ItemPage(opened: false);

            if (!_collectionUnderItem) return;

            _collectionUnderItem = false;
            CollectionPage.Reveal();
        };

        AppServices.QuickView.Requested += (_, request) => QuickView.Show(request);

        // A collection opened from anywhere opens as its Workshop page, over the page it came from -
        // and over an item page it was opened from, which then closes.
        AppServices.CollectionOverlay.Requested += (_, request) =>
        {
            // A list deleted since the link was drawn opens nothing, and leaves the page as it was.
            if (!request.IsPublic && (request.ListId is not { } listId || AppServices.ModLists.Find(listId) is null)) return;

            QuickView.Close();
            _collectionUnderItem = false;
            ItemPage.Close();
            CollectionPage.Show(request);
        };

        // Constructs and shows the mod update dialog, awaitable so callers know when it closes.
        AppServices.ModUpdateOverlay.ShowAsync = async mod =>
        {
            var dialog = new ModUpdateContentDialog(RootContentDialogPresenter, mod);
            AppServices.ModUpdateOverlay.IsOpen = true;
            try
            {
                await dialog.ShowAsync();
            }
            finally
            {
                AppServices.ModUpdateOverlay.IsOpen = false;
            }

            return dialog.ViewModel.MadeChanges;
        };
    }

    // True while an item page opened from a collection's page is over it.
    private bool _collectionUnderItem;

    // How many SteamDialogs are open over this window; the backdrop shows while any is.
    private int _modalDims;

    /// <summary>Dims the window behind a SteamDialog, as Steam dims the page behind its modals.</summary>
    public void SetModalDim(bool on)
    {
        _modalDims = Math.Max(0, _modalDims + (on ? 1 : -1));
        ModalDim.Visibility = _modalDims > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Lights the hub tab for the page on screen - the Workshop tab for every Workshop page but
    // Subscribed items, which has a tab of its own - and
    // shows the Workshop strip over the two Workshop pages that have no banner of their own.
    private void SyncHeader(Type pageType)
    {
        foreach (var tab in HubTabs.Children.OfType<ToggleButton>())
        {
            var target = tab.Tag as Type;
            tab.IsChecked = target == pageType
                || (target == typeof(WorkshopHomePage) && AppNavigation.IsWorkshopPage(pageType) && pageType != typeof(InstalledPage));
        }

        WorkshopStrip.Visibility = pageType == typeof(InstalledPage) || pageType == typeof(ModListsPage)
                || pageType == typeof(FollowedAuthorsPage)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HubTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: Type pageType }) return;

        AppNavigation.Navigate(pageType);

        // The click has already flipped this tab; put every tab back to what the page says.
        if (AppNavigation.Current is { } current) SyncHeader(current);
    }

    // Steam's "Store Page" button, pointed at the catalog this app browses.
    private void ModSite_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(new SpModApiOptions().BaseUrl) { UseShellExecute = true });

    // The banner's action takes the user to the update page to read what changed and decide there,
    // rather than starting a download straight off a banner.
    private void AppUpdateBanner_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Navigate(typeof(AppUpdatePage));
}
