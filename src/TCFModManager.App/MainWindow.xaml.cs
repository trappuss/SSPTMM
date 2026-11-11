using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using TCFModManager.App.Help;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Wpf.Ui.Controls;

namespace TCFModManager.App;

public partial class MainWindow : FluentWindow
{
    // Fork (1.4.3): how pages change with Animations on (Behaviors/Motion).
    internal Wpf.Ui.Animations.Transition PageTransition { get; private set; }

    //
    // Fork (1.4.3): an archive dropped from Explorer anywhere on the window installs as one dropped
    // on Subscribed items does - which is opened, for its questions and what it says. Subscribed
    // items settles its own drops first; anything that isn't a file is left to the page under it.
    //
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        // Nothing lands behind the update dialog.
        if (Blocked)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Fork (1.4.3): a link to a mod, dragged from a browser, opens its page here.
        if (DroppedModLink(e.Data) is not null)
        {
            e.Effects = DragDropEffects.Link;
            e.Handled = true;
            return;
        }

        // Anything else no page took (text, a card dragged out of its list) has nowhere to go.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(InstalledViewModel.IsModArchive)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (Blocked)
        {
            e.Handled = true;
            return;
        }

        if (DroppedModLink(e.Data) is { } link)
        {
            e.Handled = true;
            MarkupActions.OpenLink(link);
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || !files.Any(InstalledViewModel.IsModArchive)) return;

        e.Effects = DragDropEffects.Copy;
        var kept = InstalledPage.KeepDroppedTempFiles(files);

        // Not while OLE is still waiting for the drop to finish (as InstalledPage).
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        AppNavigation.Navigate(typeof(InstalledPage));
        await InstalledViewModel.ForActions.InstallArchivesAsync(kept);
    }

    // A browser's link drag carries the address as text; a file drag never is one.
    internal static string? DroppedModLink(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop)) return null;

        try
        {
            return MarkupActions.ModLinkIn(data.GetData(DataFormats.UnicodeText) as string);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // Fork (1.4.3): Ctrl+V outside a text box, with a mod's sp-mod.com link copied.
    private static string? ModLinkOnClipboard()
    {
        try
        {
            return Clipboard.ContainsText() ? MarkupActions.ModLinkIn(Clipboard.GetText()) : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null; // the clipboard is held by another program just now
        }
    }

    //
    // Fork (1.4.3): Ctrl+F to the page's search box, F5 its Rescan or Refresh, Ctrl+V a mod's link,
    // and the interface size (Help > Keyboard shortcuts). True when it was one of them.
    //
    private bool ShortcutKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var scope = (DependencyObject?)TopPage ?? RootNavigationView;
        var page = !Blocked; // with the update dialog open, the page behind it is left alone

        if (page && ctrl && key == Key.F && Behaviors.Shortcuts.SearchBoxIn(scope) is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
        else if (page && key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None
                 && Behaviors.Shortcuts.RefreshIn(scope) is { } refresh && refresh.CanExecute(null))
        {
            refresh.Execute(null);
        }
        else if (page && ctrl && key == Key.V
                 && Keyboard.FocusedElement is not (System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox)
                 && ModLinkOnClipboard() is { } link)
        {
            MarkupActions.OpenLink(link);
        }
        else if (ctrl && key is Key.OemPlus or Key.Add) Behaviors.InterfaceScale.Step(+1);
        else if (ctrl && key is Key.OemMinus or Key.Subtract) Behaviors.InterfaceScale.Step(-1);
        else if (ctrl && key is Key.D0 or Key.NumPad0) Behaviors.InterfaceScale.Set(1.0);
        else return false;

        e.Handled = true;
        return true;
    }

    //
    // Fork (1.4.3): a queue that finishes while the window is out of sight says so - a Windows
    // notification from the tray, the taskbar button flashing when minimised or behind others.
    //
    private void OnQueueFinished(object? sender, DownloadQueueFinished done)
    {
        if (!IsVisible)
        {
            UpdateToasts.ShowMessage("downloads", Strings.Downloads_FinishedTitle,
                LocalizationService.Text(Strings.Downloads_FinishedBodyFormat, done.Completed, done.Failed));
        }
        else if (WindowState == WindowState.Minimized || !IsActive)
        {
            WindowActivation.Flash(this);
        }
    }

    public MainWindow()
    {
        InitializeComponent();

        // Fork (1.4.3): Options > Look > Interface size, as saved.
        Behaviors.InterfaceScale.Attach(RootLayout);
        AppServices.DownloadQueue.QueueFinished += OnQueueFinished;

        // Fork (1.4.3): Options > Animations off - pages change without sliding in.
        PageTransition = RootNavigationView.Transition;
        if (!Behaviors.Motion.Enabled) RootNavigationView.Transition = Wpf.Ui.Animations.Transition.None;

        // Before the window is shown, not on Loaded: WindowStartupLocation reads Width/Height while
        // it decides where to put the window, and by Loaded it has already decided. This also wires
        // F11/Escape and records the window's position on close - see WindowLayout.
        WindowLayout.Attach(this, RootTitleBar);

        // The hub tabs and the Workshop strips navigate through AppNavigation, which hears every
        // navigation once it is attached (on Loaded, below) and reports it back here.
        AppNavigation.Navigated += (_, pageType) =>
        {
            SyncHeader(pageType);
            Visited();
            AppNavigation.RememberPage(pageType);
        };
        _history.Changed += (_, _) =>
        {
            BackButton.IsEnabled = _history.CanGoBack;
            ForwardButton.IsEnabled = _history.CanGoForward;
        };

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
            // On the way to another page: closing these is not going back to the one under them.
            var moving = _moving;
            _moving = true;
            try
            {
                CollectionPage.Close();
                AuthorPage.Close();
                ItemPage.Close();
            }
            finally
            {
                _moving = moving;
            }
        };

        // A menu that stays open by itself would float over other windows once this one is left.
        Deactivated += (_, _) => CloseHubMenus();

        // F1 is the title bar's "?" (Help R8). Preview, so a focused text box doesn't get it first.
        PreviewKeyDown += (_, e) =>
        {
            if (BackForwardKey(e)) return;
            if (ShortcutKey(e)) return;
            if (e.Key != Key.F1 || Keyboard.Modifiers != ModifierKeys.None) return;

            ShowHelp();
            e.Handled = true;
        };

        // The mouse's own back and forward buttons, as in Steam and a browser.
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.XButton1) GoBack();
            else if (e.ChangedButton == MouseButton.XButton2) GoForward();
            else return;

            e.Handled = true;
        };

        Loaded += (_, _) =>
        {
            HelpButtonToolTip();

            // The theme itself was applied at startup. This hooks up the two things that need a
            // window: repainting the chrome when the theme changes, and following Windows.
            AppTheme.Attach(this);

            // Subscribed items when this launch came from clicking an update notification (§6);
            // otherwise the Home page chosen in Options (the Workshop's front page unless changed).
            // Attached first, so a
            // click landing from here on navigates by itself.
            AppNavigation.Attach(RootNavigationView);
            AppNavigation.Navigate(AppNavigation.StartOnInstalled ? typeof(InstalledPage) : AppNavigation.HomePage);

            // Fire-and-forget: whether a newer SSPTMM is out on GitHub has no bearing on the window
            // opening, and a failed check just leaves the dot off. It asks first (once), and checks
            // only with the user's yes.
            _ = AppServices.AppUpdate.CheckOnStartupAsync();

            // Same arrangement, same reason: a server that is off, unreachable or simply not
            // configured just leaves the Server Map tab without anything to show, so nothing here is
            // worth holding the window open for.
            _ = AppServices.ServerMap.ConnectOnStartupAsync();

            Services.GameCloser.Start(); // Fork: close the game when the server stops, if ticked

            // Fork (1.4.7): a Find the broken mod search the last run did not finish - its mods go back on.
            Dispatcher.BeginInvoke(() => _ = AppServices.BrokenModFinder.RecoverAsync(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

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
            OpenItem(request);
            Visited();
        };

        // Esc on a required item, back to the item it was opened from: a step back.
        ItemPage.WalkedBack += (_, _) => Returned();

        ItemPage.Closed += (_, _) =>
        {
            PerfProbe.ItemPage(opened: false);
            Uncover(ItemPage);
            Returned();
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
            if (OpenCollection(request)) Visited();
        };

        CollectionPage.Closed += (_, _) =>
        {
            Uncover(CollectionPage);
            Returned();
        };

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
            OpenAuthor(request);
            Visited();
        };

        AuthorPage.Closed += (_, _) =>
        {
            Uncover(AuthorPage);
            Returned();
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

    private void OpenItem(ModDetailsRequest request, bool again = false)
    {
        Cover(ItemPage);
        ItemPage.Show(request, again);
        PerfProbe.ItemPage(opened: true);
    }

    // False when it opened nothing: a list deleted since the link was drawn leaves the page as it was.
    private bool OpenCollection(CollectionRequest request)
    {
        if (!request.IsPublic && (request.ListId is not { } listId || AppServices.ModLists.Find(listId) is null)) return false;

        Cover(CollectionPage);
        _opened[CollectionPage] = request;
        CollectionPage.Show(request);
        return true;
    }

    private void OpenAuthor(AuthorRequest request)
    {
        Cover(AuthorPage);
        _opened[AuthorPage] = request;
        AuthorPage.Show(request);
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

        if (!AppServices.BrokenModFinder.MayClose() || !MayStopAnInstall(this) || !MayLeaveServerRunning()) // Fork (1.4.7): a search first
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
        // Fork (1.3.0): Steam's modal - over the window when it shows, mid-screen from the tray.
        _ = owner;
        var answer = TCFModManager.App.Views.SteamMessageBox.Show(
            text, Localization.Strings.App_CloseWhileInstallingTitle, System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);
        return answer == System.Windows.MessageBoxResult.Yes;
    }

    //
    // Fork (1.4.7): closing SSPTMM leaves the SPT server running - with no window at all, if it was
    // started hidden - so while it runs the user is asked first, unless the question's "Don't ask
    // again" (or Options > Play) turned that off. The tray's Quit asks it too.
    //
    public static bool MayLeaveServerRunning()
    {
        var settingsService = new SettingsService();
        if (!settingsService.Load().ConfirmCloseWithServer) return true;
        if (!SptLaunchService.Describe(AppServices.SptEnvironment.InstallPath, SptLaunchTarget.Server).IsRunning) return true;

        var body = new System.Windows.Controls.StackPanel();
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Strings.App_CloseWithServerBody,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            Margin = new Thickness(0, 0, 0, 12),
        });

        // As the unsubscribe question's tick box: no padding before the box, the dialog's own colours.
        var dontAsk = new System.Windows.Controls.CheckBox
        {
            Content = Strings.App_CloseWithServerDontAsk,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(0, 5, 11, 6),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAC, 0xB2, 0xB8)),
        };
        body.Children.Add(dontAsk);
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Strings.App_CloseWithServerDontAskNote,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 480,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0x98, 0xA0)),
        });

        var answer = SteamDialog.Show(
            Strings.App_CloseWithServerTitle,
            body,
            new SteamDialogChoice(Strings.App_CloseWithServerClose, SteamDialogButton.Blue, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
        if (answer != 0) return false;

        // Ticked and then cancelled changes nothing: the box goes with the answer.
        if (dontAsk.IsChecked == true)
        {
            var settings = settingsService.Load();
            settings.ConfirmCloseWithServer = false;
            settingsService.Save(settings);
            AppLog.Info("Options", "close-with-server question turned off from the question");
        }

        return true;
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
        [typeof(ConfigsPage), typeof(DependenciesPage), typeof(DiagnosePage), typeof(FootprintPage), typeof(ServerMapPage)];

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
        // Fork: the Collections tab opens Your collections; the Manage page under it keeps it lit.
        var collections = pageType == typeof(YourCollectionsPage) || pageType == typeof(ModListsPage);
        var ownTab = (pageType == typeof(InstalledPage) && appearance.ShowSubscribedItemsTab)
            || (collections && appearance.ShowCollectionsTab);

        foreach (var tab in HubTabs.Children.OfType<ToggleButton>())
        {
            if (tab.Tag is not Type target) continue;
            tab.IsChecked = target == pageType
                || (target == typeof(YourCollectionsPage) && collections)
                || (target == typeof(WorkshopHomePage) && AppNavigation.IsWorkshopPage(pageType) && !ownTab);
        }

        ToolsTab.IsChecked = ToolsPages.Contains(pageType);
        HelpTab.IsChecked = HelpPages.Contains(pageType);
        OptionsGear.IsChecked = pageType == typeof(OptionsPage);

        _stripWanted = pageType == typeof(InstalledPage) || pageType == typeof(ModListsPage) || pageType == typeof(YourCollectionsPage)
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

    // ------------------------------------------------------------------ fork (1.4.2): back, forward, home

    //
    // Steam's arrows at the left of its top bar, and its Home. A place is the page on screen and
    // the item, collection or author page over it, if any - kept as the request that opened it, so
    // going back opens it again. The history itself is NavigationHistory: going somewhere new
    // drops what was ahead, and closing a page back to the place before counts as a step back.
    //
    // Opened again, an item page starts at its top, and the pages that were under it are not
    // stacked back up: the history walks back through them instead.
    //
    private sealed record Place(Type Page, object? Over);

    private readonly NavigationHistory<Place> _history = new();

    private readonly Dictionary<FrameworkElement, object> _opened = [];

    // True while Back or Forward, or a navigation closing the pages over the page area, is under
    // way: the places passed through on the way are not places visited.
    private bool _moving;

    // The item page says which item it shows itself: Esc walks it back through required items.
    private Place? Here() => AppNavigation.Current is { } page
        ? new Place(page, TopPage is not { } top ? null
            : ReferenceEquals(top, ItemPage) ? ItemPage.Current
            : _opened.GetValueOrDefault(top))
        : null;

    private void Visited()
    {
        if (!_moving && Here() is { } here) _history.Visit(here);
    }

    private void Returned()
    {
        if (!_moving && Here() is { } here) _history.Return(here);
    }

    private void GoBack() => GoTo(Blocked ? null : _history.Back());

    private void GoForward() => GoTo(Blocked ? null : _history.Forward());

    private void GoHome()
    {
        if (!Blocked) AppNavigation.Navigate(AppNavigation.HomePage);
    }

    // The mod update dialog is over the window: it is closed by its own buttons, not navigated away from.
    private static bool Blocked => AppServices.ModUpdateOverlay.IsOpen;

    private void GoTo(Place? place)
    {
        if (place is null) return;

        _moving = true;
        try
        {
            AppNavigation.Navigate(place.Page);
            var opened = true;
            switch (place.Over)
            {
                case ModDetailsRequest item:
                    OpenItem(item, again: true);
                    break;
                case CollectionRequest collection:
                    opened = OpenCollection(collection);
                    break;
                case AuthorRequest author:
                    OpenAuthor(author);
                    break;
            }

            // What it showed can't be shown again (a collection deleted since): the history stands on
            // the page that is shown instead.
            if (!opened) _history.ReplaceCurrent(new Place(place.Page, null));
        }
        finally
        {
            _moving = false;
        }
    }

    // Alt+Left, Alt+Right and Alt+Home, as in a browser, and a keyboard's own back, forward and
    // home keys. True when it was one of them.
    private bool BackForwardKey(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;

        if (key == Key.BrowserBack || (alt && key == Key.Left)) GoBack();
        else if (key == Key.BrowserForward || (alt && key == Key.Right)) GoForward();
        else if (key == Key.BrowserHome || (alt && key == Key.Home)) GoHome();
        else return false;

        e.Handled = true;
        return true;
    }

    // Right-click on an arrow: the places that way, nearest first, as in Steam and a browser.
    private void HistoryMenu_Opening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        var menu = ((FrameworkElement)sender).ContextMenu!;
        menu.Items.Clear();

        var forward = ReferenceEquals(sender, ForwardButton);
        var entries = _history.Entries;
        var step = forward ? 1 : -1;
        // HUNCH: fifteen, as a browser's list shows about that many.
        for (var (i, shown) = (_history.Index + step, 0); i >= 0 && i < entries.Count && shown < 15; i += step, shown++)
        {
            var index = i;
            var item = new System.Windows.Controls.MenuItem
            {
                Header = PlaceName(entries[i]),
                Style = (Style)Application.Current.FindResource("SteamPopupMenuItem"),
            };
            item.Click += (_, _) => GoTo(Blocked ? null : _history.MoveTo(index));
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0) e.Handled = true;
    }

    // What a place is called in that list: the mod, collection or author over the page, else the page.
    private static string PlaceName(Place place) => place.Over switch
    {
        ModDetailsRequest { Mod.Name: { Length: > 0 } name } => name,
        AuthorRequest { Name: { Length: > 0 } name } => name,
        CollectionRequest { ListId: { } id } when AppServices.ModLists.Find(id) is { } list => list.Name,
        CollectionRequest { PublicSlug: { Length: > 0 } slug } => slug.Replace('-', ' '),
        _ => place.Page.Name switch
        {
            nameof(WorkshopHomePage) => Strings.Options_HomeWorkshop,
            nameof(CollectionsBrowsePage) => Strings.Collections_Title,
            nameof(YourCollectionsPage) => Strings.Workshop_YourCollections,
            nameof(FavoriteCollectionsPage) => Strings.Favorites_Title,
            nameof(FollowedAuthorsPage) => Strings.FollowedAuthors_Title,
            nameof(HelpPage) => Strings.Nav_Help,
            _ => HelpCatalog.PageName(place.Page),
        },
    };

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void Forward_Click(object sender, RoutedEventArgs e) => GoForward();

    private void Home_Click(object sender, RoutedEventArgs e) => GoHome();

    // ------------------------------------------------------------------

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
        Process.Start(new ProcessStartInfo(SpModApiOptions.OfficialSite) { UseShellExecute = true });

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
