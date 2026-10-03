using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM): what changed between two copies of one collection - for a friend whose copy has
// just been replaced by a newer one ("3 added, 1 removed, 2 updated").
//
// Compares only what the friend's game acts on: which mods, at which version, for which machine.
// Not names, plugin GUIDs or folders - a list that arrived as a share code has its names from the
// catalog and no folders at all, and none of that is a change to anyone's install.
// ModListPublication.ContentsMatch, which does compare them, answers a different question: whether
// a list has to be numbered again before it goes out.
//
public sealed record ModListDiff(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    //
    // Whether a copy that has just turned up - a code on the clipboard, a followed file - is worth
    // telling anyone about: a collection not here yet, a newer revision, or the same revision with
    // different mods in it (an owner whose app never renumbered). An OLDER copy is not news, however
    // different it is - it is what the friend's copy used to be.
    //
    public static bool IsNews(ModList? held, ModList arriving) =>
        held is null
        || arriving.Revision > held.Revision
        || (arriving.Revision == held.Revision && !Between(held, arriving).IsEmpty);

    public static ModListDiff Between(ModList before, ModList after)
    {
        var added = after.Entries
            .Where(e => !ModListEntries.Contains(before.Entries, e))
            .Select(e => e.Name)
            .ToList();

        var removed = before.Entries
            .Where(e => !ModListEntries.Contains(after.Entries, e))
            .Select(e => e.Name)
            .ToList();

        var changed = after.Entries
            .Select(now => (Now: now, Was: before.Entries.FirstOrDefault(b => ModListEntries.SameMod(b, now))))
            .Where(p => p.Was is not null && !SameInstall(p.Was, p.Now))
            .Select(p => p.Now.Name)
            .ToList();

        return new ModListDiff(added, removed, changed);
    }

    private static bool SameInstall(ModListEntry a, ModListEntry b) =>
        a.VersionId == b.VersionId
        && string.Equals(a.Version?.Trim() ?? "", b.Version?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
        && a.EffectiveScope == b.EffectiveScope;
}
