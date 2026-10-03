using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM): removing a mod whose sp-mod.com page shows the profile notice ("may make permanent
// changes to your profile, and may not be removable without starting a new profile" - SVM, Skills
// Extended, many traders). The removal says so first, whatever the unsubscribe question is set to:
// a single removal in its own prompt, a removal of several in a check that can leave those installed.
//
// Read from the cached catalog's ShowsProfileBindingNotice, the field the item page's notice shows.
// A mod not in the catalog, or an addon (addons have no such field), is never flagged.
//
public partial class InstalledViewModel
{
    private static bool ChangesProfile(InstalledModCardViewModel card) =>
        !card.IsAddon
        && card.ModId is { } id
        && AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is { ShowsProfileBindingNotice: true };

    // The warning paragraph, in the caution colour, above the prompt's usual text.
    private static TextBlock ProfileWarning(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 12),
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        return block;
    }

    // The mods to remove: all of them, or the ones that don't change the profile. Null when cancelled.
    private static List<InstalledModCardViewModel>? CheckProfileMods(List<InstalledModCardViewModel> targets)
    {
        var flagged = targets.Where(ChangesProfile).ToList();
        if (flagged.Count == 0) return targets;

        var body = new StackPanel { MaxWidth = 640 };
        body.Children.Add(ProfileWarning(Text(
            Strings.Installed_RemoveProfileManyFormat,
            string.Join("\n", flagged.Select(c => c.DisplayTitle)))));

        if (flagged.Count == targets.Count)
        {
            return SteamDialog.Show(
                Strings.Installed_RemoveProfileManyTitle,
                body,
                new SteamDialogChoice(Strings.Installed_RemoveProfileRemove, SteamDialogButton.Grey),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Blue, IsDefault: true)) == 0
                ? targets
                : null;
        }

        return SteamDialog.Show(
                Strings.Installed_RemoveProfileManyTitle,
                body,
                new SteamDialogChoice(Strings.Installed_RemoveProfileKeepThese, SteamDialogButton.Blue, IsDefault: true),
                new SteamDialogChoice(Strings.Installed_RemoveProfileRemoveAll, SteamDialogButton.Grey),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey)) switch
        {
            0 => targets.Except(flagged).ToList(),
            1 => targets,
            _ => null,
        };
    }
}
