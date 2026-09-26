using TCFModManager.App.ViewModels;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Turns the Installed page's cards into the shape every Core mod-list piece takes.
//
// Kept apart from ModListService, which needs WPF for the dispatcher: this half references no UI
// type, so it compiles into a plain console app and can be exercised headlessly - and it is the
// half most worth proving, since a wrong folder name or a dropped Entries list only shows up when
// an apply moves the wrong thing.
//
public static class ModListCandidates
{
    public static List<ModListCandidate> From(IEnumerable<InstalledModCardViewModel> cards) =>
        [.. cards.Select(From)];

    //
    // The cards, plus any addon the manifest knows about that no card covers.
    //
    // An addon usually installs into its parent mod's folder, so the scanner sees one folder and
    // the Installed page shows one card - the parent's. Without this, a list would silently leave
    // those addons out, which is the whole thing lists carrying addons was meant to avoid. They go
    // in as install/update-only: there is no folder of their own to move, so nothing can disable
    // one on its own, and disabling the parent takes it with it regardless.
    //
    public static List<ModListCandidate> From(
        IEnumerable<InstalledModCardViewModel> cards,
        IReadOnlyList<InstalledModRecord> records)
    {
        var candidates = From(cards);

        var covered = candidates
            .Where(c => c is { IsAddon: true, ModId: not null })
            .Select(c => c.ModId!.Value)
            .ToHashSet();

        foreach (var record in records.Where(r => r.IsAddon && !covered.Contains(r.ModId)))
        {
            candidates.Add(new ModListCandidate
            {
                Name = record.Name,
                ModId = record.ModId,
                IsAddon = true,
                Version = record.Version,
                CanBeDisabled = false,

                // Deliberately no folders: the ones this record names are its parent's, and putting
                // them on the addon would let a list entry match the parent by folder name when the
                // id tier missed. An addon always has an id, so the id tier always applies.
                Folders = [],
            });
        }

        return candidates;
    }

    //
    // Which machines a card's mod is for, as a list entry's scope. The scanner already knows which
    // halves of the install it occupies - HasPlugin/HasPatcher/HasServer come straight off the
    // folders it found - so an operator publishing their install does not tag twenty mods by hand.
    //
    // A client mod is Client|Headless: a Fika headless client runs the game, so a plugin is at least
    // potentially its business, and NOTHING ON DISK separates a bot overhaul from a HUD widget. Both
    // are a DLL in BepInEx\plugins. The scanner cannot make that call and neither should this, so
    // the headless is included and the operator prunes the ones it does not need - the direction
    // where being wrong is merely wasteful rather than something everyone in the raid feels.
    //
    // A server-only mod stays Server alone. It still reaches a headless box, because that machine
    // is a full SPT install and asks for server-scoped entries too; it just is not a headless
    // concern in its own right.
    //
    // A card that occupies neither half is Everyone rather than a guess: the honest answer for
    // something the scanner could not place, and the value that changes nothing.
    //
    public static ModListEntryScope ScopeOf(InstalledModCardViewModel card) =>
        (card.HasPlugin || card.HasPatcher, card.HasServer) switch
        {
            (true, false) => ModListEntryScope.Client | ModListEntryScope.Headless,
            (false, true) => ModListEntryScope.Server,
            _ => ModListEntryScope.Everyone,
        };

    public static ModListCandidate From(InstalledModCardViewModel card) => new()
    {
        Scope = ScopeOf(card),

        //
        // The card's DisplayTitle, not its Name: Name is the raw folder the scanner found, and this
        // is what a list entry stores and shows. A list read "acidphantasm-itemvaluewatermark"
        // where it could have read "Item Value Watermark", and once the row started printing the
        // folder underneath as well it said the same thing twice.
        //
        // DisplayTitle falls back to the folder name when nothing in the catalog matched, so a
        // hand-installed mod is named exactly as it was before.
        //
        // Safe for matching: the planner walks mod id -> GUID -> folder -> name, and Folders is
        // untouched, so a list captured before this still finds its mods on the folder tier.
        //
        Name = card.DisplayTitle,

        // An archive installed from a file under a local id (LocalArchive) is no sp-mod.com listing:
        // listed like a hand install, by its folders and name.
        ModId = card.ModId is > 0 ? card.ModId : null,
        IsAddon = card.IsAddon,
        Version = card.InstalledVersion,
        Guid = card.Guid,
        IsDisabled = card.IsDisabled,

        // A half-installed mod reports the version it was installed as, so without this the planner
        // reads it as satisfied and the missing half never comes back.
        IsIncomplete = card.IsIncompleteInstall,
        Folders = FoldersOf(card),
        Entries = card.Entries,
    };

    private static List<string> FoldersOf(InstalledModCardViewModel card)
    {
        // Every folder the card covers, not just the two it leads with: a mod that installed several
        // plugin folders, or shipped a patcher, answers to all of them, and a list entry captured
        // against any one of them has to find it again.
        var folders = card.Entries
            .Select(e => e.Name)
            .Concat([card.ClientFolderName, card.ServerFolderName])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        // A card that reported neither still needs something for a list entry with no mod id to be
        // matched on, and its own name is what the scanner named the folder after.
        if (folders.Count == 0) folders.Add(card.Name.Trim().ToLowerInvariant());

        return folders;
    }
}
