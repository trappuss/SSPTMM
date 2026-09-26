using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace TCFModManager.Core.Services;

/// <summary>A damaged or unreadable data file found while reading it, and what was done.</summary>
/// <param name="Path">The file.</param>
/// <param name="KeptAs">Where the damaged copy was kept, when it could be kept.</param>
/// <param name="RestoredFromBackup">True when the last good copy (.bak) was put back in its place.</param>
/// <param name="CouldNotRead">True when the file could not be read at all (held open elsewhere) -
/// it is left as it is, and not saved over this session.</param>
public sealed record SafeFileProblem(string Path, string? KeptAs, bool RestoredFromBackup, bool CouldNotRead = false);

//
// Writing and reading the app's own data files so that a crash, a power cut or a locked file can
// never quietly cost the user their settings, their collections or the record of what each mod
// installed.
//
// Writing: the new content goes to a file beside the old one, is flushed to the disk, and only then
// takes the old one's place - a crash at any moment leaves either the old file or the new one, never
// half of one. Files that hold what the user made (settings, collections, install records) also keep
// the previous version as a .bak.
//
// Reading: a file that is not valid JSON any more is not thrown away. It is copied aside as
// <name>.corrupt-<time>, the .bak is put back if it reads, and the problem is reported (Problems /
// ProblemFound) so the app can say so. A file that cannot be read at all (held open by an antivirus
// scan, a sync client) is tried again a few times; if it still cannot be read, writes to it are
// refused for the rest of the session, so defaults can never be saved over data that is still there.
//
public static class SafeFile
{
    private static readonly ConcurrentDictionary<string, byte> Unreadable = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> Reported = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<SafeFileProblem> Found = new();

    // How many times, and how far apart, a locked file is tried again before giving up.
    private const int ReadAttempts = 5;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Every problem found this session, oldest first.</summary>
    public static IReadOnlyList<SafeFileProblem> Problems => [.. Found];

    /// <summary>Raised when a damaged or unreadable file is found; may be raised off the UI thread.</summary>
    public static event EventHandler<SafeFileProblem>? ProblemFound;

    // ------------------------------------------------------------------ writing

    /// <summary>Writes text so the file is either all old or all new, never half written.
    /// <paramref name="keepBackup"/> keeps the previous version beside it as <c>.bak</c>.
    /// Returns false - and writes nothing - for a file that could not be read this session.</summary>
    public static bool WriteAllText(string path, string text, Encoding? encoding = null, bool keepBackup = false)
    {
        // As File.WriteAllText: UTF-8 without a byte order mark unless an encoding with one is given.
        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)], keepBackup);
    }

    /// <inheritdoc cref="WriteAllText"/>
    public static bool WriteAllBytes(string path, byte[] bytes, bool keepBackup = false)
    {
        var full = Path.GetFullPath(path);
        if (Unreadable.ContainsKey(full))
        {
            AppLog.Warn("Data", $"not saving {Path.GetFileName(full)}: it could not be read this session, and saving now would replace what is in it");
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var temp = full + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        if (!File.Exists(full))
        {
            File.Move(temp, full);
            return true;
        }

        if (keepBackup)
        {
            try
            {
                File.Replace(temp, full, full + ".bak", ignoreMetadataErrors: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Some file systems (and some sync clients) refuse the swap; the same result, in two steps.
                AppLog.Debug("Data", $"replace refused for {Path.GetFileName(full)} ({ex.Message}); copying instead");
                File.Copy(full, full + ".bak", overwrite: true);
            }
        }

        File.Move(temp, full, overwrite: true);
        return true;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>Reads a JSON data file. Null when there is none - or when it is damaged and has no
    /// good backup, in which case the damaged copy is kept aside and the problem reported.</summary>
    public static T? ReadJson<T>(string path, Func<string, T?> parse) where T : class
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return null;

        string text;
        try
        {
            text = ReadWithRetry(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still there, and still ours - just not readable right now. Never save defaults over it.
            Unreadable[full] = 0;
            AppLog.Error("Data", $"{Path.GetFileName(full)} could not be read; it will not be saved over this session", ex);
            Report(new SafeFileProblem(full, null, false, CouldNotRead: true));
            return null;
        }

        Unreadable.TryRemove(full, out _);

        try
        {
            return parse(text);
        }
        catch (JsonException ex)
        {
            AppLog.Error("Data", $"{Path.GetFileName(full)} is damaged: {ex.Message}");
            return Recover(full, parse);
        }
    }

    // The damaged file kept aside; the backup put back if it reads.
    private static T? Recover<T>(string full, Func<string, T?> parse) where T : class
    {
        string? keptAs = $"{full}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Copy(full, keptAs, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Already kept a moment ago (the same second), or the folder is read-only.
            AppLog.Warn("Data", $"could not keep a copy of the damaged {Path.GetFileName(full)}: {ex.Message}");
            if (!File.Exists(keptAs)) keptAs = null;
        }

        var backup = full + ".bak";
        if (File.Exists(backup))
        {
            try
            {
                var restored = parse(File.ReadAllText(backup));
                if (restored is not null)
                {
                    File.Copy(backup, full, overwrite: true);
                    AppLog.Warn("Data", $"{Path.GetFileName(full)} restored from its backup");
                    Report(new SafeFileProblem(full, keptAs, true));
                    return restored;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Data", $"the backup of {Path.GetFileName(full)} could not be used either: {ex.Message}");
            }
        }

        Report(new SafeFileProblem(full, keptAs, false));
        return null;
    }

    private static string ReadWithRetry(string full)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(full);
            }
            catch (IOException) when (attempt < ReadAttempts)
            {
                Thread.Sleep(ReadRetryDelay);
            }
        }
    }

    // Once per file per session: a file read ten times before it is saved again is one problem.
    private static void Report(SafeFileProblem problem)
    {
        if (!Reported.TryAdd(problem.Path, 0)) return;

        Found.Enqueue(problem);
        ProblemFound?.Invoke(null, problem);
    }

    /// <summary>For tests: forgets what this session has found.</summary>
    public static void ResetForTests()
    {
        Unreadable.Clear();
        Reported.Clear();
        while (Found.TryDequeue(out _)) { }
    }
}
