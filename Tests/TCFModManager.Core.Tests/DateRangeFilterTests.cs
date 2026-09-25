using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class DateRangeFilterTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int month, int day, int hour = 12) => new(2026, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_at_both_ends_keeps_everything_even_undated()
    {
        Assert.True(DateRangeFilter.Contains(null, null, null, Utc));
        Assert.True(DateRangeFilter.Contains(At(1, 1), null, null, Utc));
    }

    [Fact]
    public void An_undated_mod_drops_out_once_a_range_is_set()
    {
        Assert.False(DateRangeFilter.Contains(null, new DateTime(2026, 1, 1), null, Utc));
    }

    [Fact]
    public void The_and_day_counts_in_full()
    {
        var before = new DateTime(2026, 9, 24);

        Assert.True(DateRangeFilter.Contains(At(9, 24, 23), null, before, Utc));
        Assert.False(DateRangeFilter.Contains(At(9, 25, 0), null, before, Utc));
    }

    [Fact]
    public void The_between_day_starts_at_midnight()
    {
        var after = new DateTime(2026, 9, 24);

        Assert.True(DateRangeFilter.Contains(At(9, 24, 0), after, null, Utc));
        Assert.False(DateRangeFilter.Contains(At(9, 23, 23), after, null, Utc));
    }

    [Fact]
    public void Days_are_the_pcs_own()
    {
        // 23:30 UTC on the 23rd is already the 24th two hours east.
        var east = TimeZoneInfo.CreateCustomTimeZone("east", TimeSpan.FromHours(2), "east", "east");
        var when = new DateTimeOffset(2026, 9, 23, 23, 30, 0, TimeSpan.Zero);

        Assert.True(DateRangeFilter.Contains(when, new DateTime(2026, 9, 24), null, east));
        Assert.False(DateRangeFilter.Contains(when, new DateTime(2026, 9, 24), null, Utc));
    }
}
