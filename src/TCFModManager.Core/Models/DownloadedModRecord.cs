using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

//
// A mod archive saved for the user to install by hand (Monitor mode), and what it would place.
//
// Kept apart from installed-mods.json on purpose: that file is a record of what is on disk, and a
// downloaded archive is not an install. A download only reaches the manifest once a scan finds it
// installed and the user confirms it.
//
public sealed class DownloadedModRecord
{
    public required int ModId { get; init; }

    // Same meaning as InstalledModRecord.IsAddon - identity is the (ModId, IsAddon) pair.
    public bool IsAddon { get; init; }

    public string? Guid { get; init; }

    public required string Name { get; init; }

    public int? VersionId { get; init; }

    public required string Version { get; init; }

    public required DateTimeOffset DownloadedAt { get; init; }

    // The saved archive. Never deleted by the app - it is the user's file in the user's folder.
    public required string ArchivePath { get; init; }

    // The mod folders the archive would place, the same names InstalledModRecord.Folders holds.
    public List<string> ExpectedFolders { get; init; } = [];

    // Every file the archive would place, install-relative and forward-slash, with its size.
    public List<ExpectedFile> ExpectedFiles { get; init; } = [];

    //
    // True when the archive had no known root folder, couldn't be read, or held an entry escaping its
    // own root. The download still stands - the user may know better than the app - but there is
    // nothing to compare the disk against, so it never matches a scan.
    //
    public bool Unrecognised { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<DownloadState>))]
    public DownloadState State { get; set; } = DownloadState.Pending;
}

public sealed record ExpectedFile(string Path, long Size);

public enum DownloadState
{
    // Saved, not yet seen installed and confirmed.
    Pending,

    // Seen installed and confirmed; the manifest now holds its record.
    Confirmed,

    // Seen installed and declined, so that version isn't asked about again.
    Dismissed,
}

// Every download-only save for one install.
public sealed class DownloadLedger
{
    public List<DownloadedModRecord> Downloads { get; init; } = [];
}
