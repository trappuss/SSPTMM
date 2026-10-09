using System.Runtime.CompilerServices;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Which mod lists each installed mod belongs to.
//
// Answered by running the planner once per list and keeping what it matched, rather than by a
// second copy of the matching rules. Entry-to-installed matching walks mod id -> plugin GUID ->
// folder name -> display name and claims each installed mod once; a badge that disagreed with what
// applying the list would actually do would be worse than no badge at all.
//
public static class ModListMembership
{
    //
    // For each installed mod, by the same index it was passed in at, the lists that name it.
    //
    // Indexed rather than keyed on the candidate: ModListCandidate is a record, so two mods with
    // the same fields would collide as dictionary keys.
    //
    public static IReadOnlyList<IReadOnlyList<ModList>> Build(
        IReadOnlyList<ModList> lists,
        IReadOnlyList<ModListCandidate> installed)
    {
        var membership = new List<ModList>[installed.Count];
        for (var i = 0; i < membership.Length; i++) membership[i] = [];

        if (lists.Count == 0 || installed.Count == 0) return membership;

        var indexOf = new Dictionary<ModListCandidate, int>(ByReference.Instance);
        for (var i = 0; i < installed.Count; i++) indexOf.TryAdd(installed[i], i);

        foreach (var list in lists)
        {
            if (list.IsSnapshot) continue;

            foreach (var action in ModListPlanner.Build(list, installed).Actions)
            {
                // A Disable names a mod the list deliberately leaves out, so it is not membership.
                if (action.Kind is ModListActionKind.Disable or ModListActionKind.Pinned) continue;
                if (action.Installed is not { } candidate) continue;
                if (!indexOf.TryGetValue(candidate, out var index)) continue;

                // Once per list: two entries of one list can both resolve to the same installed mod
                // (ModListMatch does not claim a mod it has matched), and its badge then named the
                // collection twice. Seen 2026-10-08; the plan still holds both actions.
                if (!membership[index].Contains(list)) membership[index].Add(list);
            }
        }

        return membership;
    }

    // Just the names, in the order the lists were given - what a row of badges renders.
    public static IReadOnlyList<IReadOnlyList<string>> Names(
        IReadOnlyList<ModList> lists,
        IReadOnlyList<ModListCandidate> installed) =>
        [.. Build(lists, installed).Select(ls => (IReadOnlyList<string>)[.. ls.Select(l => l.Name)])];

    private sealed class ByReference : IEqualityComparer<ModListCandidate>
    {
        public static readonly ByReference Instance = new();

        public bool Equals(ModListCandidate? x, ModListCandidate? y) => ReferenceEquals(x, y);

        public int GetHashCode(ModListCandidate obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
