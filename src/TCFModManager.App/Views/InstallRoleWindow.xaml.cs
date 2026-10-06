using System.Windows;
using Wpf.Ui.Controls;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Views;

// What the user said about the machine an install sits on.
public enum InstallRoleChoice
{
    //
    // Not now. Deliberately leaves the setting unanswered rather than storing a guess, so the
    // question comes back the next time the install folder is set - and until then the app treats
    // the machine as an ordinary player, which is the reading that installs everything.
    //
    AskLater,

    // Somebody plays here, and it also runs a headless client.
    PlaysHereToo,

    // A dedicated headless box. Nobody plays on it.
    HostsOnly,
}

//
// Asked once, when an install folder turns out to hold a Fika headless launcher.
//
// The launcher is the only thing on disk that says anything about this - a headless install has
// SPT.Server.exe, BepInEx and a game client exactly like a player's - and it only answers half the
// question. Whether anyone sits at this machine is not discoverable at all, which is why it is asked
// rather than detected.
//
public partial class InstallRoleWindow : SteamModalWindow
{
    private InstallRoleWindow(string launcherPath)
    {
        InitializeComponent();

        LauncherText.Text = LocalizationService.Text(
            Strings.Options_HeadlessFoundFormat, launcherPath);

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    public static InstallRoleChoice Ask(string launcherPath)
    {
        var window = new InstallRoleWindow(launcherPath);
        window.ShowDialog();
        return window.Choice;
    }

    private InstallRoleChoice Choice { get; set; } = InstallRoleChoice.AskLater;

    private void LaterButton_Click(object sender, RoutedEventArgs e) => Close(InstallRoleChoice.AskLater);

    private void HostsOnlyButton_Click(object sender, RoutedEventArgs e) => Close(InstallRoleChoice.HostsOnly);

    private void PlaysHereButton_Click(object sender, RoutedEventArgs e) => Close(InstallRoleChoice.PlaysHereToo);

    private void Close(InstallRoleChoice choice)
    {
        Choice = choice;
        DialogResult = choice != InstallRoleChoice.AskLater;
    }
}
