using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// Browsing: Collections. See the header of the XAML.
public partial class CollectionsBrowsePage : Page
{
    // Shared app-lifetime view model, so the page, search and filter stay as left.
    public CollectionsBrowseViewModel ViewModel { get; } = AppServices.CollectionsBrowse;

    public CollectionsBrowsePage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // A new page of cards starts at the top of them, as a Steam page load does.
        ViewModel.NavigatedToPage += (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                if (PageScroll.VerticalOffset > 0) PageScroll.ScrollToTop();
            }, DispatcherPriority.Loaded);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ViewModel.EnsureLoadedAsync();

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CollectionCardViewModel card }) return;

        e.Handled = true;
        ViewModel.OpenCommand.Execute(card);
    }

    // The magnifier: the collection's Quick View, stepping through the cards on the page.
    private void QuickView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CollectionCardViewModel card }) return;

        e.Handled = true;
        AppServices.CollectionQuickView.Show(card.Summary, ViewModel.Lists.Select(c => c.Summary).ToList());
    }

    // Choosing a sort order closes the panel.
    private void SortChoice_Click(object sender, RoutedEventArgs e) => SortToggle.IsChecked = false;

    // The infinite list adds its next cards about two rows before the end, as Browse's does.
    private const double LoadMoreDistance = 600;

    private void PageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ViewModel.HasMore) return;

        var remaining = PageScroll.ExtentHeight - PageScroll.ViewportHeight - PageScroll.VerticalOffset;
        if (remaining <= LoadMoreDistance) ViewModel.LoadMore();
    }
}
