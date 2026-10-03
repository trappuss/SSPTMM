using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TCFModManager.App.Localization;
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
            _ = ViewModel.RefreshConflictsAsync();
        };

        Unloaded += (_, _) => ViewModel.StopPolling();
    }

    // The newest lines at the bottom, as a console has them.
    private void ServerLogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ServerLogBox.ScrollToEnd();
        ViewModel.FollowServerLog = true;
    }

    // Scrolled up by the user: stop replacing the text until they are back at the bottom.
    private void ServerLogBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0) return;
        ViewModel.FollowServerLog = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 2;
    }

    private void RoleShowMeHow_Click(object sender, RoutedEventArgs e) =>
        AppNavigation.ShowHelp("options", "options.role");

    // Fork (experimental): the Play card's "..." - the things done now and then, kept off the card.
    private void DirectMore_Click(object sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = PlacementMode.Bottom,
            DataContext = vm,
        };

        menu.Items.Add(new MenuItem
        {
            Header = vm.WipeNextStart ? Strings.Play_DirectWipeCancel : Strings.Play_DirectWipe,
            ToolTip = Strings.Play_DirectWipeToolTip,
            Command = vm.ToggleWipeCommand,
            IsEnabled = vm.SelectedProfile is not null && !vm.IsPlaying,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectNewProfile,
            ToolTip = Strings.Play_DirectNewProfileToolTip,
            Command = vm.NewProfileCommand,
            IsEnabled = !vm.IsPlaying,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectDeleteProfile,
            ToolTip = Strings.Play_DirectDeleteProfileToolTip,
            Command = vm.AskDeleteProfileCommand,
            IsEnabled = vm.SelectedProfile is not null && !vm.IsPlaying && !vm.IsGameRunning,
        });

        menu.Items.Add(new Separator());

        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectClearCache,
            ToolTip = Strings.Play_DirectClearCacheToolTip,
            Command = vm.ClearGameCacheNowCommand,
            IsEnabled = !vm.IsPlaying && !vm.IsGameRunning,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectOpenLauncher,
            ToolTip = Strings.Play_DirectOpenLauncherToolTip,
            Command = vm.StartClientCommand,
            IsEnabled = vm.CanStartClient,
        });
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Play_DirectSettings,
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => AppNavigation.Navigate(typeof(OptionsPage))),
        });

        menu.IsOpen = true;
    }
}
