using TCFModManager.App.Localization;

namespace TCFModManager.App.Services;

//
// Dates written the way Steam's item pages write them: "Sep 24 @ 2:34AM" within this year,
// "Mar 3, 2021 @ 5:12PM" before it - in this PC's time zone, not the UTC the API sends.
//
public static class SteamDates
{
    public static string? Short(DateTimeOffset? value)
    {
        if (value is not { } date) return null;

        var local = date.ToLocalTime();
        return LocalizationService.Text(
            local.Year == DateTimeOffset.Now.Year ? Strings.Item_DateThisYearFormat : Strings.Item_DateFormat,
            local);
    }

    /// <summary>The full date and time, for a tooltip.</summary>
    public static string? Full(DateTimeOffset? value) =>
        value is { } date ? LocalizationService.Text("{0:F}", date.ToLocalTime()) : null;
}
