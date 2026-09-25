using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

//
// The Workshop item page. MainWindow shows it for every mod opened anywhere in the app; a required
// item opened from it replaces it, and Esc walks back through those one at a time before closing.
//
public partial class WorkshopItemView : UserControl
{
    // The item pages this one was opened over, most recent last.
    private readonly Stack<ModDetailsRequest> _history = new();

    private ModDetailsRequest? _current;

    public WorkshopItemView()
    {
        InitializeComponent();
    }

    public void Show(ModDetailsRequest request)
    {
        if (IsVisible && _current is not null) _history.Push(_current);

        Open(request);
    }

    public void Close()
    {
        _history.Clear();
        _current = null;

        if (DataContext is WorkshopItemViewModel old) old.Detach();
        DataContext = null;
        Visibility = Visibility.Collapsed;
    }

    private void Open(ModDetailsRequest request)
    {
        if (DataContext is WorkshopItemViewModel old) old.Detach();

        _current = request;
        var viewModel = new WorkshopItemViewModel(request);
        DataContext = viewModel;

        // A tab switch is a new page on Steam; start it at the top.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkshopItemViewModel.IsChangeNotesShown)) Scroll.ScrollToTop();
        };

        Visibility = Visibility.Visible;
        Scroll.ScrollToTop();

        // Focus so Esc reaches this rather than the page underneath.
        Focus();

        _ = viewModel.LoadAsync();
    }

    private void GoBack()
    {
        if (_history.Count > 0) Open(_history.Pop());
        else Close();
    }

    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        GoBack();
    }

    // The first two breadcrumbs lead back to the Workshop page this was opened from.
    private void Workshop_Click(object sender, RoutedEventArgs e) => Close();
}
