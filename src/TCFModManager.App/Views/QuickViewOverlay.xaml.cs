using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Markup;

namespace TCFModManager.App.Views;

//
// Quick View, over the whole window. MainWindow shows it for AppServices.QuickView's requests; Esc,
// the X or a click beside it closes it, and the arrow keys step through the list it came from.
//
public partial class QuickViewOverlay : UserControl
{
    private QuickViewViewModel? _viewModel;

    // Where the keyboard was before, to hand it back on close.
    private IInputElement? _focusBefore;

    public QuickViewOverlay()
    {
        InitializeComponent();
    }

    public bool IsOpen => _viewModel is not null;

    public void Show(QuickViewRequest request)
    {
        if (_viewModel is null) _focusBefore = Keyboard.FocusedElement;

        Release();
        _viewModel = new QuickViewViewModel(request);
        _viewModel.Leaving += OnLeaving;
        _viewModel.PropertyChanged += OnViewModelChanged;
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
        _viewModel.PropertyChanged -= OnViewModelChanged;
        Watch(null);
        _viewModel.Detach();
        _viewModel = null;
    }

    private void OnLeaving(object? sender, EventArgs e) => Close();

    // The shown item's view model, watched for the chosen picture.
    private WorkshopItemViewModel? _watched;

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickViewViewModel.Item)) Watch(_viewModel?.Item);
    }

    private void Watch(WorkshopItemViewModel? item)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnItemChanged;
        _watched = item;
        if (_watched is not null) _watched.PropertyChanged += OnItemChanged;

        SyncPreviewStretch();
        Strip.ScrollToLeftEnd();
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkshopItemViewModel.SelectedMedia)) SyncPreviewStretch();
    }

    // As on the item page: the mod's own picture and a video's thumbnail fill the preview; a picture
    // from the description is never blown up past its own size.
    private void SyncPreviewStretch()
    {
        var item = _watched;
        var first = item?.SelectedMedia is { } media && ReferenceEquals(item.Gallery.FirstOrDefault(), media);
        PreviewPicture.StretchDirection = first || item?.SelectedIsVideo == true
            ? StretchDirection.Both
            : StretchDirection.DownOnly;
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

    // A click inside the modal stays there (it would otherwise reach the backdrop's parent grid
    // only by accident, but also keeps the keyboard here).
    private void Modal_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Focus();
        e.Handled = true;
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel?.Item?.OpenSelectedMediaCommand.Execute(null);
    }

    private void Thumb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MarkupMedia media) return;

        e.Handled = true;
        _viewModel?.Item?.SelectMediaCommand.Execute(media);
    }

    // The dark end over the strip shows while there is more of it to the right.
    private void Strip_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        StripMore.Visibility = Strip.HorizontalOffset + Strip.ViewportWidth < Strip.ExtentWidth - 1
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void StripMore_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Strip.ScrollToHorizontalOffset(Strip.HorizontalOffset + Math.Max(168, Strip.ViewportWidth - 168));
    }
}
