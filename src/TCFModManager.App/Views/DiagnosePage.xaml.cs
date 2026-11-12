using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// Fork (SSPTMM): Diagnose logs. Reads the logs each time it is shown - they change with every run.
public partial class DiagnosePage : Page
{
    public DiagnoseViewModel ViewModel { get; }

    public DiagnosePage()
    {
        ViewModel = new DiagnoseViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        // The wheel scrolls the findings from anywhere on the page - see DependenciesPage.
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), true);
    }

    private async void DiagnosePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Fork (1.4.7): a Find the broken mod search starts the server many times - read again after.
        AppServices.BrokenModFinder.Ended -= Search_Ended;
        AppServices.BrokenModFinder.Ended += Search_Ended;
        Unloaded -= DiagnosePage_Unloaded;
        Unloaded += DiagnosePage_Unloaded;

        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void DiagnosePage_Unloaded(object sender, RoutedEventArgs e) =>
        AppServices.BrokenModFinder.Ended -= Search_Ended;

    private async void Search_Ended(object? sender, EventArgs e) =>
        await ViewModel.RefreshCommand.ExecuteAsync(null);

    // Fork (1.4.7): the "server stopped" card's Find the broken mod - the search's card is at the top.
    private void FindBroken_Click(object sender, RoutedEventArgs e)
    {
        FindingsScrollViewer.ScrollToTop();
        if (ViewModel.Search.CanStart) ViewModel.Search.StartCommand.Execute(null);
    }

    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Behaviors.SmoothScrolling.Glide(FindingsScrollViewer, e.Delta);
        e.Handled = true;
    }
}
