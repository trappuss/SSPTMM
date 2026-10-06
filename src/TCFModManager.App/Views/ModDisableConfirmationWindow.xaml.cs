using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Views;

// What the user chose in ModDisableConfirmationWindow.
public enum ModDisableChoice
{
    // Back out - nothing moves.
    Cancel,

    // Move only the mods the user picked, leaving the affected ones as they are.
    ProceedOnly,

    // Move the affected mods along with them.
    ProceedWithCascade,
}

// One affected mod in the list: what it is, and why this change reaches it.
public sealed class ModDisableImpactRow(string name, string detail, bool isSoft)
{
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    public bool IsSoft { get; } = isSoft;

    // A soft dependency still loads without the thing it names, so it reads as a caution rather
    // than a break.
    public string Glyph => IsSoft ? "QuestionCircle24" : "ErrorCircle24";

    public Brush Brush => IsSoft
        ? Converters.ThemeBrush.Resolve(Converters.ThemeBrush.Caution, Brushes.Goldenrod)
        : Converters.ThemeBrush.Resolve(Converters.ThemeBrush.Critical, Brushes.OrangeRed);
}

//
// Modal warning shown before a disable that would break other mods, or an enable whose own
// dependencies are still disabled. Offers to carry the affected mods along rather than only
// reporting them.
//
public partial class ModDisableConfirmationWindow : SteamModalWindow
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private ModDisableConfirmationWindow(bool disabling, IReadOnlyList<string> targets, IReadOnlyList<ModDisableImpactRow> impact)
    {
        InitializeComponent();

        // One whole title per action and count rather than a verb and a noun phrase concatenated.
        Title = targets.Count == 1
            ? Text(
                disabling ? Strings.Disable_TitleDisableNamedFormat : Strings.Disable_TitleEnableNamedFormat,
                targets[0])
            : Text(
                disabling ? Strings.Disable_TitleDisableCountFormat : Strings.Disable_TitleEnableCountFormat,
                targets.Count);

        SummaryText.Text = disabling
            ? Strings.Disable_ImpactDisable(impact.Count)
            : Strings.Disable_ImpactEnable(impact.Count);

        TargetsText.Text = targets.Count == 1
            ? Text(Strings.Disable_SelectedFormat, targets[0])
            : Text(
                Strings.Disable_SelectedFormat,
                string.Join(Strings.Common_ListSeparator, targets));

        ImpactHeading.Text = disabling
            ? Strings.Disable_HeadingAffected
            : Strings.Disable_HeadingStillDisabled;

        ImpactList.ItemsSource = impact;

        ProceedOnlyButton.Content = disabling
            ? Strings.Disable_ProceedDisable
            : Strings.Disable_ProceedEnable;
        CascadeButton.Content = disabling
            ? Strings.Disable_CascadeDisable
            : Strings.Disable_CascadeEnable;

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
    }

    //
    // Shows the warning and returns what to do. <paramref name="targets"/> are the mods the user
    // picked; <paramref name="impact"/> is everything else this reaches.
    //
    public static ModDisableChoice Confirm(bool disabling, IReadOnlyList<string> targets, IReadOnlyList<ModDisableImpactRow> impact)
    {
        var window = new ModDisableConfirmationWindow(disabling, targets, impact);
        window.ShowDialog();
        return window.Choice;
    }

    private ModDisableChoice Choice { get; set; } = ModDisableChoice.Cancel;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close(ModDisableChoice.Cancel);

    private void ProceedOnlyButton_Click(object sender, RoutedEventArgs e) => Close(ModDisableChoice.ProceedOnly);

    private void CascadeButton_Click(object sender, RoutedEventArgs e) => Close(ModDisableChoice.ProceedWithCascade);

    private void Close(ModDisableChoice choice)
    {
        Choice = choice;
        DialogResult = choice != ModDisableChoice.Cancel;
    }
}
