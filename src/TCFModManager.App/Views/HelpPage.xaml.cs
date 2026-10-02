using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class HelpPage : Page
{
    public HelpViewModel ViewModel { get; }

    public HelpPage()
    {
        ViewModel = new HelpViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        // Wheel scrolls the how-tos from anywhere on the page, the search box included - the same
        // tunnelling handler as InstalledPage and DependenciesPage, for the same reason.
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), true);

        // Navigating to this page again while it is on screen doesn't reload it, so a request made
        // from here - the no-install banner - arrives by the event instead.
        Loaded += (_, _) =>
        {
            ViewModel.RefreshOpenButtons();
            TakeRequest();
        };

        // The page is cached, so without this the next visit opens on whatever was left open, or
        // searched for.
        Unloaded += (_, _) => ViewModel.Leave();

        AppNavigation.HelpRequested += (_, _) =>
        {
            if (IsLoaded) TakeRequest();
        };
    }

    private void TakeRequest()
    {
        if (AppNavigation.TakeHelpRequest() is not { } request) return;

        var (section, topic) = ViewModel.Arrive(request.SectionId, request.TopicId);

        // After the expanders have opened and the list has laid itself out again, or the offset
        // is measured against the old, collapsed heights.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var target = topic is null
                ? SectionsList.ItemContainerGenerator.ContainerFromItem(section) as FrameworkElement
                : FindByDataContext(SectionsList, topic);

            if (target is null) return;

            var top = target.TranslatePoint(new Point(0, 0), SectionsList).Y;
            SectionsScrollViewer.ScrollToVerticalOffset(top);
        });
    }

    // The CardExpander showing a topic - it sits in its section's own ItemsControl, one level down,
    // so the outer list's generator can't hand it back.
    private static FrameworkElement? FindByDataContext(DependencyObject root, object item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Wpf.Ui.Controls.CardExpander card && card.DataContext == item) return card;
            if (FindByDataContext(child, item) is { } found) return found;
        }

        return null;
    }

    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SectionsScrollViewer.ScrollToVerticalOffset(SectionsScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
