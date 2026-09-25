using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Views;

//
// Asked before a mod that would write over another mod's files is queued (see
// BrowseViewModel.ConfirmFileClashesAsync): which files, and whose they are now. Steam has nothing
// like it - a Workshop game gives every item its own folder - so the wording is the app's; the
// dialog is Steam's modal.
//
public static class FileClashDialog
{
    // How many files are listed before "and N more".
    private const int Shown = 12;

    /// <summary>True to install anyway.</summary>
    /// <param name="handOwner">For a file no install record lists: the installed mod whose folder
    /// holds it, if one does.</param>
    public static bool Ask(string modName, string version, IReadOnlyList<FileClash> clashes, Func<string, string?> handOwner)
    {
        var body = new StackPanel { MaxWidth = 640 };
        body.Children.Add(new TextBlock
        {
            Text = LocalizationService.Text(Strings.FileClash_IntroFormat, modName, version),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 12),
        });

        var list = new StackPanel();
        foreach (var clash in clashes.Take(Shown))
        {
            var owner = clash.OwnerName
                ?? (handOwner(clash.Path) is { } hand
                    ? LocalizationService.Text(Strings.FileClash_ByHandFormat, hand)
                    : Strings.FileClash_Unknown);

            var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4), FontSize = 13 };
            line.Inlines.Add(new System.Windows.Documents.Run(clash.Path)
            {
                FontFamily = (FontFamily)Application.Current.FindResource("SteamMonospace"),
                Foreground = Brushes.White,
            });
            line.Inlines.Add(new System.Windows.Documents.Run("  " + owner) { Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x98, 0xA0)) });
            list.Children.Add(line);
        }

        if (clashes.Count > Shown)
        {
            list.Children.Add(new TextBlock
            {
                Text = LocalizationService.Text(Strings.FileClash_MoreFormat, clashes.Count - Shown),
                FontSize = 13,
            });
        }

        body.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        body.Children.Add(new TextBlock
        {
            Text = Strings.FileClash_Outro,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 12, 0, 0),
        });

        var answer = SteamDialog.Show(
            Strings.FileClash_Title,
            body,
            new SteamDialogChoice(Strings.FileClash_InstallAnyway, SteamDialogButton.Blue),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey, IsDefault: true));

        return answer == 0;
    }
}
