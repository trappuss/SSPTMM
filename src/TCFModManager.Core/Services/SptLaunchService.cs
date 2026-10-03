using System.ComponentModel;
using System.Diagnostics;

namespace TCFModManager.Core.Services;

public enum SptLaunchTarget
{
    // SPT.Server.exe, wherever this install's layout keeps it.
    Server,

    // SPT.Launcher.exe - the launcher a player picks a profile in and starts the game from.
    Client,

    // A Fika install's own launcher, which is only present on a setup running a headless client.
    Headless,
}

//
// Why a target could not be started. The App words it; this reports what happened and the values
// behind it, the same split ModInstallService and the self-updater use.
//
public enum SptLaunchProblem
{
    None,

    // No install folder is configured, or it is not there any more.
    NoInstallFolder,

    // The folder holds no executable for this target in any layout this app knows.
    // Carries InstallPath.
    ExeNotFound,

    // Already running, so there is nothing to start. Carries ProcessName.
    AlreadyRunning,

    // Starting the process threw. Carries ExePath and Error.
    StartFailed,

    // Asked to restart something that is not up. Restart is not a second Start - a target that is
    // not running has nothing to put back the way it was.
    NotRunning,

    // It would not stop: refused, or still there after being asked and then killed. Carries
    // ProcessName and Error.
    StopFailed,
}

// What this install can currently do for one target.
public sealed record SptLaunchTargetInfo
{
    public required SptLaunchTarget Target { get; init; }

    public string? InstallPath { get; init; }

    // Null when Problem says why it could not be found.
    public string? ExePath { get; init; }

    // The exe's own name without its extension, which is also how it appears in the process list.
    public string? ProcessName { get; init; }

    public bool IsRunning { get; init; }

    public SptLaunchProblem Problem { get; init; }

    public bool Exists => ExePath is not null;

    public bool CanLaunch => ExePath is not null && !IsRunning;
}

public sealed record SptLaunchResult
{
    public required SptLaunchTargetInfo Info { get; init; }

    public bool Started { get; init; }

    // How many processes a restart stopped on the way. Zero for an ordinary start.
    public int Stopped { get; init; }

    public SptLaunchProblem Problem { get; init; }

    public Exception? Error { get; init; }
}

//
// Starts an install's server, its game launcher, and - where one exists - its Fika headless
// launcher, and reports which of them is already up.
//
// It also RESTARTS and STOPS one. The Play page offers Stop server in place of Start server while
// the server is up (asked for, and confirmed on the page first, since a raid in progress does not
// survive it), and a restart is a thing an operator does constantly (a config edited, a server mod
// dropped in) - the alternative was hunting a console window. Every stop is scoped to ONE target of
// THIS install and is asked for explicitly.
//
public static partial class SptLaunchService // Fork: partial - see SptLaunchService.Fork.cs
{
    // Beside the server exe, in whichever folder that turned out to be.
    private static readonly string[] ClientLauncherCandidates =
    [
        "SPT.Launcher.exe",
        "Aki.Launcher.exe",
    ];

    //
    // What starts a Fika headless client, at the install root.
    //
    // A headless install still has SPT.Server.exe, BepInEx and a game client exactly like a player's,
    // so nothing else about the folder tells the two apart. This exe is the whole signal, which is
    // why it has to be the RIGHT exe.
    //
    // "HEADLESS" IN THE NAME IS THE DISCRIMINATOR, and the only one. Two wrong versions of this have
    // now shipped, in opposite directions:
    //
    //   - "*Fika*Launcher*.exe" alone, which does NOT match FikaHeadlessManager.exe - so the Play
    //     page's headless card never once appeared on the machine it exists for.
    //   - the same pattern kept on as a fallback, which matches "SPT-Fika Launcher.exe" - the
    //     ordinary Fika PLAYER launcher, present on every Fika player install. That is the far worse
    //     of the two: it makes a player's machine look like a headless, and answering the setup
    //     prompt on the back of it would have a server's mod list arrive stripped of the mods only a
    //     player needs.
    //
    // So: the confirmed name first, and one pattern behind it that still requires the word Headless.
    // A player-facing launcher will never carry it. Do not widen this to match "Fika" and "Launcher"
    // again - that is the bug, not the safety net.
    //
    private static readonly string[] HeadlessLauncherCandidates =
    [
        "FikaHeadlessManager.exe",
    ];

    private static readonly string[] HeadlessLauncherWildcards =
    [
        "*Headless*.exe",
    ];

    // Other processes that mean this target is already up, whatever the exe on disk is called.
    private static readonly string[] ServerProcessNames = ["SPT.Server", "Aki.Server"];

    private static readonly string[] ClientProcessNames = ["EscapeFromTarkov"];

    //
    // headlessExePath is the launcher named by hand in settings, used for the Headless target only
    // and ignored when it is not a file that exists. Threaded through rather than read here: Core
    // does not read settings, and a service that quietly consulted them would be one the tests
    // could not put in a known state.
    //
    public static SptLaunchTargetInfo Describe(
        string? installPath, SptLaunchTarget target, string? headlessExePath = null)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return new SptLaunchTargetInfo { Target = target, Problem = SptLaunchProblem.NoInstallFolder };
        }

        var exePath = FindExe(installPath!, target, headlessExePath);

        if (exePath is null)
        {
            return new SptLaunchTargetInfo
            {
                Target = target,
                InstallPath = installPath,
                Problem = SptLaunchProblem.ExeNotFound,
            };
        }

        var processName = Path.GetFileNameWithoutExtension(exePath);

        return new SptLaunchTargetInfo
        {
            Target = target,
            InstallPath = installPath,
            ExePath = exePath,
            ProcessName = processName,
            IsRunning = IsRunning(target, processName, exePath, installPath),
        };
    }

    //
    // hidden: start the SERVER with no console window (the other targets are windows people use,
    // and ignore it). Its log is still written to user/logs, which the Play page shows.
    //
    public static SptLaunchResult Launch(
        string? installPath, SptLaunchTarget target, string? headlessExePath = null, bool hidden = false)
    {
        var info = Describe(installPath, target, headlessExePath);

        if (info.Problem != SptLaunchProblem.None)
        {
            return new SptLaunchResult { Info = info, Problem = info.Problem };
        }

        if (info.IsRunning)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.AlreadyRunning };
        }

        try
        {
            //
            // UseShellExecute starts it as the shell would - detached, so closing this app does not
            // take the server down with it, and with the console window SPT.Server draws.
            //
            // WorkingDirectory is the exe's own folder because SPT resolves its data folders
            // relative to it; started from this app's directory it looks for them here.
            //
            // Never while a stop has this app ignoring Ctrl+C (see Interrupt): a process started then
            // would inherit that, and could not be asked to stop with it later.
            lock (Consoles)
            {
                if (hidden && target == SptLaunchTarget.Server)
                {
                    StartHidden(info.ExePath!);
                    return new SptLaunchResult { Info = info, Started = true };
                }

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = info.ExePath!,
                    WorkingDirectory = Path.GetDirectoryName(info.ExePath!),
                    UseShellExecute = true,
                });

                return new SptLaunchResult { Info = info, Started = true };
            }
        }
        catch (Exception ex)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StartFailed, Error = ex };
        }
    }

    //
    // Stops this target and starts it again.
    //
    // ONE target of ONE install: the processes it will stop are matched on where their exe lives, so
    // a second install on another drive - which this app is expected to be managing at the same time
    // - is never touched, and neither is the headless manager when the server is what was asked for.
    // The game a headless manager may have launched is left alone too: restarting the manager is
    // what was asked for, and killing a raid nobody mentioned is not a thing to do as a side effect.
    //
    // Refused when the target is not running. A restart that quietly becomes a start is how somebody
    // ends up with a second server they did not know they had.
    //
    public static SptLaunchResult Restart(
        string? installPath, SptLaunchTarget target, string? headlessExePath = null, bool hidden = false)
    {
        var info = Describe(installPath, target, headlessExePath);

        if (info.Problem != SptLaunchProblem.None)
        {
            return new SptLaunchResult { Info = info, Problem = info.Problem };
        }

        if (!info.IsRunning)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.NotRunning };
        }

        int stopped;

        try
        {
            stopped = Stop(info);
        }
        catch (Exception ex)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StopFailed, Error = ex };
        }

        if (stopped == 0)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StopFailed };
        }

        //
        // A moment between the two. The server binds its port on startup and the one going down has
        // only just let go of it; starting into that window fails in a way that reads as the restart
        // itself being broken.
        //
        Thread.Sleep(StopSettleDelay);

        var result = Launch(installPath, target, headlessExePath, hidden);

        return result with { Stopped = stopped };
    }

    //
    // Stops this target on this install and leaves it down. Stopped says how many processes went;
    // Started is always false.
    //
    public static SptLaunchResult StopTarget(
        string? installPath, SptLaunchTarget target, string? headlessExePath = null)
    {
        var info = Describe(installPath, target, headlessExePath);

        if (info.Problem != SptLaunchProblem.None)
        {
            return new SptLaunchResult { Info = info, Problem = info.Problem };
        }

        if (!info.IsRunning)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.NotRunning };
        }

        try
        {
            var stopped = Stop(info);
            return stopped == 0
                ? new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StopFailed }
                : new SptLaunchResult { Info = info, Stopped = stopped };
        }
        catch (Exception ex)
        {
            return new SptLaunchResult { Info = info, Problem = SptLaunchProblem.StopFailed, Error = ex };
        }
    }

    //
    // The server with no window: a console of its own that is never shown (CREATE_NO_WINDOW), so its
    // console calls work as they always do and Ctrl+C still reaches it (see Interrupt). Nothing is
    // read from it - SPT writes its own log file.
    //
    // Its input is an empty pipe, closed at once. When SPT cannot start (its port is taken, a mod
    // fails validation) it says "Press any key to exit" and waits for a key; with no window nobody
    // could press one, and the server would sit there invisibly for ever. With no input it finds
    // none and exits, and the Play page says it stopped before it was ready.
    //
    // Not tied to this app: closing the app leaves the server running, as a visible one does.
    //
    private static void StartHidden(string exePath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        }) ?? throw new InvalidOperationException("the server process did not start");

        process.StandardInput.Close();
    }

    private static readonly TimeSpan StopSettleDelay = TimeSpan.FromMilliseconds(750);

    // How long a process gets to close on its own after being asked, before it is killed.
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);

    //
    // Closes every process this target owns on this install, and returns how many went.
    //
    // Asked first, killed second: SPT.Server draws a console window, and a WM_CLOSE lets it finish
    // whatever it was writing to a profile rather than losing it. Five seconds is generous for a
    // process that has nothing to flush and short enough that nobody thinks the button did nothing.
    //
    // NOT the process tree. Killing the tree would take a headless manager's game client with it,
    // which is a decision this method has not been asked to make.
    //
    private static int Stop(SptLaunchTargetInfo info) => StopAll(ProcessesFor(info)); // Fork: the loop is shared with CloseGame

    private static int StopAll(IEnumerable<Process> processes)
    {
        var stopped = 0;

        foreach (var process in processes)
        {
            var name = "a process";
            try
            {
                if (process.HasExited) continue;
                name = $"{process.ProcessName} (pid {process.Id})";

                // A window to close when it has one; with none (started hidden, or hosted in Windows
                // Terminal), Ctrl+C - which SPT answers by shutting down properly. Killed only when
                // neither worked in time.
                // Fork: every step is logged at Info, so a stop that "did nothing" can be told apart
                // afterwards - asked and gone, killed, or still there.
                var hasWindow = process.MainWindowHandle != IntPtr.Zero;
                var asked = hasWindow ? process.CloseMainWindow() : Interrupt(process);
                AppLog.Info("Launch", $"{name}: {(asked ? (hasWindow ? "asked its window to close" : "sent Ctrl+C") : "couldn't be asked to close")}");

                if (asked && process.WaitForExit((int)CloseGrace.TotalMilliseconds))
                {
                    AppLog.Info("Launch", $"{name} closed");
                }
                else
                {
                    AppLog.Info("Launch", asked ? $"{name} still running after {CloseGrace.TotalSeconds:0} s; killing it" : $"killing {name}");
                    process.Kill();
                    AppLog.Info("Launch", process.WaitForExit((int)CloseGrace.TotalMilliseconds)
                        ? $"{name} killed"
                        : $"{name} still running {CloseGrace.TotalSeconds:0} s after being killed");
                }

                if (!process.HasExited) continue;

                stopped++;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // A process this app may not touch, or one that exited while being asked. Neither is
                // a reason to abandon the others; a stop that took nothing down is reported by the
                // count, and the caller turns that into StopFailed.
                // Fork: a warning, not Debug - it is the one line that says why something stayed up.
                AppLog.Warn("Launch", $"could not stop {name}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return stopped;
    }

    //
    // Ctrl+C to a console process with no window: this app joins its console for a moment, sends the
    // event, and leaves - ignoring the event itself meanwhile, since everything on that console gets
    // it. The ignore is lifted straight after: it is inherited by processes started meanwhile (Launch
    // waits for it), and a server started with it set could not be asked to stop.
    //
    // False when it could not be sent (not Windows, the process has no console, this app has one of
    // its own): the caller then kills it, as before.
    //
    private static bool Interrupt(Process process)
    {
        if (!OperatingSystem.IsWindows()) return false;

        // A process has one console at a time: one stop at a time joins one.
        lock (Consoles)
        {
            return InterruptAttached(process);
        }
    }

    private static readonly Lock Consoles = new();

    private static bool InterruptAttached(Process process)
    {
        // Ignoring Ctrl+C first, and not at all without it: everything on the console gets the
        // event, and this app, with no handler of its own, would be closed by it.
        if (!NativeConsole.SetConsoleCtrlHandler(IntPtr.Zero, true)) return false;

        try
        {
            if (!NativeConsole.AttachConsole((uint)process.Id)) return false;

            var sent = NativeConsole.GenerateConsoleCtrlEvent(NativeConsole.CtrlCEvent, 0);
            NativeConsole.FreeConsole();

            // Long enough for the event to have been handed out (it goes to each process on the
            // console on a thread of its own); the caller waits for the process to go.
            if (sent) Thread.Sleep(InterruptSettle);
            return sent;
        }
        finally
        {
            NativeConsole.SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
    }

    // HUNCH: how long the console takes to hand a Ctrl+C to the processes on it - a few
    // milliseconds in practice; half a second is a wide margin that still keeps the ignore short.
    private static readonly TimeSpan InterruptSettle = TimeSpan.FromMilliseconds(500);

    private static class NativeConsole
    {
        public const uint CtrlCEvent = 0;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool AttachConsole(uint processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool FreeConsole();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetConsoleCtrlHandler(IntPtr handler, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool add);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
    }

    //
    // The running processes that ARE this target here: the exe named in the info, plus the names a
    // target is known by, and only where the running exe is this install's or the very file that
    // would be started.
    //
    // The path test is what keeps a second install out of it. The named-exe test is what keeps a
    // headless launcher named outside the install folder in it.
    //
    private static IEnumerable<Process> ProcessesFor(SptLaunchTargetInfo info)
    {
        var names = KnownProcessNames(info.Target)
            .Append(info.ProcessName ?? "")
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            Process[] found;

            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in found)
            {
                if (IsThisInstall(process, info))
                {
                    yield return process;
                }
                else
                {
                    process.Dispose();
                }
            }
        }
    }

    //
    // Unlike the in-use check in ModInstallService, an unreadable path here reads as NOT ours.
    //
    // The two are asked opposite questions. That one asks "is anything using this install", where
    // not knowing has to block, because guessing wrong writes over a running install. This one asks
    // "may I kill this process", where not knowing has to refuse - guessing wrong kills something
    // that was never this app's business.
    //
    private static bool IsThisInstall(Process process, SptLaunchTargetInfo info)
    {
        string? executable;

        try
        {
            executable = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            AppLog.Debug("Launch", $"could not read the path of {process.ProcessName}; leaving it alone");
            return false;
        }

        if (executable is null) return false;

        if (info.ExePath is { } exe
            && string.Equals(Path.GetFullPath(executable), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return info.InstallPath is { } root && ModInstallService.IsInside(executable, root);
    }

    // True for a process started from this install (or from the exact exe), and for one whose path
    // cannot be read; false only for one known to run from somewhere else.
    private static bool RunsFrom(Process process, string? exePath, string? installPath)
    {
        string? executable;
        try
        {
            executable = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return true;
        }

        if (executable is null || (exePath is null && installPath is null)) return true;

        if (exePath is not null
            && string.Equals(Path.GetFullPath(executable), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
            return true;

        return installPath is not null && ModInstallService.IsInside(executable, installPath);
    }

    private static string[] KnownProcessNames(SptLaunchTarget target) => target switch
    {
        SptLaunchTarget.Server => ServerProcessNames,
        SptLaunchTarget.Client => ClientProcessNames,
        _ => [],
    };

    private static string? FindExe(string installPath, SptLaunchTarget target, string? headlessExePath = null)
    {
        switch (target)
        {
            case SptLaunchTarget.Server:
                return SptInstallationService.TryFindServerExe(installPath, out var server) ? server : null;

            case SptLaunchTarget.Client:
                return TryFindClientLauncherExe(installPath, out var client) ? client : null;

            case SptLaunchTarget.Headless:
                return FindHeadlessLauncher(installPath, headlessExePath);

            default:
                return null;
        }
    }

    // The launcher a player starts the game from. It sits beside the server exe in every layout.
    public static bool TryFindClientLauncherExe(string installPath, out string exePath)
    {
        exePath = "";

        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return false;

        var searchDirectory = string.IsNullOrEmpty(serverRoot)
            ? installPath
            : Path.Combine(installPath, serverRoot);

        foreach (var name in ClientLauncherCandidates)
        {
            var candidate = Path.Combine(searchDirectory, name);
            if (File.Exists(candidate))
            {
                exePath = candidate;
                return true;
            }
        }

        return false;
    }

    // True when this install has a Fika headless launcher, which is what makes it a headless setup.
    public static bool TryFindHeadlessLauncherExe(
        string installPath, out string exePath, string? headlessExePath = null)
    {
        exePath = FindHeadlessLauncher(installPath, headlessExePath) ?? "";
        return exePath.Length > 0;
    }

    //
    // The named path first, and it is not required to be anywhere near the install folder: the
    // point of naming it is a layout the search cannot reach.
    //
    private static string? FindHeadlessLauncher(string installPath, string? headlessExePath = null)
    {
        if (!string.IsNullOrWhiteSpace(headlessExePath))
        {
            try
            {
                if (File.Exists(headlessExePath)) return Path.GetFullPath(headlessExePath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Unusable as a path at all - fall through to the search rather than throwing at a
                // page that is only asking what this machine has.
            }
        }

        foreach (var name in HeadlessLauncherCandidates)
        {
            var candidate = Path.Combine(installPath, name);
            if (File.Exists(candidate)) return candidate;
        }

        foreach (var pattern in HeadlessLauncherWildcards)
        {
            if (FindFirstFile(installPath, pattern) is { } found) return found;
        }

        return null;
    }

    private static string? FindFirstFile(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    //
    // Running FROM THIS INSTALL. With a second SPT on the machine (another version, a dedicated
    // server kept apart), its server being up made this install's Play page read as running, refuse
    // Start, and then fail to Stop it. A process whose path cannot be read (one running elevated)
    // still counts, as before: this cannot tell it is someone else's.
    //
    private static bool IsRunning(SptLaunchTarget target, string processName, string? exePath, string? installPath)
    {
        //
        // The headless launcher is checked by its own name only. A headless client runs
        // EscapeFromTarkov like any other, so folding that in would make this read as running
        // whenever the player's own game was open on the same machine.
        //
        var known = KnownProcessNames(target);

        foreach (var name in known.Append(processName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                // Process list unavailable - read as nothing running rather than blocking the user.
                continue;
            }

            try
            {
                if (found.Any(p => RunsFrom(p, exePath, installPath))) return true;
            }
            finally
            {
                foreach (var process in found) process.Dispose();
            }
        }

        return false;
    }
}
