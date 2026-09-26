namespace TCFModManager.Core.Models;

// Which half of an SPT install a discovered mod lives in: BepInEx plugins/patchers (client) or user/mods (server).
public enum InstalledModTarget
{
    Client,
    Server,
}

//
// One dependency an installed mod declares about itself. Identifier is a BepInEx plugin GUID for a
// client mod (from [BepInDependency]), a package.json package name for an SPT 3 server mod (from
// "modDependencies"), or another server mod's ModGuid for an SPT 4 one (from its metadata's
// ModDependencies). A soft dependency is one the dependent still loads without.
//
public sealed record ModDependencyRef(string Identifier, bool IsSoft);

// One mod found on disk by InstalledModScanner. Represents what's installed locally, not a catalog listing.
public sealed class InstalledMod
{
    public required string Name { get; init; }

    // Null when no version could be determined.
    public string? Version { get; init; }

    //
    // A plugin's own version, from its [BepInPlugin] - the one BepInEx loads it as - when that differs
    // from Version (the DLL's file version). Which of the two a published version is, is decided
    // where the published versions are known (InstalledModCardViewModel.PluginVersionOf).
    //
    public string? PluginVersion { get; init; }

    //
    // The GUID that stands for this mod's identity - the first [BepInPlugin] GUID found in its
    // folder, or an SPT 4 server mod's ModGuid (from the metadata class in its DLL). Null for an
    // SPT 3 server mod (package.json), and wherever none could be read. Catalog matching and the
    // dependency graph key on this, so it stays one value per mod.
    //
    // A mod's two halves often share one GUID (SAIN's plugin and server mod are both "me.sol.sain")
    // and often do not ("com.chazut.orbit" and "com.chazut.orbit.server"). A client dependency is
    // only ever met by a client GUID, a server one by a server GUID - see ModDependencyGraph.
    //
    public string? Guid { get; init; }

    //
    // Every [BepInPlugin] GUID found in this mod's folder, not just the one above (for a server
    // mod, its one ModGuid).
    //
    // A single mod folder routinely holds several plugin DLLs - an API, a config UI, a utilities
    // assembly - each registering its own GUID, and each keeping its own file in BepInEx\config
    // named after it. Keeping only the first would leave all the others' configs looking like they
    // belonged to no installed mod at all.
    //
    public IReadOnlyList<string> Guids { get; init; } = [];

    // Guids, falling back to the single Guid when the list wasn't populated - so a mod built by
    // hand with only Guid set still answers correctly.
    public IReadOnlyList<string> AllGuids =>
        Guids.Count > 0 ? Guids : Guid is null ? [] : [Guid];

    // Populated only for server mods, from package.json's "author" field or the DLL's metadata.
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

    //
    // True for a server mod whose name for dependencies could not be read - an SPT 4 mod whose DLL
    // gives no GUID as a plain literal. A server dependency nothing installed is found for may then
    // be this one, so none is called missing (see ModDependencyGraph).
    //
    public bool IdentityUnknown { get; init; }

    // What this mod declares it needs, read from its own files. Empty when it declares nothing.
    public IReadOnlyList<ModDependencyRef> Dependencies { get; init; } = [];
}
