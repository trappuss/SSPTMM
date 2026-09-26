namespace TCFModManager.Core.Services;

//
// Which operation was refused, so the App can word the refusal without Core having to.
//
// Core names the operation; the sentence the user reads is built in App/Services/
// ModInstallProblems. Passing a verb phrase in the other direction is what this replaces - Core
// used to take strings like "sorting out a duplicated mod" from its callers and paste them into a
// finished sentence, which put the wording on the wrong side of the boundary and made it
// impossible to change without editing a project that has no UI.
//
public enum ModInstallAction
{
    Install,
    Remove,
    Disable,
    Enable,
    Undo,
    SortOutDuplicate,

    // Applying a mod list, which enables and disables mods in one pass.
    ApplyList,

    // Putting a copy of the SPT profiles back.
    RestoreProfiles,
}

//
// Why an install, removal or enable/disable could not go ahead.
//
public enum ModInstallFailure
{
    // SPT or its server is running, so files inside the install are locked. Carries Running - the
    // process names to close - and Action, the operation that was refused.
    InstallInUse,

    // No SPT folder is configured yet. Carries nothing.
    NoInstallFolder,

    // The chosen version has no file to fetch. Carries ModName and Version.
    NoDownloadLink,

    // The archive downloaded, but nothing in it looks like an SPT mod. Carries ModName and Version.
    UnrecognisedArchive,

    // Files were already being written when something failed, so the install is half-done. Carries
    // ModName, Version, PlacedFiles, TotalFiles, and the underlying exception as InnerException.
    PartlyInstalled,

    // An archive entry's path would land outside the folder being extracted into - the classic zip
    // traversal. Carries ArchiveEntry.
    UnsafeArchiveEntry,

    // The download ended before all of the bytes the server promised had arrived. Carries
    // ExpectedBytes and ReceivedBytes.
    DownloadIncomplete,

    // Placing the files failed part-way, and the install was put back exactly as it was before - the
    // previous version, if there was one, is still installed. Carries ModName, Version and the
    // underlying exception as InnerException.
    RolledBack,

    // Not enough free space on the install's drive to copy the files in. Carries ModName, Version,
    // ExpectedBytes (needed) and ReceivedBytes (free).
    NotEnoughSpace,
}

//
// The values are init-only and nullable because which of them is filled depends on Reason - see the
// comment on each case for what to expect. Message is the reason name, for the log and the
// debugger; it is not shown to anyone. Every consumer is a catch that runs it through
// ModInstallProblems.Describe instead.
//
public sealed class ModInstallException(ModInstallFailure reason, Exception? inner = null)
    : Exception(reason.ToString(), inner)
{
    public ModInstallFailure Reason { get; } = reason;

    // What the user has to close. In the order RunningBlockers found them, which is the order the
    // sentence lists them in.
    public IReadOnlyList<string> Running { get; init; } = [];

    public ModInstallAction Action { get; init; }

    public string? ModName { get; init; }

    public string? Version { get; init; }

    public int? PlacedFiles { get; init; }

    public int? TotalFiles { get; init; }

    public string? ArchiveEntry { get; init; }

    // Content-Length, and how much of it actually arrived.
    public long? ExpectedBytes { get; init; }

    public long? ReceivedBytes { get; init; }
}
