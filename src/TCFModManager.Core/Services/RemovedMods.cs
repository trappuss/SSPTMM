using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// How long removed mods are kept in the holding folder (R11, D27). Default FourteenDays (R15).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RemovedModsRetention
{
    DeleteStraightAway,
    OneDay,
    SevenDays,
    FourteenDays,
    ThirtyDays,
    UntilCleared,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RemovalKind
{
    // Remove on a mod this app installed: its recorded files.
    AppInstalled,

    // Remove on a mod installed by hand: its whole folder(s).
    HandInstalled,

    // The removal half of an update: the previous version's files, before the new ones are placed.
    ReplacedByUpdate,
}

// What a removal did with one path - one line of the trail (D26).
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RemovalOutcome
{
    // Moved out of the install into the holding folder.
    Moved,

    // Recorded, but not on disk any more.
    AlreadyGone,

    // Left in place: another mod's record lists it too (D9).
    KeptOwnedByAnotherMod,

    // Left in place: not what was installed any more - changed by the user, another mod or a reused folder (D21).
    KeptChangedSinceInstall,

    // Left in place: a config the update couldn't copy aside, or one of the user's own documents.
    KeptForTheUser,

    // Left in place: failed one of InstallPathGuard's checks (Refusal says which).
    Refused,

    // The move itself failed - the operating system's reason is in the log.
    Failed,

    // A file this mod had replaced was put back (D22).
    OriginalRestored,

    // A replaced file was not put back because something now occupies its path; the original is in the holding folder.
    OriginalNotRestoredOccupied,

    // An earlier copy of the same mod, proven by its GUID (R16): kept in the holding folder, not put back.
    OriginalHeldSameMod,

    // A replaced file's kept copy was missing from Data.
    OriginalMissing,
}

public sealed record RemovalEntry(string Path, RemovalOutcome Outcome, PathRefusal? Refusal = null);

//
// removal.json in each holding folder - exactly what one removal did, so it can be shown, logged and
// undone (D23, D26, D28).
//
public sealed class RemovalLog
{
    public int SchemaVersion { get; init; } = 1;

    public required string Id { get; init; }

    public required RemovalKind Kind { get; init; }

    public required string ModName { get; init; }

    public required DateTimeOffset RemovedAt { get; init; }

    public required string InstallPath { get; init; }

    // The record that was removed, for an app-installed mod - what Undo puts back.
    public InstalledModRecord? Record { get; init; }

    public List<RemovalEntry> Entries { get; init; } = [];

    // Where the removal moved the mod's configs (Data\LegacyConfigs\...), so Undo can bring them back.
    public string? ConfigsKeptFolder { get; set; }

    // The mod's folders a removal had to leave because they still hold files the mod didn't install
    // (a file the user added, say), install-relative. The Installed page marks those folders' cards.
    public List<string> FoldersLeft { get; set; } = [];

    // True once Undo has put this removal back.
    public bool Undone { get; set; }
}

//
// The holding folder: <install>\.tcfmm-removed\<time>_<mod>\, with files\ (what left the install, at
// its install-relative path), originals\ (the copies this mod had kept of files it replaced, at their
// path under Data) and removal.json (D23). A rename on the install's own drive, so even a large mod
// moves instantly.
//
// Deleting from here - retention, Clear, Delete straight away - is the only place the app deletes mod
// files outright, and it never deletes outside this folder: every delete is checked to be strictly
// inside it, and a folder holding a link is left alone rather than walked.
//
public sealed class RemovedMods(Func<RemovedModsRetention>? retention = null)
{
    public const string FolderName = ".tcfmm-removed";
    public const string LogName = "removal.json";
    public const string FilesFolder = "files";
    public const string OriginalsFolder = "originals";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly Func<RemovedModsRetention> _retention =
        retention ?? (() => new SettingsService().Load().RemovedModsRetention);

    public static string Root(string installPath) => Path.Combine(installPath, FolderName);

    // Starts a removal: makes its folder and returns it. Nothing has moved yet.
    public static RemovalSession Begin(string installPath, string modName, RemovalKind kind, InstalledModRecord? record)
    {
        var root = Root(installPath);
        Directory.CreateDirectory(root);
        TryHide(root);

        var stamp = DateTimeOffset.Now;
        var id = $"{stamp:yyyyMMdd-HHmmss-fff}_{SafeName(modName)}";
        var folder = Path.Combine(root, id);
        for (var n = 2; Directory.Exists(folder); n++) folder = Path.Combine(root, $"{id}-{n}");

        Directory.CreateDirectory(folder);

        return new RemovalSession(installPath, folder, new RemovalLog
        {
            Id = Path.GetFileName(folder),
            Kind = kind,
            ModName = modName,
            RemovedAt = stamp,
            InstallPath = InstallStamp.Of(installPath),
            Record = record,
        });
    }

    //
    // Writes the trail, logs every line of it, and - when the setting is Delete straight away - deletes
    // this removal's folder as the last step. Returns the folder, or null once it has been deleted.
    //
    public string? Finish(RemovalSession session)
    {
        foreach (var entry in session.Log.Entries)
        {
            AppLog.Info("Remove",
                $"{session.Log.ModName}: {entry.Path} - {entry.Outcome}{(entry.Refusal is { } r ? $" ({r})" : "")}");
        }

        SafeFile.WriteText(Path.Combine(session.Folder, LogName), JsonSerializer.Serialize(session.Log, Options));

        if (_retention() == RemovedModsRetention.DeleteStraightAway)
        {
            DeleteHeld(session.InstallPath, session.Folder);
            return null;
        }

        return session.Folder;
    }

    // Every removal still held for this install, newest first. Folders with no readable log are skipped.
    public static List<(string Folder, RemovalLog Log)> List(string installPath)
    {
        var root = Root(installPath);
        if (!Directory.Exists(root)) return [];

        var found = new List<(string, RemovalLog)>();

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (ReadLog(folder) is { } log) found.Add((folder, log));
        }

        return [.. found.OrderByDescending(f => f.Item2.RemovedAt)];
    }

    //
    // Every removal the user made in this install that can still be undone, newest first (D28). An
    // update's removal half, one already undone and one from another install are left out. Any of
    // them can be undone in any order: Undo only ever puts things back into free paths.
    //
    public static List<(string Folder, RemovalLog Log)> Undoable(string installPath)
    {
        var stamp = InstallStamp.Of(installPath);

        return
        [
            .. List(installPath).Where(r =>
                r.Log.Kind != RemovalKind.ReplacedByUpdate
                && !r.Log.Undone
                && string.Equals(StampOrNull(r.Log.InstallPath), stamp, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    // The most recent of those, if any.
    public static (string Folder, RemovalLog Log)? LatestUndoable(string installPath) =>
        Undoable(installPath) is [var latest, ..] ? latest : null;

    private static string? StampOrNull(string path)
    {
        try { return InstallStamp.Of(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // Deletes held removals older than the retention (D27). Run at startup.
    public int Prune(string installPath, DateTimeOffset now)
    {
        var age = _retention() switch
        {
            RemovedModsRetention.DeleteStraightAway => TimeSpan.Zero,
            RemovedModsRetention.OneDay => TimeSpan.FromDays(1),
            RemovedModsRetention.SevenDays => TimeSpan.FromDays(7),
            RemovedModsRetention.FourteenDays => TimeSpan.FromDays(14),
            RemovedModsRetention.ThirtyDays => TimeSpan.FromDays(30),
            _ => (TimeSpan?)null,
        };

        if (age is not { } keepFor) return 0;

        var deleted = 0;

        foreach (var (folder, log) in List(installPath))
        {
            if (now - log.RemovedAt >= keepFor && DeleteHeld(installPath, folder)) deleted++;
        }

        return deleted;
    }

    // Deletes every held removal for this install - the Clear removed mods button.
    public static int Clear(string installPath)
    {
        var root = Root(installPath);
        if (!Directory.Exists(root)) return 0;

        return Directory.EnumerateDirectories(root).ToList().Count(folder => DeleteHeld(installPath, folder));
    }

    // Bytes held for this install.
    public static long Size(string installPath)
    {
        var root = Root(installPath);
        if (!Directory.Exists(root)) return 0;

        try
        {
            return new DirectoryInfo(root)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static RemovalLog? ReadLog(string folder)
    {
        var path = Path.Combine(folder, LogName);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<RemovalLog>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Remove", $"couldn't read {path}: {ex.Message}");
            return null;
        }
    }

    //
    // Deletes one held removal. Refuses anything not strictly inside this install's holding folder, and
    // a folder with a link anywhere inside it - that is left for the user rather than walked.
    //
    internal static bool DeleteHeld(string installPath, string folder)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root(installPath))) + Path.DirectorySeparatorChar;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.Length <= root.Length)
            {
                AppLog.Warn("Remove", $"refused to delete {folder}: not inside {root}");
                return false;
            }

            if (InstallPathGuard.IsLink(full) || InstallPathGuard.FirstLink(full) is not null)
            {
                AppLog.Warn("Remove", $"left {folder} in place: it is or holds a link");
                return false;
            }

            Directory.Delete(full, recursive: true);
            AppLog.Info("Remove", $"deleted the held removal {Path.GetFileName(full)}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn("Remove", $"couldn't delete {folder}: {ex.Message}");
            return false;
        }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray()).Trim('_');
        if (cleaned.Length > 60) cleaned = cleaned[..60];
        return cleaned.Length == 0 ? "mod" : cleaned;
    }

    private static void TryHide(string directory)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            if (!info.Attributes.HasFlag(FileAttributes.Hidden)) info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }
}

//
// One removal in progress: moves things into its holding folder and collects the trail.
//
public sealed class RemovalSession(string installPath, string folder, RemovalLog log)
{
    public string InstallPath { get; } = installPath;
    public string Folder { get; } = folder;
    public RemovalLog Log { get; } = log;

    public void Note(string path, RemovalOutcome outcome, PathRefusal? refusal = null) =>
        Log.Entries.Add(new RemovalEntry(path, outcome, refusal));

    // Moves a file from the install into files\<install-relative path>. Never overwrites.
    public void MoveFileIn(string fullSource, string installRelative)
    {
        var destination = Path.Combine(Folder, RemovedMods.FilesFolder, installRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(fullSource, destination, overwrite: false);
    }

    // Moves a whole folder (a hand-installed mod) from the install into files\<install-relative path>.
    public void MoveFolderIn(string fullSource, string installRelative)
    {
        var destination = Path.Combine(Folder, RemovedMods.FilesFolder, installRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(fullSource, destination);
    }

    //
    // Moves a kept original out of Data into originals\<its path under Data>. A file move, so it works
    // across drives when the app's Data isn't on the install's drive.
    //
    public void MoveOriginalIn(string dataRelative)
    {
        var source = Path.Combine(AppPaths.DataDirectory, dataRelative.Replace('/', Path.DirectorySeparatorChar));
        var destination = Path.Combine(Folder, RemovedMods.OriginalsFolder, dataRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: false);
    }
}
