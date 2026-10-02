using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

/// <summary>A page that can show what an action on one of its mods said - the item page's line
/// under the subscribe box, Browse's status line, and so on.</summary>
public interface IModActionHost
{
    void ShowActionMessage(string? message);
}

//
// What can be done to one mod from anywhere it is shown: the right-click menu and Quick View both
// come here, and each action goes through the same code the item page's buttons use - Browse's own
// install command, the Subscribed items page's own removal, the same update dialog - so a mod
// subscribed from a menu is subscribed exactly the way the button would have done it.
//
// Each returns what it said, or null when it said nothing.
//
public static class ModActions
{
    public static bool HasPage(Mod mod) => !string.IsNullOrWhiteSpace(mod.DetailUrl);

    public static bool IsSubscribed(Mod mod) => AppServices.Browse.InstalledMatchFor(mod) is not null;

    // A newer version than the installed one, on a mod that is not set aside - the item page's own
    // test for showing Update.
    public static bool CanUpdate(Mod mod) =>
        AppServices.Browse.InstalledMatchFor(mod) is not null
        && AppServices.Browse.BuildCard(mod) is { UpdateAvailable: true, IsDisabled: false };

    public static Task<string?> OpenAsync(Mod mod) => AppServices.Browse.LoadDetailsAsync(mod);

    public static async Task<string?> SubscribeAsync(Mod mod)
    {
        await AppServices.Browse.InstallCommand.ExecuteAsync(AppServices.Browse.BuildCard(mod));
        return AppServices.Browse.StatusMessage;
    }

    // Through the Subscribed items page's own removal, confirmations and all, answering with what
    // the removal said (null when it was cancelled).
    public static async Task<string?> UnsubscribeAsync(Mod mod)
    {
        if (AppServices.Browse.InstalledMatchFor(mod) is not { } installed) return null;

        var page = InstalledViewModel.ForActions;

        // What the removal itself said - not whatever the page's status line said last (its rescan
        // puts a count there).
        return await page.RemoveOneAsync(installed);
    }

    // The same dialog Subscribed items opens for a mod: every version, its changelog, Update.
    public static async Task UpdateAsync(Mod mod)
    {
        if (AppServices.Browse.InstalledMatchFor(mod) is not { } installed
            || AppServices.ModUpdateOverlay.ShowAsync is not { } show)
        {
            return;
        }

        await show(installed);
    }

    public static string? AddToCollection(Mod mod) => AddToCollectionDialog.Ask(mod);

    public static string? CopyLink(Mod mod)
    {
        if (!HasPage(mod)) return null;

        try
        {
            Clipboard.SetText(mod.DetailUrl!);
            return Strings.Item_LinkCopied;
        }
        catch (Exception ex)
        {
            // The clipboard can be held by another process; that is worth a log line, not a crash.
            AppLog.Warn("Workshop", $"couldn't copy the mod link: {ex.Message}");
            return null;
        }
    }

    public static void OpenPage(Mod mod)
    {
        if (HasPage(mod)) MarkupActions.OpenInBrowser(mod.DetailUrl!);
    }

    // An author's Workshop page: their items, their collections, Follow. With their sp-mod.com id
    // when the caller has it; by name - looked up in the catalog - otherwise.
    public static void ShowAuthor(string author, int? id = null)
    {
        if (string.IsNullOrWhiteSpace(author)) return;

        AppServices.AuthorOverlay.Show(new AuthorRequest(id is > 0 ? id : null, author.Trim()));
    }
}
