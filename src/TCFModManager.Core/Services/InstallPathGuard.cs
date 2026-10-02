using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// The form an install path is stamped on a record in (D17): full, without a trailing separator, so the
// same folder always compares equal however it was typed.
//
public static class InstallStamp
{
    public static string Of(string installPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));

    // Whether this app's own folder sits inside the install - the layout where an unstamped record
    // can only have been made in this install.
    public static bool AppIsInside(string installPath)
    {
        try
        {
            var root = Of(installPath) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(AppContext.BaseDirectory).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

//
// Why a path was refused before anything was deleted or moved.
//
public enum PathRefusal
{
    // Resolves outside the SPT install - an absolute path, a drive, or one that climbs out with "..".
    OutsideInstall,

    // SPT's, BepInEx's or the game's - see ProtectedInstallPaths.
    Protected,

    // Inside this app's own folder, which sits in the install on a normal setup.
    AppFolder,

    // Not a mod folder directly inside BepInEx\plugins, BepInEx\patchers or user\mods.
    NotAModFolder,

    // A junction or symbolic link, or a folder with one inside it, or reached through one.
    Link,
}

//
// The checks every deletion and move inside an install goes through first
// (CLOSED-10-TCFResilience-DESIGN.md §4, D13-D15). Each answers from the path and the disk as they are
// now - never from what a record or a scan said earlier - and anything it cannot work out is a
// refusal, never a pass.
//
public static class InstallPathGuard
{
    //
    // Container folders whose immediate children are mods, install-relative. Both server layouts and
    // a standalone server are listed, each also as its ".disabled" sibling.
    //
    private static readonly string[][] ModContainers = BuildContainers();

    private static string[][] BuildContainers()
    {
        string[][] enabled =
        [
            ["BepInEx", "plugins"],
            ["BepInEx", "patchers"],
            ["user", "mods"],
            ["SPT", "user", "mods"],
            ["SPT_Runtime", "user", "mods"],
        ];

        return
        [
            .. enabled,
            .. enabled.Select(c => (string[])[.. c[..^1], c[^1] + DisabledModPaths.DisabledSuffix]),
        ];
    }

    //
    // Resolves a recorded, install-relative path to the full path a deletion or move would act on,
    // or says why it must not be acted on. Checks, in order: the path reads as install-relative,
    // resolves inside the install, isn't inside this app's own folder, isn't protected, and isn't
    // reached through a link anywhere between the install root and the file.
    //
    public static PathRefusal? CheckRecordedPath(string installPath, string installRelative, out string fullPath) =>
        Check(installPath, installRelative, allowNewFileOnly: false, out fullPath);

    //
    // CheckRecordedPath, except a path in a new-file-only area (ProtectedInstallPaths.IsNewFileOnly,
    // R17) isn't refused as protected. Passing this proves nothing about the file: the caller must
    // still check that the path is free (placing, Undo) or that the file is provably this app's
    // (removal, ProvenPlacedFile).
    //
    public static PathRefusal? CheckPlacedPath(string installPath, string installRelative, out string fullPath) =>
        Check(installPath, installRelative, allowNewFileOnly: true, out fullPath);

    //
    // Whether a recorded file in a new-file-only area is provably the one this app placed there: the
    // record carries its fingerprint and the file on disk still matches it byte-for-byte (R17). A
    // record from before v1.19.0 has no fingerprint, so its files there are never taken.
    //
    public static bool ProvenPlacedFile(string installPath, InstalledModRecord record, string installRelative, out string fullPath)
    {
        fullPath = string.Empty;

        return ProtectedInstallPaths.IsNewFileOnly(installRelative)
            && record.FingerprintFor(installRelative) is { } print
            && CheckPlacedPath(installPath, installRelative, out fullPath) is null
            && File.Exists(fullPath)
            && print.Matches(fullPath);
    }

    private static PathRefusal? Check(string installPath, string installRelative, bool allowNewFileOnly, out string fullPath)
    {
        fullPath = string.Empty;

        if (ProtectedInstallPaths.Segments(installRelative) is null) return PathRefusal.OutsideInstall;

        if (!TryFullPath(Path.Combine(installPath, installRelative.Replace('/', Path.DirectorySeparatorChar)), out var full)
            || !TryFullPath(installPath, out var root)
            || !IsStrictlyInside(full, root))
        {
            return PathRefusal.OutsideInstall;
        }

        if (IsInAppFolder(full)) return PathRefusal.AppFolder;

        var relative = Path.GetRelativePath(root, full);
        if (ProtectedInstallPaths.IsProtected(relative)
            && !(allowNewFileOnly && ProtectedInstallPaths.IsNewFileOnly(relative)))
        {
            return PathRefusal.Protected;
        }

        if (PassesThroughLink(root, full)) return PathRefusal.Link;

        fullPath = full;
        return null;
    }

    //
    // Whether a hand-installed mod's folder (or loose DLL) may be removed whole: it must sit directly
    // inside one of the mod containers of this install, not be protected or in this app's folder, and
    // be neither a link nor hold one anywhere inside it, nor be reached through one (D15). A recursive
    // delete is never pointed at a link - what Windows does to a link's target was not something to
    // rely on.
    //
    public static PathRefusal? CheckModFolder(string installPath, string path)
    {
        if (!TryFullPath(path, out var full) || !TryFullPath(installPath, out var root) || !IsStrictlyInside(full, root))
            return PathRefusal.OutsideInstall;

        if (IsInAppFolder(full)) return PathRefusal.AppFolder;

        var relative = Path.GetRelativePath(root, full);
        if (ProtectedInstallPaths.Segments(relative) is not { } segments) return PathRefusal.OutsideInstall;

        if (!ModContainers.Any(c => segments.Length == c.Length + 1 && StartsWith(segments, c)))
            return PathRefusal.NotAModFolder;

        if (ProtectedInstallPaths.IsProtected(relative)) return PathRefusal.Protected;

        if (PassesThroughLink(root, full) || IsLink(full) || ContainsLink(full)) return PathRefusal.Link;

        return null;
    }

    //
    // The install-relative path of the mod folder (or loose DLL) a path sits in - the container plus
    // its first segment below - or null when the path isn't inside a mod container.
    //
    public static string? ModFolderOf(string installRelative)
    {
        if (ProtectedInstallPaths.Segments(installRelative) is not { } segments) return null;

        foreach (var container in ModContainers.OrderByDescending(c => c.Length))
        {
            if (segments.Length > container.Length && StartsWith(segments, container))
                return string.Join('/', segments[..(container.Length + 1)]);
        }

        return null;
    }

    //
    // Whether an empty folder left behind by a removal may itself be removed: only a mod's own folder
    // or something below it (D14). A container, anything above one, and every folder outside the mod
    // containers (BepInEx\config, user\, the install root) are left even when empty.
    //
    public static bool MayRemoveEmptyFolder(string installPath, string directory)
    {
        if (!TryFullPath(directory, out var full) || !TryFullPath(installPath, out var root) || !IsStrictlyInside(full, root))
            return false;

        if (IsInAppFolder(full)) return false;

        if (ProtectedInstallPaths.Segments(Path.GetRelativePath(root, full)) is not { } segments) return false;

        if (!ModContainers.Any(c => segments.Length > c.Length && StartsWith(segments, c))) return false;

        return !IsLink(full) && !PassesThroughLink(root, full);
    }

    //
    // Whether anything below a folder is a link. Stops at the first one found, so a link is reported
    // rather than walked into.
    //
    public static bool ContainsLink(string directory) => FirstLink(directory) is not null;

    //
    // The first link found below a folder, relative to it - or the folder itself when it cannot be
    // walked, since a folder that can't be read can't be shown to be free of links. Null when there
    // is none.
    //
    public static string? FirstLink(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
        };

        try
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return Path.GetRelativePath(directory, entry.FullName);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ".";
        }
    }

    public static bool IsLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    // Whether any folder strictly between the install root and the path is a link.
    private static bool PassesThroughLink(string root, string full)
    {
        for (var dir = Path.GetDirectoryName(full); dir is not null && IsStrictlyInside(dir, root); dir = Path.GetDirectoryName(dir))
        {
            if (IsLink(dir)) return true;
        }

        return false;
    }

    private static bool IsInAppFolder(string full) =>
        TryFullPath(AppContext.BaseDirectory, out var app)
        && (IsStrictlyInside(full, app) || PathsEqual(full, app));

    private static bool StartsWith(string[] segments, string[] prefix) =>
        prefix.Select((p, i) => string.Equals(segments[i], p, StringComparison.OrdinalIgnoreCase)).All(x => x);

    private static bool TryFullPath(string path, out string full)
    {
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            full = string.Empty;
            return false;
        }
    }

    // Strictly inside: the folder itself is not inside itself.
    private static bool IsStrictlyInside(string full, string root)
    {
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return full.Length > prefix.Length && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}
