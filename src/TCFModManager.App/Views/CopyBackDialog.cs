using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Views;

//
// Fork (1.4.4): a friend's copy of one of your collections has come back (ModListCopyBack) - the
// mods it adds as tick boxes, all ticked, to take into yours; or the copy kept as a collection of
// its own, as any other arrival is.
//
public static class CopyBackDialog
{
    public enum Choice { Take, KeepSeparate, Cancel }

    public static (Choice Choice, IReadOnlyList<ModListEntry> Taken) Ask(ModListCopyBack back)
    {
        var from = back.Theirs.Source ?? Strings.Sharing_Someone;

        var body = new StackPanel { MaxWidth = 560 };
        body.Children.Add(new TextBlock
        {
            Text = LocalizationService.Text(
                back.Adds.Count == 0 ? Strings.Sharing_CopyBackNothingFormat : Strings.Sharing_CopyBackIntroFormat,
                back.Yours.Name,
                from),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var boxes = new List<(CheckBox Box, ModListEntry Entry)>();
        if (back.Adds.Count > 0)
        {
            var rows = new StackPanel();
            foreach (var entry in back.Adds)
            {
                // The mod, and the version the copy has it at, dimmer beside it.
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                label.Children.Add(new TextBlock { Text = entry.Name, Foreground = Brushes.White, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
                if (!string.IsNullOrWhiteSpace(entry.Version))
                    label.Children.Add(new TextBlock { Text = entry.Version, Foreground = Brushes.White, Opacity = 0.6, FontSize = 14, Margin = new Thickness(8, 0, 0, 0) });

                var box = new CheckBox
                {
                    Content = label,
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 0, 6),
                };
                boxes.Add((box, entry));
                rows.Children.Add(box);
            }

            body.Children.Add(new ScrollViewer
            {
                Content = rows,
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
        }

        if (back.LeftOut.Count > 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = Strings.Sharing_CopyBackLeftOut(back.LeftOut.Count, back.LeftOut.Count),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        var choices = new List<SteamDialogChoice>();
        if (back.Adds.Count > 0) choices.Add(new SteamDialogChoice(Strings.Sharing_CopyBackAdd, SteamDialogButton.Green, IsDefault: true));
        choices.Add(new SteamDialogChoice(Strings.Sharing_CopyBackKeepSeparate, SteamDialogButton.Blue, IsDefault: back.Adds.Count == 0));
        choices.Add(new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        var answer = SteamDialog.Show(Strings.Sharing_CopyBackTitle, body, [.. choices]);
        if (back.Adds.Count == 0) answer++;

        var taken = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Entry).ToList();
        return answer switch
        {
            0 when taken.Count > 0 => (Choice.Take, taken),
            1 => (Choice.KeepSeparate, []),
            _ => (Choice.Cancel, []),
        };
    }
}
