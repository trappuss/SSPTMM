using System.Globalization;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What the user is told when installing, removing, enabling or disabling a mod can't go ahead.
//
// ModInstallService and ModDisableService report a ModInstallFailure plus the values behind it and
// stop there; the wording lives here, beside the rest of this app's prose, for the same reason
// AppUpdateProblems exists - so it can be read and changed in one place rather than hunted for
// among file operations in a project that has no UI.
//
// Every sentence says what happened, then what the user can do about it.
//
public static class ModInstallProblems
{
    public static string Describe(ModInstallException problem) => problem.Reason switch
    {
        ModInstallFailure.InstallInUse => InstallInUse(problem.Running, problem.Action),

        ModInstallFailure.NoInstallFolder => AppMessages.NoSptInstallFolder,

        ModInstallFailure.NoDownloadLink => NoDownloadLink(problem.ModName, problem.Version),

        ModInstallFailure.UnrecognisedArchive => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_UnrecognisedArchiveFormat,
            problem.ModName,
            problem.Version),

        // The inner exception is what actually went wrong part way through, and it is the only
        // thing here that says why - so it is quoted rather than summarised.
        ModInstallFailure.PartlyInstalled => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_PartlyInstalledFormat,
            problem.ModName,
            problem.Version,
            problem.PlacedFiles,
            problem.TotalFiles,
            problem.InnerException?.Message),

        ModInstallFailure.DownloadIncomplete => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_DownloadIncompleteFormat,
            Size(problem.ReceivedBytes),
            Size(problem.ExpectedBytes)),

        ModInstallFailure.UnsafeArchiveEntry => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_UnsafeArchiveEntryFormat,
            problem.ArchiveEntry),

        ModInstallFailure.DownloadFolderMissing => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_DownloadFolderMissingFormat,
            problem.Folder),

        ModInstallFailure.DownloadFolderNotWritable => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_DownloadFolderNotWritableFormat,
            problem.Folder,
            problem.InnerException?.Message),

        ModInstallFailure.ArchiveContainsLink => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_ArchiveContainsLinkFormat,
            problem.ModName,
            problem.Version,
            problem.ArchiveEntry),

        ModInstallFailure.RemovalRefused => RemovalRefused(problem.Folder, problem.Refusal),

        ModInstallFailure.InstallThroughLink => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_InstallThroughLinkFormat,
            problem.ModName,
            problem.Version,
            problem.Folder),

        ModInstallFailure.RecordFromAnotherInstall => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_RecordFromAnotherInstallFormat,
            problem.ModName,
            problem.Folder),

        ModInstallFailure.OriginalNotKept => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_OriginalNotKeptFormat,
            problem.ModName,
            problem.Version,
            problem.Folder,
            problem.InnerException?.Message),

        ModInstallFailure.NothingToPlace => string.Format(
            CultureInfo.CurrentCulture,
            Strings.ModInstall_NothingToPlaceFormat,
            problem.ModName,
            problem.Version,
            problem.TotalFiles,
            problem.ArchiveEntry),

        _ => string.Format(CultureInfo.CurrentCulture, Strings.ModInstall_UnexpectedFormat, problem.Reason),
    };

    // Why a path was not removed. Every one of these is said before anything was deleted.
    public static string RemovalRefused(string? path, PathRefusal? refusal) => string.Format(
        CultureInfo.CurrentCulture,
        refusal switch
        {
            PathRefusal.Protected => Strings.ModInstall_RemoveRefusedProtectedFormat,
            PathRefusal.AppFolder => Strings.ModInstall_RemoveRefusedAppFolderFormat,
            PathRefusal.NotAModFolder => Strings.ModInstall_RemoveRefusedNotModFolderFormat,
            PathRefusal.Link => Strings.ModInstall_RemoveRefusedLinkFormat,
            _ => Strings.ModInstall_RemoveRefusedOutsideFormat,
        },
        path);

    // Bytes as the Downloads page writes them, so one failure doesn't spell sizes its own way.
    private static string Size(long? bytes) =>
        bytes is null ? Strings.Common_UnknownAmount : DownloadQueueItemViewModel.SizeLabel(bytes.Value);

    //
    // Public for the same reason InstallInUse is: the addon rows say this before anything is
    // attempted, where the switch above says it as a refusal at install time. One situation, and it
    // used to be two sentences - one of them not naming where the link was missing from.
    //
    public static string NoDownloadLink(string? modName, string? version) => string.Format(
        CultureInfo.CurrentCulture,
        Strings.ModInstall_NoDownloadLinkFormat,
        modName,
        version);

    //
    // Public because the pages check for a running install BEFORE asking the user anything, so a
    // locked install is reported up front rather than after they have answered a confirmation.
    // Those checks used to build their own version of this sentence, which is how the app ended up
    // with three wordings of it that did not agree.
    //
    //
    // One whole sentence per action rather than one sentence with the action dropped into it. The
    // old shape spliced a verb phrase - "installing a mod" - into the middle of a sentence, which
    // is the fragment problem D8 describes: a translator handed "installing a mod" on its own has
    // no way to make it agree with the sentence around it, and several languages would not put it
    // in that position at all.
    //
    // The process names are joined by TextLists rather than " and " for the same reason.
    //
    public static string InstallInUse(IReadOnlyList<string> running, ModInstallAction action) =>
        string.Format(CultureInfo.CurrentCulture, Sentence(action), TextLists.Join(running));

    private static string Sentence(ModInstallAction action) => action switch
    {
        ModInstallAction.Install => Strings.ModInstall_InUseInstallFormat,
        ModInstallAction.Remove => Strings.ModInstall_InUseRemoveFormat,
        ModInstallAction.Disable => Strings.ModInstall_InUseDisableFormat,
        ModInstallAction.Enable => Strings.ModInstall_InUseEnableFormat,
        ModInstallAction.Undo => Strings.ModInstall_InUseUndoFormat,
        ModInstallAction.ApplyList => Strings.ModInstall_InUseApplyListFormat,
        ModInstallAction.SortOutDuplicate => Strings.ModInstall_InUseSortOutFormat,
        ModInstallAction.RestoreProfiles => Strings.ModInstall_InUseRestoreProfilesFormat,
        _ => Strings.ModInstall_InUseGenericFormat,
    };
}
