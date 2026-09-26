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

    // The one sort order: choosing it closes the panel.
    private void SortChoice_Click(object sender, RoutedEventArgs e) => SortToggle.IsChecked = false;
}
