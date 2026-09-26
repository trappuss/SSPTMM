using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

//
// A collection's Workshop page. MainWindow shows it over the page area when a collection is opened
// (the item page's "In N of your collections", the Collections page's View collection); an item
// opened from it goes over it, and closing that item page brings this back.
//
public partial class WorkshopCollectionView : UserControl
{
    private WorkshopCollectionViewModel? _viewModel;

    public WorkshopCollectionView()
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

    /// <summary>True while a collection is open here, shown or hidden under an item page.</summary>
    public bool IsOpen => _viewModel is not null;

    /// <summary>Opens a collection: one of this install's mod lists, or - read from sp-mod.com -
    /// a public one. A stored copy of a public list opens as the public list, so what its author
    /// has changed since shows. False when there is nothing to open.</summary>
    public bool Show(CollectionRequest request)
    {
        WorkshopCollectionViewModel viewModel;

        if (request.IsPublic)
        {
            viewModel = new WorkshopCollectionViewModel(request.PublicId, request.PublicSlug!);
        }
        else if (request.ListId is { } listId && AppServices.ModLists.Find(listId) is { } list)
        {
            viewModel = Services.PublicCollections.TryGetSource(list, out var id, out var slug)
                ? new WorkshopCollectionViewModel(id, slug)
                : new WorkshopCollectionViewModel(list);
        }
        else
        {
            return false;
        }

        _viewModel?.Detach();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Reveal();
        Scroll.ScrollToTop();

        if (viewModel.IsPublic) _ = LoadAsync(viewModel);
        return true;
    }

    // Read from sp-mod.com. A stored copy that cannot be read (offline, the list taken down) is
    // shown as stored instead, with why.
    private async Task LoadAsync(WorkshopCollectionViewModel viewModel)
    {
        if (await viewModel.LoadAsync() || !ReferenceEquals(_viewModel, viewModel)) return;

        if (viewModel.List is { } stored && viewModel.LoadFailed is { } why)
        {
            viewModel.Detach();
            _viewModel = new WorkshopCollectionViewModel(stored) { Message = why };
            DataContext = _viewModel;
        }
    }

    /// <summary>Shows the open collection again, as it was.</summary>
    public void Reveal()
    {
        if (_viewModel is null) return;

        Visibility = Visibility.Visible;

        // Focus so Esc reaches this rather than the page underneath.
        Focus();
    }

    /// <summary>Hides the open collection while an item page is over it.</summary>
    public void Hide() => Visibility = Visibility.Collapsed;

    public void Close()
    {
        _viewModel?.Detach();
        _viewModel = null;
        DataContext = null;
        Visibility = Visibility.Collapsed;
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
        if ((sender as FrameworkElement)?.DataContext is not WorkshopCollectionItem item) return;

        e.Handled = true;
        _viewModel?.OpenItemCommand.Execute(item);
    }
}
