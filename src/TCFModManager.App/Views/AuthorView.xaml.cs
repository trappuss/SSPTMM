using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

//
// An author's Workshop page. MainWindow shows it over the page area when an author is opened (a
// name under CREATED BY, the last breadcrumb of an item page, a collection's author); an item or a
// collection opened from it goes over it, and closing that brings this back.
//
public partial class AuthorView : UserControl
{
    private AuthorPageViewModel? _viewModel;

    public AuthorView()
    {
        InitializeComponent();

        // The mouse's back button, anywhere on the page.
        MouseUp += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.XButton1) return;
            e.Handled = true;
            Close();
        };
    }

    /// <summary>Raised when the page closes - whatever it was opened over comes back.</summary>
    public event EventHandler? Closed;

    public void Show(AuthorRequest request)
    {
        Attach(new AuthorPageViewModel(request));

        Reveal();
        Scroll.ScrollToTop();

        _ = _viewModel!.LoadAsync();
    }

    private void Attach(AuthorPageViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.PageChanged -= OnPageChanged;
            _viewModel.Detach();
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelChanged;
            viewModel.PageChanged += OnPageChanged;
        }

        DataContext = viewModel;
    }

    // A tab switch is a new page on Steam; start it at the top.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthorPageViewModel.Tab)) Scroll.ScrollToTop();
    }

    // Another page of items: back up to the first of them, as a new Steam page opens at its top.
    private void OnPageChanged(object? sender, EventArgs e) => Scroll.ScrollToTop();

    // How close to the bottom, in pixels, the infinite list adds its next items - as Browse does.
    // ScrollChanged also fires when the list grows, so a window tall enough to show every item
    // added keeps asking until the list is longer than the view.
    private const double LoadMoreDistance = 800;

    private void Scroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_viewModel is not { IsInfinite: true, Tab: AuthorTab.Items }) return;

        var remaining = Scroll.ExtentHeight - Scroll.ViewportHeight - Scroll.VerticalOffset;
        if (remaining <= LoadMoreDistance) _viewModel.LoadMore();
    }

    /// <summary>Shows the open author again, as they were.</summary>
    public void Reveal()
    {
        if (_viewModel is null) return;

        Visibility = Visibility.Visible;

        // Focus so Esc reaches this rather than the page underneath.
        Focus();
    }

    /// <summary>Hides the open author while something opened from here is over them.</summary>
    public void Hide() => Visibility = Visibility.Collapsed;

    public void Close()
    {
        var wasOpen = _viewModel is not null;

        Attach(null);
        Visibility = Visibility.Collapsed;

        if (wasOpen) Closed?.Invoke(this, EventArgs.Empty);
    }

    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        var back = e.Key == Key.Escape || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) ||
                   (e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt);
        if (!back) return;

        e.Handled = true;
        Close();
    }

    // The first two breadcrumbs lead back to the page this was opened over.
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AuthorItem item) return;

        e.Handled = true;
        _viewModel?.OpenItemCommand.Execute(item);
    }

    private void Addon_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AuthorAddon addon) return;

        e.Handled = true;
        _viewModel?.OpenAddonCommand.Execute(addon);
    }

    private void Collection_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CollectionCardViewModel card) return;

        e.Handled = true;
        _viewModel?.OpenCollectionCommand.Execute(card);
    }
}
