namespace TCFModManager.Core.Services;

//
// What an extracted mod archive holds, and where in an SPT install each of its files goes.
//
// Authors package mods a few ways, and the install has to read all of them the same:
//
//   BepInEx/..., user/...              - the layout of the install itself (the usual one)
//   MyMod-1.0/BepInEx/...              - that, inside one or more wrapper folders
//   README.txt + MyMod-1.0/BepInEx/... - a wrapper folder with a read-me beside it
//   README.md + BepInEx/...            - a read-me beside the real content
//   plugins/MyMod.dll                  - BepInEx's own folders without the BepInEx folder around them
//
// Read-me files, licences and change logs at the top of the archive are for the person reading it,
// not for the game: they are left out rather than dropped into the SPT folder. Pictures and web pages
// beside a wrapper folder or plugins/ are left out too; beside BepInEx/ or user/ they are placed.
//
// A lone DLL with nothing to say where it goes is NOT placed: under SPT 4 a server mod is a DLL too,
// and putting one in BepInEx\plugins would be a guess. It stays "not recognised", as before.
//
public static class ArchiveLayout
{
    public sealed record Entry(string Source, string Relative);

    private static readonly HashSet<string> KnownRoots =
        new(StringComparer.OrdinalIgnoreCase) { "BepInEx", "user", "SPT", "SPT_Runtime" };

    // BepInEx's own folders, found without the BepInEx folder around them.
    private static readonly Dictionary<string, string> BepInExFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plugins"] = "BepInEx/plugins",
        ["patchers"] = "BepInEx/patchers",
    };

    // Text for people. Pictures and web pages are not on the list: nothing says one at the top of an
    // archive is not meant for the game, so those are placed as they always were.
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".url", ".pdf", ".rtf", ".nfo",
    };

    // The same, with no extension at all - matched on the whole name.
    private static readonly HashSet<string> DocumentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "license", "licence", "changelog", "changes", "credits", "copying", "notice",
    };

    /// <summary>True for a file at the top of an archive that is for reading, not for the game.</summary>
    public static bool IsDocument(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);

        return extension.Length == 0
            ? DocumentNames.Contains(name)
            : DocumentExtensions.Contains(extension);
    }

    // Pictures and web pages: placed when the archive has the install's own layout (nothing says one
    // there is not for the game), but no reason to doubt a wrapper folder or plugins/ beside them -
    // a preview.png next to "MyMod-1.0/" is the archive's cover, not content without a home.
    private static readonly HashSet<string> PresentationExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".html", ".htm",
    };

    private static bool IsBeside(string path) =>
        IsDocument(path) || PresentationExtensions.Contains(Path.GetExtension(path));

    /// <summary>Every file to place, with its install-relative path; null when nothing in the
    /// archive says where it goes.</summary>
    public static List<Entry>? Read(string extractDir)
    {
        var root = ContentRoot(extractDir);
        var directories = Directory.GetDirectories(root);
        var looseFiles = Directory.GetFiles(root).Where(f => !IsBeside(f)).ToList();

        // The install's own layout: everything below the root, except the read-me files at its top.
        if (directories.Any(d => KnownRoots.Contains(Path.GetFileName(d))))
        {
            return
            [
                .. Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Where(f => !(IsTopLevel(root, f) && IsDocument(f)))
                    .Select(f => new Entry(f, Path.GetRelativePath(root, f))),
            ];
        }

        // BepInEx's folders on their own, and nothing else that would need a guess.
        if (looseFiles.Count == 0 && directories.Length > 0
            && directories.All(d => BepInExFolders.ContainsKey(Path.GetFileName(d))))
        {
            var entries = new List<Entry>();
            foreach (var directory in directories)
            {
                var target = BepInExFolders[Path.GetFileName(directory)].Replace('/', Path.DirectorySeparatorChar);
                entries.AddRange(Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(f => new Entry(f, Path.Combine(target, Path.GetRelativePath(directory, f)))));
            }

            return entries.Count > 0 ? entries : null;
        }

        return null;
    }

    //
    // Looks through wrapper folders: a level holding exactly one folder - and at most read-me files
    // or pictures beside it - is a wrapper, unless that folder is already one of the install's own. Four levels
    // at most.
    //
    public static string ContentRoot(string extractDir)
    {
        var current = extractDir;

        for (var depth = 0; depth < 4; depth++)
        {
            var directories = Directory.GetDirectories(current);
            var files = Directory.GetFiles(current);
            if (directories.Length != 1 || files.Any(f => !IsBeside(f))) break;

            var name = Path.GetFileName(directories[0]);
            if (KnownRoots.Contains(name) || BepInExFolders.ContainsKey(name)) break;

            current = directories[0];
        }

        return current;
    }

    private static bool IsTopLevel(string root, string file) =>
        string.Equals(Path.GetDirectoryName(file), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
