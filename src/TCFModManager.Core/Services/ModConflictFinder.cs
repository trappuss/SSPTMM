using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// The three things the conflict check looks for (OPEN-11 §1).
public enum ModConflictKind
{
    // C1 - one [BepInPlugin] GUID in two mods' enabled client folders. BepInEx loads one and skips
    // the other, and the user doesn't get to choose which.
    DuplicatePlugin,

    // C2 - one SPT 4.x server ModGuid in two mods' enabled server folders. SPT skips every copy, so
    // the mod doesn't load at all (R6, tested on SPT 4.1.5).
    DuplicateServerMod,

    // C3 - one assembly in two mods' enabled client folders, with different contents. Whichever
    // copy loads first is used by both mods. Identical copies are never reported (R1).
    DifferentAssemblyCopies,
}

//
// One mod's part in a conflict. ModIndex is its position in the list Find was given; Entry is the
// folder (or loose DLL) holding the clashing thing. Assembly is set for C3 only - which copy, with
// its version.
//
public sealed record ModConflictMember(int ModIndex, InstalledMod Entry, ModAssembly? Assembly = null);

//
// Identifier is the GUID (C1, C2) or the assembly name - the file name for a native DLL (C3).
// Members holds every copy, at least two mods' worth.
//
public sealed record ModConflict(ModConflictKind Kind, string Identifier, IReadOnlyList<ModConflictMember> Members);

//
// Finds mods that will fight at load time, from what the scan found on disk (OPEN-11). Nothing here
// touches the network or the catalog, so it works for hand installs and offline. It only reads:
// the scan's metadata, and - for C3, when two copies of one assembly are the same size - their bytes.
//
// It is given mods, not scan entries: each inner list is everything the Installed page shows as one
// mod (client and server halves, a folded patcher, every folder one install placed). A mod's own
// folders never conflict with each other (D5). Disabled entries are never compared - SPT doesn't load
// them, and an enabled/disabled pair is the existing Duplicate case.
//
public static class ModConflictFinder
{
    public static List<ModConflict> Find(
        IReadOnlyList<IReadOnlyList<InstalledMod>> mods,
        Func<string, string?>? hashFile = null)
    {
        hashFile ??= HashFile;

        var conflicts = new List<ModConflict>();

        conflicts.AddRange(ByIdentifier(
            mods,
            ModConflictKind.DuplicatePlugin,
            e => e is { Target: InstalledModTarget.Client, IsPatcher: false },
            e => e.AllGuids));

        conflicts.AddRange(ByIdentifier(
            mods,
            ModConflictKind.DuplicateServerMod,
            e => e.Target == InstalledModTarget.Server,
            e => e.Guid is { Length: > 0 } guid ? [guid] : []));

        // Patchers are compared with patchers only, plugins with plugins (R2): they load at
        // different stages.
        var alreadyPaired = conflicts
            .Where(c => c.Kind == ModConflictKind.DuplicatePlugin)
            .Select(c => MemberSet(c))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var patchers in new[] { false, true })
        {
            foreach (var conflict in DifferentCopies(mods, patchers, hashFile))
            {
                // Two mods already reported as carrying the same plugin also share its DLL - one
                // finding, not two.
                if (!alreadyPaired.Contains(MemberSet(conflict))) conflicts.Add(conflict);
            }
        }

        return conflicts;
    }

    private static IEnumerable<ModConflict> ByIdentifier(
        IReadOnlyList<IReadOnlyList<InstalledMod>> mods,
        ModConflictKind kind,
        Func<InstalledMod, bool> applies,
        Func<InstalledMod, IEnumerable<string>> identifiers)
    {
        var holders = new Dictionary<string, List<ModConflictMember>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < mods.Count; i++)
        {
            foreach (var entry in mods[i].Where(e => !e.IsDisabled && applies(e)))
            {
                foreach (var id in identifiers(entry).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!holders.TryGetValue(id, out var list)) holders[id] = list = [];
                    list.Add(new ModConflictMember(i, entry));
                }
            }
        }

        return holders
            .Where(h => h.Value.Select(m => m.ModIndex).Distinct().Count() > 1)
            .Select(h => new ModConflict(kind, h.Key, h.Value));
    }

    private static IEnumerable<ModConflict> DifferentCopies(
        IReadOnlyList<IReadOnlyList<InstalledMod>> mods,
        bool patchers,
        Func<string, string?> hashFile)
    {
        var copies = new Dictionary<string, List<ModConflictMember>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < mods.Count; i++)
        {
            foreach (var entry in mods[i].Where(e => !e.IsDisabled && e.Target == InstalledModTarget.Client && e.IsPatcher == patchers))
            {
                foreach (var assembly in entry.Assemblies)
                {
                    var key = assembly.AssemblyName ?? assembly.FileName;
                    if (!copies.TryGetValue(key, out var list)) copies[key] = list = [];
                    list.Add(new ModConflictMember(i, entry, assembly));
                }
            }
        }

        foreach (var (key, members) in copies)
        {
            if (members.Select(m => m.ModIndex).Distinct().Count() < 2) continue;
            if (AllIdentical(members, hashFile)) continue;

            yield return new ModConflict(ModConflictKind.DifferentAssemblyCopies, key, members);
        }
    }

    //
    // Whether every copy is byte-for-byte the same. Cheap checks first - a different version or size
    // settles it without reading anything - and a file that can't be read counts as different, so a
    // problem is never hidden because a hash failed.
    //
    private static bool AllIdentical(List<ModConflictMember> members, Func<string, string?> hashFile)
    {
        var first = members[0].Assembly!;

        if (members.Any(m => m.Assembly!.Size != first.Size || m.Assembly.Size < 0)) return false;
        if (members.Any(m => !string.Equals(m.Assembly!.AssemblyVersion, first.AssemblyVersion, StringComparison.Ordinal)))
            return false;

        string? firstHash = null;
        foreach (var member in members)
        {
            var hash = hashFile(FullPath(member));
            if (hash is null) return false;

            firstHash ??= hash;
            if (!string.Equals(hash, firstHash, StringComparison.Ordinal)) return false;
        }

        return true;
    }

    // A loose DLL's FolderPath is the DLL itself; a folder entry's assemblies are relative to it.
    public static string FullPath(ModConflictMember member) =>
        File.Exists(member.Entry.FolderPath)
            ? member.Entry.FolderPath
            : Path.Combine(member.Entry.FolderPath, member.Assembly!.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string MemberSet(ModConflict conflict) =>
        string.Join(',', conflict.Members.Select(m => m.ModIndex).Distinct().Order());

    private static string? HashFile(string path) => FileFingerprint.Compute(path, path)?.Sha256;
}
