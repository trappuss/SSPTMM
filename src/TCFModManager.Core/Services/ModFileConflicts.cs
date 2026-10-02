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
    /// <summary>Where each file of an archive would land, relative to the install folder and with
    /// forward slashes (the install records' own form) - ArchiveLayout's plan, the same one the
    /// install follows. Empty for an archive the install would not recognise.</summary>
    public static IReadOnlyList<string> InstallRelativePaths(IEnumerable<string> archiveFiles, string? serverRoot)
    {
        var entries = archiveFiles
            .Select(f => f.Replace('\\', '/'))
            .Where(f => f.Trim('/').Length > 0 && !f.EndsWith('/'))
            .Select(f => new ArchiveFileEntry(f, 0))
            .ToList();

        var plan = ArchiveLayout.Plan(entries, serverRoot ?? "");
        return plan.Recognised ? [.. plan.Files.Select(f => f.Path)] : [];
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
        // Every record that lists a file: after an Install Anyway two do. Only this install's records -
        // one stamped with another install (1.19's global records) says nothing about these files.
        var stamp = InstallStamp.Of(installPath);
        var owners = new Dictionary<string, List<InstalledModRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record.InstallPath is { } stamped && !string.Equals(stamped, stamp, StringComparison.OrdinalIgnoreCase)) continue;

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

            // 1.19's new-file-only areas: a file already there is left as it is, never written over.
            if (ProtectedInstallPaths.IsNewFileOnly(relative)) continue;

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
