using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class DependenciesPage : Page
{
    public DependenciesViewModel ViewModel { get; }

    public DependenciesPage()
    {
        ViewModel = new DependenciesViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        // Registered directly on the Page so it's the first thing to see every wheel event over
        // this page - PreviewMouseWheel tunnels root-to-leaf, running before any descendant's own
        // bubble-phase handling, including the internal ScrollViewer part several controls
        // (ui:TextBox, ComboBox, ...) use for their own content, which otherwise swallows the
        // wheel event even when there's nothing for that control itself to scroll.
        // handledEventsToo:true means it still runs even if something upstream already marked the
        // event handled. Same fix as InstalledPage's group view - see Page_PreviewMouseWheel below.
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), true);
    }

    // Resolves on first open only; re-navigating reuses what's already there, and Refresh re-runs it.
    private async void DependenciesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasLoaded || ViewModel.IsBusy) return;

        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    // Lets the wheel scroll the dependency tree from anywhere on the page, not just while
    // hovering it directly - drives TreesScrollViewer ourselves unconditionally (see the
    // constructor for why a plain bubble MouseWheel handler wasn't reliable here).
    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TreesScrollViewer.Visibility != Visibility.Visible) return;

        Behaviors.SmoothScrolling.Glide(TreesScrollViewer, e.Delta);
        e.Handled = true;
    }
}
