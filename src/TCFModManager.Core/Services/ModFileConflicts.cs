using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>A file an install would write over, and whose it is now: an installed mod's name, or
/// null when this app did not install whatever put it there.</summary>
public sealed record FileClash(string Path, string? OwnerName);

//
// Which of another mod's files an install would write over, worked out before anything is
// downloaded - from sp-mod.com's record of the version's archive (its file-tree, kept once the
// version passes the site's file check).
//
// The archive's paths are placed exactly as ModInstallService places them: a wrapper folder that is
// not one of SPT's own is looked through (up to four deep), and "user/..." goes wherever this
// install keeps user/mods. A file that is already there belongs to the installed mod whose install
// record lists it; one that no record lists was put there by hand or by another tool. The mod being
// installed is never in its own way - an update over itself is what an update is.
//
public static class ModFileConflicts
{
    // The first folders an SPT archive's content starts with - ModInstallService's own list.
    private static readonly HashSet<string> KnownRootFolders =
        new(StringComparer.OrdinalIgnoreCase) { "BepInEx", "user", "SPT", "SPT_Runtime" };

    /// <summary>Where each file of an archive would land, relative to the install folder and with
    /// forward slashes (the install records' own form).</summary>
    public static IReadOnlyList<string> InstallRelativePaths(IEnumerable<string> archiveFiles, string? serverRoot)
    {
        var paths = archiveFiles
            .Select(f => f.Replace('\\', '/').TrimStart('/'))
            .Select(f => f.StartsWith("./", StringComparison.Ordinal) ? f[2..] : f)
            .Where(f => f.Length > 0 && !f.EndsWith('/'))
            .ToList();

        // Looks through a single wrapper folder that is not SPT's own, as the install does.
        for (var depth = 0; depth < 4 && paths.Count > 0; depth++)
        {
            var first = paths[0].Split('/', 2)[0];
            if (KnownRootFolders.Contains(first)) break;
            if (!paths.All(p => p.Contains('/') && string.Equals(p.Split('/', 2)[0], first, StringComparison.Ordinal))) break;

            paths = paths.Select(p => p.Split('/', 2)[1]).ToList();
        }

        var root = (serverRoot ?? "").Replace('\\', '/').Trim('/');

        return paths
            .Select(p => root.Length > 0 && string.Equals(p.Split('/', 2)[0], "user", StringComparison.OrdinalIgnoreCase)
                ? root + "/" + p
                : p)
            .ToList();
    }

    /// <summary>The files among <paramref name="installRelative"/> that are already there and are
    /// not the installing mod's own.</summary>
    /// <param name="belongsToTarget">For a file no install record lists: true when it is inside the
    /// installing mod's own (hand-installed) folders.</param>
    public static IReadOnlyList<FileClash> Find(
        string installPath,
        IEnumerable<string> installRelative,
        InstallTarget target,
        IReadOnlyList<InstalledModRecord> records,
        Func<string, bool>? belongsToTarget = null)
    {
        var owners = new Dictionary<string, InstalledModRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
            foreach (var file in record.Files)
                owners.TryAdd(file.Replace('\\', '/'), record);

        var clashes = new List<FileClash>();

        foreach (var relative in installRelative)
        {
            var full = Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;

            if (owners.TryGetValue(relative, out var owner))
            {
                if (!target.Matches(owner)) clashes.Add(new FileClash(relative, owner.Name));
                continue;
            }

            if (belongsToTarget?.Invoke(full) == true) continue;

            clashes.Add(new FileClash(relative, null));
        }

        return clashes;
    }

    /// <summary>Whether a file sits inside a folder (full paths, case-insensitive).</summary>
    public static bool IsInside(string filePath, string folder) => ModInstallService.IsInside(filePath, folder);
}
