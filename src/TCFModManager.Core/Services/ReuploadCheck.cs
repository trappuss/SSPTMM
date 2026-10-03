using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>How the same version came to be uploaded again.</summary>
public enum ReuploadKind
{
    /// <summary>sp-mod.com lists a later entry under the same version number, with a different size.</summary>
    NewEntry,

    /// <summary>The installed entry's listed size is not what it was when it was installed.</summary>
    ListingChanged,
}

/// <summary>A mod whose installed version was uploaded again. <paramref name="Version"/> is the entry to
/// install to get it; the sizes are sp-mod.com's listed sizes, before and now.</summary>
public sealed record Reupload(ReuploadKind Kind, ModVersionSummary Version, long? Before, long Now);

//
// Fork (SSPTMM): an author putting up a fixed file under the same version number. The update check
// compares version numbers, so it never says. Two shapes it can take in sp-mod.com's listing
// (checked 2026-10-03):
//
//   - A second entry with the same number: ten mods in the catalog have one. Where sizes are
//     listed they match - double submits of one file (ODT's Item Info 2.0.15 is two entries of
//     42,510 bytes, FikaSync 0.3.5 two of 952,879) - so only a later entry of a DIFFERENT size counts.
//     "Later" is by when it was created: entry ids are not in that order (Accurate Circular Radar
//     1.1.10 has id 942 created after id 1156).
//   - The size listed for the installed entry changing after the install (HUNCH: that sp-mod.com
//     updates the listed size when an author replaces the file - not yet seen happen).
//
// Sizes are compared listing to listing, never the listing to the file on disk: the listing can be
// out of step with the file it points at. Skills Extended 3.1.1 is listed at 109,594,837 bytes while
// the file its link serves (a GitHub release asset) is 109,594,883 - read as a re-upload, that would
// send everyone who already has the current file to get it again.
//
// Not caught: the file swapped where it is hosted with the listing left as it was. That is what the
// Skills Extended difference looks like (its link goes to a GitHub release asset); seeing it would
// take asking the host for the file's size, and the way there is the sp-mod.com download link, which
// may count as a download (not confirmed either way).
//
// The catalog only carries a mod's latest ten versions (include=versions), so an older installed
// version is never flagged. A record from before the sizes were kept (ListedBytes null) still gets
// the first check, against the installed entry's current listing.
//
// What the link serves when two entries share a number is sp-mod.com's choice: the link is by
// version number, not entry (HUNCH: the latest one).
//
public static class ReuploadCheck
{
    public static Reupload? Find(InstalledModRecord record, IReadOnlyList<ModVersionSummary>? versions)
    {
        if (record.IsAddon || !record.IsAppManaged || record.Incomplete) return null;
        if (record.VersionId is not { } installedId || versions is not { Count: > 0 }) return null;

        var same = versions.Where(v => string.Equals(v.Version, record.Version, StringComparison.Ordinal)).ToList();
        var installed = same.FirstOrDefault(v => v.Id == installedId);

        // What sp-mod.com listed for the installed file: as recorded at install, or as it lists it now.
        var listed = record.ListedBytes is > 0 ? record.ListedBytes : installed?.ContentLength;

        var newer = same
            .Where(v => v.Id != installedId && IsLater(v, installed, installedId))
            .OrderByDescending(v => v.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Id)
            .FirstOrDefault();

        if (newer is { ContentLength: > 0 and var newerBytes } && listed is > 0 && newerBytes != listed)
            return new Reupload(ReuploadKind.NewEntry, newer, listed, newerBytes);

        if (installed is { ContentLength: > 0 and var nowBytes }
            && record.ListedBytes is > 0 and var thenBytes
            && nowBytes != thenBytes)
        {
            return new Reupload(ReuploadKind.ListingChanged, installed, thenBytes, nowBytes);
        }

        return null;
    }

    // By creation time when both are known; by id only when neither entry says when it was made.
    private static bool IsLater(ModVersionSummary candidate, ModVersionSummary? installed, int installedId) =>
        candidate.CreatedAt is { } made && installed?.CreatedAt is { } installedMade
            ? made > installedMade
            : candidate.Id > installedId;
}
