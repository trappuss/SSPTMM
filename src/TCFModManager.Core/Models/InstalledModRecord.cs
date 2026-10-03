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
    // The size and SHA-256 of files in Files as they were placed, so a removal only ever takes a file
    // that is still exactly what was put there (D21). Empty on records written before v1.19.0 and on
    // manually-confirmed ones; a file with no fingerprint falls back to the path checks alone.
    // A list rather than a dictionary because a deserialized dictionary would lose its
    // case-insensitive comparer - look one up with FingerprintFor.
    //
    public List<FileFingerprint> Fingerprints { get; init; } = [];

    //
    // The SPT install this record was made in, as a full path - removal refuses anywhere else (D17).
    // Null on records written before v1.19.0 until they are stamped.
    //
    public string? InstallPath { get; init; }

    // Files this install placed over that no record owned, with where the originals were kept (D22).
    public List<OverwrittenFile> Overwrote { get; init; } = [];

    // Fork: the archive's size in bytes, and the size sp-mod.com listed for the version (its
    // content_length) when it was installed. Null on records made before they were kept. Together they
    // let ReuploadCheck tell when the same version was uploaded again.
    public long? ArchiveBytes { get; init; }

    public long? ListedBytes { get; init; }

    public FileFingerprint? FingerprintFor(string path) =>
        Fingerprints.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
}

// The full set of installed-mod records for one SPT install.
public sealed class ModInstallManifest
{
    public List<InstalledModRecord> Mods { get; init; } = [];

    // Mods and addons are numbered separately and the numbers overlap, so a record is only ever
    // found by both halves of its identity.
    public InstalledModRecord? Find(int modId, bool isAddon) =>
        Mods.FirstOrDefault(m => m.ModId == modId && m.IsAddon == isAddon);

    // Fork: the records that can describe <paramref name="installPath"/> - its own, and the ones made
    // before 1.19.0 that name no install. Another install's record says nothing about the files here:
    // read as this install's, a folder there showed as app-installed at that install's version, and
    // could not then be removed or updated here (D17). Every record when no install is given.
    public List<InstalledModRecord> ModsFor(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return Mods;

        var stamp = Services.InstallStamp.Of(installPath);
        return [.. Mods.Where(m => m.InstallPath is null || string.Equals(m.InstallPath, stamp, StringComparison.OrdinalIgnoreCase))];
    }
}
