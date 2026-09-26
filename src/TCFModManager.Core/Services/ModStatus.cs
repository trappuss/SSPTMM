namespace TCFModManager.Core.Services;

// 
// How a mod stands against what's installed. Shared by Browse cards, the Installed page and the
// Dependencies page so one situation always looks the same wherever it's shown.
// 
public enum ModStatus
{
    // Present, and at or above the version wanted.
    Installed,

    // Present, but a newer compatible version is published.
    UpdateAvailable,

    // Not on disk.
    NotInstalled,

    // Nothing published fits the installed SPT version.
    NoCompatibleVersion,

    // Two mods need versions of this that can't both be satisfied.
    Conflict,

    // On disk, but whether anything newer exists couldn't be determined - no catalog match,
    // or a version string neither side could parse.
    Unknown,

    // On disk, but moved out of the folder SPT loads from, so nothing loads it.
    Disabled,

    // On disk, but newer than the newest version that fits what needs it (and this SPT) - a major
    // version past what the mod depending on it was made for, most often.
    TooNew,
}

// The icon for a <see cref="ModStatus"/>, kept in one place so the pages can't drift apart.
// The sentence that goes with it is the App's - see ModStatusWording.
public static class ModStatusDisplay
{
    // Fluent icon name, matching a SymbolRegular member in the WPF-UI build in use.
    public static string Glyph(ModStatus status) => status switch
    {
        ModStatus.Installed => "CheckmarkCircle24",
        ModStatus.UpdateAvailable => "ArrowCircleUp24",
        ModStatus.NotInstalled => "DismissCircle24",
        ModStatus.NoCompatibleVersion => "QuestionCircle24",
        ModStatus.Unknown => "QuestionCircle24",
        ModStatus.Disabled => "PlugDisconnected24",
        ModStatus.TooNew => "Warning24",
        _ => "ErrorCircle24",
    };
}
