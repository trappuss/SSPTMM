namespace TCFModManager.Core.Models;

// Which half of an SPT install a discovered mod lives in: BepInEx plugins/patchers (client) or user/mods (server).
public enum InstalledModTarget
{
    Client,
    Server,
}

//
// One dependency an installed mod declares about itself. Identifier is a BepInEx plugin GUID for a
// client mod (from [BepInDependency]), a package.json package name for an SPT 3.x server mod (from
// "modDependencies"), or a mod GUID for an SPT 4.x server mod (from its ModDependencies). A soft
// dependency is one the dependent still loads without.
//
// VersionRange is the version the dependant asks for, as written ("~1.0.0", ">=2.0.0"), when it
// declared one. Carried, not judged: compare it with ModVersionMatcher, never SptVersionMatcher.
//
public sealed record ModDependencyRef(string Identifier, bool IsSoft, string? VersionRange = null);

//
// One DLL inside an enabled client entry, for the conflict check (OPEN-11 C3). RelativePath is from
// the entry's own folder, forward-slash; a loose DLL's is just its file name. AssemblyName and
// AssemblyVersion come from the assembly's own definition and are null for a file that isn't a
// managed assembly (a native DLL), which is then known only by its file name.
//
public sealed record ModAssembly(string RelativePath, string? AssemblyName, string? AssemblyVersion, long Size)
{
    public string FileName => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
}

// One mod found on disk by InstalledModScanner. Represents what's installed locally, not a catalog listing.
public sealed class InstalledMod
{
    public required string Name { get; init; }

    //
    // The version the mod says it is: package.json's "version" for an SPT 3.x server mod, the
    // declared version for an SPT 4.x server mod or a client plugin ([BepInPlugin]'s third
    // argument), and the DLL's file version only when nothing is declared. Null when no version
    // could be determined.
    //
    public string? Version { get; init; }

    //
    // The DLL's own file version, whatever Version ended up as. Authors routinely leave it at
    // 1.0.0.0, which is why it is only a fallback - kept for the "Files report ..." line.
    //
    public string? FileVersion { get; init; }

    //
    // The name the mod gives itself - [BepInPlugin]'s name, or an SPT 4.x server mod's declared
    // Name - when it declares one. Display only: Name stays the folder name, since groups, pins
    // and list entries are keyed on it.
    //
    public string? DeclaredName { get; init; }

    // SPT 4.x server mods only: the SPT range the mod says it was built for, e.g. "~4.0.0".
    public string? SptVersion { get; init; }

    //
    // The GUID that stands for this mod's identity - the first [BepInPlugin] GUID found in a client
    // mod's folder, or an SPT 4.x server mod's declared ModGuid. Null for an SPT 3.x server mod, and
    // whenever none could be read. Catalog matching and the dependency graph key on this, so it
    // stays one value per mod.
    //
    public string? Guid { get; init; }

    //
    // Every [BepInPlugin] GUID found in this mod's folder, not just the one above.
    //
    // A single mod folder routinely holds several plugin DLLs - an API, a config UI, a utilities
    // assembly - each registering its own GUID, and each keeping its own file in BepInEx\config
    // named after it. Keeping only the first would leave all the others' configs looking like they
    // belonged to no installed mod at all. A server mod has at most one - its declared ModGuid.
    //
    public IReadOnlyList<string> Guids { get; init; } = [];

    // Guids, falling back to the single Guid when the list wasn't populated - so a mod built by
    // hand with only Guid set still answers correctly.
    public IReadOnlyList<string> AllGuids =>
        Guids.Count > 0 ? Guids : Guid is null ? [] : [Guid];

    // Populated only for server mods, from package.json's "author" field or the declared Author.
    public string? Author { get; init; }

    public required InstalledModTarget Target { get; init; }

    //
    // True when this was found under BepInEx\patchers rather than BepInEx\plugins - always false for
    // a server mod. A patcher is run by BepInEx's preloader before the game's own assemblies load,
    // not as a plugin, so it carries no [BepInPlugin] attribute and has no GUID of its own to match
    // against a catalog listing. A mod that ships one almost always ships a plugin alongside it,
    // under a different folder name; InstalledModCardViewModel folds the two back together.
    //
    public bool IsPatcher { get; init; }

    // Full path to the mod's folder, or the DLL itself for a loose client DLL.
    public required string FolderPath { get; init; }

    // The folder's (or loose DLL's) filesystem creation time, used as a proxy for install date. Null if it couldn't be read.
    public DateTimeOffset? InstalledAt { get; init; }

    //
    // True when this mod was found under a ".disabled" sibling of its normal container
    // (e.g. user\mods.disabled) rather than the live one - still on disk, but not loaded by SPT.
    //
    public bool IsDisabled { get; init; }

    // What this mod declares it needs, read from its own files. Empty when it declares nothing.
    public IReadOnlyList<ModDependencyRef> Dependencies { get; init; } = [];

    //
    // Every DLL BepInEx would load from this entry - recursive, as BepInEx searches plugins - with its
    // assembly identity. Filled for enabled client entries only: a disabled one isn't loaded, so it
    // can't conflict, and a server mod's DLLs are loaded by SPT one mod at a time.
    //
    public IReadOnlyList<ModAssembly> Assemblies { get; init; } = [];
}
