using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
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

        // The hub tabs and the Workshop strips navigate through AppNavigation, which hears every
        // navigation once it is attached (on Loaded, below) and reports it back here.
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

        // A menu that stays open by itself would float over other windows once this one is left.
        Deactivated += (_, _) => CloseHubMenus();

        // F1 is the title bar's "?" (Help R8). Preview, so a focused text box doesn't get it first.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F1 || Keyboard.Modifiers != ModifierKeys.None) return;

            ShowHelp();
            e.Handled = true;
        };

        Loaded += (_, _) =>
        {
            HelpButtonToolTip();

            // The theme itself was applied at startup. This hooks up the two things that need a
            // window: repainting the chrome when the theme changes, and following Windows.
            AppTheme.Attach(this);

            // Subscribed items when this launch came from clicking an update notification (§6);
            // otherwise Steam's Workshop tab, on the Workshop's front page. Attached first, so a
            // click landing from here on navigates by itself.
            AppNavigation.Attach(RootNavigationView);
            AppNavigation.Navigate(AppNavigation.StartOnInstalled ? typeof(InstalledPage) : typeof(WorkshopHomePage));

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

        //
        // Running in the tray (§8a): with the setting on, closing hides the window instead, and the
        // tray icon goes as soon as the window is back, however it came back.
        //
        Closing += (_, e) =>
        {
            if (!AppTray.HidesOnClose()) return;

            e.Cancel = true;
            AppTray.HideToTray(this);
        };

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) AppTray.OnWindowShown();
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
    // Closing while a mod's files are being placed would stop it part-way, and the install the user
    // asked for would not have happened - so they are asked first. Not when closing only hides the
    // window to the tray (the install carries on), nor once the app is quitting, which no answer
    // here could stop.
    //
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (AppTray.IsQuitting || AppTray.HidesOnClose())
        {
            base.OnClosing(e);
            return;
        }

        if (!MayStopAnInstall(this))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    // Whether ending the app now is all right: asked only while a mod's files are being placed.
    // The tray's Quit asks it too, since with the window hidden it never gets OnClosing's turn.
    public static bool MayStopAnInstall(Window? owner)
    {
        var installing = AppServices.DownloadQueue.Items.FirstOrDefault(i => i.Status == ViewModels.DownloadQueueItemStatus.Installing);
        if (installing is null) return true;

        var text = Localization.LocalizationService.Text(Localization.Strings.App_CloseWhileInstallingFormat, installing.ModName);
        var answer = owner is { IsVisible: true }
            ? System.Windows.MessageBox.Show(owner, text, Localization.Strings.App_CloseWhileInstallingTitle, System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : System.Windows.MessageBox.Show(text, Localization.Strings.App_CloseWhileInstallingTitle, System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return answer == System.Windows.MessageBoxResult.Yes;
    }

    // How many SteamDialogs are open over this window; the backdrop shows while any is.
    private int _modalDims;

    /// <summary>Dims the window behind a SteamDialog, as Steam dims the page behind its modals.</summary>
    public void SetModalDim(bool on)
    {
        _modalDims = Math.Max(0, _modalDims + (on ? 1 : -1));
        ModalDim.Visibility = _modalDims > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // The pages under each hub menu tab. The tab is lit while any of them is on screen.
    private static readonly Type[] ToolsPages =
        [typeof(ConfigsPage), typeof(DependenciesPage), typeof(FootprintPage), typeof(ServerMapPage)];

    private static readonly Type[] HelpPages = [typeof(HelpPage), typeof(AppUpdatePage)];

    // Lights the hub tab for the page on screen - the Workshop tab for every Workshop page but
    // Subscribed items and Collections while they have tabs of their own, Tools and Help for the
    // pages in their menus, the gear for Options - and shows the Workshop strip over the Workshop
    // pages that have no banner of their own. Downloads lights nothing: it is the bar at the bottom.
    private void SyncHeader(Type pageType)
    {
        // A page with a tab of its own lights that tab; every other Workshop page lights Workshop -
        // Subscribed items and Your collections too, when their own tab is switched off.
        var appearance = AppServices.Appearance;
        var ownTab = (pageType == typeof(InstalledPage) && appearance.ShowSubscribedItemsTab)
            || (pageType == typeof(ModListsPage) && appearance.ShowCollectionsTab);

        foreach (var tab in HubTabs.Children.OfType<ToggleButton>())
        {
            if (tab.Tag is not Type target) continue;
            tab.IsChecked = target == pageType
                || (target == typeof(WorkshopHomePage) && AppNavigation.IsWorkshopPage(pageType) && !ownTab);
        }

        ToolsTab.IsChecked = ToolsPages.Contains(pageType);
        HelpTab.IsChecked = HelpPages.Contains(pageType);
        OptionsGear.IsChecked = pageType == typeof(OptionsPage);

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

    // ------------------------------------------------------------------ the Tools and Help menus

    // How long the pointer can be off both a tab and its menu before the menu closes - the
    // Workshop strip's own delay, so every menu in the window behaves alike.
    private static readonly TimeSpan HubMenuCloseDelay = TimeSpan.FromMilliseconds(250);

    private System.Windows.Threading.DispatcherTimer? _hubMenuTimer;

    private (Popup Popup, ToggleButton Tab, FrameworkElement Panel)[] HubMenus =>
    [
        (ToolsPopup, ToolsTab, ToolsPanel),
        (HelpPopup, HelpTab, HelpPanel),
    ];

    // Under the pointer, a tab's menu opens - and the other closes.
    private void HubMenu_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hubMenuTimer?.Stop();
        foreach (var (popup, tab, _) in HubMenus) popup.IsOpen = ReferenceEquals(tab, sender);
    }

    private void HubMenu_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_hubMenuTimer is null)
        {
            _hubMenuTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input)
            {
                Interval = HubMenuCloseDelay,
            };
            _hubMenuTimer.Tick += (_, _) =>
            {
                _hubMenuTimer.Stop();
                foreach (var (popup, tab, panel) in HubMenus)
                {
                    if (popup.IsOpen && !tab.IsMouseOver && !panel.IsMouseOver) popup.IsOpen = false;
                }
            };
        }

        _hubMenuTimer.Start();
    }

    // A click on the tab itself opens its menu too (for a touch screen, or a pointer that came in
    // from the side); it goes nowhere by itself, as Steam's Your Items does not.
    private void HubMenuTab_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (popup, tab, _) in HubMenus) popup.IsOpen = ReferenceEquals(tab, sender);
        if (AppNavigation.Current is { } current) SyncHeader(current);
    }

    private void HubMenuEntry_Click(object sender, RoutedEventArgs e)
    {
        CloseHubMenus();
        if (sender is FrameworkElement { Tag: Type pageType }) AppNavigation.Navigate(pageType);
    }

    private void HubMenu_Closed(object? sender, EventArgs e)
    {
        if (AppNavigation.Current is { } current) SyncHeader(current);
    }

    private void CloseHubMenus()
    {
        _hubMenuTimer?.Stop();
        foreach (var (popup, _, _) in HubMenus) popup.IsOpen = false;
    }

    // Steam's "Store Page" button, pointed at the catalog this app browses.
    private void ModSite_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(new SpModApiOptions().BaseUrl) { UseShellExecute = true });

    private void RootTitleBar_HelpClicked(TitleBar sender, RoutedEventArgs e) => ShowHelp();

    //
    // The "?" and F1. A ContentDialog (mod details, mod update) sits over the page inside this
    // window, so both still reach the window while it is up - and Help would open underneath it,
    // hidden. The dialog is closed first, the same as its Close button, and Help opens at the
    // section for the page it was over. The other dialogs are separate modal windows, which keep
    // this window from getting either.
    //
    private void ShowHelp()
    {
        if (RootContentDialogPresenter.Content is ContentDialog dialog) dialog.Hide(ContentDialogResult.None);

        AppNavigation.ShowHelpForCurrentPage();
    }

    private void HowToSetUp_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.ShowHelp(HelpCatalog.StartSectionId);

    //
    // The caption "?" is a template part with no tooltip of its own. Bound rather than set, so it
    // follows a language change like every {loc:Str}. Whether Windows shows it depends on the
    // caption hit-testing handing the mouse to WPF there; if it doesn't, nothing is lost.
    //
    private void HelpButtonToolTip()
    {
        if (RootTitleBar.Template?.FindName("PART_HelpButton", RootTitleBar) is not FrameworkElement button) return;

        button.SetBinding(ToolTipProperty, new Binding($"[{nameof(Strings.Help_TitleBarToolTip)}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
        });
    }

    // The banner's action takes the user to the update page to read what changed and decide there,
    // rather than starting a download straight off a banner.
    private void AppUpdateBanner_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.Navigate(typeof(AppUpdatePage));
}
