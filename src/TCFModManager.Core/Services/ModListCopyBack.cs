using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.4): a friend's copy of one of your collections, come back to you.
//
// A friend can't change your collection - their copy of it is read-only, and the code they were
// sent is the whole list rather than a link to it. What they can do is Make a copy, which is theirs
// to change and remembers yours (DerivedFrom), add the mods they would like, and send you its code.
// Arriving here, it is offered for what it is: the mods it has that yours doesn't, each one yours
// to take or leave. What you take joins your collection, and reaches everyone the next time you
// share it.
//
// Only additions. A mod the copy left out stays in yours - leaving one out of a copy is as likely
// to be "I don't want that one" as "the group shouldn't have it" - and a different version of a
// mod both have is the copy's business. The count of what it left out is told, never acted on.
//
public sealed record ModListCopyBack(
    ModList Yours,
    ModList Theirs,
    IReadOnlyList<ModListEntry> Adds,
    IReadOnlyList<ModListEntry> LeftOut)
{
    //
    // What this arrival is, if it is a copy of one of your own collections. Null for anything else:
    // a collection with no DerivedFrom, one copied from a list that isn't yours to change (a
    // friend's, a server's, a public collection), and your own collection read back, which keeps
    // the clash it already has (ModListImportClash.YourOwnList).
    //
    public static ModListCopyBack? For(ModList incoming, IEnumerable<ModList> held)
    {
        if (incoming.DerivedFrom is not { } parent || parent == incoming.Id) return null;

        var lists = held.ToList();
        if (lists.Any(l => l.Id == incoming.Id && l.IsEditable)) return null;
        if (lists.FirstOrDefault(l => l.Id == parent) is not { IsEditable: true } yours) return null;

        var adds = new List<ModListEntry>();
        foreach (var entry in incoming.Entries)
        {
            if (!ModListEntries.Contains(yours.Entries, entry) && !ModListEntries.Contains(adds, entry)) adds.Add(entry);
        }

        var leftOut = yours.Entries.Where(e => !ModListEntries.Contains(incoming.Entries, e)).ToList();
        return new ModListCopyBack(yours, incoming, adds, leftOut);
    }

    // Your collection's entries with these of the copy's added, in the order a list keeps.
    public List<ModListEntry> With(IEnumerable<ModListEntry> taken) =>
        ModListEntries.Sorted(Yours.Entries.Concat(taken.Where(t => !ModListEntries.Contains(Yours.Entries, t))));
}
