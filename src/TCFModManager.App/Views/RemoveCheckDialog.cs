using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Views;

//
// Asked before a collection's Unsubscribe from all removes its items (InstalledViewModel.
// RemoveModsAsync), only when there is something the first question could not show: installed
// mods outside the removal that use what is being removed, and the folders of hand-installed mods
// that removing them deletes - what a single removal's confirmation lists. Steam's modal.
//
public static class RemoveCheckDialog
{
    // How many lines each list shows before "and N more".
    private const int Shown = 12;

    /// <summary>True to remove anyway.</summary>
    /// <param name="needed">Each installed mod that uses one being removed, and how.</param>
    /// <param name="folders">The folders removing hand-installed mods deletes.</param>
    public static bool Ask(IReadOnlyList<(string Name, string Detail)> needed, IReadOnlyList<string> folders)
    {
        var body = new StackPanel { MaxWidth = 640 };

        if (needed.Count > 0)
        {
            body.Children.Add(Paragraph(Strings.RemoveCheck_NeededIntro, top: 0));
            body.Children.Add(List(needed.Select(n => (n.Name, n.Detail)).ToList(), monospace: false));
        }

        if (folders.Count > 0)
        {
            body.Children.Add(Paragraph(Strings.RemoveCheck_FoldersIntro, top: needed.Count > 0 ? 16 : 0));
            body.Children.Add(List(folders.Select(f => (f, "")).ToList(), monospace: true));
        }

        var answer = SteamDialog.Show(
            Strings.RemoveCheck_Title,
            body,
            new SteamDialogChoice(Strings.RemoveCheck_RemoveAnyway, SteamDialogButton.Blue),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey, IsDefault: true));

        return answer == 0;
    }

    private static TextBlock Paragraph(string text, double top) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 21,
        Margin = new Thickness(0, top, 0, 8),
    };

    private static ScrollViewer List(IReadOnlyList<(string Main, string Aside)> lines, bool monospace)
    {
        var list = new StackPanel();
        foreach (var (main, aside) in lines.Take(Shown))
        {
            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), FontSize = 13 };
            var run = new System.Windows.Documents.Run(main) { Foreground = Brushes.White };
            if (monospace) run.FontFamily = (FontFamily)Application.Current.FindResource("SteamMonospace");
            line.Inlines.Add(run);
            if (aside.Length > 0)
                line.Inlines.Add(new System.Windows.Documents.Run("  " + aside) { Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0)) });
            list.Children.Add(line);
        }

        if (lines.Count > Shown)
        {
            list.Children.Add(new TextBlock
            {
                Text = LocalizationService.Text(Strings.FileClash_MoreFormat, lines.Count - Shown),
                FontSize = 13,
            });
        }

        return new ScrollViewer
        {
            Content = list,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }
}
