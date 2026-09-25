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

    /// <summary>Opens the mod list with this id as its collection page; false when it is gone.</summary>
    public bool Show(Guid listId)
    {
        if (AppServices.ModLists.Find(listId) is not { } list) return false;

        _viewModel?.Detach();
        _viewModel = new WorkshopCollectionViewModel(list);
        DataContext = _viewModel;

        Reveal();
        Scroll.ScrollToTop();
        return true;
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
