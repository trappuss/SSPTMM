using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Works out which mod folders an install placed, from the file list it recorded. These are the
// names InstalledModScanner reports for the same mod, so they're what links a manifest record to
// what's on disk - far more reliable than guessing the catalog listing from a folder name
// ("EpicsAIO" is never going to fuzzy-match "Epic's All in One").
// 
public static class InstalledModFolders
{
    // Folders whose immediate child is a mod. Matched anywhere in the path, since server content is
    // remapped under the install's own server root (e.g. "SPT_Runtime/user/mods/...").
    private static string[] Containers => DisabledModPaths.Containers;

    // 
    // The distinct mod folder names in <paramref name="relativeFiles"/>, in the order first seen.
    // A loose DLL sitting directly in a container contributes its own file name without extension,
    // matching how the scanner names it.
    // 
    public static List<string> FromPlacedFiles(IEnumerable<string> relativeFiles)
    {
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in relativeFiles)
        {
            var name = FolderFor(file);
            if (name is null || !seen.Add(name)) continue;

            folders.Add(name);
        }

        return folders;
    }

    // The folder names for a record, falling back to deriving them from its file list for
    // records written before the folder names were stored.
    public static IReadOnlyList<string> Resolve(InstalledModRecord record) =>
        record.Folders.Count > 0 ? record.Folders : FromPlacedFiles(record.Files);

    //
    // Whether a scanned mod whose folder names are <paramref name="presentFolders"/> is the one an
    // app-managed record installed: it holds at least one of the folders the record placed. What
    // tells the install apart from a copy of the same mod under another name - a renamed backup
    // DLL, a second folder - which matches the same catalog listing by its GUID but was not put
    // there by this app, and whose removal must delete the copy, not the files the record placed.
    //
    public static bool Placed(InstalledModRecord record, IEnumerable<string> presentFolders)
    {
        var present = presentFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Resolve(record).Any(present.Contains);
    }

    //
    // The folders a record placed that the scan can no longer find.
    //
    // A record naming three folders where only one is on disk is a HALF-INSTALLED mod: the scanner
    // still sees that one folder, the card still reports the recorded version, and nothing else in
    // the app ever notices the other two are gone. That is how a mod installed under the wrong root
    // survived a mod list being re-applied - see ModListCandidate.IsIncomplete.
    //
    // Only for app-managed records. A manually-confirmed one names whatever folders happened to be
    // on disk when the version was confirmed, which is not a claim about what belongs there.
    //
    // Folders the scanner never reports are not missing either - a record that placed
    // FixPluginTypesSerialization would otherwise be permanently half-installed, and a mod list
    // would re-fetch it on every single apply.
    //
    public static List<string> MissingFrom(InstalledModRecord record, IEnumerable<string> presentFolders)
    {
        if (!record.IsAppManaged) return [];

        var present = presentFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. Resolve(record)
                .Where(folder => !present.Contains(folder) && !InstalledModScanner.IsNeverReported(folder))
        ];
    }

    //
    // The files a record placed inside one of its folders, each relative to that folder. Empty for a
    // folder the record doesn't name, and for a manually-confirmed record, which places no files.
    //
    // What this is for: a folder still holding the files this app put in it is still that install's,
    // whatever the DLLs inside it claim to be.
    //
    public static List<string> PlacedFilesUnder(InstalledModRecord record, string folderName)
    {
        var results = new List<string>();

        foreach (var file in record.Files)
        {
            if (Split(file) is not { } split) continue;
            if (!string.Equals(split.Folder, folderName, StringComparison.OrdinalIgnoreCase)) continue;

            results.Add(split.Relative);
        }

        return results;
    }

    private static string? FolderFor(string relativePath) => Split(relativePath)?.Folder;

    // The mod folder a placed file sits in, and the rest of its path below that folder.
    private static (string Folder, string Relative)? Split(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');

        foreach (var container in Containers)
        {
            var marker = container + "/";
            var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            var remainder = path[(index + marker.Length)..];
            if (remainder.Length == 0) continue;

            var slash = remainder.IndexOf('/');

            // No further separator means the file sits directly in the container - a loose DLL,
            // which the scanner names after the file itself, so it is both the folder and the only
            // file in it.
            return slash < 0
                ? (Path.GetFileNameWithoutExtension(remainder), remainder)
                : (remainder[..slash], remainder[(slash + 1)..]);
        }

        return null;
    }
}
