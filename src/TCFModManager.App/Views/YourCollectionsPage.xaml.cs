using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// Fork (SSPTMM): your collections - see YourCollectionsPage.xaml.
public partial class YourCollectionsPage : Page
{
    public YourCollectionsViewModel ViewModel { get; } = new();

    public YourCollectionsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    //
    // Read again whenever the page shows - a collection made, applied or received elsewhere is here
    // when you come back - and the clipboard looked at again whenever the window comes back to the
    // front, which is when a code copied from a chat would have arrived.
    //
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is { } window) window.Activated += Window_Activated;
        ViewModel.CheckClipboard();
        await ViewModel.RefreshAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is { } window) window.Activated -= Window_Activated;
    }

    private void Window_Activated(object? sender, EventArgs e) => ViewModel.CheckClipboard();

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CollectionTileViewModel tile }) ViewModel.OpenCommand.Execute(tile);
    }
}
