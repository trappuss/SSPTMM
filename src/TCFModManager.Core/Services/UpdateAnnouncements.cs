using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Update notifications (docs\CLOSED-07-TCFUpdateNotifications-DESIGN.md): which updates are news.
//
// The watcher works out what has an update the same way the Installed page does, then hands the
// list here. This decides which of them to announce - once per version (D5), none at all on the
// check that sets the baseline (D6), and none for a mod whose update the user has already
// downloaded in Monitor mode (§7). Pure apart from the store, so it is tested without a timer.
//

// One installed mod or addon with an update the Installed page would show.
public sealed record UpdateCandidate(int ModId, bool IsAddon, string Name, string Version);

// A version that has been announced, so it isn't announced again.
public sealed class AnnouncedUpdate
{
    public required int ModId { get; init; }

    public bool IsAddon { get; init; }

    public required string Version { get; init; }

    public required DateTimeOffset AnnouncedAt { get; init; }
}

public sealed class UpdateNotificationState
{
    //
    // False until the first check after the feature is switched on has run. That check records
    // every update already available and announces none of them (D6) - switching it on shouldn't
    // unload twenty toasts about updates the Installed page already shows.
    //
    public bool BaselineTaken { get; set; }

    // At most one entry per (ModId, IsAddon): the newest version announced for it.
    public List<AnnouncedUpdate> Announced { get; init; } = [];
}

// What one check decided: the updates to announce, in the order they arrived, and the state to keep.
public sealed record UpdateAnnouncement(IReadOnlyList<UpdateCandidate> New, UpdateNotificationState State);

public static class UpdateAnnouncer
{
    //
    // <paramref name="available"/> is every installed mod with an update right now. The state that
    // comes back holds exactly those - an entry for a mod that no longer has an update (it was
    // installed, or removed) is dropped, so the same version reappearing later is news again.
    //
    // <paramref name="pendingDownloads"/> is Monitor mode's Pending ledger. A mod downloaded but not
    // yet confirmed still reports its old version, so without this the user would be told about the
    // update they have just downloaded. Skipped, and not recorded, while that download is at or
    // above the version on offer; once it is confirmed or dismissed it stops being pending.
    //
    public static UpdateAnnouncement Pick(
        IReadOnlyList<UpdateCandidate> available,
        UpdateNotificationState state,
        IEnumerable<DownloadedModRecord> pendingDownloads,
        DateTimeOffset now)
    {
        var pending = pendingDownloads
            .GroupBy(d => (d.ModId, d.IsAddon))
            .ToDictionary(g => g.Key, g => g.Last());

        var previous = state.Announced
            .GroupBy(a => (a.ModId, a.IsAddon))
            .ToDictionary(g => g.Key, g => g.Last());

        var fresh = new List<UpdateCandidate>();
        var kept = new List<AnnouncedUpdate>();

        foreach (var candidate in available.DistinctBy(c => (c.ModId, c.IsAddon)))
        {
            var key = (candidate.ModId, candidate.IsAddon);

            if (pending.TryGetValue(key, out var download) && CoversCandidate(download.Version, candidate.Version))
            {
                if (previous.TryGetValue(key, out var old)) kept.Add(old);
                continue;
            }

            if (previous.TryGetValue(key, out var announced)
                && string.Equals(announced.Version, candidate.Version, StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(announced);
                continue;
            }

            if (state.BaselineTaken) fresh.Add(candidate);

            kept.Add(new AnnouncedUpdate
            {
                ModId = candidate.ModId,
                IsAddon = candidate.IsAddon,
                Version = candidate.Version,
                AnnouncedAt = now,
            });
        }

        return new UpdateAnnouncement(fresh, new UpdateNotificationState { BaselineTaken = true, Announced = kept });
    }

    // A pending download at the candidate's version or newer. Unparsable versions only count when
    // they are the same string - a guess the other way would hide a real update.
    private static bool CoversCandidate(string downloaded, string candidate) =>
        string.Equals(downloaded, candidate, StringComparison.OrdinalIgnoreCase)
        || ModVersionComparer.IsUpdateAvailable(downloaded, candidate) == false;
}

//
// Loads/saves UpdateNotificationState as <app folder>\Data\update_notifications.json. A corrupt
// file reads as a fresh state with the baseline not yet taken, so the worst it costs is one silent
// check - never a burst of repeats.
//
public sealed class UpdateNotificationStore
{
    private readonly string _filePath;

    public UpdateNotificationStore(string? filePath = null)
    {
        // Optional path for testability, matching ModListStore's accepted deviation.
        _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "update_notifications.json");
    }

    public UpdateNotificationState Load()
    {
        if (!File.Exists(_filePath)) return new UpdateNotificationState();

        try
        {
            return JsonSerializer.Deserialize<UpdateNotificationState>(File.ReadAllText(_filePath))
                ?? new UpdateNotificationState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            if (ex is JsonException) SafeFile.PreserveDamaged(_filePath);
            return new UpdateNotificationState();
        }
    }

    public void Save(UpdateNotificationState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        SafeFile.WriteText(_filePath, json, keepBackups: true);
    }

    //
    // Called when the feature is switched on. The versions already announced are kept (switching
    // off and on again doesn't repeat them, §8), but the next check is a baseline again (D6).
    //
    public void ResetBaseline()
    {
        var state = Load();
        state.BaselineTaken = false;
        Save(state);
    }
}
