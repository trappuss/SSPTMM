using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// How to undo one install, written into its work folder before it changes anything in the SPT
// folder - so a failure halfway, or the app being closed or killed halfway, can always be put back.
//
// What an install does to the SPT folder, in order: the previous version's files are moved into the
// work folder ("previous"), files already there that the new version replaces are copied into the
// ReplacedFileStore, then the new files are placed. Undoing is the same in reverse: every planned
// file that was the previous version's goes back from "previous", every one that was someone else's
// comes back from the store, and every other one - placed by this install - is removed.
//
// Once every file is placed the journal is marked Placed and carries the record to write: from then
// on the install is finished, and a journal found in that state (the app stopped between placing the
// files and writing the record) is completed rather than undone.
//
public sealed class InstallJournal
{
    public const string FileName = "journal.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string InstallPath { get; set; } = string.Empty;

    public string ModName { get; set; } = string.Empty;

    public int ModId { get; set; }

    public bool IsAddon { get; set; }

    // Where copies of replaced files are kept - see ReplacedFileStore.
    public string ReplacedRoot { get; set; } = string.Empty;

    // Every install-relative file (forward slashes) the install will write.
    public List<string> Planned { get; set; } = [];

    // The files among Planned that were already there, someone else's, copied into the store by
    // THIS install - the ones to put back when undoing it.
    public List<string> BackedUp { get; set; } = [];

    // True once every planned file is in place.
    public bool Placed { get; set; }

    // The record to write once placed.
    public InstalledModRecord? Record { get; set; }

    [JsonIgnore]
    public string WorkDirectory { get; private set; } = string.Empty;

    [JsonIgnore]
    public string PreviousDirectory => Path.Combine(WorkDirectory, "previous");

    public static InstallJournal Begin(string workDirectory, string installPath, InstallTarget target, string replacedRoot) =>
        new()
        {
            WorkDirectory = workDirectory,
            InstallPath = installPath,
            ModName = target.Name,
            ModId = target.Id,
            IsAddon = target.IsAddon,
            ReplacedRoot = replacedRoot,
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
        var store = new ReplacedFileStore(ReplacedRoot);
        var previous = PreviousDirectory;
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in Planned)
        {
            var destination = Resolve(relative);
            var stashed = Path.Combine(previous, relative.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (File.Exists(stashed))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(stashed, destination, overwrite: true);
                }
                else if (BackedUp.Contains(relative, StringComparer.OrdinalIgnoreCase)
                         && store.Restore(InstallPath, ModId, IsAddon, relative))
                {
                    // Someone else's file, back where it was.
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                    touched.Add(Path.GetDirectoryName(destination)!);
                }
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
