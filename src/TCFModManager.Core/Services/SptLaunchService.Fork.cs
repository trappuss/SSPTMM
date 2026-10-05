using System.ComponentModel;
using System.Diagnostics;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM): the game itself - EscapeFromTarkov.exe - as opposed to the launcher it was started
// from. Close game on the Play page, and closing it when the server stops (the App's GameCloser).
//
// Only a game running from THIS install is touched, by the same rule Stop uses (IsThisInstall): a
// process whose path cannot be read is left alone.
//
// sparesWindowless: a Fika headless client is also EscapeFromTarkov.exe, started with no window, and
// may run from the same install. When this machine runs one, only a game with a window - the one a
// person is playing - is closed. (Whether a headless client from the same folder ever has a window is
// not known here; with a window it would be closed too.)
//
public static partial class SptLaunchService
{
    private const string GameProcessName = "EscapeFromTarkov";

    /// <summary>Whether the game is running from this install.</summary>
    public static bool IsGameRunning(string? installPath, bool sparesWindowless)
    {
        var found = false;
        foreach (var process in GameProcesses(installPath, sparesWindowless))
        {
            found = true;
            process.Dispose();
        }

        return found;
    }

    //
    // Closes the game: asked to close its window first, killed only if it hasn't gone within the
    // grace (Stop's five seconds unless given). Stopped is how many closed; NotRunning when there was
    // none.
    //
    // A shorter grace is for a game whose server is already gone: its quit asks the server to cancel
    // invites and log out, and waits for answers that never come. On the user's PC (2026-10-04) every
    // close with the server already down sat the full five seconds and was killed (3 of 3); every
    // close with the server still up finished on its own in 2-5 s.
    //
    public static SptLaunchResult CloseGame(string? installPath, bool sparesWindowless, TimeSpan? grace = null)
    {
        var gameRoot = SptInstallationService.ToGameRoot(installPath);
        var info = new SptLaunchTargetInfo
        {
            Target = SptLaunchTarget.Client,
            InstallPath = gameRoot,
            ExePath = gameRoot is null ? null : Path.Combine(gameRoot, GameProcessName + ".exe"),
            ProcessName = GameProcessName,
            IsRunning = true,
        };

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return new SptLaunchResult { Info = info with { IsRunning = false }, Problem = SptLaunchProblem.NoInstallFolder };

        var processes = GameProcesses(installPath, sparesWindowless).ToList();
        if (processes.Count == 0)
            return new SptLaunchResult { Info = info with { IsRunning = false }, Problem = SptLaunchProblem.NotRunning };

        try
        {
            var stopped = StopAll(processes, grace ?? CloseGrace);
            return stopped == 0
                ? new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StopFailed }
                : new SptLaunchResult { Info = info, Stopped = stopped };
        }
        catch (Exception ex)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StopFailed, Error = ex };
        }
    }

    // The caller disposes what it is given.
    private static IEnumerable<Process> GameProcesses(string? installPath, bool sparesWindowless)
    {
        var gameRoot = SptInstallationService.ToGameRoot(installPath);
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) yield break;

        var info = new SptLaunchTargetInfo
        {
            Target = SptLaunchTarget.Client,
            InstallPath = gameRoot,
            ExePath = Path.Combine(gameRoot, GameProcessName + ".exe"),
            ProcessName = GameProcessName,
        };

        Process[] found;
        try
        {
            found = Process.GetProcessesByName(GameProcessName);
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        foreach (var process in found)
        {
            if (IsThisInstall(process, info) && (!sparesWindowless || HasWindow(process)))
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }

    private static bool HasWindow(Process process)
    {
        try
        {
            return !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}
