using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>Why one of the archive's files is not on disk as the archive has it.</summary>
public enum InstallMismatchKind
{
    // Nothing at the path once the install finished.
    Missing,

    // There, but a different size.
    DifferentSize,

    // Same size, different contents.
    DifferentContents,

    // There, but it couldn't be read to compare.
    Unreadable,
}

public sealed record InstallMismatch(string Path, InstallMismatchKind Kind, long ArchiveSize, long? DiskSize);

//
// Fork (SSPTMM): after an install or update, every file the archive meant to place is checked on
// disk against the archive's own copy - size and SHA-256 - and anything that isn't the same is named.
//
// Asked for after the user\patchers bug: the install skipped Skills Extended's server prepatch and
// said only "kept 1 of SPT's own files" on the download card, and the stale file lost four raids
// before anyone found it. A check of what actually landed would have named it at once.
//
// Files this app deliberately did not place as the archive has them are not asked about - the caller
// passes them as excluded: SPT's own files it refused (named separately), the user's plugin settings
// and documents it kept, and server configs it merged or kept. Everything else must match.
//
public static class InstallVerification
{
    /// <param name="archive">The archive's files, as they were before placing, by install-relative
    /// forward-slash path. A file that couldn't be read there is not in it and is not checked.</param>
    /// <param name="placed">What is on disk now - the record's fingerprints.</param>
    /// <param name="sizeOnDisk">A path's size on disk, or null when nothing is there - asked only for
    /// a path with no fingerprint.</param>
    public static List<InstallMismatch> Check(
        IReadOnlyDictionary<string, FileFingerprint> archive,
        IEnumerable<FileFingerprint> placed,
        IReadOnlySet<string> excluded,
        Func<string, long?> sizeOnDisk)
    {
        var onDisk = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var print in placed) onDisk[print.Path] = print;

        var mismatches = new List<InstallMismatch>();

        foreach (var (path, expected) in archive.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (excluded.Contains(path)) continue;

            if (!onDisk.TryGetValue(path, out var actual))
            {
                // Not fingerprinted: either not there, or there but unreadable.
                var size = sizeOnDisk(path);
                mismatches.Add(new InstallMismatch(
                    path, size is null ? InstallMismatchKind.Missing : InstallMismatchKind.Unreadable, expected.Size, size));
                continue;
            }

            if (actual.Size != expected.Size)
                mismatches.Add(new InstallMismatch(path, InstallMismatchKind.DifferentSize, expected.Size, actual.Size));
            else if (!string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                mismatches.Add(new InstallMismatch(path, InstallMismatchKind.DifferentContents, expected.Size, actual.Size));
        }

        return mismatches;
    }
}
