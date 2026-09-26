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

        foreach (var relative in Planned)
        {
            var destination = Resolve(relative);
            var stashed = Path.Combine(previous, relative.Replace('/', Path.DirectorySeparatorChar));
            var before = BeforeCopyOf(relative);

            try
            {
                if (File.Exists(stashed))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(stashed, destination, overwrite: true);
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

        // The previous version's files the new one does not place: back too.
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

        return failed;
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
