using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;

namespace TCFModManager.App.Views;

//
// Steam's "Add to Collection" (sharedfiles ShowAddToCollection, worded as the new Workshop pages
// word it): the collections as tick boxes, ticked where the item already is, a name box for a new
// one, and Add to Selected Collections. What it sends is the difference - ticked ones gain the
// item, unticked ones lose it.
//
// Collections here are this install's mod lists. Only those made here are offered: a list received
// from someone or served by a server is theirs to change.
//
public static class AddToCollectionDialog
{
    /// <summary>Asks, applies what was chosen, and returns what happened to say, or null when
    /// nothing changed.</summary>
    public static string? Ask(Mod mod)
    {
        var installed = AppServices.Browse.InstalledMatchFor(mod);
        var name = mod.Name ?? mod.Id.ToString();
        var editable = ModListService.Collections().Where(l => l.IsEditable).ToList();
        var holding = ModListService.CollectionsWith(mod).Select(l => l.Id).ToHashSet();

        var body = new StackPanel { MaxWidth = 560 };
        body.Children.Add(new TextBlock
        {
            Text = editable.Count == 0
                ? Strings.Collection_AddNoneYet
                : LocalizationService.Text(Strings.Collection_AddIntroFormat, name),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 14),
        });

        var boxes = new List<(CheckBox Box, ModList List)>();
        if (editable.Count > 0)
        {
            var rows = new StackPanel();
            foreach (var list in editable)
            {
                var box = new CheckBox
                {
                    Content = new TextBlock { Text = list.Name, Foreground = Brushes.White, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis },
                    IsChecked = holding.Contains(list.Id),
                    Margin = new Thickness(0, 0, 0, 6),
                };
                boxes.Add((box, list));
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

        // "Create New Collection": a name typed here makes one, with the item in it.
        var newName = new TextBox
        {
            Style = (Style)Application.Current.FindResource("SteamSearchField"),
            Tag = Strings.Collection_CreateNew,
            Height = 34,
            Margin = new Thickness(0, 12, 0, 0),
        };
        body.Children.Add(newName);

        var answer = SteamDialog.Show(
            Strings.Collection_AddTitle,
            body,
            new SteamDialogChoice(Strings.Collection_AddConfirm, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        if (answer != 0) return null;

        var chosen = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.List.Id).ToHashSet();
        var (added, removed) = ModListService.SetCollections(mod, installed, chosen, newName.Text);

        if (added == 0 && removed == 0) return null;

        return string.Join(
            Strings.Common_SentenceSeparator,
            new[]
            {
                added > 0 ? Strings.Collection_Added(added, name, added) : null,
                removed > 0 ? Strings.Collection_Removed(removed, name, removed) : null,
            }.Where(s => s is not null));
    }
}
