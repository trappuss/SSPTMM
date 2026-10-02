using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

//
// Update notifications (docs\CLOSED-07-TCFUpdateNotifications-DESIGN.md §8): a Windows notification when
// an installed mod gets a new release, checked on a timer while the app runs. One object in
// settings.json, the same as Monitor and ServerMap, so the feature reads as one block to anyone
// hand-editing the file.
//
public sealed class UpdateNotificationSettings
{
    // Off by default (R1). Switching it on takes the D6 baseline, so nothing already on the
    // Installed page is announced.
    public bool Enabled { get; set; }

    // How often the check runs. A name rather than a number, like Theme, so the file stays readable.
    [JsonConverter(typeof(JsonStringEnumConverter<UpdateCheckInterval>))]
    public UpdateCheckInterval Interval { get; set; } = UpdateCheckInterval.OneHour;

    //
    // Closing the window hides it to the tray instead of quitting, so the checks carry on (§8a).
    // Off by default (D8): an app that stays running after you close it is a surprise nobody opted
    // into. Only honoured while Enabled is on - see KeepsRunningInTray.
    //
    public bool KeepRunningInTray { get; set; }

    // Whether the one-time "still running in the tray" notification (D9) has been shown.
    public bool TrayNoticeShown { get; set; }

    // Hiding to the tray is for the checks; with notifications off there is nothing to stay for.
    [JsonIgnore]
    public bool KeepsRunningInTray => Enabled && KeepRunningInTray;
}

//
// The choices on the Options page (§5). Nothing below 30 minutes: mods aren't released that often,
// and every check is a request to somebody else's server.
//
public enum UpdateCheckInterval
{
    ThirtyMinutes,
    OneHour,
    ThreeHours,
    SixHours,
    TwelveHours,
}

public static class UpdateCheckIntervals
{
    // An unknown value - a hand-edited number - reads as the default rather than as a tight loop.
    public static TimeSpan ToTimeSpan(this UpdateCheckInterval interval) => interval switch
    {
        UpdateCheckInterval.ThirtyMinutes => TimeSpan.FromMinutes(30),
        UpdateCheckInterval.ThreeHours => TimeSpan.FromHours(3),
        UpdateCheckInterval.SixHours => TimeSpan.FromHours(6),
        UpdateCheckInterval.TwelveHours => TimeSpan.FromHours(12),
        _ => TimeSpan.FromHours(1),
    };
}
