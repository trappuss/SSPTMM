using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM 1.3.0): an install cut off part-way - the power going, the app killed - is put on
// record at the next start, as an incomplete install.
//
// An install that fails while the app is running already records what it placed (Incomplete), so a
// retry overwrites those files and a removal cleans them up. One that is cut off never gets that
// far: the previous version may already be gone, the new one half placed, and nothing on record -
// files the app no longer knows are its own.
//
// So just before an install starts changing the SPT folder, what it is about to do is written to
// a small file beside the install records: which mod and version, every path it will place, and
// the originals it kept. It is removed once the install's record is saved. One still there at the
// next start means the install never finished: the planned paths that are on disk now are recorded
// as that mod's, marked incomplete - the same record a failed install leaves - and the user is told.
//
// What it does not do is put the previous version back: its files were removed rather than kept.
// Subscribed items shows the mod as incomplete, and updating or reinstalling it finishes the job.
//
public static class InstallJournal
{
    private const string FolderName = "install-journal";

    public sealed class Entry
    {
        public int SchemaVersion { get; set; } = 1;

        public required int ModId { get; set; }

        public bool IsAddon { get; set; }

        public string? Guid { get; set; }

        public required string Name { get; set; }

        public int? VersionId { get; set; }

        public required string Version { get; set; }

        public long? ListedBytes { get; set; }

        public long? ArchiveBytes { get; set; }

        public required string InstallPath { get; set; }

        public DateTimeOffset StartedAt { get; set; }

        // Every install-relative path (forward slashes) the install was going to place.
        public List<string> Planned { get; set; } = [];

        // Originals kept in Data before anything was replaced - owed back whatever happens next.
        public List<OverwrittenFile> Overwrote { get; set; } = [];

        // Planned paths that were already on disk when the install began, as they were then - filled
        // by Begin. At recovery such a file counts as this install's only if it has changed since (or
        // the mod's previous record owned it): one left as it was is somebody else's - a config the
        // user keeps, another mod's file - and is not claimed.
        public List<ExistingFile> Existed { get; set; } = [];
    }

    public sealed record ExistingFile(string Path, long Bytes, DateTime WrittenUtc);

    // What was found and put on record at start.
    public sealed record Recovered(string Name, string Version, int FilesOnRecord);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DirectoryFor(ModInstallManifestService manifest) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest.FilePath))!, FolderName);

    // Written before the install touches the SPT folder. Returns the file, to be removed on success.
    public static string Begin(ModInstallManifestService manifest, Entry entry)
    {
        entry.Existed = [.. entry.Planned
            .Select(path => InstallPathGuard.CheckPlacedPath(entry.InstallPath, path, out var full) is null ? Describe(path, full) : null)
            .OfType<ExistingFile>()];

        var directory = DirectoryFor(manifest);
        var path = Path.Combine(directory, $"{entry.ModId}-{(entry.IsAddon ? "addon" : "mod")}-{System.Guid.NewGuid():N}.json");
        SafeFile.WriteText(path, JsonSerializer.Serialize(entry, Options));
        return path;
    }

    // The install's record is saved: the journal is no longer needed.
    public static void End(string? path)
    {
        if (path is null) return;

        // A scanner holding the new file a moment is the usual reason a delete fails - tried again.
        // One still left behind is harmless: recovery sees the install's finished record and drops it.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 3)
                {
                    AppLog.Warn("Install", $"couldn't remove the install journal {path}: {ex.Message}");
                    return;
                }

                Thread.Sleep(200);
            }
        }
    }

    private static ExistingFile? Describe(string path, string full)
    {
        try
        {
            var info = new FileInfo(full);
            return info.Exists ? new ExistingFile(path, info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    //
    // At start: every journal left behind becomes an incomplete record. A journal that can't be
    // read is set aside (SafeFile.PreserveDamaged) rather than acted on.
    //
    public static List<Recovered> RecoverAll(ModInstallManifestService manifest)
    {
        var found = new List<Recovered>();
        var directory = DirectoryFor(manifest);
        if (!Directory.Exists(directory)) return found;

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(File.GetLastWriteTimeUtc))
        {
            if (RecoverOne(path, manifest) is { } recovered) found.Add(recovered);
        }

        return found;
    }

    //
    // One journal: put on record and removed. Also used by an install that failed in a way it did not
    // record itself, so nothing it placed is left untracked.
    //
    public static Recovered? RecoverOne(string path, ModInstallManifestService manifest)
    {
        if (!File.Exists(path)) return null;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or unreadable for now - left for the next start, not thrown away.
            AppLog.Warn("Install", $"an install journal couldn't be read now ({ex.Message}); left for the next start");
            return null;
        }

        Entry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<Entry>(text);
        }
        catch (JsonException ex)
        {
            entry = null;
            AppLog.Warn("Install", $"an install journal is damaged ({ex.Message})");
        }

        // Required parts missing (a hand-edited or damaged file): set aside, never acted on.
        if (entry is null || string.IsNullOrWhiteSpace(entry.InstallPath) || entry.Planned is null
            || entry.Name is null || entry.Version is null)
        {
            AppLog.Warn("Install", $"the install journal {Path.GetFileName(path)} can't be used; set aside");
            SafeFile.PreserveDamaged(path);
            End(path);
            return null;
        }

        entry.Overwrote ??= [];
        entry.Existed ??= [];
        entry.Planned = [.. entry.Planned.Where(p => !string.IsNullOrWhiteSpace(p))];

        try
        {
            if (Record(entry, manifest) is not { } record)
            {
                // The install did finish - only its journal was left behind.
                AppLog.Info("Install", $"{entry.Name} {entry.Version}: finished install, its journal removed late");
                End(path);
                return null;
            }

            AppLog.Warn("Install",
                $"{entry.Name} {entry.Version} did not finish installing (started {entry.StartedAt:u}); "
                + $"{record.Files.Count} of {entry.Planned.Count} file(s) on record, marked incomplete");
            End(path);
            return new Recovered(entry.Name, entry.Version, record.Files.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next start rather than lost.
            AppLog.Error("Install", $"couldn't put {entry.Name} {entry.Version}'s unfinished install on record", ex);
            return null;
        }
    }

    // Null when the mod's record shows the install finished after all.
    private static InstalledModRecord? Record(Entry entry, ModInstallManifestService manifestService)
    {
        var manifest = manifestService.Load();
        var previous = manifest.Find(entry.ModId, entry.IsAddon);
        if (previous is { Incomplete: false } && previous.InstalledAt >= entry.StartedAt) return null;

        // The mod's previous record, when it describes this same install.
        var stamp = InstallStamp.Of(entry.InstallPath);
        if (previous is not null && previous.InstallPath is not null
            && !string.Equals(previous.InstallPath, stamp, StringComparison.OrdinalIgnoreCase))
        {
            previous = null;
        }

        var owned = new HashSet<string>(previous?.Files ?? [], StringComparer.OrdinalIgnoreCase);
        var before = entry.Existed
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Each planned path on disk now, inside the install and somewhere a mod may be, and whether
        // this install wrote it: it wasn't there when the install began, or it has changed since.
        var onDiskNow = entry.Planned
            .Select(path => (Path: path, Now: InstallPathGuard.CheckPlacedPath(entry.InstallPath, path, out var full) is null
                ? Describe(path, full)
                : null))
            .Where(p => p.Now is not null)
            .Select(p => (p.Path, Written: !before.TryGetValue(p.Path, out var then)
                || then.Bytes != p.Now!.Bytes || then.WrittenUtc != p.Now.WrittenUtc))
            .ToList();

        // On record: what this install wrote, and the previous version's files it was going to
        // replace but hadn't yet. A file that was there before, unchanged, and not this mod's is
        // somebody else's.
        var placed = onDiskNow.Where(p => p.Written || owned.Contains(p.Path)).Select(p => p.Path).ToList();

        // The previous version's files still as they were - their fingerprints still fit.
        var untouchedOld = new HashSet<string>(
            onDiskNow.Where(p => !p.Written && owned.Contains(p.Path)).Select(p => p.Path),
            StringComparer.OrdinalIgnoreCase);
        var anyNew = onDiskNow.Any(p => p.Written);

        //
        // Cut off while the previous version was being removed: what is left of it stays on record,
        // with the fingerprints it had, so Unsubscribe still clears it. (Its files at paths this
        // install was going to place are covered above.)
        //
        var planned = new HashSet<string>(entry.Planned, StringComparer.OrdinalIgnoreCase);
        var leftOver = (previous?.Files ?? [])
            .Where(path => !planned.Contains(path)
                && InstallPathGuard.CheckPlacedPath(entry.InstallPath, path, out var full) is null
                && File.Exists(full))
            .ToList();
        var leftOverSet = new HashSet<string>(leftOver, StringComparer.OrdinalIgnoreCase);

        var onDisk = placed.Concat(leftOver).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var overwrote = (previous?.Overwrote ?? []).Concat(entry.Overwrote)
            .GroupBy(o => o.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var record = new InstalledModRecord
        {
            ModId = entry.ModId,
            IsAddon = entry.IsAddon,
            Guid = entry.Guid,
            Name = entry.Name,
            // Nothing of the new version written yet (cut off while the previous one was being
            // removed): what is on disk is the previous version's, so its label stays.
            VersionId = !anyNew && previous is not null ? previous.VersionId : entry.VersionId,
            Version = !anyNew && previous is not null ? previous.Version : entry.Version,
            InstalledAt = entry.StartedAt,
            Files = onDisk,
            Folders = InstalledModFolders.FromPlacedFiles(onDisk),
            Incomplete = true,
            InstallPath = stamp,
            Overwrote = overwrote,
            Fingerprints = [.. (previous?.Fingerprints ?? []).Where(f => leftOverSet.Contains(f.Path) || untouchedOld.Contains(f.Path))],
            ArchiveBytes = entry.ArchiveBytes,
            ListedBytes = entry.ListedBytes,
        };

        manifest.Mods.RemoveAll(m => m.ModId == entry.ModId && m.IsAddon == entry.IsAddon);
        manifest.Mods.Add(record);
        manifestService.Save(manifest);
        return record;
    }
}
