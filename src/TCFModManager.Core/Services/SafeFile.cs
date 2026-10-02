using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// How every file this app keeps is written, and what happens when one can't be read back
// (CLOSED-10-TCFResilience-DESIGN.md §9, D18/D19).
//
// A write goes to a temporary file beside the target, is flushed to disk, and only then renamed over
// the old one - so a crash, a full disk or a killed process leaves either the old file or the new
// one, never half of either. ServerMapServerSettings already worked this way; now everything does.
//
// A file that can't be parsed is copied aside as "<name>.damaged-<time>" before its store falls back
// to an empty default, so the next save - which writes that default over it - can no longer lose
// what was there.
//
// User-data files also keep rolling backups: before a save replaces a file that still parses, a copy
// goes into "backups\<name>\" beside it. At most one every BackupInterval, the newest BackupsKept
// kept, so a burst of saves (a mod list applying fifty mods) can't push every older copy out.
//
public static class SafeFile
{
    // ------------------------------------------------------------------ fork: telling the user
    //
    // The Steam fork says so when a file is found damaged (App.ReportDataProblems), rather than
    // leaving it to the log: settings or collections falling back to empty is something to know.
    // Each damaged copy kept is reported once, whichever store found it, from whichever thread.
    //
    private static readonly System.Collections.Concurrent.ConcurrentQueue<SafeFileProblem> Found = new();

    /// <summary>Every damaged file found this session, oldest first.</summary>
    public static IReadOnlyList<SafeFileProblem> Problems => [.. Found];

    /// <summary>Raised when a damaged file is found and kept aside; may be raised off the UI thread.</summary>
    public static event EventHandler<SafeFileProblem>? ProblemFound;

    private static void Report(SafeFileProblem problem)
    {
        if (Found.Any(p => string.Equals(p.KeptAs, problem.KeptAs, StringComparison.OrdinalIgnoreCase))) return;

        Found.Enqueue(problem);
        ProblemFound?.Invoke(null, problem);
    }

    // ------------------------------------------------------------------

    public const int BackupsKept = 5;
    public const int DamagedCopiesKept = 5;
    public static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(10);

    private const string BackupFolderName = "backups";
    private const string DamagedMarker = ".damaged-";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // The text is written as UTF-8 without a byte order mark unless another encoding is given.
    public static void WriteText(string path, string text, bool keepBackups = false, Encoding? encoding = null) =>
        WriteBytes(path, (encoding ?? Utf8NoBom).GetPreamble().Concat((encoding ?? Utf8NoBom).GetBytes(text)).ToArray(), keepBackups);

    public static void WriteBytes(string path, byte[] bytes, bool keepBackups = false)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, $".{Path.GetFileName(full)}.tmp-{Guid.NewGuid():N}");

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (keepBackups) TryBackUp(full);

            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    //
    // Copies a file that failed to parse to "<name>.damaged-<time>" beside it, and logs it at Error.
    // Called by a store's Load just before it falls back to an empty default. Returns the copy, or
    // null when there was nothing to copy or the copy itself failed.
    //
    // A store can be loaded many times while its file stays damaged, so an identical copy already
    // there is reused rather than piling up another one each time.
    //
    public static string? PreserveDamaged(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) return null;

            var directory = Path.GetDirectoryName(full)!;
            var name = Path.GetFileName(full);
            var bytes = File.ReadAllBytes(full);

            var existing = Directory.EnumerateFiles(directory, name + DamagedMarker + "*")
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .FirstOrDefault();

            if (existing is not null && SameBytes(existing, bytes)) return existing;

            var copy = UniquePath(Path.Combine(directory, $"{name}{DamagedMarker}{DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture)}"));
            File.WriteAllBytes(copy, bytes);

            AppLog.Error("Data", $"{name} could not be read; kept it as {Path.GetFileName(copy)} and carried on without it");
            Report(new SafeFileProblem(full, copy));

            Prune(Directory.EnumerateFiles(directory, name + DamagedMarker + "*"), DamagedCopiesKept);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Data", $"{Path.GetFileName(path)} could not be read, and a copy of it could not be kept either: {ex.Message}");
            return null;
        }
    }

    // The folder a file's rolling backups go in: "backups\<file name>\" beside it.
    public static string BackupFolderFor(string path)
    {
        var full = Path.GetFullPath(path);
        return Path.Combine(Path.GetDirectoryName(full)!, BackupFolderName, Path.GetFileName(full));
    }

    //
    // Backs the current file up before it is replaced - only if it still parses as JSON (a damaged
    // file is PreserveDamaged's, and must not push a good backup out), and only if the newest backup
    // is older than BackupInterval. Never stops the save: a backup that can't be written is logged.
    //
    private static void TryBackUp(string full)
    {
        try
        {
            if (!File.Exists(full)) return;
            if (!ParsesAsJson(full)) return;

            var folder = BackupFolderFor(full);
            Directory.CreateDirectory(folder);

            // Each backup is named for when it was taken, so its age is read from the name rather than
            // from file times, which a copy carries over from the original.
            var newest = Directory.EnumerateFiles(folder)
                .Select(f => StampOf(Path.GetFileName(f)))
                .Where(t => t is not null)
                .Max();

            if (newest is { } taken && DateTime.Now - taken < BackupInterval) return;

            var backup = UniquePath(Path.Combine(
                folder,
                DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture) + Path.GetExtension(full)));
            File.Copy(full, backup);

            Prune(Directory.EnumerateFiles(folder), BackupsKept);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Data", $"couldn't back up {Path.GetFileName(full)} before saving it: {ex.Message}");
        }
    }

    private static bool ParsesAsJson(string full)
    {
        try
        {
            using var stream = File.OpenRead(full);
            using var _ = JsonDocument.Parse(stream);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Keeps the newest `keep` files by name - every name here starts with a sortable timestamp.
    private static void Prune(IEnumerable<string> files, int keep)
    {
        foreach (var old in files.OrderByDescending(f => f, StringComparer.Ordinal).Skip(keep))
            TryDelete(old);
    }

    // Two in the same millisecond only happen in tests, but a name that's taken is never reused.
    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        for (var n = 2; ; n++)
        {
            var candidate = $"{path}-{n}";
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private const string StampFormat = "yyyyMMdd-HHmmss-fff";

    // The time at the start of a backup's name, or null for anything else in the folder.
    private static DateTime? StampOf(string fileName) =>
        fileName.Length >= StampFormat.Length
        && DateTime.TryParseExact(fileName[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp)
            ? stamp
            : null;

    private static bool SameBytes(string path, byte[] bytes)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>A data file found damaged and kept aside (SafeFile.PreserveDamaged): the file, and the
/// copy kept of it, when one could be kept.</summary>
public sealed record SafeFileProblem(string Path, string? KeptAs);
