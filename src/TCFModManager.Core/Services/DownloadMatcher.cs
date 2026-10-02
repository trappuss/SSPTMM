using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Whether a downloaded archive now looks installed by hand.
//
// Two questions, cheapest first. Is any folder it would place in the scan? If not, nothing has
// happened yet and nothing is said. If so, is every file it would place on disk at the size the
// archive says? Presence and size only - no hashing - which is enough to tell "installed" from
// "the folder is there but it's still the old version", and costs one stat per file.
//
public static class DownloadMatcher
{
    //
    // Every name a scan knows its folders by, for Check's first question: each entry's own name,
    // its folder's name, and a loose DLL's file name without the extension - the same spellings an
    // install record's folder list uses.
    //
    // Disabled entries are left out. A hand install lands in the live folders, so a copy sitting in
    // a ".disabled" container is not it - and counting it would call every disabled mod with a
    // pending download half-installed.
    //
    public static HashSet<string> FolderNames(IEnumerable<InstalledMod> scanned)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in scanned)
        {
            if (entry.IsDisabled) continue;
            if (!string.IsNullOrWhiteSpace(entry.Name)) names.Add(entry.Name);
            if (string.IsNullOrWhiteSpace(entry.FolderPath)) continue;

            var path = entry.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            names.Add(Path.GetFileName(path));
            names.Add(Path.GetFileNameWithoutExtension(path));
        }

        return names;
    }

    public static DownloadMatch Check(
        DownloadedModRecord download, string installPath, IEnumerable<string> scannedFolders)
    {
        if (download.Unrecognised || download.ExpectedFolders.Count == 0 || download.ExpectedFiles.Count == 0)
            return DownloadMatch.Absent;

        var present = scannedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!download.ExpectedFolders.Any(present.Contains)) return DownloadMatch.Absent;

        var missing = 0;
        var differing = 0;

        // A download recorded before protected paths were left out may still list SPT's own files;
        // the user was right not to copy those over (D6).
        foreach (var file in download.ExpectedFiles.Where(f => !ProtectedInstallPaths.IsProtected(f.Path)))
        {
            var info = new FileInfo(Path.Combine(installPath, file.Path.Replace('/', Path.DirectorySeparatorChar)));

            if (!info.Exists) missing++;
            else if (file.Size >= 0 && info.Length != file.Size) differing++;
        }

        return missing == 0 && differing == 0
            ? DownloadMatch.Installed
            : new DownloadMatch(DownloadMatchKind.Partial, missing, differing);
    }
}

public enum DownloadMatchKind
{
    // None of its folders are in the scan.
    Absent,

    // A folder is there, but some files are missing or a different size.
    Partial,

    // Every file it would place is on disk at the expected size.
    Installed,
}

public sealed record DownloadMatch(DownloadMatchKind Kind, int MissingFiles = 0, int DifferingFiles = 0)
{
    public static DownloadMatch Absent { get; } = new(DownloadMatchKind.Absent);

    public static DownloadMatch Installed { get; } = new(DownloadMatchKind.Installed);
}
