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

    private async void DiagnosePage_Loaded(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshCommand.ExecuteAsync(null);

    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Behaviors.SmoothScrolling.Glide(FindingsScrollViewer, e.Delta);
        e.Handled = true;
    }
}
