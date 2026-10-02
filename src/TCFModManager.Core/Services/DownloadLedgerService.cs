using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Loads/saves the DownloadLedger under <app folder>\Data\downloads.json, the same way
// ModInstallManifestService handles installed-mods.json. A corrupt file falls back to an empty
// ledger: the archives are still in the user's folder, and the worst case is a hand install that
// goes unconfirmed until the mod is downloaded again.
//
// One entry per (ModId, IsAddon). A newer download replaces the older one whatever its state, since
// only the newest can be what is on disk now - and that replacement is the only pruning there is.
//
public sealed class DownloadLedgerService
{
    private readonly string _filePath;

    public DownloadLedgerService(string? filePath = null)
    {
        // Optional path for testability, matching ModListStore's accepted deviation.
        _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "downloads.json");
    }

    public DownloadLedger Load()
    {
        if (!File.Exists(_filePath)) return new DownloadLedger();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<DownloadLedger>(json) ?? new DownloadLedger();
        }
        catch (JsonException)
        {
            SafeFile.PreserveDamaged(_filePath);
            return new DownloadLedger();
        }
    }

    public void Save(DownloadLedger ledger)
    {
        var json = JsonSerializer.Serialize(ledger, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteText(_filePath, json, keepBackups: true);
    }

    // Adds a download, replacing any earlier entry for the same mod or addon.
    public void Record(DownloadedModRecord record)
    {
        var ledger = Load();
        ledger.Downloads.RemoveAll(d => d.ModId == record.ModId && d.IsAddon == record.IsAddon);
        ledger.Downloads.Add(record);
        Save(ledger);
    }

    public DownloadedModRecord? Find(int modId, bool isAddon) =>
        Load().Downloads.FirstOrDefault(d => d.ModId == modId && d.IsAddon == isAddon);

    public List<DownloadedModRecord> Pending() =>
        [.. Load().Downloads.Where(d => d.State == DownloadState.Pending)];

    //
    // Moves one download to Confirmed or Dismissed. Keyed on the version as well as the mod, so a
    // prompt answered after a newer download has replaced the entry doesn't settle the new one.
    // Returns false when there was no such entry.
    //
    public bool SetState(int modId, bool isAddon, string version, DownloadState state)
    {
        var ledger = Load();
        var entry = ledger.Downloads.FirstOrDefault(d =>
            d.ModId == modId && d.IsAddon == isAddon && string.Equals(d.Version, version, StringComparison.Ordinal));
        if (entry is null) return false;

        entry.State = state;
        Save(ledger);
        return true;
    }
}
