using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TCFModManager.App.Behaviors;
using TCFModManager.App.ViewModels;

using TCFModManager.Core.Services;

namespace TCFModManager.App.Views;

public partial class BrowsePage : Page
{
    // Shared app-lifetime ViewModel so results persist across navigations.
    public BrowseViewModel ViewModel { get; } = AppServices.Browse;

    public BrowsePage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // A new page of results starts at the top of the grid, as a Steam page load does - not at
        // the bottom of the page, where the pager that was just clicked is.
        ViewModel.NavigatedToPage += (_, _) => ScrollToResults();
    }

    private async void BrowsePage_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Debug("Browse", "Loaded: start");

        // Run the initial search once per app session; loads default results.
        if (!ViewModel.HasLoadedResults)
        {
            AppLog.Debug("Browse", "Loaded: about to await SearchCommand.ExecuteAsync");
            await ViewModel.SearchCommand.ExecuteAsync(null);
            AppLog.Debug("Browse", "Loaded: SearchCommand.ExecuteAsync await resumed");
        }
        else
        {
            ViewModel.RefreshPins();
        }

        // Re-sync the column count against the current width.
        SyncColumns(ResultsGrid.ActualWidth);

        // After layout, when there is a grid to measure.
        if (_scrollToResultsPending)
            _ = Dispatcher.BeginInvoke(new Action(ScrollToResults), DispatcherPriority.Loaded);

        AppLog.Debug("Browse", "Loaded: handler returning (WPF layout/render still pending)");

        // Logs once WPF has finished pending layout/render work.
        _ = Dispatcher.BeginInvoke(
            new Action(() => AppLog.Debug("Browse", "Loaded: WPF caught up on layout/render")),
            DispatcherPriority.ContextIdle);
    }

    // A click anywhere on a card opens its Workshop item page. The card's own subscribe button
    // handles its click first and marks it handled, so pressing that one does not also open the page.
    private async void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModCardViewModel card }) return;

        e.Handled = true;
        await ViewModel.LoadDetailsAsync(card.Mod);
    }

    // The magnifier: Quick View, stepping through the cards on the page.
    private void QuickView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModCardViewModel card } button) return;

        e.Handled = true;
        AppServices.QuickView.Show(card.Mod, ModContextMenu.SequenceAround(button));
    }

    private void ResultsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncColumns(e.NewSize.Width);

        // New cards, or a new column count: rows have moved.
        ScheduleNearViewUpdate();
    }

    // The grid is widened by its -16 right margin so the last column's gap falls outside the page;
    // take that back off before counting how many cards fit.
    private void SyncColumns(double gridWidth) =>
        ViewModel.UpdateLayoutForWidth(gridWidth - BrowseViewModel.CardGap);

    // Choosing a sort order closes the panel, as Steam's does.
    private void SortChoice_Click(object sender, RoutedEventArgs e) => SortToggle.IsChecked = false;

    // The gear opens Steam's search options panel below it.
    // A click on the gear while the panel is open closes it (StaysOpen="False" does that on the
    // press); the click that follows must not open it straight back.
    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        if (Environment.TickCount64 - _searchOptionsClosedAt < 250) return;
        SearchOptions.IsOpen = true;
    }

    private long _searchOptionsClosedAt;

    private void SearchOptions_Closed(object? sender, EventArgs e) => _searchOptionsClosedAt = Environment.TickCount64;

    // Refresh and Save as default close the panel behind them.
    private void SearchOption_Click(object sender, RoutedEventArgs e) => SearchOptions.IsOpen = false;

    // How close to the bottom, in pixels, the infinite list adds its next cards: about two rows of
    // cards ahead, so the next ones are there before the end is reached.
    private const double LoadMoreDistance = 800;

    // Also fires when the list grows, so a window tall enough to show every card added still asks
    // for more until the page is longer than the view.
    private void PageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ScheduleNearViewUpdate();

        if (!ViewModel.HasMore) return;

        var remaining = PageScroll.ExtentHeight - PageScroll.ViewportHeight - PageScroll.VerticalOffset;
        if (remaining <= LoadMoreDistance) ViewModel.LoadMore();
    }

    //
    // The infinite list is not virtualised - the page scrolls as one, sidebar and all, and a
    // virtualising panel needs to own its scrolling - so every card loaded stays on the page. What
    // is let go is the part that weighs: the picture of any card more than two screens from the
    // view. Scrolling back brings it back from ThumbnailLoader's cache, well before it is on screen.
    //
    // Worked out from the grid's rows rather than per card: UniformGrid gives every cell the same
    // height, so the first card's position and height say where every row is.
    //
    private const double NearViewScreens = 2;

    private bool _nearViewPending;

    private void ScheduleNearViewUpdate()
    {
        if (_nearViewPending) return;

        // Once per burst of scroll events - a glide raises one a frame - after layout has settled.
        _nearViewPending = true;
        _ = Dispatcher.BeginInvoke(new Action(UpdateNearView), DispatcherPriority.Background);
    }

    private void UpdateNearView()
    {
        _nearViewPending = false;

        var cards = ViewModel.Results;
        if (!IsLoaded || cards.Count == 0) return;
        if (ResultsGrid.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement first) return;

        var pitch = first.ActualHeight;
        if (pitch <= 0 || !first.IsDescendantOf(PageScroll)) return;

        var top = first.TransformToAncestor(PageScroll).Transform(new Point(0, 0)).Y + PageScroll.VerticalOffset;
        var margin = PageScroll.ViewportHeight * NearViewScreens;
        var columns = Math.Max(1, ViewModel.Columns);

        var firstRow = (int)Math.Floor((PageScroll.VerticalOffset - margin - top) / pitch);
        var lastRow = (int)Math.Ceiling((PageScroll.VerticalOffset + PageScroll.ViewportHeight + margin - top) / pitch);

        for (var i = 0; i < cards.Count; i++)
        {
            var row = i / columns;
            cards[i].IsNearView = row >= firstRow && row <= lastRow;
        }
    }

    // A new page of results that arrived while this page was not on screen - "View all" or a
    // category on Home, an author on an item page - is scrolled to once the page is back.
    private bool _scrollToResultsPending;

    private void ScrollToResults()
    {
        // Only once the page has been laid out; before that there is nothing to measure against.
        if (!IsLoaded || ResultsGrid.ActualHeight <= 0)
        {
            _scrollToResultsPending = true;
            return;
        }

        _scrollToResultsPending = false;

        var top = ResultsGrid.TransformToAncestor(PageScroll).Transform(new Point(0, 0)).Y + PageScroll.VerticalOffset;

        // Only upward: a filter change made near the top should not jump the page down to the grid.
        if (PageScroll.VerticalOffset > top) PageScroll.ScrollToVerticalOffset(Math.Max(0, top - 16));
    }
}
