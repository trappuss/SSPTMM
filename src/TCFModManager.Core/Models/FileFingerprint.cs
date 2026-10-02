using System.Security.Cryptography;

namespace TCFModManager.Core.Models;

//
// A placed file's size and SHA-256, so a later removal can tell whether the file on disk is still
// exactly what this app put there (CLOSED-10-TCFResilience-DESIGN.md §6, D21). Path is the same
// forward-slash, install-relative path the record's Files list uses.
//
// SHA-256 because it is in the base library: XxHash64 would need the System.IO.Hashing package, and
// hashing is limited by the disk either way.
//
public sealed record FileFingerprint(string Path, long Size, string Sha256)
{
    // Null when the file can't be read - a fingerprint is never guessed.
    public static FileFingerprint? Compute(string fullPath, string recordedPath)
    {
        try
        {
            using var stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 20, FileOptions.SequentialScan);

            var size = stream.Length;
            var hash = Convert.ToHexString(SHA256.HashData(stream));

            return new FileFingerprint(recordedPath, size, hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    //
    // Whether the file at fullPath is byte-for-byte what this fingerprint describes. The size is
    // checked first, so a file that differs in length is never hashed. A file that is missing or
    // can't be read does not match.
    //
    public bool Matches(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length != Size) return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return Compute(fullPath, Path) is { } now && string.Equals(now.Sha256, Sha256, StringComparison.OrdinalIgnoreCase);
    }
}

//
// A file that was already in the install, owned by no record, when an install placed over it. The
// original is kept in the app's Data folder (D22). BackupPath is relative to the Data folder, so
// moving the app with its Data keeps it valid.
//
// SameMod: the mod folder the file sat in declared the GUID of the mod being installed, so it was an
// earlier copy of that same mod - proven, not guessed (R16). Removal keeps those recoverable but does
// not put them back, so Remove really removes the mod. Every other original - another mod's file, an
// addon's parent's file, anything with no declared ID, anything outside a mod folder - is put back.
//
public sealed record OverwrittenFile(string Path, long Size, string Sha256, string BackupPath, bool SameMod = false);
