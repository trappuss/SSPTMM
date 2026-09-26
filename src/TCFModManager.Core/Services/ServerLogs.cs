using System.Text;

namespace TCFModManager.Core.Services;

//
// The SPT server's own log, read from where it writes it (user/logs under the server folder) rather
// than by capturing its console: the server keeps running in its own window exactly as it always
// has - nothing about how it is started or stopped changes - and what it logs shows on the Play page.
//
// Whichever log file there was written last is the one shown; the file names are the server's
// business and have changed between SPT versions, so none is assumed - except that the launcher's
// own log, when it writes one there, is not the server's.
//
public static class ServerLogs
{
    private static readonly string[] Extensions = [".log", ".txt"];

    /// <summary>The server's logs folder, or null when it has none (never run, or no server found).</summary>
    public static string? Folder(string? installPath)
    {
        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return null;

        var folder = Path.Combine(installPath!, serverRoot, "user", "logs");
        return Directory.Exists(folder) ? folder : null;
    }

    /// <summary>The log file written to last, or null.</summary>
    public static string? Newest(string? installPath)
    {
        if (Folder(installPath) is not { } folder) return null;

        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)
                    && !f.Name.StartsWith("launcher", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The last <paramref name="lines"/> lines of a file the server may be writing to right
    /// now, reading at most <paramref name="maxBytes"/> from its end.</summary>
    public static IReadOnlyList<string> Tail(string path, int lines = 400, int maxBytes = 256 * 1024)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();

            var all = text.Split('\n').Select(l => StripAnsi(l.TrimEnd('\r'))).ToList();

            // Starting part way into the file, the first line is most likely cut; it goes.
            if (start > 0 && all.Count > 0) all.RemoveAt(0);
            if (all.Count > 0 && all[^1].Length == 0) all.RemoveAt(all.Count - 1);

            return all.Count > lines ? all.GetRange(all.Count - lines, lines) : all;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Colour codes a console would draw, shown as text here - taken out.
    private static string StripAnsi(string line) =>
        line.Contains('\u001b')
            ? System.Text.RegularExpressions.Regex.Replace(line, "\u001b\\[[0-9;]*[A-Za-z]", "")
            : line;
}
