using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Mod and addon archives the download queue has fetched, kept in Data\Downloads so installing the
// same version again - subscribing again after unsubscribing, putting a version back, a mod list
// re-applied - reads it from disk instead of downloading it again. (A download through sp-mod.com
// also counts as one on the author's page, so a second one for the same thing is worth saving.)
//
// Keyed by what was fetched: mod or addon, its id, and the version's own id, which sp-mod.com gives
// each upload. A kept file whose size no longer matches the size sp-mod.com gives for that version
// is not used, and is deleted.
//
// Kept to a budget: past it, the files used longest ago go first. Files being written are named
// .part until they are complete, so a download cut short is never taken for a kept one.
//
public sealed class ModArchiveCache(string directory, long budgetBytes)
{
    /// <summary>Data\Downloads.</summary>
    public static string DefaultDirectory => Path.Combine(AppPaths.DataDirectory, "Downloads");

    /// <summary>4 GB: a few dozen ordinary mods, or one of the largest.</summary>
    public const long DefaultBudget = 4L * 1024 * 1024 * 1024;

    private const string Extension = ".archive";
    private const string PartExtension = ".part";

    public string Directory { get; } = directory;

    public long BudgetBytes { get; } = budgetBytes;

    /// <summary>Where this version's archive is kept, or null when the version has no id to key it by.</summary>
    public string? PathFor(InstallTarget target, ModVersion version) =>
        version.Id > 0
            ? Path.Combine(Directory, $"{(target.IsAddon ? "addon" : "mod")}-{target.Id}-{version.Id}{Extension}")
            : null;

    /// <summary>The kept archive for this version, if there is one that can be used.</summary>
    public bool TryGet(InstallTarget target, ModVersion version, out string path)
    {
        path = PathFor(target, version) ?? string.Empty;
        if (path.Length == 0) return false;

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0) return false;

            if (version.ContentLength is > 0 and var expected && file.Length != expected)
            {
                AppLog.Info("Downloads", $"kept {file.Name} is {file.Length:N0} bytes, sp-mod.com says {expected:N0} - downloading it again");
                file.Delete();
                return false;
            }

            // Last used now, for the budget.
            file.LastWriteTimeUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Where to download to before the file is complete: <paramref name="path"/> plus .part.</summary>
    public static string PartPathFor(string path) => path + PartExtension;

    private const string OncePrefix = "once-";

    /// <summary>A one-off place for a download that will not be kept.</summary>
    public string TemporaryPath() => Path.Combine(Directory, $"{OncePrefix}{Guid.NewGuid():N}{Extension}");

    /// <summary>Whether this downloaded archive can be kept: not a one-off, and not by itself
    /// larger than the whole budget (which would push every other kept archive out).</summary>
    public bool IsKeepable(string path)
    {
        if (Path.GetFileName(path).StartsWith(OncePrefix, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            return new FileInfo(path).Length <= BudgetBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>What the kept archives take up, in bytes.</summary>
    public long Size()
    {
        try
        {
            return System.IO.Directory.Exists(Directory)
                ? new DirectoryInfo(Directory).EnumerateFiles("*" + Extension).Sum(f => f.Length)
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Deletes kept archives used longest ago until the rest fit the budget. Paths in
    /// <paramref name="inUse"/> are left alone.</summary>
    public void Trim(IReadOnlySet<string> inUse) => DeleteUntil(BudgetBytes, inUse);

    /// <summary>Deletes every kept archive but those in <paramref name="inUse"/>, and leftovers
    /// from downloads that never finished.</summary>
    public void Clear(IReadOnlySet<string> inUse) => DeleteUntil(0, inUse);

    private void DeleteUntil(long budget, IReadOnlySet<string> inUse)
    {
        if (!System.IO.Directory.Exists(Directory)) return;

        try
        {
            // In use: the archive itself, or the .part it is being downloaded into.
            bool InUse(FileInfo f) =>
                inUse.Contains(f.FullName)
                || (IsPart(f) && inUse.Contains(f.FullName[..^PartExtension.Length]));

            var files = new DirectoryInfo(Directory).EnumerateFiles().Where(f => !InUse(f)).ToList();

            // A .part not being written is what a download cut short by the app closing leaves, and
            // a one-off not in use is one the app was closed before it could delete.
            foreach (var leftover in files.Where(f => IsPart(f) || f.Name.StartsWith(OncePrefix, StringComparison.OrdinalIgnoreCase)))
                TryDelete(leftover);

            // Larger than the whole budget first, so one of those does not push out all the rest;
            // then those used longest ago.
            var kept = files.Where(f => f.Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                    && !f.Name.StartsWith(OncePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Length > BudgetBytes)
                .ThenBy(f => f.LastWriteTimeUtc)
                .ToList();

            var total = kept.Sum(f => f.Length);
            foreach (var file in kept)
            {
                if (total <= budget) break;
                if (TryDelete(file)) total -= file.Length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Downloads", $"couldn't tidy {Directory}: {ex.Message}");
        }
    }

    private static bool IsPart(FileInfo file) => file.Name.EndsWith(PartExtension, StringComparison.OrdinalIgnoreCase);

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
