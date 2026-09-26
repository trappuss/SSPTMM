using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

/// <summary>One mod to update: what to call it, its listing, the version to update to, and whether
/// this app installed the copy it replaces.</summary>
public readonly record struct UpdateTarget(string Title, Mod Mod, string Version, bool IsAppManaged);

//
// Queues a batch of updates the one way both Update all buttons do it - Subscribed items' and a
// collection page's: one warning for every hand-installed mod in it, the page gate asked once for
// the lot (each with the change notes of the version it updates to), then each into the download
// queue. Moved here from InstalledViewModel unchanged, so the two cannot drift apart.
//
public static class ModUpdates
{
    /// <summary>Queues the updates, asking first. Returns what to say - queued, or why not -
    /// and whether anything was queued.</summary>
    public static (string Message, bool Queued) Queue(IReadOnlyList<UpdateTarget> targets)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return (AppMessages.NoSptInstallFolder, false);

        //
        // One warning covering every hand-installed mod in the batch, for the same reason the
        // single-mod path warns at all: there is no record of which files the current version
        // placed, so the new one goes on top of it.
        //
        var handInstalled = targets.Where(t => !t.IsAppManaged).Select(t => t.Title).ToList();
        if (handInstalled.Count > 0 && MessageBox.Show(
                LocalizationService.Text(Strings.Installed_UpdateHandInstalledBodyFormat, TextLists.Join(handInstalled)),
                Strings.Installed_UpdateHandInstalledTitle(handInstalled.Count),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return (Strings.Installed_UpdateCancelled, false);
        }

        // The same dialog a single install goes through, asked once for the batch - each with the
        // change notes of the version it updates to. ConfirmAll honours the Options switch that
        // turns the dialog off.
        var links = targets
            .Select(t => new ModPageLink(t.Title, t.Mod.DetailUrl) { ModId = t.Mod.Id, ChangeNotesVersion = t.Version })
            .ToList();
        if (!ReadModPageConfirmationWindow.ConfirmAll(links)) return (Strings.Installed_UpdateCancelledUnread, false);

        foreach (var target in targets)
        {
            var mod = target.Mod;
            var version = target.Version;
            AppServices.DownloadQueue.Enqueue(
                InstallTarget.For(mod), version, installPath, () => ResolveVersionLinkAsync(mod, version));
        }

        return (Strings.Installed_UpdateQueued(targets.Count), true);
    }

    // The version's own record, for its download link.
    private static async Task<ModVersion?> ResolveVersionLinkAsync(Mod mod, string version)
    {
        var versions = await AppServices.SpModApi.GetModVersionsAsync(
            mod.Id.ToString(), new ModVersionsQuery { FilterVersion = version, PerPage = 5 });
        return versions.Data.FirstOrDefault(v => v.Version == version) ?? versions.Data.FirstOrDefault();
    }
}
