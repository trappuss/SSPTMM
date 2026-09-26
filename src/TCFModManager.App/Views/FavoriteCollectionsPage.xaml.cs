using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class FavoriteCollectionsPage : Page
{
    public FavoriteCollectionsViewModel ViewModel { get; } = new();

    public FavoriteCollectionsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    // Read afresh each time the page is opened: collections are favorited from other pages, and
    // their authors change them.
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            // An event handler has nowhere to throw to; the list stays as it was.
            Core.Services.AppLog.Warn("Workshop", $"favorites page: {ex.Message}");
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => ViewModel.Stop();

    // A row opens its collection; the Favorite button in it does not.
    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FavoriteCollectionRow row) return;
        if (e.OriginalSource is DependencyObject source && FindButton(source) is not null) return;

        e.Handled = true;
        ViewModel.OpenCommand.Execute(row);
    }

    private static Button? FindButton(DependencyObject? node)
    {
        while (node is not null && node is not Border { Name: "Row" })
        {
            if (node is Button button) return button;
            node = node is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}
