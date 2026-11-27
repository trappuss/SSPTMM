namespace TCFModManager.Core.Services;

//
// Pristine copies of the config files each mod version shipped, captured at install:
// <root>\<modId>[-addon]\<version>\<install-relative path>.
//
// The baseline is what lets an update tell a setting the user changed from a default the mod
// changed. It is captured, never derived - a two-way compare of the user's file against the new one
// can't separate the two, so a changed default would never reach anyone.
//
// Settings files, a few KB per mod. The current and the previous version are kept; anything older is
// pruned. A removed mod's baselines go with it.
//
public sealed class ConfigBaselineStore(string? root = null)
{
    public string Root { get; } = root ?? Path.Combine(AppPaths.DataDirectory, "ConfigBaselines");

    // Copies the named files, as they sit in the install right now, into the version's baseline.
    // Best-effort: a file that can't be copied just has no baseline, which the merge treats as "not
    // known" rather than as an error.
    public void Capture(string installPath, int modId, bool isAddon, string version, IEnumerable<string> relativePaths)
    {
        var versionDir = VersionDirectory(modId, isAddon, version);

        foreach (var relative in relativePaths)
        {
            var source = Path.Combine(installPath, ToNative(relative));
            if (!File.Exists(source)) continue;

            var destination = Path.Combine(versionDir, ToNative(relative));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Configs", $"couldn't record the shipped copy of {relative}: {ex.Message}");
            }
        }
    }

    // The shipped copy of one file for one version, or null when none was captured.
    public string? Find(int modId, bool isAddon, string? version, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var path = Path.Combine(VersionDirectory(modId, isAddon, version), ToNative(relativePath));
        return File.Exists(path) ? path : null;
    }

    //
    // Fork (1.4.9): moves one version's shipped copies aside, for an install of that same version
    // over itself (Get again, a re-uploaded file). Capture is about to write the new file's copies
    // into the same place, and what the old file shipped has to be read after that. Returns the
    // folder they are in now, or null when there were none.
    //
    public string? SetAside(int modId, bool isAddon, string version)
    {
        var versionDir = VersionDirectory(modId, isAddon, version);
        if (!Directory.Exists(versionDir)) return null;

        var aside = versionDir + ".before";
        try
        {
            TryDelete(aside);
            Directory.Move(versionDir, aside);
            return aside;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't set the shipped copies of {version} aside: {ex.Message}");
            return null;
        }
    }

    /// <summary>The shipped copy of one file in a folder SetAside returned, or null.</summary>
    public static string? FindIn(string asideFolder, string relativePath)
    {
        var path = Path.Combine(asideFolder, ToNative(relativePath));
        return File.Exists(path) ? path : null;
    }

    public static void Discard(string? asideFolder)
    {
        if (asideFolder is not null) TryDelete(asideFolder);
    }

    // Drops every version of this mod's baseline except the ones named.
    public void Prune(int modId, bool isAddon, params string?[] keepVersions)
    {
        var modDir = ModDirectory(modId, isAddon);
        if (!Directory.Exists(modDir)) return;

        var keep = keepVersions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => SafeSegment(v!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.EnumerateDirectories(modDir))
        {
            if (keep.Contains(Path.GetFileName(dir))) continue;
            TryDelete(dir);
        }
    }

    public void Remove(int modId, bool isAddon) => TryDelete(ModDirectory(modId, isAddon));

    private string ModDirectory(int modId, bool isAddon) =>
        Path.Combine(Root, isAddon ? $"{modId}-addon" : modId.ToString());

    private string VersionDirectory(int modId, bool isAddon, string version) =>
        Path.Combine(ModDirectory(modId, isAddon), SafeSegment(version));

    private static string ToNative(string relative) => relative.Replace('/', Path.DirectorySeparatorChar);

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Configs", $"couldn't prune {dir}: {ex.Message}");
        }
    }
}
