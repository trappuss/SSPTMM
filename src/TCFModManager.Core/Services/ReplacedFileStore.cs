namespace TCFModManager.Core.Services;

//
// Copies of files an install put its own over - a DLL placed by hand, or another mod's copy of a
// shared library - kept so removing that install can put them back. One folder per mod
// (Data\ReplacedFiles\mod-<id> or addon-<id>), holding each file at its install-relative path.
//
// Only the first copy of a path is kept: an update that places the same file again is replacing the
// mod's own earlier copy, and the file worth putting back is still the one that was there before the
// mod was ever installed.
//
public sealed class ReplacedFileStore(string root)
{
    public string Root { get; } = root;

    public string FolderFor(int modId, bool isAddon) => Path.Combine(Root, (isAddon ? "addon-" : "mod-") + modId);

    private string PathFor(int modId, bool isAddon, string relative) =>
        Path.Combine(FolderFor(modId, isAddon), relative.Replace('/', Path.DirectorySeparatorChar));

    public bool Has(int modId, bool isAddon, string relative) => File.Exists(PathFor(modId, isAddon, relative));

    /// <summary>Keeps a copy of the install's file before it is replaced. False when a copy of that
    /// path is already kept (the first one stays).</summary>
    public bool Keep(string installPath, int modId, bool isAddon, string relative)
    {
        var copy = PathFor(modId, isAddon, relative);
        if (File.Exists(copy)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar)), copy);
        return true;
    }

    /// <summary>Keeps <paramref name="copy"/> (a file already copied out of the install) as the
    /// copy of <paramref name="relative"/>. False when one is already kept (the first one stays).</summary>
    public bool KeepCopy(string copy, int modId, bool isAddon, string relative)
    {
        var kept = PathFor(modId, isAddon, relative);
        if (File.Exists(kept)) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.Copy(copy, kept);
        return true;
    }

    /// <summary>Puts the kept copy back in the install (over whatever is there) and lets it go.
    /// False when there was no copy.</summary>
    public bool Restore(string installPath, int modId, bool isAddon, string relative)
    {
        var copy = PathFor(modId, isAddon, relative);
        if (!File.Exists(copy)) return false;

        var destination = Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(copy, destination, overwrite: true);
        Drop(modId, isAddon, relative);
        return true;
    }

    /// <summary>Lets a kept copy go without putting it back.</summary>
    public void Drop(int modId, bool isAddon, string relative)
    {
        var copy = PathFor(modId, isAddon, relative);
        try
        {
            if (File.Exists(copy)) File.Delete(copy);
            RemoveEmptyFolders(Path.GetDirectoryName(copy), FolderFor(modId, isAddon));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Install", $"couldn't drop the kept copy of {relative}: {ex.Message}");
        }
    }

    /// <summary>Lets every kept copy of a mod go.</summary>
    public void DropAll(int modId, bool isAddon)
    {
        var folder = FolderFor(modId, isAddon);
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Install", $"couldn't clear {folder}: {ex.Message}");
        }
    }

    private static void RemoveEmptyFolders(string? directory, string stopAt)
    {
        var stop = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
        for (var dir = directory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            if (full.Length < stop.Length || !full.StartsWith(stop, StringComparison.OrdinalIgnoreCase)) break;
            if (!Directory.Exists(full) || Directory.EnumerateFileSystemEntries(full).Any()) break;
            Directory.Delete(full);
            if (string.Equals(full, stop, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
}
