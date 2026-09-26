using System.Windows.Controls;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Views;

public partial class PlayPage : Page
{
    public PlayViewModel ViewModel { get; } = new();

    public PlayPage()
    {
        DataContext = ViewModel;
        InitializeComponent();

        // NavigationCacheMode keeps this page alive for the app's lifetime, so the poll has to be
        // stopped when it goes off screen rather than left running behind every other page.
        Loaded += (_, _) =>
        {
            ViewModel.StartPolling();

            //
            // Fire-and-forget, and deliberately not part of the poll: the check reads the whole
            // install to compare it against the server's list, which is far too much to do every two
            // seconds beside a launch button. Opening the page is the moment it needs to be current,
            // and "Check again" covers the rest.
            //
            _ = AppServices.PreLaunchCheck.CheckAsync();
        };

        Unloaded += (_, _) => ViewModel.StopPolling();
    }

    // The newest lines at the bottom, as a console has them.
    private void ServerLogBox_TextChanged(object sender, TextChangedEventArgs e) => ServerLogBox.ScrollToEnd();

    // Scrolled up by the user: stop replacing the text until they are back at the bottom.
    private void ServerLogBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0) return;
        ViewModel.FollowServerLog = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 2;
    }
}
