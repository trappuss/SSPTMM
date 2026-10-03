using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// Fork (SSPTMM): Workshop Home's "Getting started" list - see GettingStartedPanel.xaml.
public partial class GettingStartedPanel : UserControl
{
    private readonly GettingStartedViewModel _viewModel = new();

    public GettingStartedPanel()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    // Read again each time Workshop Home is shown: a step done on another page (Options, Browse,
    // Play) is ticked by the time you come back.
    private void Panel_Loaded(object sender, RoutedEventArgs e) => _viewModel.Refresh();

    private void Panel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) _viewModel.Refresh();
    }
}
