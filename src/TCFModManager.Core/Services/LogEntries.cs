using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

/// <summary>Which log an entry came from.</summary>
public enum LogKind
{
    /// <summary>The SPT server's log (user\logs\spt\spt&lt;date&gt;.log on SPT 4).</summary>
    Server,

    /// <summary>BepInEx's LogOutput.log in the game folder - the last game session.</summary>
    BepInEx,

    /// <summary>The game's own errors.log, in the newest Logs\log_* folder.</summary>
    GameErrors,
}

/// <summary>One entry of a log: its header line and the lines under it (a stack trace, a list).</summary>
/// <param name="Line">The header's line number in the file, from 1.</param>
/// <param name="At">When, as the log writes it (local to the machine that wrote it); null when the log has no time.</param>
/// <param name="Level">"Error", "Critical", "Warning", "Info"... as the log spells it.</param>
/// <param name="Source">The logger: a class (server), a plugin or "BepInEx" (BepInEx), a channel (game).</param>
public sealed record LogEntry(
    LogKind Log, string File, int Line, DateTime? At, string Level, string Source, string Message, IReadOnlyList<string> More)
{
    public bool IsError => Level is "Error" or "Critical" or "Fatal";

    public bool IsWarning => Level == "Warning";

    /// <summary>The message and every line under it.</summary>
    public string Text => More.Count == 0 ? Message : Message + "\n" + string.Join("\n", More);
}

//
// Fork (SSPTMM): the logs Diagnose logs reads, split into entries. Formats as written on SPT 4.1.6,
// read from a real install (2026-10-03):
//
//   server   [2026-10-03 05:21:06.980][Error][SPTarkov.Server.Core.Loaders.BundleLoader] Unable to add bundle: ...
//   BepInEx  [Error  :ZGFueDkx-AllQuestsCheckmarks] Failed to set custom checkmark in ItemSpecificationPanel!
//   game     2026-10-03 01:22:18.403 -04:00|0.16.9.5.40743|Error|assetBundle|Bundle "mods/ak/..." has dependency ...
//
// A line that doesn't start an entry belongs to the one above it: stack traces, and the list of
// plugins under "N plugins failed to load due to errors:".
//
public static partial class LogEntries
{
    [GeneratedRegex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?)\]\[(\w+)\]\[([^\]]*)\] ?(.*)$")]
    private static partial Regex ServerHeader();

    [GeneratedRegex(@"^\[(\w+) *: *([^\]]*?) *\] ?(.*)$")]
    private static partial Regex BepInExHeader();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?)(?: [+-]\d{2}:\d{2})?\|[^|]*\|(\w+)\|([^|]*)\|(.*)$")]
    private static partial Regex GameHeader();

    public static List<LogEntry> Parse(LogKind log, string file, IEnumerable<string> lines, int firstLine = 1)
    {
        var entries = new List<LogEntry>();
        var header = log switch
        {
            LogKind.Server => ServerHeader(),
            LogKind.BepInEx => BepInExHeader(),
            _ => GameHeader(),
        };

        (int Line, DateTime? At, string Level, string Source, string Message)? open = null;
        var more = new List<string>();

        void Close()
        {
            if (open is not { } e) return;
            entries.Add(new LogEntry(log, file, e.Line, e.At, e.Level, e.Source, e.Message, [.. more]));
            more.Clear();
        }

        var number = firstLine - 1;
        foreach (var raw in lines)
        {
            number++;
            var line = raw.TrimEnd('\r');
            var match = header.Match(line);

            if (!match.Success)
            {
                if (open is not null && line.Length > 0) more.Add(line);
                continue;
            }

            Close();
            open = log == LogKind.BepInEx
                ? (number, null, match.Groups[1].Value, match.Groups[2].Value.Trim(), match.Groups[3].Value)
                : (number, ParseTime(match.Groups[1].Value), match.Groups[2].Value, match.Groups[3].Value.Trim(), match.Groups[4].Value);
        }

        Close();
        return entries;
    }

    private static DateTime? ParseTime(string text) =>
        DateTime.TryParseExact(
            text,
            ["yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var at)
            ? at
            : null;

    /// <summary>Every line of a file another program may be writing to; empty when it can't be read.</summary>
    public static IReadOnlyList<string> ReadLines(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Diagnose", $"couldn't read {path}: {ex.Message}");
            return [];
        }
    }
}
