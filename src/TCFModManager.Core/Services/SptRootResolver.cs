namespace TCFModManager.Core.Services;

public enum SptRootKind
{
    // The folder the game runs from - BepInEx and EscapeFromTarkov.exe.
    Game,

    // The folder the server exe sits in, and the parent of user\mods.
    Server,
}

//
// Why a root could not be resolved. The App words it; this reports what happened and the values
// behind it, per the convention that Core owns no user-facing prose.
//
public enum SptRootProblem
{
    None,

    // The directory the search was to start from is unset or not there. Carries StartDirectory.
    StartDirectoryMissing,

    // A root was set explicitly and is not there. Carries ConfiguredDirectory.
    ConfiguredDirectoryMissing,

    // Nothing at or above StartDirectory looks like this root. Carries StartDirectory.
    NotFound,
}

public sealed record SptRootResult
{
    public required SptRootKind Kind { get; init; }

    public string? Directory { get; init; }

    public SptRootProblem Problem { get; init; }

    public string? StartDirectory { get; init; }

    public string? ConfiguredDirectory { get; init; }

    // The exe a server root was derived from. Null for a game root or a configured root.
    public string? ServerExePath { get; init; }

    public bool Found => Directory is not null;
}

//
// Resolves the game root and the server root independently.
//
// They are the same folder on a bundled install and different on a standalone server, and the
// single "SPT root" that used to stand for both is the reason 4.1's SPT_Runtime\ layout and
// server-only machines both went wrong. Neither root is derived from the other, and a failure names
// which one was missing rather than reporting a generic "couldn't detect".
//
// Layouts are probed, never mapped from a version number - see SptInstallationService for the
// server exe candidates and DisabledModPaths for the mod containers.
//
public static class SptRootResolver
{
    private static readonly string[] GameRootMarkerFiles = ["EscapeFromTarkov.exe"];

    private static readonly string[] GameRootMarkerDirectories = ["BepInEx"];

    public static SptRootResult ResolveGameRoot(string? startDirectory, string? configuredDirectory = null)
    {
        if (TryUseConfigured(SptRootKind.Game, startDirectory, configuredDirectory, out var configured))
            return configured;

        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            return Failed(SptRootKind.Game, SptRootProblem.StartDirectoryMissing, startDirectory);

        foreach (var directory in SelfAndAncestors(startDirectory!))
        {
            if (IsGameRoot(directory))
            {
                return new SptRootResult
                {
                    Kind = SptRootKind.Game,
                    Directory = directory,
                    StartDirectory = startDirectory,
                };
            }
        }

        return Failed(SptRootKind.Game, SptRootProblem.NotFound, startDirectory);
    }

    public static SptRootResult ResolveServerRoot(string? startDirectory, string? configuredDirectory = null)
    {
        if (TryUseConfigured(SptRootKind.Server, startDirectory, configuredDirectory, out var configured))
            return configured;

        if (string.IsNullOrWhiteSpace(startDirectory) || !Directory.Exists(startDirectory))
            return Failed(SptRootKind.Server, SptRootProblem.StartDirectoryMissing, startDirectory);

        var ancestors = SelfAndAncestors(startDirectory!).ToList();

        //
        // A named exe anywhere above beats a wildcard guess anywhere above, so the named candidates
        // are exhausted across the whole chain before the fallback runs at all. Doing both per
        // directory instead would let a stray *Server*.exe shipped by some mod win over the real
        // SPT.Server.exe further up.
        //
        foreach (var directory in ancestors)
        {
            if (SptInstallationService.TryFindNamedServerExe(directory, out var named))
                return ServerRootFor(named, startDirectory);
        }

        foreach (var directory in ancestors)
        {
            if (SptInstallationService.TryFindServerExeByWildcard(directory, out var wildcard))
                return ServerRootFor(wildcard, startDirectory);
        }

        return Failed(SptRootKind.Server, SptRootProblem.NotFound, startDirectory);
    }

    //
    // Fork (SSPTMM 1.3.0): the SPT install SSPTMM was unzipped into, if any. Since 1.2.1 the release
    // zip holds a single SSPTMM folder meant to be unzipped into the SPT folder, so on a first start
    // with no install set, the folder above the app's own is checked. Only that one, and only when it
    // has both the game exe and SPT's server - a BepInEx folder alone, anything further up, or a live
    // (non-SPT) game folder is too loose a guess to act on without asking.
    //
    public static string? InstallAroundApp(string? appDirectory)
    {
        if (string.IsNullOrWhiteSpace(appDirectory)) return null;

        try
        {
            var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory)));
            if (parent is null) return null;

            return GameRootMarkerFiles.Any(file => File.Exists(Path.Combine(parent.FullName, file)))
                && SptInstallationService.TryGetServerRoot(parent.FullName, out _)
                ? parent.FullName
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                   or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    // True for a folder holding the game, either state of BepInEx included.
    public static bool IsGameRoot(string directory)
    {
        foreach (var file in GameRootMarkerFiles)
        {
            if (File.Exists(Path.Combine(directory, file))) return true;
        }

        foreach (var folder in GameRootMarkerDirectories)
        {
            if (Directory.Exists(Path.Combine(directory, folder))) return true;
            if (Directory.Exists(DisabledModPaths.Disabled(Path.Combine(directory, folder)))) return true;
        }

        return false;
    }

    private static SptRootResult ServerRootFor(string exePath, string? startDirectory)
    {
        var directory = Path.GetDirectoryName(exePath);

        if (string.IsNullOrEmpty(directory))
            return Failed(SptRootKind.Server, SptRootProblem.NotFound, startDirectory);

        return new SptRootResult
        {
            Kind = SptRootKind.Server,
            Directory = directory,
            StartDirectory = startDirectory,
            ServerExePath = exePath,
        };
    }

    private static SptRootResult Failed(SptRootKind kind, SptRootProblem problem, string? startDirectory,
        string? configuredDirectory = null) =>
        new()
        {
            Kind = kind,
            Problem = problem,
            StartDirectory = startDirectory,
            ConfiguredDirectory = configuredDirectory,
        };

    private static bool TryUseConfigured(SptRootKind kind, string? startDirectory, string? configuredDirectory,
        out SptRootResult result)
    {
        result = null!;

        if (string.IsNullOrWhiteSpace(configuredDirectory)) return false;

        string full;
        try
        {
            full = Path.GetFullPath(configuredDirectory!);
        }
        catch
        {
            result = Failed(kind, SptRootProblem.ConfiguredDirectoryMissing, startDirectory, configuredDirectory);
            return true;
        }

        result = Directory.Exists(full)
            ? new SptRootResult
            {
                Kind = kind,
                Directory = full,
                StartDirectory = startDirectory,
                ConfiguredDirectory = configuredDirectory,
            }
            : Failed(kind, SptRootProblem.ConfiguredDirectoryMissing, startDirectory, full);

        return true;
    }

    private static IEnumerable<string> SelfAndAncestors(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }
}
