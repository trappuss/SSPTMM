using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Monitor mode's alternative to ModInstallService.InstallAsync: fetch a version's Forge archive into
// a folder the user chose, and record what it would place - without touching the SPT install.
//
// It shares the download with the installer (progress, throttling, the short-read check) and the
// layout rules through ArchiveLayout, and nothing else. No work folder in the install, no
// running-SPT guard, no manifest record: the only files written are the archive and one entry in
// the download ledger.
//
public sealed class ModArchiveService(ModDownloadService downloadService, DownloadLedgerService ledger)
{
    private const string PartExtension = ".part";

    //
    // Saves <paramref name="version"/> of <paramref name="target"/> into <paramref name="folder"/>, or
    // into a subfolder of it named <paramref name="subfolder"/> (a mod list's name) when one is given.
    //
    // The folder itself must exist; the subfolder is created. <paramref name="installPath"/> is only
    // read, to find where the install keeps user\mods so the recorded file list matches what a hand
    // install would produce - null records the archive's layout as it stands.
    //
    // The archive downloads under a temporary ".part" name and is renamed only once complete, so a
    // cancelled or failed download never leaves something that looks like a finished zip.
    //
    public async Task<ModDownloadResult> SaveArchiveAsync(
        InstallTarget target,
        ModVersion version,
        string folder,
        string? installPath,
        string? subfolder = null,
        IProgress<double>? downloadProgress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version.Link))
            throw new ModInstallException(ModInstallFailure.NoDownloadLink)
            {
                ModName = target.Name,
                Version = version.Version,
            };

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new ModInstallException(ModInstallFailure.DownloadFolderMissing) { Folder = folder };

        var destinationFolder = string.IsNullOrWhiteSpace(subfolder)
            ? folder
            : Path.Combine(folder, DownloadFolders.SafeName(subfolder));

        var partPath = CreatePartFile(destinationFolder);

        AppLog.Info("Download",
            $"{target.Name} {version.Version} ({(target.IsAddon ? "addon" : "mod")} {target.Id}) -> {destinationFolder}");

        string archivePath;

        try
        {
            var response = await downloadService
                .DownloadAsync(version.Link, partPath, downloadProgress, ct)
                .ConfigureAwait(false);

            var fileName = DownloadFolders.FileNameFor(
                response.OfferedFileName, target.Name, version.Version, ArchiveLayout.Detect(partPath));

            archivePath = MoveIntoPlace(partPath, destinationFolder, fileName);
        }
        catch (Exception ex)
        {
            TryDelete(partPath);

            if (ex is OperationCanceledException)
                AppLog.Info("Download", $"{target.Name} {version.Version} cancelled");
            else
                AppLog.Error("Download", $"{target.Name} {version.Version} failed", ex);

            throw;
        }

        SptInstallationService.TryGetServerRoot(installPath, out var serverRoot);

        var plan = ArchiveLayout.Plan(archivePath, serverRoot);

        var record = new DownloadedModRecord
        {
            ModId = target.Id,
            IsAddon = target.IsAddon,
            Guid = target.Guid,
            Name = target.Name,
            VersionId = version.Id,
            Version = version.Version ?? "unknown",
            DownloadedAt = DateTimeOffset.UtcNow,
            ArchivePath = archivePath,
            ExpectedFolders = [.. plan.Folders],
            ExpectedFiles = [.. plan.Files.Select(f => new ExpectedFile(f.Path, f.Size))],
            Unrecognised = !plan.Recognised,
        };

        ledger.Record(record);

        AppLog.Info("Download",
            $"{target.Name} {version.Version} saved as {archivePath}; " +
            (plan.Recognised
                ? $"{plan.Files.Count} file(s) in folders [{string.Join(", ", plan.Folders)}]"
                : "no known root folder"));

        return new ModDownloadResult(record);
    }

    //
    // Creates the temporary file up front, so a folder that can't be written to is reported before
    // anything is fetched rather than after the whole archive has arrived.
    //
    private static string CreatePartFile(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + PartExtension);
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { }
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ModInstallException(ModInstallFailure.DownloadFolderNotWritable, ex) { Folder = folder };
        }
    }

    //
    // Renames the finished download to its real name. The name is re-checked on a collision rather
    // than trusted from one look, since something else may land in a Downloads folder in between.
    //
    private static string MoveIntoPlace(string partPath, string folder, string fileName)
    {
        for (var attempt = 0; ; attempt++)
        {
            var target = DownloadFolders.UniquePath(folder, fileName);

            try
            {
                File.Move(partPath, target, overwrite: false);
                return target;
            }
            catch (IOException) when (attempt < 5 && File.Exists(target))
            {
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// What a download-only save produced: the ledger entry, which carries where the archive went.
public sealed record ModDownloadResult(DownloadedModRecord Record);
