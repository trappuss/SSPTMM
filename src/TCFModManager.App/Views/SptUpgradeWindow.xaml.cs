using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Wpf.Ui.Controls;

namespace TCFModManager.App.Views;

/// <summary>One row of the upgrade report as drawn.</summary>
public sealed record SptUpgradeRowView(string Name, string Detail, SymbolRegular Glyph, Brush Brush);

//
// The SPT upgrade check - see SptUpgradeReport. The release picked is the only input; the rows are
// worked out again from the catalog each time it changes.
//
public partial class SptUpgradeWindow : FluentWindow
{
    private readonly List<SptUpgradeInput> _installed;
    private readonly IReadOnlyList<Mod> _catalog;

    private SptUpgradeWindow(List<SptUpgradeInput> installed, IReadOnlyList<Mod> catalog, IReadOnlyList<SptRelease> releases)
    {
        InitializeComponent();
        _installed = installed;
        _catalog = catalog;

        Owner = Application.Current?.MainWindow;
        WindowStartupLocation = Owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;

        TargetBox.ItemsSource = releases;
        TargetBox.SelectedIndex = releases.Count > 0 ? 0 : -1;

        // The installed SPT is newer than every release known here (a test build, or a list not
        // fetched yet): nothing to pick, said so rather than left blank.
        if (releases.Count == 0) SummaryText.Text = Strings.Upgrade_NoReleases;
    }

    /// <summary>Opens the check for these installed mods, offering the releases from
    /// <paramref name="installedSpt"/> on (newest first).</summary>
    public static void Open(IEnumerable<SptUpgradeInput> installed, IReadOnlyList<Mod> catalog,
        IReadOnlyList<SptRelease> releases, string? installedSpt)
    {
        var current = installedSpt is null ? null : SptVersionMatcherShim.Parse(installedSpt);
        var offered = releases.Where(r => current is null || r.Value >= current).ToList();

        new SptUpgradeWindow([.. installed], catalog, offered).ShowDialog();
    }

    private void TargetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetBox.SelectedItem is not SptRelease target) return;

        var rows = SptUpgradeReport.Build(_installed, _catalog, target.Label);

        RowsList.ItemsSource = rows.Select(r => new SptUpgradeRowView(r.Name, Detail(r, target.Label), Glyph(r.Standing), BrushFor(r.Standing))).ToList();

        int Count(SptUpgradeStanding s) => rows.Count(r => r.Standing == s);
        SummaryText.Text = LocalizationService.Text(Strings.Upgrade_SummaryFormat,
            target.Label, Count(SptUpgradeStanding.Ready), Count(SptUpgradeStanding.UpdateNeeded),
            Count(SptUpgradeStanding.NotYet), Count(SptUpgradeStanding.Unknown));
    }

    private static string Detail(SptUpgradeRow row, string target) => row.Standing switch
    {
        SptUpgradeStanding.Ready => LocalizationService.Text(Strings.Upgrade_ReadyFormat, row.Installed, target),
        SptUpgradeStanding.UpdateNeeded => LocalizationService.Text(Strings.Upgrade_UpdateFormat, row.Installed, row.Version, target),
        SptUpgradeStanding.NotYet => LocalizationService.Text(Strings.Upgrade_NotYetFormat, target),
        _ => row.ModId is > 0 ? Strings.Upgrade_UnknownConstraint : Strings.Upgrade_UnknownNotListed,
    };

    private static SymbolRegular Glyph(SptUpgradeStanding standing) => standing switch
    {
        SptUpgradeStanding.Ready => SymbolRegular.CheckmarkCircle24,
        SptUpgradeStanding.UpdateNeeded => SymbolRegular.ArrowCircleUp24,
        SptUpgradeStanding.NotYet => SymbolRegular.DismissCircle24,
        _ => SymbolRegular.QuestionCircle24,
    };

    private static Brush BrushFor(SptUpgradeStanding standing) =>
        Application.Current?.TryFindResource(standing switch
        {
            SptUpgradeStanding.Ready => "SystemFillColorSuccessBrush",
            SptUpgradeStanding.UpdateNeeded => "SystemFillColorCautionBrush",
            SptUpgradeStanding.NotYet => "SystemFillColorCriticalBrush",
            _ => "TextFillColorTertiaryBrush",
        }) as Brush ?? Brushes.Gray;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // SptVersionMatcher.ParseVersion is internal to Core; the same reading here.
    private static class SptVersionMatcherShim
    {
        public static Version? Parse(string raw)
        {
            var parts = raw.Split('-', 2)[0].Split('.');
            if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return null;
            int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
            return new Version(major, Part(1), Part(2), Part(3));
        }
    }
}
