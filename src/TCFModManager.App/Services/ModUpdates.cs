using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Services;

/// <summary>One mod to update: what to call it, its listing, the version to update to, and whether
/// this app installed the copy it replaces. KeepDisabled: the switched-off copy's folders, put back
/// for the update and switched off again after it.</summary>
public readonly record struct UpdateTarget(
    string Title, Mod Mod, string Version, bool IsAppManaged, IReadOnlyList<InstalledMod>? KeepDisabled = null);

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
        // Fork (1.4.9): with each update, the ones it only works together with (HeldBackUpdates) -
        // whichever page asked, so a collection's Update all cannot send one of a pair alone. And
        // not what is already in the queue: a card keeps its arrow until its update has landed, and
        // a second click queued the same update again.
        //
        var batch = targets.ToList();
        foreach (var target in targets)
        {
            foreach (var id in AppServices.HeldBack.Partners(target.Mod.Id))
            {
                if (batch.Any(t => t.Mod.Id == id)) continue;
                if (ViewModels.InstalledViewModel.CardOf(id) is not { } card || ViewModels.InstalledViewModel.UpdateTo(card) is not { } to) continue;

                batch.Add(new UpdateTarget(card.DisplayTitle, to.Mod, to.Version, card.IsAppManaged, ViewModels.InstalledViewModel.KeepDisabled(card)));
            }
        }

        var waiting = batch
            .Where(t => AppServices.DownloadQueue.Items.Any(i => !i.IsFinished && !i.Target.IsAddon && i.Target.Id == t.Mod.Id))
            .ToList();
        batch.RemoveAll(waiting.Contains);
        if (batch.Count == 0 && waiting.Count > 0)
            return (LocalizationService.Text(Strings.Dependencies_AlreadyQueuedFormat, TextLists.Join([.. waiting.Select(t => t.Title)])), false);

        targets = batch;

        // 1.19: follows Monitor mode's setting. A download places nothing, so the warning below is moot.
        var downloadOnly = AppServices.ModPageGate.DownloadOnlyFor(alternate: false);

        //
        // One warning covering every hand-installed mod in the batch, for the same reason the
        // single-mod path warns at all: there is no record of which files the current version
        // placed, so the new one goes on top of it.
        //
        // Fork (1.4.3): with other mods in the batch too, Leave them out updates those and not the
        // hand-installed ones. No used to cancel the lot, so one mod of your own held back every
        // other update.
        //
        var queue = targets.ToList();
        var handLeftOut = string.Empty;
        var handInstalled = targets.Where(t => !t.IsAppManaged).Select(t => t.Title).ToList();
        if (!downloadOnly && handInstalled.Count > 0)
        {
            switch (AskHandInstalled(handInstalled, mixed: handInstalled.Count < targets.Count))
            {
                case HandInstalledAnswer.Cancel:
                    return (Strings.Installed_UpdateCancelled, false);
                case HandInstalledAnswer.LeaveOut:
                    queue.RemoveAll(t => !t.IsAppManaged);
                    handLeftOut = LocalizationService.Text(Strings.Installed_UpdateHandInstalledLeftOutFormat, TextLists.Join(handInstalled));
                    break;
            }
        }

        //
        // 1.19: any update whose version doesn't support the installed SPT - PickDisplayVersion falls
        // back to the newest when none does - is asked about once for the batch. No leaves those out
        // and updates the rest.
        //
        var incompatible = queue
            .Select(t => (Target: t, Version: t.Mod.Versions?.FirstOrDefault(v => v.Version == t.Version)))
            .Where(t => SptCompatibility.IsIncompatible(t.Version?.SptVersionConstraint))
            .ToList();

        var leftOut = string.Empty;
        if (incompatible.Count > 0 && !SptCompatibility.ConfirmAnyway(
                [.. incompatible.Select(t => SptCompatibility.Line(t.Target.Title, t.Version!.Version, t.Version.SptVersionConstraint))],
                batch: true,
                downloadOnly)) // Fork: worded for a download
        {
            var dropped = incompatible.Select(t => t.Target).ToHashSet();
            queue.RemoveAll(dropped.Contains);
            leftOut = LocalizationService.Text(
                Strings.Install_IncompatibleLeftOutFormat,
                AppServices.SptEnvironment.InstalledVersion,
                TextLists.Join([.. incompatible.Select(t => t.Target.Title)]));

            if (queue.Count == 0) return (Join(handLeftOut, leftOut), false);
        }

        // The same dialog a single install goes through, asked once for the batch - each with the
        // change notes of the version it updates to. ConfirmAll honours the Options switch that
        // turns the dialog off. Fork (1.4.7): not a mod from GitHub, which has no sp-mod.com page -
        // Add from GitHub said where its files come from when it was installed.
        var links = queue
            .Where(t => !LocalArchive.IsLocalId(t.Mod.Id))
            .Select(t => new ModPageLink(t.Title, t.Mod.DetailUrl) { ModId = t.Mod.Id, ChangeNotesVersion = t.Version })
            .ToList();
        if (!ReadModPageConfirmationWindow.ConfirmAll(links)) return (Strings.Installed_UpdateCancelledUnread, false);

        foreach (var target in queue)
        {
            var mod = target.Mod;
            var version = target.Version;
            AppServices.DownloadQueue.Enqueue(
                InstallTarget.For(mod), version, installPath, () => ResolveVersionLinkAsync(mod, version),
                downloadOnly: downloadOnly, keepDisabled: target.KeepDisabled);
        }

        return (Join(Strings.Installed_UpdateQueued(queue.Count), handLeftOut, leftOut), true);
    }

    //
    // Fork (1.4.9): after one update is queued from its own dialog, the updates it only works
    // together with (HeldBackUpdates.Partners) - each at the release sp-mod.com holds back, when this
    // app installed the copy it replaces. One installed by hand is left: the queue then says, before
    // installing, that the version there will not do. Returns the note to show, or null.
    //
    public static string? QueuePartners(int? modId, string version, string installPath, bool downloadOnly)
    {
        if (!string.Equals(AppServices.HeldBack.HeldVersion(modId), version, StringComparison.OrdinalIgnoreCase)) return null;

        var queued = false;
        foreach (var id in AppServices.HeldBack.Partners(modId))
        {
            if (AppServices.HeldBack.HeldVersion(id) is not { } held
                || AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == id) is not { } partner
                || AppServices.Browse.InstalledMatchFor(partner) is not { IsAppManaged: true } card
                || !ViewModels.InstalledViewModel.IsUpdatable(card)
                || AppServices.DownloadQueue.Items.Any(i => !i.IsFinished && !i.Target.IsAddon && i.Target.Id == id))
            {
                continue;
            }

            AppServices.DownloadQueue.Enqueue(
                InstallTarget.For(partner), held, installPath, () => ResolveVersionLinkAsync(partner, held),
                downloadOnly: downloadOnly, keepDisabled: ViewModels.InstalledViewModel.KeepDisabled(card));
            queued = true;
        }

        return queued ? AppServices.HeldBack.Note(modId) : null;
    }

    private enum HandInstalledAnswer
    {
        Update,
        LeaveOut,
        Cancel,
    }

    private static HandInstalledAnswer AskHandInstalled(IReadOnlyList<string> titles, bool mixed)
    {
        var body = LocalizationService.Text(Strings.Installed_UpdateHandInstalledBodyFormat, TextLists.Join(titles));
        var title = Strings.Installed_UpdateHandInstalledTitle(titles.Count);

        if (!mixed)
        {
            return SteamMessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes
                ? HandInstalledAnswer.Update
                : HandInstalledAnswer.Cancel;
        }

        var chosen = SteamDialog.Show(
            title,
            body + "\n\n" + Strings.Installed_UpdateHandInstalledMixedNote,
            new SteamDialogChoice(Strings.Installed_UpdateHandInstalledUpdate, SteamDialogButton.Green),
            new SteamDialogChoice(Strings.Installed_UpdateHandInstalledLeaveOut, SteamDialogButton.Blue, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        return chosen switch
        {
            0 => HandInstalledAnswer.Update,
            1 => HandInstalledAnswer.LeaveOut,
            _ => HandInstalledAnswer.Cancel,
        };
    }

    private static string Join(params string[] sentences) =>
        string.Join(Strings.Common_SentenceSeparator, sentences.Where(s => s.Length > 0));

    // The version's own record, for its download link.
    private static async Task<ModVersion?> ResolveVersionLinkAsync(Mod mod, string version)
    {
        // Fork (1.4.7): a GitHub release's file, from the releases already read.
        if (LocalArchive.IsLocalId(mod.Id)) return AppServices.GitHubMods.VersionFor(mod.Id, version);

        var versions = await AppServices.SpModApi.GetModVersionsAsync(
            mod.Id.ToString(), new ModVersionsQuery { FilterVersion = version, PerPage = 5 });
        return versions.Data.Exactly(version);
    }
}
