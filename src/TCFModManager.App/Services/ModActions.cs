using System.ComponentModel;
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

    // Through the Subscribed items page's own removal, confirmations and all. The removal's own
    // message is the first thing that page says; the reload it sets off then replaces it with the
    // page's mod count, which means nothing elsewhere.
    public static async Task<string?> UnsubscribeAsync(Mod mod)
    {
        if (AppServices.Browse.InstalledMatchFor(mod) is not { } installed) return null;

        var page = InstalledViewModel.Current ?? new InstalledViewModel();

        string? said = null;
        void Listen(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InstalledViewModel.StatusMessage)) said ??= page.StatusMessage;
        }

        page.PropertyChanged += Listen;
        try
        {
            await page.RemoveCommand.ExecuteAsync(installed);
        }
        finally
        {
            page.PropertyChanged -= Listen;
        }

        return said;
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

    // Everything by this author, in Browse - the item page's last breadcrumb.
    public static void ShowAuthor(string author)
    {
        if (string.IsNullOrWhiteSpace(author)) return;

        AppServices.Browse.ShowSearch("@" + author);
        AppNavigation.Navigate(typeof(BrowsePage));
    }
}
