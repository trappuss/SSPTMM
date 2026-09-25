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
// installed is never in its own way - an update over itself is what an update is - and neither is a
// file of the user's own that the install leaves where it is (ConfigCarryOver's preserved user data).
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
    /// <param name="configOptions">The mods' config entries: a file one of them names as user data
    /// (SVM's presets) is never placed over by an install, so it is never in the way.</param>
    public static IReadOnlyList<FileClash> Find(
        string installPath,
        IEnumerable<string> installRelative,
        InstallTarget target,
        IReadOnlyList<InstalledModRecord> records,
        Func<string, bool>? belongsToTarget = null,
        IReadOnlyDictionary<string, ModConfigOptions>? configOptions = null)
    {
        // Every record that lists a file: after an Install Anyway two do.
        var owners = new Dictionary<string, List<InstalledModRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            foreach (var file in record.Files)
            {
                var key = file.Replace('\\', '/');
                if (!owners.TryGetValue(key, out var list)) owners[key] = list = [];
                list.Add(record);
            }
        }

        var clashes = new List<FileClash>();

        foreach (var relative in installRelative)
        {
            var full = Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;

            if (ModConfigFiles.IsUserData(relative, ModConfigFiles.OptionsFor(relative, configOptions))) continue;

            if (owners.TryGetValue(relative, out var listed))
            {
                // Once the mod's own record lists it, putting it there again was agreed to already.
                if (!listed.Any(target.Matches)) clashes.Add(new FileClash(relative, listed[0].Name));
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
