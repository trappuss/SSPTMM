using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace TCFModManager.Core.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

// 
// Timestamped file log under &lt;app folder&gt;\Data\logs, written on a background queue so logging
// never blocks the UI. One file per day, older files pruned. Debug is off unless a file named
// "verbose" exists next to the exe or in the log folder, so the chatty per-item lines can be switched
// on to chase a bug without a rebuild.
// 
public static class AppLog
{
    private const int RetentionDays = 7;

    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly Lock StartupGate = new();

    private static int _draining;
    private static bool _started;

    // Folder holding the log files. Safe to show the user or open in Explorer.
    public static string Directory { get; } = Path.Combine(AppPaths.DataDirectory, "logs");

    // The file today's entries go to.
    public static string CurrentFile => Path.Combine(Directory, $"tcfmm-{DateTime.Now:yyyyMMdd}.log");

    // Entries below this are dropped. Debug is enabled by the "verbose" marker file.
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static void Debug(string area, string message) => Write(LogLevel.Debug, area, message);

    public static void Info(string area, string message) => Write(LogLevel.Info, area, message);

    public static void Warn(string area, string message) => Write(LogLevel.Warn, area, message);

    public static void Error(string area, string message, Exception? exception = null) =>
        Write(LogLevel.Error, area, exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    // 
    // Prepares the log folder, prunes old files and records a session header. Called once at
    // startup; later calls are ignored.
    // 
    public static void Start(string? extraContext = null)
    {
        lock (StartupGate)
        {
            if (_started) return;
            _started = true;
        }

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            // The Help page tells users to put it next to TCFModManager.exe; the log folder works too.
            if (File.Exists(Path.Combine(Directory, "verbose"))
                || File.Exists(Path.Combine(AppContext.BaseDirectory, "verbose")))
            {
                MinimumLevel = LogLevel.Debug;
            }
            PruneOldFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the thing that breaks the app.
            return;
        }

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        var header = new StringBuilder()
            .AppendLine()
            .AppendLine("========================================================")
            .AppendLine($"TCF Mod Manager {version} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"OS {Environment.OSVersion} / .NET {Environment.Version} / {(Environment.Is64BitProcess ? "x64" : "x86")}")
            .AppendLine($"Data folder: {AppPaths.DataDirectory}")
            .AppendLine($"Log level: {MinimumLevel} (create a file named \"verbose\" here for Debug)");

        if (!string.IsNullOrWhiteSpace(extraContext)) header.AppendLine(extraContext);

        header.Append("========================================================");

        Queue.Enqueue(header.ToString());
        Drain();
    }

    // Writes anything still queued, for use on shutdown.
    public static void Flush()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var file = CurrentFile;

            while (Queue.TryDequeue(out var line))
                File.AppendAllText(file, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do while shutting down.
        }
    }

    private static void Write(LogLevel level, string area, string message)
    {
        if (level < MinimumLevel) return;

        Queue.Enqueue(
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level.ToString().ToUpperInvariant(),-5} [{area}] {message}");

        Drain();
    }

    private static void Drain()
    {
        // Only one drain loop runs at a time; others just enqueue and move on.
        if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0) return;

        _ = Task.Run(DrainQueueAsync);
    }

    private static async Task DrainQueueAsync()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var file = CurrentFile;

            while (Queue.TryDequeue(out var line))
            {
                try
                {
                    await File.AppendAllTextAsync(file, line + Environment.NewLine).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Drop this line and carry on rather than spinning on a locked file.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Log folder unavailable; discard what's queued.
            while (Queue.TryDequeue(out _)) { }
        }
        finally
        {
            Interlocked.Exchange(ref _draining, 0);
        }
    }

    private static void PruneOldFiles()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "tcfmm-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Leave it; it'll be retried next launch.
            }
        }
    }
}
