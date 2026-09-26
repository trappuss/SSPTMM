using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// How to undo one install, written into its work folder before it changes anything in the SPT
// folder - so a failure halfway, or the app being closed or killed halfway, can always be put back.
//
// Written ahead of each step, so it never claims more than has happened:
//   1. Planned - every file the install will write - is saved before anything is touched.
//   2. The previous version's files are moved into the work folder ("previous"), each one found
//      there by looking, not by a list.
//   3. Every file still in the way of a planned one is copied into the work folder ("before"), and
//      each planned path with nothing there is written down as Absent. Saved.
//   4. The new files are placed, the configs merged, and the record to write saved as Record.
//   5. The manifest is written with Record: from then on the install is done.
//
// Undoing puts each planned path back to what it was: the previous version's file, else the copy
// from "before", else - only for a path written down as Absent - nothing. A planned path with none
// of those (the app stopped before step 3 got to it) is left exactly as it is. Nothing is ever
// deleted that was there before the install.
//
// A journal whose Record is already in the manifest was finished; only the tidying was left.
//
// A planned path the previous version had is never written down as Absent: its file is in
// "previous", and once undoing has moved it back, a second attempt must not take it for new.
//
//
public sealed class InstallJournal
{
    public const string FileName = "journal.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string InstallPath { get; set; } = string.Empty;

    public string ModName { get; set; } = string.Empty;

    public int ModId { get; set; }

    public bool IsAddon { get; set; }

    // Every install-relative file (forward slashes) the install will write.
    public List<string> Planned { get; set; } = [];

    // The planned paths that held nothing before the install - the only ones undoing may delete.
    public List<string> Absent { get; set; } = [];

    // The record the install writes last; in the manifest means the install finished.
    public InstalledModRecord? Record { get; set; }

    // What the previous version had replaced (its Replaced list), for the tidying after the record is
    // written - which may happen at the next start, when that record is no longer in the manifest.
    public List<string> PreviousReplaced { get; set; } = [];

    // When the previous version was installed - which mods were installed over it since.
    public DateTimeOffset? PreviousInstalledAt { get; set; }

    // Set once the record is written: from then on this install is never undone, only tidied - even
    // if the record has since been replaced by a later install or removal.
    public bool Committed { get; set; }

    // Other mods' kept copies of the previous version's files, which this install places again: they
    // are copies of a version that is gone, let go once the install is written.
    public List<StaleCopy> StaleCopies { get; set; } = [];

    [JsonIgnore]
    public string WorkDirectory { get; private set; } = string.Empty;

    [JsonIgnore]
    public string PreviousDirectory => Path.Combine(WorkDirectory, "previous");

    // Copies of what was in the way of planned files, someone else's or the mod's own leftovers.
    [JsonIgnore]
    public string BeforeDirectory => Path.Combine(WorkDirectory, "before");

    public string BeforeCopyOf(string relative) =>
        Path.Combine(BeforeDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

    public static InstallJournal Begin(string workDirectory, string installPath, InstallTarget target) =>
        new()
        {
            WorkDirectory = workDirectory,
            InstallPath = installPath,
            ModName = target.Name,
            ModId = target.Id,
            IsAddon = target.IsAddon,
        };

    public void Save() =>
        SafeFile.WriteAllText(Path.Combine(WorkDirectory, FileName), JsonSerializer.Serialize(this, Options));

    /// <summary>The journal left in a work folder, if there is one and it reads.</summary>
    public static InstallJournal? Load(string workDirectory)
    {
        var path = Path.Combine(workDirectory, FileName);
        if (!File.Exists(path)) return null;

        try
        {
            var journal = JsonSerializer.Deserialize<InstallJournal>(File.ReadAllText(path), Options);
            if (journal is not null) journal.WorkDirectory = workDirectory;
            return journal;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Install", $"install journal in {workDirectory} could not be read", ex);
            return null;
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(Path.Combine(WorkDirectory, FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Install", $"couldn't remove the install journal: {ex.Message}");
        }
    }

    //
    // Puts the SPT folder back as it was before the install. Returns the files that could not be put
    // back (locked, or the disk refused); an empty list means nothing of the install is left.
    //
    public List<string> Undo()
    {
        var failed = new List<string>();
        var previous = PreviousDirectory;
        var absent = Absent.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //
        // In three passes, so a previous version's FILE can come back where the new version made a
        // FOLDER (v1 "Lib", v2 "Lib/inner.dll"): the new files go first, then the folders they leave
        // empty, then the previous version's files. Each pass is safe to run again - a second attempt
        // after a partial one (a file held open) redoes only what is still undone.
        //
        foreach (var relative in Planned)
        {
            var destination = Resolve(relative);
            var stashed = Path.Combine(previous, relative.Replace('/', Path.DirectorySeparatorChar));
            var before = BeforeCopyOf(relative);

            try
            {
                if (File.Exists(stashed))
                {
                    // Pass three puts it back.
                }
                else if (File.Exists(before))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(before, destination, overwrite: true);
                }
                else if (absent.Contains(relative) && File.Exists(destination))
                {
                    File.Delete(destination);
                    touched.Add(Path.GetDirectoryName(destination)!);
                }

                // Otherwise: never looked at before the app stopped, so as it was. Left alone.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(relative);
            }
        }

        // Folders the install created and left empty.
        foreach (var dir in touched.OrderByDescending(d => d.Length))
        {
            for (var current = dir; IsInsideInstall(current); current = Path.GetDirectoryName(current))
            {
                try
                {
                    if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any()) break;
                    Directory.Delete(current);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }
            }
        }

        // The previous version's files, placed again by the new one or not.
        if (Directory.Exists(previous))
        {
            foreach (var stashed in Directory.GetFiles(previous, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(previous, stashed).Replace('\\', '/');
                try
                {
                    var destination = Resolve(relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(stashed, destination, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed.Add(relative);
                }
            }
        }

        return [.. failed.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private string Resolve(string relative) =>
        Path.Combine(InstallPath, relative.Replace('/', Path.DirectorySeparatorChar));

    private bool IsInsideInstall(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;

        var full = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        var install = Path.GetFullPath(InstallPath).TrimEnd(Path.DirectorySeparatorChar);
        return full.Length > install.Length && full.StartsWith(install, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Another mod's kept copy of one path, no longer wanted.</summary>
public sealed record StaleCopy(int ModId, bool IsAddon, string Path);
