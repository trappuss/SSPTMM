using System.Windows.Controls;

using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

//
// Where the user acts on a new version of this app: what's installed, what sp-mod.com has, what
// kind of change it is, the release notes, and the button that installs it.
//
// Not to be confused with the old Updates page for mods, which was removed - the Installed page
// covers that. This one is about the manager itself.
//
public partial class AppUpdatePage : Page
{
    // The app-lifetime instance, shared with the banner in MainWindow so both show the same state.
    public AppUpdateViewModel ViewModel { get; } = AppServices.AppUpdate;

    public AppUpdatePage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // The Server Map mod's files can change while the app runs (an operator dropping in a new
        // payload), and reading them is cheap, so they are read again every time the page shows.
        Loaded += (_, _) => ViewModel.RefreshServerMapInstall();
    }
}
