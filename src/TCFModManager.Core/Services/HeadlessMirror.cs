using System.Text.Json;

namespace TCFModManager.Core.Services;

/// <summary>Why nothing could be copied to the headless client's folder.</summary>
public enum HeadlessMirrorProblem
{
    /// <summary>The folder isn't there.</summary>
    NoFolder,

    /// <summary>The folder has no BepInEx folder: not a game folder.</summary>
    NotAGame,

    /// <summary>It is the SPT install itself.</summary>
    SameAsInstall,

    /// <summary>Its BepInEx, plugins or patchers folder is a junction or symbolic link.</summary>
    Link,

    /// <summary>The game is running from that folder.</summary>
    InUse,
}

/// <summary>What a copy to the headless client's folder did. Names are folder or file names under BepInEx.</summary>
public sealed record HeadlessMirrorResult(
    IReadOnlyList<string> Copied,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<(string Name, string Reason)> Failed,
    HeadlessMirrorProblem? Problem = null)
{
    public static HeadlessMirrorResult Stopped(HeadlessMirrorProblem problem) => new([], [], [], [], problem);
}

//
// Fork (1.4.8): a Fika headless client kept in a folder of its own (Options > Headless client
// folder). SSPTMM manages one SPT install; this copies, from that install, the game halves of the
// mods a collection marks for the headless client - their folders and files in BepInEx\plugins and
// BepInEx\patchers - into the same places in the headless folder.
//
// It is a copy, not an install: nothing is downloaded, no install record is made, and the headless
// folder's other files are left alone. What it placed is written down in the headless folder
// (ManifestName), so a mod no longer wanted there is taken out again and nothing else ever is. A
// folder of the same name that it did not place is set aside (SetAsideFolder), not deleted.
//
// Links are refused, as they are everywhere else: a headless folder whose BepInEx is a link back to
// the main install would have the copy write into the install it is reading from.
//
public static class HeadlessMirror
{
    public const string ManifestName = ".ssptmm-headless.json";

    public const string SetAsideFolder = ".ssptmm-headless-replaced";

    private static readonly string[] Containers = [Path.Combine("BepInEx", "plugins"), Path.Combine("BepInEx", "patchers")];

    /// <summary>Whether the folder can be copied to at all; null when it can.</summary>
    public static HeadlessMirrorProblem? Check(string installPath, string headlessPath)
    {
        if (!Directory.Exists(headlessPath)) return HeadlessMirrorProblem.NoFolder;
        if (Same(installPath, headlessPath)) return HeadlessMirrorProblem.SameAsInstall;

        var bepInEx = Path.Combine(headlessPath, "BepInEx");
        if (!Directory.Exists(bepInEx)) return HeadlessMirrorProblem.NotAGame;

        if (InstallPathGuard.IsLink(bepInEx) || Containers.Any(c => InstallPathGuard.IsLink(Path.Combine(headlessPath, c))))
            return HeadlessMirrorProblem.Link;

        return ModInstallService.RunningBlockers(headlessPath).Count > 0 ? HeadlessMirrorProblem.InUse : null;
    }

    /// <summary>Makes the headless folder hold these and nothing else of what was copied before.</summary>
    /// <param name="wanted">Mod folders or loose files in the install's BepInEx\plugins or BepInEx\patchers.</param>
    public static HeadlessMirrorResult Sync(string installPath, string headlessPath, IEnumerable<string> wanted, DateTimeOffset now)
    {
        if (Check(installPath, headlessPath) is { } problem) return HeadlessMirrorResult.Stopped(problem);

        var copied = new List<string>();
        var removed = new List<string>();
        var unchanged = new List<string>();
        var failed = new List<(string, string)>();

        var before = ReadManifest(headlessPath);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in wanted.Select(w => RelativeOf(installPath, w)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var source = Path.Combine(installPath, relative);
            var destination = Path.Combine(headlessPath, relative);
            var name = Path.GetFileName(relative);

            try
            {
                if (!Exists(source)) continue;

                if (InstallPathGuard.IsLink(source) || InstallPathGuard.IsLink(destination))
                {
                    failed.Add((name, "it is a link"));
                    if (before.Contains(relative)) placed.Add(relative);
                    continue;
                }

                if (Exists(destination) && IsSame(source, destination))
                {
                    unchanged.Add(name);
                    placed.Add(relative);
                    continue;
                }

                if (Exists(destination))
                {
                    if (before.Contains(relative)) Delete(destination);
                    else SetAside(headlessPath, relative, now);
                }

                Copy(source, destination);
                copied.Add(name);
                placed.Add(relative);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Headless", $"couldn't copy {relative}: {ex.Message}");
                failed.Add((name, ex.Message));
                if (before.Contains(relative)) placed.Add(relative);
            }
        }

        foreach (var relative in before.Where(r => !placed.Contains(r)))
        {
            var target = Path.Combine(headlessPath, relative);
            try
            {
                if (IsInContainer(relative) && Exists(target) && !InstallPathGuard.IsLink(target))
                {
                    Delete(target);
                    removed.Add(Path.GetFileName(relative));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Headless", $"couldn't remove {relative}: {ex.Message}");
                failed.Add((Path.GetFileName(relative), ex.Message));
                placed.Add(relative);
            }
        }

        WriteManifest(headlessPath, placed);
        AppLog.Info("Headless", $"{copied.Count} copied, {unchanged.Count} unchanged, {removed.Count} removed, {failed.Count} failed");

        return new HeadlessMirrorResult(copied, removed, unchanged, failed);
    }

    // "BepInEx\plugins\SAIN" for a path directly inside one of the install's two containers; null
    // for anything else - a server mod, a turned-off mod, a path outside the install.
    private static string? RelativeOf(string installPath, string path)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(Path.GetFullPath(installPath), Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return IsInContainer(relative) ? relative : null;
    }

    // Directly inside a container: the mod's own folder or loose file, never the container, never deeper.
    private static bool IsInContainer(string relative) =>
        Containers.Any(c => string.Equals(Path.GetDirectoryName(relative), c, StringComparison.OrdinalIgnoreCase))
        && Path.GetFileName(relative) is { Length: > 0 } name && name != "." && name != "..";

    private static bool Same(string a, string b) =>
        string.Equals(InstallStamp.Of(a), InstallStamp.Of(b), StringComparison.OrdinalIgnoreCase);

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    // The same files, each of the same size and written at the same time - a copy keeps a file's time.
    private static bool IsSame(string source, string destination)
    {
        if (File.Exists(source)) return File.Exists(destination) && SameFile(new FileInfo(source), new FileInfo(destination));
        if (!Directory.Exists(destination)) return false;

        var from = Files(source);
        var to = Files(destination);
        return from.Count == to.Count && from.All(f => to.TryGetValue(f.Key, out var other) && SameFile(f.Value, other));
    }

    private static Dictionary<string, FileInfo> Files(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f.FullName), StringComparer.OrdinalIgnoreCase);

    private static bool SameFile(FileInfo a, FileInfo b) => a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;

    private static void Copy(string source, string destination)
    {
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, folder)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    // "<headless>\.ssptmm-headless-replaced\20261117-064007\BepInEx\plugins\SAIN"
    private static void SetAside(string headlessPath, string relative, DateTimeOffset now)
    {
        var target = Path.Combine(headlessPath, SetAsideFolder, $"{now.ToLocalTime():yyyyMMdd-HHmmss}", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var source = Path.Combine(headlessPath, relative);
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target);

        AppLog.Info("Headless", $"set aside {relative}, which was there already");
    }

    private static HashSet<string> ReadManifest(string headlessPath)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var file = Path.Combine(headlessPath, ManifestName);
            if (!File.Exists(file)) return found;

            foreach (var item in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? [])
            {
                var relative = item.Replace('/', Path.DirectorySeparatorChar);
                if (IsInContainer(relative)) found.Add(relative);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Warn("Headless", $"couldn't read {ManifestName}: {ex.Message}");
        }

        return found;
    }

    private static void WriteManifest(string headlessPath, IEnumerable<string> placed)
    {
        try
        {
            var list = placed.Select(p => p.Replace(Path.DirectorySeparatorChar, '/')).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            File.WriteAllText(Path.Combine(headlessPath, ManifestName), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Headless", $"couldn't write {ManifestName}: {ex.Message}");
        }
    }
}
