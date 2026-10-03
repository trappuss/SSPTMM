namespace TCFModManager.Core.Services;

/// <summary>The logs Diagnose logs reads, as found in one install. Any of them can be missing.</summary>
/// <param name="Server">The server log written to last.</param>
/// <param name="BepInEx">BepInEx\LogOutput.log - the last game session, rewritten each time the game starts.</param>
/// <param name="GameErrors">The errors log in the newest Logs\log_* folder.</param>
/// <param name="Launcher">The SPT launcher's log.</param>
/// <param name="LauncherClearsGameLogs">The launcher's log says it deleted the game's Logs folder when it
/// last started the game - so only that game session's logs are left.</param>
public sealed record LogFileSet(
    string? Server, string? BepInEx, string? GameErrors, string? Launcher, bool LauncherClearsGameLogs);

//
// Fork (SSPTMM): where the logs are, as laid out on SPT 4.1.6 (a real install, 2026-10-03):
//
//   <server>\user\logs\spt\spt20261003.log     one per day; the server's start lines say where a run begins
//   <server>\user\logs\Launcher.log            "Recursive Removal: <game>\Logs" when it starts the game
//   <game>\BepInEx\LogOutput.log
//   <game>\Logs\log_<date>_<version>\<date>_<version> errors.log   (also application, backend, traces...)
//
// <server> is SPT_Runtime on SPT 4.1's layout and the game folder before it (TryGetServerRoot). An
// older server's "server-<date>.log" is taken when there is no spt\ folder.
//
public static class LogFiles
{
    public static LogFileSet Find(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return new LogFileSet(null, null, null, null, false);

        string? server = null, launcher = null;
        if (ServerLogs.Folder(installPath) is { } logs)
        {
            server = Newest(Path.Combine(logs, "spt"), "spt*.log")
                ?? Newest(logs, "server*.log")
                ?? Newest(logs, "*.log", skip: name => name.StartsWith("launcher", StringComparison.OrdinalIgnoreCase));

            var launcherLog = Path.Combine(logs, "Launcher.log");
            if (File.Exists(launcherLog)) launcher = launcherLog;
        }

        var bepInEx = Path.Combine(installPath, "BepInEx", "LogOutput.log");

        // Fork: with no Logs folder (the clean-up before a start deleted it, and the game has not
        // written a new one yet), the copy SSPTMM kept when it started the game itself.
        var gameErrors = NewestGameErrors(Path.Combine(installPath, "Logs"))
            ?? NewestGameErrors(KeptGameLogsFolder(installPath));

        var clears = launcher is not null
            && LogEntries.ReadLines(launcher).Any(l =>
                l.Contains("Recursive Removal:", StringComparison.Ordinal)
                && l.TrimEnd().EndsWith("\\Logs", StringComparison.OrdinalIgnoreCase)); // written on Windows

        return new LogFileSet(server, File.Exists(bepInEx) ? bepInEx : null, gameErrors, launcher, clears);
    }

    private static string? NewestGameErrors(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                && new DirectoryInfo(folder).EnumerateDirectories("log_*").OrderByDescending(d => d.LastWriteTimeUtc).FirstOrDefault() is { } newest
                ? newest.EnumerateFiles("*errors.log").FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Diagnose", $"couldn't look in {folder}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Fork: where SSPTMM keeps the last game sessions' Logs\log_* folders of this install
    /// when it starts the game itself (SptDirectLaunch) - Data\GameLogs\(a short hash of the folder).</summary>
    public static string KeptGameLogsFolder(string gameRoot)
    {
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot)).ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..12];
        return Path.Combine(AppPaths.DataDirectory, "GameLogs", hash);
    }

    private static string? Newest(string folder, string pattern, Func<string, bool>? skip = null)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles(pattern)
                    .Where(f => skip?.Invoke(f.Name) != true)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
