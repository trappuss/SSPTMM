using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class ConfigsPage : Page
{
    public ConfigsViewModel ViewModel { get; } = new();

    private bool _scanned;

    public ConfigsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;

        //
        // Grouping is added to the collection's own default view rather than declared as a
        // CollectionViewSource in XAML: a resource can't bind to the page's DataContext, so the
        // XAML version needs plumbing that this one line replaces. The ListBox picks the grouping
        // up because ItemsSource binds to the same collection.
        //
        var view = CollectionViewSource.GetDefaultView(ViewModel.Results);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConfigEntryViewModel.Section)));

        //
        // Registered on the Page with handledEventsToo rather than as a bubbling MouseWheel
        // attribute in XAML: several controls (ui:TextBox in particular, via the ScrollViewer inside
        // its own template) mark the bubbling wheel event handled even when they have nothing to
        // scroll, so hovering the search box would otherwise swallow it. See the scroll-anywhere
        // corrections on InstalledPage - this is the same fix applied from the start.
        //
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), handledEventsToo: true);
    }

    private async void ConfigsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // NavigationCacheMode is Required, so this page instance survives navigating away and back.
        // Rescanning every time would re-walk the whole install for nothing; the Rescan button is
        // there for when it's actually wanted.
        if (_scanned) return;

        _scanned = true;
        await ViewModel.ScanCommand.ExecuteAsync(null);
    }

    //
    // Scrolls the file list from anywhere over the list column - including over the search box and
    // the filter dropdown, which would otherwise eat the event.
    //
    // Deliberately scoped to that column: the editor on the right is a TextBox doing its own
    // scrolling, and forwarding the wheel away from it would break scrolling the file being edited.
    //
    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (!IsWithin(source, RailPanel)) return;

        Behaviors.SmoothScrolling.Glide(ListScrollViewer, e.Delta);
        e.Handled = true;
    }

    //
    // Walks up to see whether source sits under ancestor. Prefers the visual tree, so it works for
    // the parts inside a control's template - which is where a wheel event's OriginalSource usually
    // is - and falls back to the logical tree for a ContentElement such as a Run, which the visual
    // tree has no parent for and which VisualTreeHelper.GetParent would throw on.
    //
    private static bool IsWithin(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject? current = source; current is not null;)
        {
            if (ReferenceEquals(current, ancestor)) return true;

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }
}
