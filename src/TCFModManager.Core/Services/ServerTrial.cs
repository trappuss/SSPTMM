using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

/// <summary>What one start of the server showed: whether it came up, and the errors it logged.</summary>
public sealed record ServerTrialOutcome(bool Started, bool TimedOut, IReadOnlyList<LogEntry> Errors);

/// <summary>Why a try could not be made at all - nothing to do with the mods.</summary>
public enum ServerTrialProblem
{
    AlreadyRunning,
    PortTaken,
    CouldNotStart,
    CouldNotStop,
}

public sealed class ServerTrialException(ServerTrialProblem problem, string? detail = null)
    : Exception(detail ?? problem.ToString())
{
    public ServerTrialProblem Problem { get; } = problem;
}

//
// Fork (1.4.7): one start of the SPT server to see what it does, for Find the broken mod - started
// with no window, watched until it listens on its port (it came up) or its process ends (it did
// not), then stopped again. What it logged in that time is read from its own log file, from where
// the file ended before the start: SPT keeps one log a day and adds to it.
//
// A hidden server that cannot start does not wait for a key (see SptLaunchService.StartHidden), so
// a failed start ends by itself.
//
public static partial class ServerTrial
{
    // HUNCH: longer than a heavily modded server takes to load; past it, it counts as not started.
    public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    // HUNCH: what mods log just after the port opens is still theirs.
    public static readonly TimeSpan SettleAfterStart = TimeSpan.FromSeconds(3);

    // HUNCH: for the process to be found after it was started, and to go after it was stopped.
    private static readonly TimeSpan ProcessGrace = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

    public static async Task<ServerTrialOutcome> RunAsync(string installPath, CancellationToken ct = default)
    {
        var info = SptLaunchService.Describe(installPath, SptLaunchTarget.Server);
        if (info.IsRunning) throw new ServerTrialException(ServerTrialProblem.AlreadyRunning);

        // Ports something else listens on already say nothing about this server.
        var ports = SptServerReadiness.PortsFor(info.ExePath).Except(Listening()).ToHashSet();
        if (ports.Count == 0) throw new ServerTrialException(ServerTrialProblem.PortTaken);

        var before = LogLengths(installPath);

        var launch = SptLaunchService.Launch(installPath, SptLaunchTarget.Server, hidden: true);
        if (!launch.Started)
            throw new ServerTrialException(ServerTrialProblem.CouldNotStart, launch.Error?.Message ?? launch.Problem.ToString());

        var started = false;
        var timedOut = false;
        try
        {
            var launched = DateTime.UtcNow;
            var seen = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                if (SptServerReadiness.IsListening(ports))
                {
                    started = true;
                    break;
                }

                var running = SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning;
                seen |= running;

                // Gone again - or never seen, which is the same thing a little later.
                if (!running && (seen || DateTime.UtcNow - launched > ProcessGrace)) break;

                if (DateTime.UtcNow - launched > StartTimeout)
                {
                    timedOut = true;
                    break;
                }

                await Task.Delay(Poll, ct).ConfigureAwait(false);
            }

            if (started) await Task.Delay(SettleAfterStart, ct).ConfigureAwait(false);
        }
        finally
        {
            // Whatever happened - cancelled included - the server this started does not stay up.
            await StopAsync(installPath).ConfigureAwait(false);
        }

        return new ServerTrialOutcome(started, timedOut, ErrorsSince(installPath, before));
    }

    /// <summary>Stops the install's server when it runs, and waits for it to be gone.</summary>
    public static async Task StopAsync(string installPath)
    {
        if (!SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning) return;

        await Task.Run(() => SptLaunchService.StopTarget(installPath, SptLaunchTarget.Server)).ConfigureAwait(false);

        var until = DateTime.UtcNow + ProcessGrace;
        while (SptLaunchService.Describe(installPath, SptLaunchTarget.Server).IsRunning)
        {
            if (DateTime.UtcNow > until) throw new ServerTrialException(ServerTrialProblem.CouldNotStop);
            await Task.Delay(Poll).ConfigureAwait(false);
        }
    }

    private static HashSet<int> Listening()
    {
        try
        {
            return [.. IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port)];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    // ---- the log ---------------------------------------------------------------------------------

    /// <summary>How long each of the server's log files is now.</summary>
    public static Dictionary<string, long> LogLengths(string installPath)
    {
        var lengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in LogFilesOf(installPath)) lengths[file.FullName] = file.Length;
        return lengths;
    }

    /// <summary>The errors the server logged since <see cref="LogLengths"/> was taken.</summary>
    public static IReadOnlyList<LogEntry> ErrorsSince(string installPath, IReadOnlyDictionary<string, long> before)
    {
        var errors = new List<LogEntry>();

        foreach (var file in LogFilesOf(installPath).OrderBy(f => f.LastWriteTimeUtc))
        {
            var from = before.GetValueOrDefault(file.FullName);
            if (file.Length < from) from = 0; // started over
            if (file.Length == from) continue;

            errors.AddRange(LogEntries.Parse(LogKind.Server, file.FullName, ReadFrom(file.FullName, from)).Where(e => e.IsError));
        }

        return errors;
    }

    private static List<FileInfo> LogFilesOf(string installPath)
    {
        if (ServerLogs.Folder(installPath) is not { } folder) return [];

        try
        {
            return [.. new DirectoryInfo(folder)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                    || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Name.StartsWith("launcher", StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<string> ReadFrom(string path, long from)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(from, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(StripAnsi(line));
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Search", $"couldn't read {path}: {ex.Message}");
            return [];
        }
    }

    private static string StripAnsi(string line) => line.Contains('\u001b') ? Ansi().Replace(line, "") : line;

    [GeneratedRegex("\u001b\\[[0-9;]*[A-Za-z]")]
    private static partial Regex Ansi();

    /// <summary>What makes two log lines the same error whatever numbers they carry: where it came
    /// from and what it says, numbers taken out.</summary>
    public static string Signature(LogEntry entry)
    {
        var text = Digits().Replace(entry.Message, "#");
        return entry.Source + "|" + (text.Length > 300 ? text[..300] : text);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
