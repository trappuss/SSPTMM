using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TCFModManager.App.Services;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Views;

//
// The Workshop item page. MainWindow shows it for every mod opened anywhere in the app; a required
// item opened from it replaces it, and Esc (or the mouse's back button, or Alt+Left) walks back
// through those one at a time before closing.
//
public partial class WorkshopItemView : UserControl
{
    // The item pages this one was opened over, most recent last.
    private readonly Stack<ModDetailsRequest> _history = new();

    private ModDetailsRequest? _current;

    private WorkshopItemViewModel? _viewModel;

    // The Comments tab's web view.
    private readonly SpModComments _commentsView;

    public WorkshopItemView()
    {
        InitializeComponent();

        _commentsView = new SpModComments(Scroll, CommentsHost, CommentsOff, CommentsFallback,
            () => _viewModel is { IsCommentsShown: true } shown ? shown.CommentsUrl : null);

        // The mouse's back button, anywhere on the page.
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.XButton1) return;
            e.Handled = true;
            GoBack();
        };
    }

    public void Show(ModDetailsRequest request)
    {
        // Over the item on screen, Esc comes back to it; opened afresh (or again after being hidden
        // under another page), there is nothing to come back to.
        if (IsVisible && _current is not null) _history.Push(_current);
        else _history.Clear();

        Open(request);
    }

    /// <summary>Hides the open item while something opened from it is over it.</summary>
    public void Hide() => Visibility = Visibility.Collapsed;

    /// <summary>Shows the open item again, as it was.</summary>
    public void Reveal()
    {
        if (_current is null) return;

        Visibility = Visibility.Visible;
        Focus();
    }

    public void Close()
    {
        var wasOpen = _current is not null;

        _history.Clear();
        _current = null;

        Detach();
        DataContext = null;
        Visibility = Visibility.Collapsed;
        HideComments();

        if (wasOpen) Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the page closes - the collection page opened under it comes back.</summary>
    public event EventHandler? Closed;

    private void Detach()
    {
        if (_viewModel is null) return;

        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.Detach();
        _viewModel = null;
    }

    private void Open(ModDetailsRequest request)
    {
        Detach();

        _current = request;
        _viewModel = new WorkshopItemViewModel(request);
        _viewModel.PropertyChanged += OnViewModelChanged;
        DataContext = _viewModel;

        HideComments();
        Visibility = Visibility.Visible;
        Scroll.ScrollToTop();
        Strip.ScrollToLeftEnd();
        SyncPreviewStretch();

        // Focus so Esc reaches this rather than the page underneath.
        Focus();

        _ = _viewModel.LoadAsync();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // A tab switch is a new page on Steam; start it at the top.
            case nameof(WorkshopItemViewModel.Tab):
                Scroll.ScrollToTop();
                if (_viewModel?.IsCommentsShown == true) _ = ShowCommentsAsync();
                else HideComments();
                break;

            case nameof(WorkshopItemViewModel.SelectedMedia):
                SyncPreviewStretch();
                break;
        }
    }

    private void GoBack()
    {
        if (_history.Count > 0) Open(_history.Pop());
        else Close();
    }

    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        var back = e.Key == Key.Escape || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) ||
                   (e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt);
        if (!back) return;

        e.Handled = true;
        GoBack();
    }

    // The first two breadcrumbs lead back to the Workshop page this was opened from.
    private void Workshop_Click(object sender, RoutedEventArgs e) => Close();

    // ---------------------------------------------------------------- preview and strip

    // The mod's own picture and a video's thumbnail fill the preview, as before; a picture from the
    // description is never blown up past its own size (a badge would turn to mush).
    private void SyncPreviewStretch()
    {
        var media = _viewModel?.SelectedMedia;
        var first = _viewModel is not null && media is not null && ReferenceEquals(_viewModel.Gallery.FirstOrDefault(), media);
        PreviewPicture.StretchDirection = first || _viewModel?.SelectedIsVideo == true
            ? StretchDirection.Both
            : StretchDirection.DownOnly;
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel?.OpenSelectedMediaCommand.Execute(null);
    }

    // The cover at the head of the right-hand column opens full size, as Steam's does.
    private void Cover_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _viewModel?.OpenCoverCommand.Execute(null);
    }

    // The slider under the strip mirrors the strip's own horizontal scroll, and drives it.
    private void Strip_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var overflow = Math.Max(0, Strip.ExtentWidth - Strip.ViewportWidth);
        StripSlider.Visibility = overflow > 0 ? Visibility.Visible : Visibility.Collapsed;
        StripSlider.Minimum = 0;
        StripSlider.Maximum = overflow;
        StripSlider.ViewportSize = Strip.ViewportWidth;
        StripSlider.Value = Strip.HorizontalOffset;
    }

    private void StripSlider_Scroll(object sender, ScrollEventArgs e) => Strip.ScrollToHorizontalOffset(e.NewValue);

    // ---------------------------------------------------------------- comments

    // sp-mod.com's comments for this item, in a web view - see SpModComments.
    private Task ShowCommentsAsync() => _commentsView.ShowAsync();

    private void HideComments() => _commentsView.Hide();

    private void OpenComments_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.CommentsUrl is { } url) MarkupActions.OpenInBrowser(url);
    }
}
