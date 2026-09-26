using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TCFModManager.Core.Services;

/// <summary>One copy of an install's SPT profiles, as a zip in the app's Data folder.</summary>
/// <param name="Path">The zip.</param>
/// <param name="TakenAt">When it was taken (local time, from its name).</param>
/// <param name="Reason">Why: one of the <see cref="ProfileBackups"/> reason words.</param>
/// <param name="Files">How many files it holds.</param>
/// <param name="Bytes">Its size on disk.</param>
public sealed record ProfileBackup(string Path, DateTime TakenAt, string Reason, int Files, long Bytes);

//
// Copies of the SPT profiles (user/profiles under the server folder) taken before the app changes
// the install: installing, updating or removing a mod can leave a profile holding items or quests
// the server no longer knows, and SPT then refuses to load it. A copy from just before the change
// puts the character back.
//
// A copy is taken only when the profiles have changed since the last one, so a list applying
// twenty mods takes one, not twenty. The last few are kept per install; older ones are deleted.
// Restoring first takes a copy of the profiles as they are, so a restore can itself be undone, and
// it only ever writes files - a profile made after the copy was taken is left where it is.
//
// Never a reason for an install not to go ahead: a copy that cannot be taken is logged and skipped.
//
public sealed class ProfileBackups(string? root = null, int keep = 10)
{
    public const string BeforeInstall = "install";
    public const string BeforeRemove = "remove";
    public const string BeforeList = "list";
    public const string BeforeDisable = "disable";
    public const string BeforeRestore = "restore";
    public const string ByHand = "manual";

    public string Root { get; } = root ?? System.IO.Path.Combine(AppPaths.DataDirectory, "ProfileBackups");

    public int Keep { get; } = Math.Max(1, keep);

    private readonly Lock _gate = new();

    /// <summary>The install's profiles folder, or null when it has none (no server found, or never run).</summary>
    public static string? ProfilesFolder(string? installPath)
    {
        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot)) return null;

        var folder = System.IO.Path.Combine(installPath!, serverRoot, "user", "profiles");
        return Directory.Exists(folder) ? folder : null;
    }

    /// <summary>The folder this install's copies are kept in.</summary>
    public string FolderFor(string installPath)
    {
        var full = System.IO.Path.GetFullPath(installPath).TrimEnd(System.IO.Path.DirectorySeparatorChar).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
        return System.IO.Path.Combine(Root, key);
    }

    /// <summary>Takes a copy when the profiles differ from the last one. Returns the new copy, or
    /// null when none was needed or none could be taken.</summary>
    public ProfileBackup? BackupIfChanged(string installPath, string reason)
    {
        try
        {
            lock (_gate) return Take(installPath, reason, force: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Warn("Profiles", $"couldn't back up the SPT profiles before this change: {ex.Message}");
            return null;
        }
    }

    /// <summary>Takes a copy now, even when nothing changed. Throws when it cannot.</summary>
    public ProfileBackup? BackupNow(string installPath)
    {
        lock (_gate) return Take(installPath, ByHand, force: true);
    }

    /// <summary>This install's copies, newest first.</summary>
    public IReadOnlyList<ProfileBackup> List(string installPath)
    {
        var folder = FolderFor(installPath);
        if (!Directory.Exists(folder)) return [];

        var found = new List<ProfileBackup>();
        foreach (var zip in Directory.EnumerateFiles(folder, "*.zip"))
        {
            if (Parse(zip) is not { } name) continue;

            int files;
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                files = archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                AppLog.Warn("Profiles", $"{System.IO.Path.GetFileName(zip)} could not be read: {ex.Message}");
                continue;
            }

            found.Add(new ProfileBackup(zip, name.TakenAt, name.Reason, files, new FileInfo(zip).Length));
        }

        return [.. found.OrderByDescending(b => b.TakenAt).ThenByDescending(b => b.Path, StringComparer.Ordinal)];
    }

    /// <summary>Puts a copy's files back into the install's profiles folder, after taking a copy of
    /// what is there now. Returns that copy (null when the profiles had not changed since the last
    /// one). Throws <see cref="ModInstallException"/> while the server or game runs from the install.</summary>
    public ProfileBackup? Restore(ProfileBackup backup, string installPath)
    {
        ModInstallService.EnsureInstallNotInUse(ModInstallAction.RestoreProfiles, installPath);

        if (!SptInstallationService.TryGetServerRoot(installPath, out var serverRoot))
            throw new DirectoryNotFoundException("no SPT server was found in the install folder");

        var folder = System.IO.Path.Combine(installPath, serverRoot, "user", "profiles");

        lock (_gate)
        {
            var before = Directory.Exists(folder) ? Take(installPath, BeforeRestore, force: false) : null;

            Directory.CreateDirectory(folder);
            var full = System.IO.Path.GetFullPath(folder) + System.IO.Path.DirectorySeparatorChar;

            using var archive = ZipFile.OpenRead(backup.Path);
            foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
            {
                var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, entry.FullName));
                if (!destination.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;

                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                SafeFile.WriteAllBytes(destination, bytes.ToArray());
            }

            AppLog.Info("Profiles", $"restored {System.IO.Path.GetFileName(backup.Path)} into {folder}");
            return before;
        }
    }

    private ProfileBackup? Take(string installPath, string reason, bool force)
    {
        if (ProfilesFolder(installPath) is not { } profiles) return null;

        var files = Directory.GetFiles(profiles, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: System.IO.Path.GetRelativePath(profiles, f).Replace('\\', '/')))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0) return null;

        // Read once: what is hashed is exactly what is written, even if SPT saves in between.
        var contents = files.Select(f => (f.Relative, Bytes: File.ReadAllBytes(f.Full))).ToList();
        var hash = Hash(contents);

        var folder = FolderFor(installPath);
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "install.txt"), installPath);

        var latest = Directory.EnumerateFiles(folder, "*.zip")
            .Select(z => (Zip: z, Name: Parse(z)))
            .Where(z => z.Name is not null)
            .OrderByDescending(z => z.Name!.Value.TakenAt)
            .ThenByDescending(z => z.Zip, StringComparer.Ordinal)
            .FirstOrDefault();
        if (!force && latest.Name?.Hash == hash) return null;

        var now = DateTime.Now;
        var zipPath = System.IO.Path.Combine(folder, $"{now:yyyyMMdd-HHmmss-fff}_{reason}_{hash}.zip");
        for (var n = 2; File.Exists(zipPath); n++)
            zipPath = System.IO.Path.Combine(folder, $"{now:yyyyMMdd-HHmmss-fff}_{reason}_{hash}_{n}.zip");

        var temp = zipPath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var (relative, bytes) in contents)
            {
                using var entry = archive.CreateEntry(relative, CompressionLevel.Optimal).Open();
                entry.Write(bytes);
            }
        }

        File.Move(temp, zipPath);
        AppLog.Info("Profiles", $"backed up {contents.Count} profile file(s) before {reason}: {System.IO.Path.GetFileName(zipPath)}");

        Prune(folder);
        return new ProfileBackup(zipPath, now, reason, contents.Count, new FileInfo(zipPath).Length);
    }

    // The newest Keep stay.
    private void Prune(string folder)
    {
        var old = Directory.EnumerateFiles(folder, "*.zip")
            .Select(z => (Zip: z, Name: Parse(z)))
            .Where(z => z.Name is not null)
            .OrderByDescending(z => z.Name!.Value.TakenAt)
            .ThenByDescending(z => z.Zip, StringComparer.Ordinal)
            .Skip(Keep);

        foreach (var (zip, _) in old)
        {
            try
            {
                File.Delete(zip);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Profiles", $"couldn't remove the old profile backup {System.IO.Path.GetFileName(zip)}: {ex.Message}");
            }
        }
    }

    private static string Hash(IEnumerable<(string Relative, byte[] Bytes)> contents)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (relative, bytes) in contents)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
            sha.AppendData(BitConverter.GetBytes((long)bytes.Length));
            sha.AppendData(bytes);
        }

        return Convert.ToHexString(sha.GetHashAndReset())[..12].ToLowerInvariant();
    }

    // "<yyyyMMdd-HHmmss-fff>_<reason>_<hash>[_n].zip"
    private static (DateTime TakenAt, string Reason, string Hash)? Parse(string zip)
    {
        var parts = System.IO.Path.GetFileNameWithoutExtension(zip).Split('_');
        if (parts.Length < 3) return null;

        if (!DateTime.TryParseExact(parts[0], "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var takenAt)) return null;

        return (takenAt, parts[1], parts[2]);
    }
}
