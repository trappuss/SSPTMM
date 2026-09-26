using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

//
// A collection's Quick View, over the whole window. MainWindow shows it for
// AppServices.CollectionQuickView's requests; Esc, the X or a click beside it closes it, and the
// arrow keys step through the list it came from - as an item's Quick View.
//
public partial class CollectionQuickViewOverlay : UserControl
{
    private CollectionQuickViewViewModel? _viewModel;

    // Where the keyboard was before, to hand it back on close.
    private IInputElement? _focusBefore;

    public CollectionQuickViewOverlay()
    {
        InitializeComponent();
    }

    public void Show(CollectionQuickViewRequest request)
    {
        if (_viewModel is null) _focusBefore = Keyboard.FocusedElement;

        Release();
        _viewModel = new CollectionQuickViewViewModel(request);
        _viewModel.Leaving += OnLeaving;
        DataContext = _viewModel;

        Visibility = Visibility.Visible;

        // Once it is on screen: an element that is still collapsed cannot take the keyboard.
        Dispatcher.BeginInvoke(() => Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    // The keyboard stays here while it is open, whatever is clicked inside it.
    private void Overlay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsKeyboardFocusWithin) Focus();
    }

    public void Close()
    {
        if (_viewModel is null) return;

        Release();
        DataContext = null;
        Visibility = Visibility.Collapsed;

        _focusBefore?.Focus();
        _focusBefore = null;
    }

    private void Release()
    {
        if (_viewModel is null) return;

        _viewModel.Leaving -= OnLeaving;
        _viewModel.Detach();
        _viewModel = null;
    }

    // Something opened from here: the page it opens takes the keyboard, not what was focused before.
    private void OnLeaving(object? sender, EventArgs e)
    {
        _focusBefore = null;
        Close();
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left:
                _viewModel?.PreviousCommand.Execute(null);
                break;
            case Key.Right:
                _viewModel?.NextCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Backdrop_Click(object sender, MouseButtonEventArgs e) => Close();

    // A click inside the modal stays there, and keeps the keyboard here.
    private void Modal_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Focus();
        e.Handled = true;
    }

    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CollectionQuickViewTile tile) return;

        e.Handled = true;
        _viewModel?.OpenTileCommand.Execute(tile);
    }
}
