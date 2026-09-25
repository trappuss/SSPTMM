using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
        ViewModel.PageChanged += (_, _) => ScrollToResults();
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

    private void ResultsGrid_SizeChanged(object sender, SizeChangedEventArgs e) => SyncColumns(e.NewSize.Width);

    // The grid is widened by its -16 right margin so the last column's gap falls outside the page;
    // take that back off before counting how many cards fit.
    private void SyncColumns(double gridWidth) =>
        ViewModel.UpdateLayoutForWidth(gridWidth - BrowseViewModel.CardGap);

    // Choosing a sort order closes the panel, as Steam's does.
    private void SortChoice_Click(object sender, RoutedEventArgs e) => SortToggle.IsChecked = false;

    // The gear's menu opens on a left click, below the gear - the way Steam's gear opens its panel.
    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        if (GearButton.ContextMenu is not { } menu) return;

        menu.DataContext = ViewModel;
        menu.PlacementTarget = GearButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void ScrollToResults()
    {
        // Only once the page has been laid out; before that there is nothing to measure against.
        if (!IsLoaded || ResultsGrid.ActualHeight <= 0) return;

        var top = ResultsGrid.TransformToAncestor(PageScroll).Transform(new Point(0, 0)).Y + PageScroll.VerticalOffset;

        // Only upward: a filter change made near the top should not jump the page down to the grid.
        if (PageScroll.VerticalOffset > top) PageScroll.ScrollToVerticalOffset(Math.Max(0, top - 16));
    }
}
