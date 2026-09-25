using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

// The Workshop front page. See the header of the XAML.
public partial class WorkshopHomePage : Page
{
    public WorkshopHomeViewModel ViewModel { get; } = AppServices.WorkshopHome;

    public WorkshopHomePage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkshopHomeViewModel.SelectedTab)) SyncTabs();
        };
        SyncTabs();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private void ListTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: WorkshopHomeTab tab }) ViewModel.SelectTabCommand.Execute(tab);

        // The click already flipped the button; put every tab back to what is chosen.
        SyncTabs();
    }

    private void SyncTabs()
    {
        foreach (var tab in ListTabs.Children.OfType<ToggleButton>())
        {
            tab.IsChecked = tab.Tag is WorkshopHomeTab value && value == ViewModel.SelectedTab;
        }
    }

    private async void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModCardViewModel card }) return;

        e.Handled = true;
        await ViewModel.OpenCommand.ExecuteAsync(card);
    }

    // The magnifier: Quick View, stepping through the cards of the row or list it is in.
    private void QuickView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModCardViewModel card } button) return;

        e.Handled = true;
        AppServices.QuickView.Show(card.Mod, Behaviors.ModContextMenu.SequenceAround(button));
    }

    // Subscribe straight from the card, the same way its right-click menu does; what it says shows
    // on this page.
    private async void QuickSubscribe_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ModCardViewModel card }) return;

        e.Handled = true;
        try
        {
            if (await Services.ModActions.SubscribeAsync(card.Mod) is { } message) ViewModel.Message = message;
        }
        catch (Exception ex)
        {
            // A click handler has nowhere to throw to.
            Core.Services.AppLog.Warn("Workshop", $"subscribe from the front page: {ex.Message}");
        }
    }

    // The arrows move the row by what is on screen, less one card, so the card at the edge stays in
    // view as the one the next page starts from.
    private const double CarouselStep = 183 + 16;

    private void CarouselLeft_Click(object sender, RoutedEventArgs e) =>
        Carousel.ScrollToHorizontalOffset(Math.Max(0, Carousel.HorizontalOffset - PageWidth()));

    private void CarouselRight_Click(object sender, RoutedEventArgs e) =>
        Carousel.ScrollToHorizontalOffset(Math.Min(Carousel.ScrollableWidth, Carousel.HorizontalOffset + PageWidth()));

    private double PageWidth() =>
        Math.Max(CarouselStep, Math.Floor(Carousel.ViewportWidth / CarouselStep) * CarouselStep - CarouselStep);

    // The row only scrolls sideways, and only through its arrows; the wheel belongs to the page, so
    // pass it on rather than let the row's ScrollViewer swallow it.
    private void Carousel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        e.Handled = true;
        var forward = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender,
        };

        (((FrameworkElement)sender).Parent as UIElement)?.RaiseEvent(forward);
    }
}
