using System.Windows;
using System.Windows.Controls;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Fork (1.4.5): the words for ProfileModItems - which profiles hold items or traders from mods
// about to be removed or turned off, said before they go. Asked by a removal, a disable, a preset
// and a collection's apply.
//
internal static class ProfileItemsWarning
{
    // What the install's profiles would lose with these mods gone. A mod sharing its folder with
    // another is left out: its folder isn't going anywhere.
    public static IReadOnlyList<ProfileLoss> ForCards(IEnumerable<InstalledModCardViewModel> cards) =>
        For(cards
            .Where(c => c.SharedFolderParts is null)
            .Select(c => (c.DisplayTitle, (IReadOnlyList<string>)c.Entries.Select(e => e.FolderPath).ToList())));

    public static IReadOnlyList<ProfileLoss> For(IEnumerable<(string Mod, IReadOnlyList<string> Paths)> leaving)
    {
        try
        {
            return ProfileModItems.Check(AppServices.SptEnvironment.InstallPath, [.. leaving.Where(l => l.Paths.Count > 0)]);
        }
        catch (Exception ex)
        {
            // A check that can't be made says nothing rather than stopping the removal it was for.
            AppLog.Warn("Profiles", $"profile check failed: {ex.Message}");
            return [];
        }
    }

    // The caution paragraph: what it means, then one line per profile and mod.
    public static string Text(IReadOnlyList<ProfileLoss> losses)
    {
        var lines = losses
            .OrderBy(l => l.ProfileName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.ModName, StringComparer.CurrentCultureIgnoreCase)
            .Select(l => LocalizationService.Text(Strings.ProfileItems_LineFormat, l.ProfileName, l.ModName, Counts(l)));

        return Strings.ProfileItems_Intro + "\n" + string.Join("\n", lines) + "\n\n" + Strings.ProfileItems_Outro;
    }

    private static string Counts(ProfileLoss loss)
    {
        var parts = new List<string>();
        if (loss.Items > 0) parts.Add(Strings.ProfileItems_Items(loss.Items, loss.Items));
        if (loss.Traders > 0) parts.Add(Strings.ProfileItems_Traders(loss.Traders, loss.Traders));
        return string.Join(Strings.Common_ListSeparator, parts);
    }

    // Asked on its own, before a disable or a collection's apply: true to go ahead. Cancel is the default.
    public static bool Confirm(IReadOnlyList<ProfileLoss> losses, string goAhead)
    {
        var text = new TextBlock
        {
            Text = Text(losses),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");

        return SteamDialog.Show(
            Strings.ProfileItems_Title,
            new StackPanel { MaxWidth = 640, Children = { text } },
            new SteamDialogChoice(goAhead, SteamDialogButton.Grey),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Blue, IsDefault: true)) == 0;
    }

    // Read once in the background after a scan, so the first check doesn't wait on the disk.
    public static void Warm()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        _ = Task.Run(() =>
        {
            try { ProfileModItems.Check(installPath, [("", [])]); }
            catch (Exception ex) { AppLog.Debug("Profiles", $"warm-up failed: {ex.Message}"); }
        });
    }
}
