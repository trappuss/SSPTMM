using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class FollowedAuthorsPage : Page
{
    public FollowedAuthorsViewModel ViewModel { get; } = new();

    public FollowedAuthorsPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    // Read afresh each time the page is opened: authors are followed from other pages.
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RefreshAsync();
        }
        catch (Exception ex)
        {
            // An event handler has nowhere to throw to; the list stays as it was.
            Core.Services.AppLog.Warn("Workshop", $"followed authors page: {ex.Message}");
        }
    }
}
