namespace TCFModManager.Core.Services;

//
// Browse's Filter by Date: is a moment within "between this day and that day"? Days are whole
// days in the PC's own time zone, the "and" day included; either end may be left open. A mod with
// no date at all is only kept while both ends are open.
//
public static class DateRangeFilter
{
    public static bool Contains(DateTimeOffset? when, DateTime? after, DateTime? before, TimeZoneInfo? zone = null)
    {
        if (after is null && before is null) return true;
        if (when is null) return false;

        var local = TimeZoneInfo.ConvertTime(when.Value, zone ?? TimeZoneInfo.Local).DateTime;
        return (after is null || local >= after.Value.Date)
            && (before is null || local < before.Value.Date.AddDays(1));
    }
}
