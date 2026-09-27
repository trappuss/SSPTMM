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

        // A tab switched on or off in Options: the lit tab may have moved with it.
        AppServices.Appearance.PropertyChanged += (_, _) =>
        {
            if (AppNavigation.Current is { } current) SyncHeader(current);
        };
        AppNavigation.CloseItemPageRequested += (_, _) =>
        {
            QuickView.Close();
            CollectionQuickView.Close();
            // Forgotten first, so no page closing brings another back.
            _under.Clear();
            CollectionPage.Close();
            AuthorPage.Close();
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

            // A data file found damaged while the app was starting: said now the window is up.
            Dispatcher.BeginInvoke(App.ReportDataProblems, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };

        // A mod opened from anywhere opens as its Workshop item page, over the page it came from.
        AppServices.ModDetailsOverlay.Requested += (_, request) =>
        {
            Cover(ItemPage);
            ItemPage.Show(request);
            PerfProbe.ItemPage(opened: true);
        };

        ItemPage.Closed += (_, _) =>
        {
            PerfProbe.ItemPage(opened: false);
            Uncover(ItemPage);
        };

        AppServices.QuickView.Requested += (_, request) =>
        {
            CollectionQuickView.Close();
            QuickView.Show(request);
        };

        AppServices.CollectionQuickView.Requested += (_, request) =>
        {
            QuickView.Close();
            CollectionQuickView.Show(request);
        };

        // A collection opened from anywhere opens as its Workshop page, over the page it came from.
        AppServices.CollectionOverlay.Requested += (_, request) =>
        {
            // A list deleted since the link was drawn opens nothing, and leaves the page as it was.
            if (!request.IsPublic && (request.ListId is not { } listId || AppServices.ModLists.Find(listId) is null)) return;

            Cover(CollectionPage);
            CollectionPage.Show(request);
        };

        CollectionPage.Closed += (_, _) => Uncover(CollectionPage);

        // The item, collection and author pages have no background of their own, so the window's grid
        // (or picture) shows through them as it does on the other Workshop pages. The page they were
        // opened over is hidden meanwhile - Hidden, not Collapsed, so it keeps its layout and scroll
        // for when it comes back.
        foreach (var overlay in new FrameworkElement[] { ItemPage, AuthorPage, CollectionPage })
        {
            overlay.IsVisibleChanged += (_, _) =>
            {
                RootNavigationView.Visibility = OverlayShowing ? Visibility.Hidden : Visibility.Visible;
                SyncWorkshopStrip();
            };
        }

        // An author opened from anywhere opens as their Workshop page, the same way.
        AppServices.AuthorOverlay.Requested += (_, request) =>
        {
            Cover(AuthorPage);
            AuthorPage.Show(request);
        };

        AuthorPage.Closed += (_, _) => Uncover(AuthorPage);

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

    // ------------------------------------------------------------------ the Workshop pages over the page area

    //
    // A collection, an author and an item each have a page over the page area. One is on screen at a
    // time; the ones it was opened from wait hidden underneath, most recent last, and closing the
    // one on top brings back the one under it - an item opened from an author opened from a
    // collection walks back through all three. Opening one that is waiting underneath takes it out
    // from there: it now shows something new.
    //
    private readonly List<FrameworkElement> _under = [];

    private FrameworkElement? TopPage =>
        new FrameworkElement[] { ItemPage, AuthorPage, CollectionPage }.FirstOrDefault(p => p.Visibility == Visibility.Visible);

    private void Cover(FrameworkElement next)
    {
        QuickView.Close();
        CollectionQuickView.Close();
        _under.Remove(next);

        if (TopPage is not { } top || ReferenceEquals(top, next)) return;

        HidePage(top);
        _under.Add(top);
    }

    // After `closed` has closed: the page under it, if nothing else is on screen. One that has
    // nothing left to show is passed over for the one under it.
    private void Uncover(FrameworkElement closed)
    {
        _under.Remove(closed);

        while (TopPage is null && _under.Count > 0)
        {
            var previous = _under[^1];
            _under.RemoveAt(_under.Count - 1);
            RevealPage(previous);
        }
    }

    private void HidePage(FrameworkElement page)
    {
        if (ReferenceEquals(page, ItemPage)) ItemPage.Hide();
        else if (ReferenceEquals(page, AuthorPage)) AuthorPage.Hide();
        else CollectionPage.Hide();
    }

    private void RevealPage(FrameworkElement page)
    {
        if (ReferenceEquals(page, ItemPage)) ItemPage.Reveal();
        else if (ReferenceEquals(page, AuthorPage)) AuthorPage.Reveal();
        else CollectionPage.Reveal();
    }

    //
    // Closing while a mod's files are being placed would stop it part-way. It would be put back next
    // time (see InstallJournal), but the install the user asked for would not have happened - so they
    // are asked first.
    //
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var installing = AppServices.DownloadQueue.Items.FirstOrDefault(i => i.Status == ViewModels.DownloadQueueItemStatus.Installing);
        if (installing is not null && System.Windows.MessageBox.Show(
                this,
                Localization.LocalizationService.Text(Localization.Strings.App_CloseWhileInstallingFormat, installing.ModName),
                Localization.Strings.App_CloseWhileInstallingTitle,
                System.Windows.MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

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
        // A page with a tab of its own lights that tab; every other Workshop page lights Workshop -
        // Subscribed items and Your collections too, when their own tab is switched off.
        var appearance = AppServices.Appearance;
        var ownTab = (pageType == typeof(InstalledPage) && appearance.ShowSubscribedItemsTab)
            || (pageType == typeof(ModListsPage) && appearance.ShowCollectionsTab);

        foreach (var tab in HubTabs.Children.OfType<ToggleButton>())
        {
            var target = tab.Tag as Type;
            tab.IsChecked = target == pageType
                || (target == typeof(WorkshopHomePage) && AppNavigation.IsWorkshopPage(pageType) && !ownTab);
        }

        _stripWanted = pageType == typeof(InstalledPage) || pageType == typeof(ModListsPage)
            || pageType == typeof(FollowedAuthorsPage) || pageType == typeof(FavoriteCollectionsPage);
        SyncWorkshopStrip();
    }

    // Whether the page on screen carries the Workshop strip; it is kept down while an item's, a
    // collection's or an author's page is over it, which would otherwise show it through.
    private bool _stripWanted;

    // Hidden rather than Collapsed under a page: the row keeps its height, so the page underneath
    // is not resized - and its scroll position not clamped - while it waits.
    private void SyncWorkshopStrip() =>
        WorkshopStrip.Visibility = !_stripWanted ? Visibility.Collapsed
            : OverlayShowing ? Visibility.Hidden
            : Visibility.Visible;

    private bool OverlayShowing => ItemPage.IsVisible || AuthorPage.IsVisible || CollectionPage.IsVisible;

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
