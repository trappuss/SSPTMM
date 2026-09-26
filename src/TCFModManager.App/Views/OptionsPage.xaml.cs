using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class OptionsPage : Page
{
    public OptionsViewModel ViewModel { get; } = new();

    public OptionsPage()
    {
        DataContext = ViewModel;
        InitializeComponent();
    }

    // Same reason as ServerMapPage: the key file can appear or change while this app is open, and
    // the section that shows it is right here.
    // The unsubscribe question's "Don't ask again" can have changed its switch since this page was made.
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        AppServices.ServerMap.RefreshLocalKey();
        ViewModel.Reload();
        ViewModel.Profiles.Refresh();
    }

    private void DataFiles_Click(object sender, RoutedEventArgs e) =>
        new DataFilesWindow { Owner = Window.GetWindow(this) }.Show();
}
