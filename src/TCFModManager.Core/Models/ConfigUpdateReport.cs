using TCFModManager.Core.Services;

namespace TCFModManager.Core.Models;

//
// What an update did to each of a server mod's config files. Core states what happened; the App
// words it (see App/Services/ConfigUpdateWording).
//
public sealed record ConfigUpdateReport
{
    public required int ModId { get; init; }

    public bool IsAddon { get; init; }

    public required string ModName { get; init; }

    // Null on a first install.
    public string? FromVersion { get; init; }

    public required string ToVersion { get; init; }

    public required DateTimeOffset At { get; init; }

    // The policy the update ran under - see ModConfigPolicy. Merge unless the user set otherwise.
    public ModConfigPolicy Policy { get; init; } = ModConfigPolicy.Merge;

    // Where the files as they stood before the update were copied. Null when nothing was there.
    public string? ArchiveFolder { get; init; }

    public List<ConfigFileOutcome> Files { get; init; } = [];

    // True when at least one file lost something the user had there - what the App leads with.
    public bool AnyReplaced => Files.Any(f => f.Kind == ConfigOutcomeKind.Replaced);
}

public sealed record ConfigFileOutcome
{
    // Install-relative, forward slashes.
    public required string Path { get; init; }

    public required ConfigOutcomeKind Kind { get; init; }

    // Set on Replaced, and on a Kind that fell back from a merge.
    public ConfigReplaceReason? Reason { get; init; }

    // Merge detail, by dotted path ("bots.pmc.difficulty"). Empty outside a merge.
    public List<string> Carried { get; init; } = [];

    public List<string> Dropped { get; init; } = [];

    public List<string> UserAdded { get; init; } = [];

    // Fork: true when the file on disk is now the archive's own copy - placed as it came (Added,
    // Replaced, DefaultsUpdated) or already the same bytes (Unchanged). The other outcomes leave
    // something else there on purpose: the user's merge, their file kept, a file that couldn't be
    // copied aside, a preset left alone, or a file the new version no longer ships.
    public bool IsArchivesCopy => Kind is ConfigOutcomeKind.Added or ConfigOutcomeKind.Unchanged
        or ConfigOutcomeKind.DefaultsUpdated or ConfigOutcomeKind.Replaced;
}

public enum ConfigOutcomeKind
{
    // Nothing was there before - a first install of this file.
    Added,

    // The file on disk was byte-identical to the new one.
    Unchanged,

    // The file on disk matched what the old version shipped, so the new defaults were taken and
    // nothing of the user's was lost.
    DefaultsUpdated,

    // The new file replaced one the user had changed (or one that can't be shown to be unchanged).
    // Their copy is in the report's ArchiveFolder.
    Replaced,

    // The new defaults with the user's changed values carried into them.
    Merged,

    // The user's file was left as it was, by their per-mod choice.
    KeptMine,

    // The old version had this file and the new one doesn't ship it. Archived, not restored.
    Removed,

    // The file could not be copied aside first, so it was left untouched rather than risk losing it.
    NotUpdated,

    // A user-data file (a preset) that the update leaves exactly where it is.
    Preserved,
}

public enum ConfigReplaceReason
{
    // Nothing recorded what the old version shipped, so a user edit can't be told from a changed
    // default. Clears itself after one update cycle.
    NoBaseline,

    // The mod's config policy is Take new.
    TakeNewPolicy,

    // The file isn't JSON the merge can read.
    NotMergeable,

    // Too large or too many values to be settings rather than data.
    TooLarge,

    // The merge threw; the new file was taken instead.
    MergeFailed,
}
