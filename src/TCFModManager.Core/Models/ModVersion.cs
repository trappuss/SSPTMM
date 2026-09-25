namespace TCFModManager.Core.Models;

// A full mod version as returned by /mod/{modId}/versions.
public sealed class ModVersion
{
    public int Id { get; set; }
    public int? HubId { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Link { get; set; }
    public long? ContentLength { get; set; }
    public string? SptVersionConstraint { get; set; }
    public int? Downloads { get; set; }

    // "compatible", "incompatible", or "unknown".
    public string? FikaCompatibility { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    // This version's immediate dependencies. Only populated when requested via include=dependencies.
    public List<ModVersionDependency>? Dependencies { get; set; }
}

// One immediate dependency of a mod version, as returned by GET /mod/{modId}/versions?include=dependencies.
public sealed class ModVersionDependency
{
    public int Id { get; set; }
    public int ModId { get; set; }
    public string? ModGuid { get; set; }
    public string? ModName { get; set; }
    public string? VersionConstraint { get; set; }
    public bool IsOptional { get; set; }

    // What the API sends today: each dependency is the required mod's own record, so its id is the
    // mod's id and its name the mod's name. (The mod_* fields above stay for the older shape.)
    public string? Guid { get; set; }
    public string? Name { get; set; }
}
