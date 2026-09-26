using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// One mod needing another, as declared by the dependent itself.
public sealed record ModDependencyLink(InstalledMod Dependent, InstalledMod Dependency, bool IsSoft);

/// <summary>A hard dependency a mod declares that nothing enabled provides: not installed at all
/// (<paramref name="DisabledProvider"/> null), or installed only in a disabled folder.</summary>
public sealed record ModMissingDependency(string Identifier, InstalledMod? DisabledProvider);

//
// Who needs whom among the mods actually installed, built from what each mod declares in its own
// files ([BepInDependency] for client mods, "modDependencies" for server mods) rather than from the
// catalog. Built locally so it covers hand-installed mods that never match an sp-mod.com listing,
// and needs no network.
//
public sealed class ModDependencyGraph
{
    private readonly Dictionary<InstalledMod, List<ModDependencyLink>> _dependents = [];
    private readonly Dictionary<InstalledMod, List<ModDependencyLink>> _dependencies = [];
    private readonly Dictionary<InstalledMod, List<string>> _unresolved = [];
    private readonly Dictionary<InstalledMod, List<ModMissingDependency>> _missing = [];

    private ModDependencyGraph() { }

    public static ModDependencyGraph Build(IEnumerable<InstalledMod> mods)
    {
        var all = mods.ToList();
        var graph = new ModDependencyGraph();

        // A client mod is identified by its [BepInPlugin] GUIDs, a server mod by its package name
        // (SPT 3) or ModGuid (SPT 4). Each can resolve more than one installed mod - the same mod
        // present in a container and in that container's ".disabled" sibling.
        //
        // Kept per side: BepInEx meets a [BepInDependency] only with a loaded plugin, and the SPT
        // server meets ModDependencies only with a loaded server mod. A mod's two halves often share
        // a GUID (SAIN's are both "me.sol.sain"), and its server half being there does nothing for
        // a plugin that needs the client half.
        var byIdentifier = new Dictionary<(InstalledModTarget, string), List<InstalledMod>>(SideComparer.Instance);

        foreach (var mod in all)
        {
            foreach (var identifier in Identifiers(mod))
            {
                if (!byIdentifier.TryGetValue((mod.Target, identifier), out var matches))
                    byIdentifier[(mod.Target, identifier)] = matches = [];

                matches.Add(mod);
            }
        }

        foreach (var mod in all)
        {
            // What the mod provides itself (a second plugin in its own folder) is never missing,
            // whatever other copies of it are doing.
            var own = Identifiers(mod).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var declared in mod.Dependencies)
            {
                if (!byIdentifier.TryGetValue((mod.Target, declared.Identifier), out var matches))
                {
                    graph.AddUnresolved(mod, declared.Identifier);
                    if (!declared.IsSoft) graph.AddMissing(mod, new ModMissingDependency(declared.Identifier, null));
                    continue;
                }

                // Provided only by disabled copies: on disk, but SPT loads none of them.
                var providers = matches.Where(m => !ReferenceEquals(m, mod)).ToList();
                if (!declared.IsSoft && !own.Contains(declared.Identifier) && providers.Count > 0 && providers.All(m => m.IsDisabled))
                    graph.AddMissing(mod, new ModMissingDependency(declared.Identifier, providers[0]));

                foreach (var dependency in matches)
                {
                    if (ReferenceEquals(dependency, mod)) continue;

                    var link = new ModDependencyLink(mod, dependency, declared.IsSoft);
                    List(graph._dependencies, mod).Add(link);
                    List(graph._dependents, dependency).Add(link);
                }
            }
        }

        return graph;
    }

    // Mods that declare a dependency on <paramref name="mod"/>, whatever their own state.
    public IReadOnlyList<ModDependencyLink> DependentsOf(InstalledMod mod) =>
        _dependents.TryGetValue(mod, out var links) ? links : [];

    // What <paramref name="mod"/> declares it needs, limited to what's actually installed.
    public IReadOnlyList<ModDependencyLink> DependenciesOf(InstalledMod mod) =>
        _dependencies.TryGetValue(mod, out var links) ? links : [];

    // Identifiers a mod declares that nothing installed provides.
    public IReadOnlyList<string> UnresolvedOf(InstalledMod mod) =>
        _unresolved.TryGetValue(mod, out var identifiers) ? identifiers : [];

    // What a mod declares it cannot run without (hard dependencies only) that no enabled mod provides.
    public IReadOnlyList<ModMissingDependency> MissingOf(InstalledMod mod) =>
        _missing.TryGetValue(mod, out var missing) ? missing : [];

    //
    // Every currently-enabled mod that would lose a dependency if <paramref name="roots"/> were
    // disabled, following the chain outward - a mod broken by a root, then whatever that mod
    // breaks in turn. The roots themselves are never included.
    //
    public IReadOnlyList<ModDependencyLink> DisableImpact(IEnumerable<InstalledMod> roots) =>
        Walk(roots, from => DependentsOf(from).Where(link => !link.Dependent.IsDisabled), link => link.Dependent);

    //
    // Every currently-disabled mod that <paramref name="roots"/> need in order to work once
    // enabled, following the chain inward. The roots themselves are never included.
    //
    public IReadOnlyList<ModDependencyLink> EnableRequirements(IEnumerable<InstalledMod> roots) =>
        Walk(roots, from => DependenciesOf(from).Where(link => link.Dependency.IsDisabled), link => link.Dependency);

    //
    // Breadth-first walk out from the roots, one link per mod reached - the first one that reached
    // it, with a hard link always preferred over a soft one for the same mod so the caller can word
    // the warning by the worst consequence.
    //
    private static List<ModDependencyLink> Walk(
        IEnumerable<InstalledMod> roots,
        Func<InstalledMod, IEnumerable<ModDependencyLink>> edges,
        Func<ModDependencyLink, InstalledMod> other)
    {
        var seen = new HashSet<InstalledMod>(roots);
        var queue = new Queue<InstalledMod>(seen);
        var reached = new List<ModDependencyLink>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var link in edges(current))
            {
                var next = other(link);

                if (!seen.Add(next))
                {
                    var existing = reached.FindIndex(r => ReferenceEquals(other(r), next));
                    if (existing >= 0 && reached[existing].IsSoft && !link.IsSoft) reached[existing] = link;
                    continue;
                }

                reached.Add(link);
                queue.Enqueue(next);
            }
        }

        return reached;
    }

    //
    // Every name something else could declare a dependency on this mod by. All of a folder's plugin
    // GUIDs count, not just its primary one: a mod shipping an API assembly alongside its own plugin
    // is most often depended on by that API's GUID, and matching only the primary would report the
    // dependency as unresolved and leave the dependant out of the disable cascade - while the mod
    // providing it is sitting right there installed.
    //
    private static IEnumerable<string> Identifiers(InstalledMod mod)
    {
        foreach (var guid in mod.AllGuids) yield return guid;
        if (mod.Target == InstalledModTarget.Server && !string.IsNullOrWhiteSpace(mod.Name)) yield return mod.Name;
    }

    private sealed class SideComparer : IEqualityComparer<(InstalledModTarget Side, string Identifier)>
    {
        public static readonly SideComparer Instance = new();

        public bool Equals((InstalledModTarget Side, string Identifier) x, (InstalledModTarget Side, string Identifier) y) =>
            x.Side == y.Side && StringComparer.OrdinalIgnoreCase.Equals(x.Identifier, y.Identifier);

        public int GetHashCode((InstalledModTarget Side, string Identifier) key) =>
            HashCode.Combine(key.Side, StringComparer.OrdinalIgnoreCase.GetHashCode(key.Identifier));
    }

    private static List<ModDependencyLink> List(Dictionary<InstalledMod, List<ModDependencyLink>> map, InstalledMod key)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        return list;
    }

    private void AddMissing(InstalledMod mod, ModMissingDependency missing)
    {
        if (!_missing.TryGetValue(mod, out var list)) _missing[mod] = list = [];
        if (!list.Any(m => string.Equals(m.Identifier, missing.Identifier, StringComparison.OrdinalIgnoreCase))) list.Add(missing);
    }

    private void AddUnresolved(InstalledMod mod, string identifier)
    {
        if (!_unresolved.TryGetValue(mod, out var list)) _unresolved[mod] = list = [];
        if (!list.Contains(identifier, StringComparer.OrdinalIgnoreCase)) list.Add(identifier);
    }
}
