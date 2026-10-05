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

    // Fork: putting a copy of the SPT profiles back (ProfileBackups).
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

    // A download-only save was pointed at a folder that doesn't exist - most likely one picked on
    // Options and since moved or deleted. Carries Folder.
    DownloadFolderMissing,

    // A download-only save couldn't create its file in the chosen folder. Carries Folder, and the
    // underlying exception as InnerException.
    DownloadFolderNotWritable,

    // A removal was refused before anything was deleted, because the path failed one of
    // InstallPathGuard's checks. Carries Folder (the path) and Refusal (which check).
    RemovalRefused,

    // The extracted archive holds a junction or symbolic link. Refused before anything in the install
    // is removed or placed. Carries ModName, Version and ArchiveEntry (the link, relative to the
    // archive's content).
    ArchiveContainsLink,

    // A file the archive places would be written through a junction or symbolic link in the install,
    // which would change whatever the link points at. Refused before anything is removed or placed.
    // Carries ModName, Version and Folder (the install-relative path that passes through the link).
    InstallThroughLink,

    // A file the archive places would replace one no record owns, and that file couldn't be copied
    // aside first. Refused before anything is removed or placed. Carries ModName, Version, Folder
    // (the install-relative path) and the underlying exception as InnerException.
    OriginalNotKept,

    // The record being removed or updated was made in a different SPT install (D17). Refused before
    // anything is touched. Carries ModName and Folder (the install the record belongs to).
    RecordFromAnotherInstall,

    // Every file in the archive was refused - each would have replaced SPT's, BepInEx's or the game's
    // own copy, or landed on this app's own files - so nothing was placed and nothing changed.
    // Carries ModName, Version, TotalFiles (how many were refused) and ArchiveEntry (the first of
    // them, install-relative).
    NothingToPlace,
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

    // The download folder a download-only save was refused, or the path a removal refused.
    public string? Folder { get; init; }

    // Which of InstallPathGuard's checks refused a removal.
    public PathRefusal? Refusal { get; init; }
}
