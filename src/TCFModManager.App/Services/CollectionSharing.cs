using System.IO;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Fork (SSPTMM): sharing collections with friends, so a group can keep the same modpack.
//
// Two ways, and either can be used for any of your collections:
//
//  - A share code (ModListShareCode): one line of text, pasted in Discord or anywhere. A friend adds
//    it with Add from code - or the Your collections page notices it on their clipboard. Sending a
//    newer code of the same collection updates their copy.
//
//  - A shared folder: your collection kept as a file in a folder you share through Dropbox,
//    OneDrive, Google Drive or the like. This app rewrites the file whenever the collection changes;
//    a friend follows that file in their copy of the folder, and is told when it moves on.
//
// Either way what travels is the list - mods and versions - never mod files, as with the list file.
//
// Numbering: a friend tells a newer copy from an older one by its revision, so a collection that has
// changed since it was last shared is numbered again before it goes out - the rule publishing to a
// server already follows (ModListPublication). The copy last shared is kept under
// Data\SharedCollections to compare against.
//
// On arrival a collection replaces the copy of it already here, as an imported file does, and the
// friend is told what changed and offered Sync: the Collections page's preview with "match the pack
// exactly" (Exclusive) chosen, so the group ends up on the same set - nothing changes unseen.
//
public static class CollectionSharing
{
    private static readonly SettingsService Settings = new();

    private static string LastSharedFolder => Path.Combine(AppPaths.DataDirectory, "SharedCollections");

    private static string LastSharedPath(Guid id) => Path.Combine(LastSharedFolder, id.ToString("N") + ModListFile.Extension);

    // ---------------------------------------------------------------- the person sharing

    public static string? ShareName
    {
        get => Settings.Load().ShareName;
        set
        {
            var settings = Settings.Load();
            settings.ShareName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Settings.Save(settings);
        }
    }

    public static string? SharedFileFor(Guid id) => Settings.Load().SharedFiles.GetValueOrDefault(id);

    public static string? FollowedFileFor(Guid id) => Settings.Load().FollowedFiles.GetValueOrDefault(id);

    //
    // The collection as it goes out: numbered again first if it has changed since it last went out.
    // Only your own are numbered - a friend's keeps theirs, so passing it on doesn't fork it.
    //
    private static ModList ReadyToShare(ModList list)
    {
        if (!list.IsEditable) return list;

        ModList? last = null;
        if (File.Exists(LastSharedPath(list.Id)) && ModListFile.Load(LastSharedPath(list.Id)) is { Succeeded: true } read)
            last = read.List;

        if (ModListPublication.NeedsNewRevision(list, last))
        {
            list = AppServices.ModLists.BumpRevision(list.Id) ?? list;
            AppLog.Info("Sharing", $"\"{list.Name}\" changed since it was last shared - now revision {list.Revision}");
        }

        try
        {
            ModListFile.Save(list, LastSharedPath(list.Id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Sharing", $"couldn't keep the shared copy of \"{list.Name}\": {ex.Message}");
        }

        return list;
    }

    // Who a code or file says it is from: you, for your own; the person who made it, for theirs.
    private static string? AuthorOf(ModList list) => list.IsEditable ? ShareName : list.Source;

    public static string? CodeFor(Guid id)
    {
        if (AppServices.ModLists.Find(id) is not { } list) return null;

        list = ReadyToShare(list);
        var code = ModListShareCode.Encode(list, AuthorOf(list));
        AppLog.Info("Sharing", $"share code for \"{list.Name}\" revision {list.Revision}: {list.Entries.Count} entries, {code.Length} characters");
        return code;
    }

    // Keeps one of your collections as a file in this folder from now on. Returns the error, if any.
    public static string? ShareToFolder(Guid id, string folder)
    {
        if (AppServices.ModLists.Find(id) is not { IsEditable: true } list) return null;

        var path = Path.Combine(folder, ModListFile.SuggestedFileName(list));
        var settings = Settings.Load();
        settings.SharedFiles[id] = path;
        Settings.Save(settings);

        AppLog.Info("Sharing", $"\"{list.Name}\" is kept in {path}");
        return WriteSharedFile(id, path, force: true);
    }

    public static void StopSharingToFolder(Guid id)
    {
        var settings = Settings.Load();
        if (settings.SharedFiles.Remove(id)) Settings.Save(settings);
    }

    //
    // Brings every shared folder's file up to date with its collection. Called when the Collections
    // pages show and after each change there - cheap when nothing has changed, since a file already
    // holding this revision with these contents is left alone, so a sync client has nothing to send.
    //
    public static void WriteSharedFolders()
    {
        var settings = Settings.Load();
        var gone = new List<Guid>();

        foreach (var (id, path) in settings.SharedFiles)
        {
            if (AppServices.ModLists.Find(id) is null) gone.Add(id);
            else WriteSharedFile(id, path, force: false);
        }

        if (gone.Count == 0) return;
        foreach (var id in gone) settings.SharedFiles.Remove(id);
        Settings.Save(settings);
    }

    private static string? WriteSharedFile(Guid id, string path, bool force)
    {
        if (AppServices.ModLists.Find(id) is not { IsEditable: true } list) return null;

        try
        {
            list = ReadyToShare(list);

            if (!force && File.Exists(path) && ModListFile.Load(path) is { Succeeded: true } there
                && there.List!.Revision == list.Revision && ModListPublication.ContentsMatch(there.List, list))
            {
                return null;
            }

            ModListFile.Save(list, path, AuthorOf(list));
            AppLog.Info("Sharing", $"wrote \"{list.Name}\" revision {list.Revision} to {path}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Sharing", $"couldn't write \"{list.Name}\" to {path}: {ex.Message}");
            return ex.Message;
        }
    }

    // ---------------------------------------------------------------- the friend receiving

    public enum Arrival { Added, Updated, AlreadyHave, AddedAsCopy, Kept }

    public sealed record Received(ModList List, Arrival Arrival, ModListDiff? Diff);

    // A share code's entries carry ids, not names: filled in here from the catalog, as is each
    // mod's plugin GUID, which is how a copy installed by hand is recognised.
    public static async Task<ModList> WithNamesAsync(ModList list)
    {
        await AppServices.ModCache.EnsureLoadedAsync();
        var catalog = AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

        var named = list.Entries.Select(e =>
        {
            if (e.ModId is not { } id || !e.Name.StartsWith('#')) return e;

            var mod = e.IsAddon ? null : catalog.GetValueOrDefault(id);
            var name = e.IsAddon ? AppServices.Addons.ById(id)?.Name : mod?.Name;

            return new ModListEntry
            {
                Name = string.IsNullOrWhiteSpace(name) ? e.Name : name,
                ModId = e.ModId,
                IsAddon = e.IsAddon,
                VersionId = e.VersionId,
                Version = e.Version,
                Guid = e.Guid ?? mod?.Guid,
                Folders = e.Folders,
                Scope = e.Scope,
            };
        }).ToList();

        list.Entries.Clear();
        list.Entries.AddRange(named);
        return list;
    }

    //
    // Stores a collection that has arrived - from a code, a file or a followed file - in place of
    // the copy of it already here, asking first where that would lose something: your own
    // collection of the same id (added as a copy instead), or a newer copy than the one arriving.
    // Null when the person chose not to.
    //
    public static Received? Receive(ModList incoming, string? followedFile = null)
    {
        var existing = AppServices.ModLists.Find(incoming.Id);

        switch (AppServices.ModLists.ClashFor(incoming))
        {
            case ModListImportClash.YourOwnList:
            {
                var copy = SteamDialog.Show(
                    Strings.Sharing_ArrivedTitle,
                    LocalizationService.Text(Strings.Sharing_YourOwnFormat, existing!.Name),
                    new SteamDialogChoice(Strings.Sharing_AddAsCopy, SteamDialogButton.Green, IsDefault: true),
                    new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
                if (copy != 0) return null;

                var separate = AppServices.ModLists.Add(ModListStore.AsSeparateList(
                    incoming, LocalizationService.Text(Strings.ModLists_ImportedCopyNameFormat, incoming.Name)));
                return new Received(separate, Arrival.AddedAsCopy, null);
            }

            case ModListImportClash.OlderThanStored:
            {
                var keep = SteamDialog.Show(
                    Strings.Sharing_ArrivedTitle,
                    LocalizationService.Text(Strings.Sharing_OlderFormat, existing!.Name, incoming.Revision, existing.Revision),
                    new SteamDialogChoice(Strings.Sharing_KeepMine, SteamDialogButton.Green, IsDefault: true),
                    new SteamDialogChoice(Strings.Sharing_UseOlder, SteamDialogButton.Blue),
                    new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
                if (keep != 1) return keep == 0 ? new Received(existing, Arrival.Kept, null) : null;
                break;
            }
        }

        var diff = existing is null ? null : ModListDiff.Between(existing, incoming);
        var stored = AppServices.ModLists.Add(incoming);

        if (followedFile is not null)
        {
            var settings = Settings.Load();
            settings.FollowedFiles[stored.Id] = followedFile;
            Settings.Save(settings);
        }

        var arrival = existing is null ? Arrival.Added
            : diff!.IsEmpty && existing.Revision == incoming.Revision ? Arrival.AlreadyHave
            : Arrival.Updated;

        AppLog.Info("Sharing", $"received \"{stored.Name}\" revision {stored.Revision} from {stored.Source ?? "someone"}: {arrival}"
            + (diff is null ? "" : $" (+{diff.Added.Count} -{diff.Removed.Count} ~{diff.Changed.Count})"));
        return new Received(stored, arrival, diff);
    }

    public static async Task<Received?> AddFromCodeAsync(string text)
    {
        var read = ModListShareCode.Decode(text);
        if (!read.Succeeded)
        {
            SteamDialog.Show(
                Strings.Sharing_AddFromCode,
                LocalizationService.Text(Strings.Sharing_CodeUnreadableFormat, read.Error),
                new SteamDialogChoice(Strings.Common_Close, SteamDialogButton.Blue, IsDefault: true));
            return null;
        }

        return Receive(await WithNamesAsync(read.List!));
    }

    public static async Task<Received?> FollowFileAsync(string path)
    {
        var read = ModListFile.Load(path);
        if (!read.Succeeded)
        {
            SteamDialog.Show(
                Strings.Sharing_Follow,
                LocalizationService.Text(Strings.ModLists_ImportReadFailedFormat, read.Error),
                new SteamDialogChoice(Strings.Common_Close, SteamDialogButton.Blue, IsDefault: true));
            return null;
        }

        return Receive(await WithNamesAsync(read.List!), path);
    }

    public static void StopFollowing(Guid id)
    {
        var settings = Settings.Load();
        if (settings.FollowedFiles.Remove(id)) Settings.Save(settings);
    }

    public sealed record FollowedUpdate(ModList Incoming, ModList Stored, ModListDiff Diff);

    // Every followed file that holds a newer copy than the one here. Unreadable or missing files are
    // passed over quietly - a sync client part-way through writing one is the usual reason.
    public static async Task<List<FollowedUpdate>> CheckFollowedAsync()
    {
        var updates = new List<FollowedUpdate>();
        var settings = Settings.Load();
        var gone = new List<Guid>();

        foreach (var (id, path) in settings.FollowedFiles)
        {
            if (AppServices.ModLists.Find(id) is not { } stored)
            {
                gone.Add(id);
                continue;
            }

            if (stored.IsEditable || !File.Exists(path)) continue;
            if (ModListFile.Load(path) is not { Succeeded: true } read || read.List!.Id != id) continue;

            var incoming = await WithNamesAsync(read.List);
            if (ModListDiff.IsNews(stored, incoming))
                updates.Add(new FollowedUpdate(incoming, stored, ModListDiff.Between(stored, incoming)));
        }

        if (gone.Count > 0)
        {
            foreach (var id in gone) settings.FollowedFiles.Remove(id);
            Settings.Save(settings);
        }

        return updates;
    }

    // ---------------------------------------------------------------- telling and syncing

    // "3 added (A, B, C) · 1 removed (D) · 2 updated (E, F)" - five names a part, then a count.
    public static string Describe(ModListDiff diff)
    {
        if (diff.IsEmpty) return Strings.Sharing_NoModChanges;

        var parts = new List<string>();
        if (diff.Added.Count > 0) parts.Add(LocalizationService.Text(Strings.Sharing_DiffAddedFormat, diff.Added.Count, Named(diff.Added)));
        if (diff.Removed.Count > 0) parts.Add(LocalizationService.Text(Strings.Sharing_DiffRemovedFormat, diff.Removed.Count, Named(diff.Removed)));
        if (diff.Changed.Count > 0) parts.Add(LocalizationService.Text(Strings.Sharing_DiffChangedFormat, diff.Changed.Count, Named(diff.Changed)));
        return string.Join(Strings.Common_FactSeparator, parts);
    }

    private static string Named(IReadOnlyList<string> names)
    {
        var sorted = names.Order(StringComparer.OrdinalIgnoreCase).ToList();
        return sorted.Count <= 5
            ? string.Join(Strings.Common_ListSeparator, sorted)
            : string.Join(Strings.Common_ListSeparator, sorted.Take(5)) + LocalizationService.Text(Strings.ModLists_AndMoreNamesFormat, sorted.Count - 5);
    }

    //
    // After an arrival: what came, and the way on. A new collection opens on its page, where
    // Subscribe to all is; an update offers Sync straight away.
    //
    public static void Announce(Received received)
    {
        var list = received.List;
        var from = list.Source ?? Strings.Sharing_Someone;

        switch (received.Arrival)
        {
            case Arrival.Updated:
                var sync = SteamDialog.Show(
                    Strings.Sharing_UpdatedTitle,
                    LocalizationService.Text(Strings.Sharing_UpdatedFormat, list.Name, from, Describe(received.Diff!)),
                    new SteamDialogChoice(Strings.Sharing_SyncNow, SteamDialogButton.Green, IsDefault: true),
                    new SteamDialogChoice(Strings.Sharing_Later, SteamDialogButton.Grey));
                if (sync == 0) Sync(list.Id);
                break;

            case Arrival.Kept:
                break;

            default:
                var text = received.Arrival == Arrival.AlreadyHave
                    ? LocalizationService.Text(Strings.Sharing_AlreadyHaveFormat, list.Name)
                    : LocalizationService.Text(Strings.Sharing_AddedFormat, list.Name, from, list.Entries.Count);
                var open = SteamDialog.Show(
                    Strings.Sharing_ArrivedTitle,
                    text,
                    new SteamDialogChoice(Strings.Sharing_Open, SteamDialogButton.Green, IsDefault: true),
                    new SteamDialogChoice(Strings.Common_Close, SteamDialogButton.Grey));
                if (open == 0) AppServices.CollectionOverlay.Show(list.Id);
                break;
        }
    }

    // The Collections page's preview of this collection, matching it exactly - so the group plays the
    // same set. Apply there is still what changes anything.
    public static void Sync(Guid id)
    {
        ModListsViewModel.Request(new ModListsRequest(id, ModListPolicy.Exclusive));
        AppNavigation.Navigate(typeof(ModListsPage));
    }
}
