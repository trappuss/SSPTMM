using System.Text.Json;
using System.Text.Json.Nodes;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork: the Steam Workshop fork kept its install records per SPT install, under
// Data\InstallRecords\<key>\installed-mods.json with install.txt naming the install, from 2026-09-26
// until the merge with 1.19.0-beta. 1.19 keeps one list (Data\installed-mods.json) with each record
// stamped with the install it was made in - so on the first start after the merge those records are
// moved into it, stamped, or every mod the fork installed would show as installed by hand.
//
// 1.19 holds one record per mod (ModId + IsAddon). The install the app is set to goes first and wins
// over a record already in the list for that mod; another install's record goes in only where the
// list has none for that mod yet. One left out is logged, and stays in the folder.
//
// The folder is then renamed to InstallRecords.before-1.19 rather than deleted: it also holds the
// copies of files the fork's installs replaced (ReplacedFiles), which 1.19 has no record of and
// cannot put back by itself.
//
public static class ForkInstallRecordsMigration
{
    public const string FolderName = "InstallRecords";
    public const string MovedFolderName = "InstallRecords.before-1.19";

    private const string RecordsFileName = "installed-mods.json";
    private const string InstallNameFile = "install.txt";

    // Returns how many records were moved in; 0 when there was nothing to do.
    public static int Run(string dataDirectory, string? currentInstall)
    {
        var root = Path.Combine(dataDirectory, FolderName);
        if (!Directory.Exists(root)) return 0;

        var folders = new List<(string Install, string File)>();
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var name = Path.Combine(folder, InstallNameFile);
            var file = Path.Combine(folder, RecordsFileName);
            if (!File.Exists(name) || !File.Exists(file)) continue;

            var install = File.ReadAllText(name).Trim();
            if (install.Length == 0) continue;

            folders.Add((install, file));
        }

        // The install the app is set to first: its records are the ones on screen.
        var current = string.IsNullOrWhiteSpace(currentInstall) ? null : InstallStamp.Of(currentInstall);
        folders = [.. folders.OrderBy(f => current is not null && string.Equals(InstallStamp.Of(f.Install), current, StringComparison.OrdinalIgnoreCase) ? 0 : 1)];

        var manifestService = new ModInstallManifestService(Path.Combine(dataDirectory, RecordsFileName));
        var manifest = manifestService.Load();
        var moved = 0;

        foreach (var (install, file) in folders)
        {
            var stamp = InstallStamp.Of(install);
            var isCurrent = current is not null && string.Equals(stamp, current, StringComparison.OrdinalIgnoreCase);

            List<InstalledModRecord> records;
            try
            {
                records = ReadStamped(file, stamp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Warn("Records", $"couldn't read the fork's records for {install}: {ex.Message}");
                continue;
            }

            foreach (var record in records)
            {
                var existing = manifest.Find(record.ModId, record.IsAddon);
                if (existing is not null && !isCurrent)
                {
                    AppLog.Info("Records", $"{record.Name}: the fork's record for {install} left in {MovedFolderName} - the list already has one for {existing.InstallPath ?? "an install it doesn't name"}");
                    continue;
                }

                if (existing is not null) manifest.Mods.Remove(existing);
                manifest.Mods.Add(record);
                moved++;
            }
        }

        manifestService.Save(manifest);

        var target = Path.Combine(dataDirectory, MovedFolderName);
        for (var n = 2; Directory.Exists(target); n++) target = Path.Combine(dataDirectory, $"{MovedFolderName} ({n})");
        Directory.Move(root, target);

        AppLog.Info("Records", $"moved {moved} record(s) from the fork's per-install lists into {RecordsFileName}; the old folder is {Path.GetFileName(target)}");
        return moved;
    }

    // Each record with its install stamped on, unless it already names one.
    private static List<InstalledModRecord> ReadStamped(string file, string stamp)
    {
        var root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
        if (root?["Mods"] is not JsonArray mods) return [];

        var records = new List<InstalledModRecord>();
        foreach (var node in mods.OfType<JsonObject>())
        {
            if (node["InstallPath"] is null || node["InstallPath"]!.GetValueKind() != JsonValueKind.String)
                node["InstallPath"] = stamp;

            if (node.Deserialize<InstalledModRecord>() is { } record) records.Add(record);
        }

        return records;
    }
}
