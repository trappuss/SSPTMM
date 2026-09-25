using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class ModListsPage : Page
{
    public ModListsViewModel ViewModel { get; } = new();

    public ModListsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;

        //
        // Registered on the Page with handledEventsToo rather than as a bubbling MouseWheel
        // attribute in XAML: ui:TextBox marks the bubbling wheel event handled even with nothing to
        // scroll, so hovering the name box would otherwise swallow it. Same fix as ConfigsPage.
        //
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(Page_PreviewMouseWheel), handledEventsToo: true);
    }

    private async void ModListsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();

        // A collection's page may have sent this page here with something to do - see
        // ModListsViewModel.Request - or may send it while it is showing.
        ModListsViewModel.Requested -= OnRequested;
        ModListsViewModel.Requested += OnRequested;
        await ViewModel.HandleRequestAsync();
    }

    private void ModListsPage_Unloaded(object sender, RoutedEventArgs e) => ModListsViewModel.Requested -= OnRequested;

    private async void OnRequested(object? sender, EventArgs e) => await ViewModel.HandleRequestAsync();

    //
    // Sends the wheel to whichever list the pointer is nearest, so scrolling works anywhere on the
    // page rather than only directly over a list.
    //
    // The right-hand column holds two lists in one cell - the selected list's contents and, after a
    // preview, the plan - so which one is on screen decides where the wheel goes.
    //
    private void Page_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        ListBox? right = PlanBox.IsVisible ? PlanBox : EntriesBox.IsVisible ? EntriesBox : null;

        var target = right is not null && (right.IsMouseOver || !ListsBox.IsMouseOver)
            ? Scroller(right)
            : Scroller(ListsBox);

        if (target is null || target.ScrollableHeight <= 0) return;

        Scroll(target, e.Delta);
        e.Handled = true;
    }

    //
    // One wheel notch, in whatever unit the scroller counts in.
    //
    // Every list on this page is a ListBox, and a ListBox's own style turns CanContentScroll on, so
    // its VerticalOffset counts rows rather than pixels. Handing that a raw wheel delta - 120 a
    // notch - asks it to move 120 rows, which lands at the bottom from anywhere: the list looked
    // like it was snapping rather than scrolling.
    //
    // Three rows a notch is what Windows scrolls elsewhere. A pixel scroller keeps the delta it was
    // given, so the named ScrollViewers on the other pages still feel exactly as they did.
    //
    private static void Scroll(ScrollViewer target, int delta)
    {
        var amount = target.CanContentScroll ? Math.Sign(delta) * 3.0 : delta;

        target.ScrollToVerticalOffset(target.VerticalOffset - amount);
    }

    private static ScrollViewer? Scroller(DependencyObject root)
    {
        if (root is ScrollViewer found) return found;

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (Scroller(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        }

        return null;
    }
}
