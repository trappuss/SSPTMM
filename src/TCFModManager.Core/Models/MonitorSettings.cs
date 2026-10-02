using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

//
// Monitor mode: saving a mod's Forge archive for the user to install by hand, instead of installing
// it. One object in settings.json, the same as ServerMap, so the whole feature reads as one block to
// anyone hand-editing the file.
//
// The folder, confirmation and subfolder settings apply to every download-only item, including one
// started from the per-mod action while InstallMode is Install.
//
public sealed class MonitorSettings
{
    // What the main Install / Update button does. The per-mod action beside it does the other.
    [JsonConverter(typeof(JsonStringEnumConverter<InstallMode>))]
    public InstallMode InstallMode { get; set; } = InstallMode.Install;

    //
    // Where archives are saved. NULL MEANS THE WINDOWS DOWNLOADS FOLDER, resolved when it is used
    // rather than stored, so a user who moves their Downloads folder is followed rather than left
    // pointing at the old one - see DownloadFolders.
    //
    public string? DownloadFolder { get; set; }

    // What happens when a scan finds a downloaded mod installed by hand.
    [JsonConverter(typeof(JsonStringEnumConverter<DownloadConfirmation>))]
    public DownloadConfirmation DownloadConfirmation { get; set; } = DownloadConfirmation.Ask;

    // Whether a mod list's downloads go into a subfolder named after the list.
    public bool DownloadListSubfolders { get; set; } = true;
}

public enum InstallMode
{
    Install,
    DownloadOnly,
}

public enum DownloadConfirmation
{
    // One prompt per scan listing every download that now looks installed.
    Ask,

    // No prompt: the card shows a quiet status with a Confirm action.
    MarkQuietly,
}
