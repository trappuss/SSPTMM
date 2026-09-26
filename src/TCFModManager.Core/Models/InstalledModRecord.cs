namespace TCFModManager.Core.Models;

// A mod this app knows the installed version of - either because it installed it itself (IsAppManaged,
// with the files it placed), or because the user manually confirmed/overrode the version of a mod
// installed by hand (IsAppManaged: false, Files empty - there's nothing here for this app to clean up).
public sealed class InstalledModRecord
{
    public required int ModId { get; init; }

    //
    // True when ModId is an sp-mod.com addon id rather than a mod id. The two are separate
    // sequences, so a record's identity is the (ModId, IsAddon) pair - addon 116 and mod 116 would
    // otherwise overwrite each other here. Defaults false, so every record written before addons
    // were supported keeps meaning exactly what it always meant, with no migration.
    //
    public bool IsAddon { get; init; }

    // The catalog mod's GUID at install time. Always null for an addon - sp-mod.com doesn't give
    // addons a GUID.
    public string? Guid { get; init; }

    // The catalog mod's display name at install time.
    public required string Name { get; init; }

    // Null for a manually-confirmed version that isn't one of the mod's cached published versions.
    public int? VersionId { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset InstalledAt { get; init; }

    // Every file this install placed, relative to the SPT install root, forward-slash separated. The
    // exact list Uninstall deletes. Empty for a manually-confirmed record (IsAppManaged: false).
    public List<string> Files { get; init; } = [];

    //
    // The mod folder names this install placed (e.g. "EpicsAIO", "WTT-ClientCommonLib") - the same
    // names InstalledModScanner reports, so the Installed page can tie a folder on disk back to
    // this record rather than guessing the catalog listing from the folder name.
    //
    // Empty on records written before this was stored; InstalledModFolders.Resolve derives
    // them from Files in that case, so an existing manifest keeps working without reinstalling.
    public List<string> Folders { get; init; } = [];

    //
    // True when the install failed partway through and Files lists only what was placed before it
    // stopped. The record is written anyway so those files stay app-managed - a reinstall replaces
    // them, a removal cleans them up - rather than being left orphaned in the install.
    //
    public bool Incomplete { get; init; }

    //
    // True when this app placed Files itself, so Remove/uninstall can delete exactly those files.
    // False when the record only exists to hold a manually-confirmed/overridden version for a mod
    // installed by hand - Files is empty, and Remove falls back to deleting the mod's whole folder.
    // Defaults true so records written before this field existed - every one of them a real
    // app-managed install - keep meaning what they always meant.
    //
    public bool IsAppManaged { get; init; } = true;

    //
    // Files that were already in the install - placed by hand, or by another mod - where this install
    // put one of its own (install-relative, forward-slash). A copy of each was kept first (see
    // ReplacedFileStore), and removing this mod puts it back rather than leaving a hole. Empty on
    // records written before this existed.
    //
    public List<string> Replaced { get; init; } = [];
}

// The full set of installed-mod records for one SPT install.
public sealed class ModInstallManifest
{
    public List<InstalledModRecord> Mods { get; init; } = [];
}
