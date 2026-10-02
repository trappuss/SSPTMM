namespace TCFModManager.Core.Models;

//
// What a mod's own DLL says about itself, read from its metadata without loading it. A client
// plugin declares this through [BepInPlugin]/[BepInDependency]; an SPT 4.x server mod through a
// record deriving from AbstractModMetadata (4.0) or implementing IModMetadata (4.1).
//
// Every value is optional: one the reader could not see as a constant is null, never guessed.
//
public sealed record DeclaredMetadata
{
    public static DeclaredMetadata Empty { get; } = new();

    public string? Guid { get; init; }

    public string? Name { get; init; }

    public string? Author { get; init; }

    public string? Version { get; init; }

    // Server mods only: the SPT range the mod says it was built for, e.g. "~4.0.0".
    public string? SptVersion { get; init; }

    public IReadOnlyList<ModDependencyRef> Dependencies { get; init; } = [];

    public bool IsEmpty => Guid is null && Name is null && Author is null && Version is null
        && SptVersion is null && Dependencies.Count == 0;
}
