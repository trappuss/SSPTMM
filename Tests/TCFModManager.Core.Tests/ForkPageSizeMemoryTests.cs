using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (SSPTMM): the Per page each page opens at - PageSizeMemory.
public class ForkPageSizeMemoryTests
{
    private static readonly int[] Options = [PageSizeMemory.Infinite, 10, 15, 30, 50];

    [Fact]
    public void A_page_nothing_was_picked_on_opens_on_infinite()
    {
        Assert.Equal(PageSizeMemory.Infinite, PageSizeMemory.Resolve(new AppSettings(), PageSizeMemory.Browse, null, Options));
    }

    [Fact]
    public void A_size_saved_as_the_page_default_is_still_used_until_one_is_picked()
    {
        Assert.Equal(15, PageSizeMemory.Resolve(new AppSettings(), PageSizeMemory.Browse, 15, Options));
    }

    [Fact]
    public void The_size_last_picked_wins_over_a_saved_default_including_infinite()
    {
        var settings = new AppSettings();
        PageSizeMemory.Remember(settings, PageSizeMemory.Browse, PageSizeMemory.Infinite);

        Assert.Equal(PageSizeMemory.Infinite, PageSizeMemory.Resolve(settings, PageSizeMemory.Browse, 15, Options));

        PageSizeMemory.Remember(settings, PageSizeMemory.Browse, 50);
        Assert.Equal(50, PageSizeMemory.Resolve(settings, PageSizeMemory.Browse, 15, Options));
    }

    [Fact]
    public void Each_page_keeps_its_own_size()
    {
        var settings = new AppSettings();
        PageSizeMemory.Remember(settings, PageSizeMemory.Browse, 50);

        Assert.Equal(PageSizeMemory.Infinite, PageSizeMemory.Resolve(settings, PageSizeMemory.SubscribedItems, null, Options));
    }

    [Theory]
    [InlineData(12, null, PageSizeMemory.Infinite)] // a size this page doesn't offer, nothing saved
    [InlineData(12, 30, 30)]                        // ... falls back to the saved default
    [InlineData(12, 7, PageSizeMemory.Infinite)]    // ... unless that isn't offered either
    public void A_size_the_page_does_not_offer_is_passed_over(int picked, int? saved, int expected)
    {
        var settings = new AppSettings();
        settings.PageSizes[PageSizeMemory.Browse] = picked;

        Assert.Equal(expected, PageSizeMemory.Resolve(settings, PageSizeMemory.Browse, saved, Options));
    }

    [Fact]
    public void Picking_the_size_already_kept_changes_nothing()
    {
        var settings = new AppSettings();

        Assert.True(PageSizeMemory.Remember(settings, PageSizeMemory.AuthorItems, 18));
        Assert.False(PageSizeMemory.Remember(settings, PageSizeMemory.AuthorItems, 18));
        Assert.True(PageSizeMemory.Remember(settings, PageSizeMemory.AuthorItems, PageSizeMemory.Infinite));
    }

    [Fact]
    public void The_sizes_survive_settings_json_and_an_older_file_without_them_reads_as_none()
    {
        var settings = new AppSettings();
        settings.PageSizes[PageSizeMemory.CollectionsBrowse] = 30;
        settings.PageSizes[PageSizeMemory.SubscribedItems] = PageSizeMemory.Infinite;

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(30, back.PageSizes[PageSizeMemory.CollectionsBrowse]);
        Assert.Equal(PageSizeMemory.Infinite, back.PageSizes[PageSizeMemory.SubscribedItems]);

        var older = JsonSerializer.Deserialize<AppSettings>("{\"SptInstallPath\":\"C:\\\\SPT\"}")!;
        Assert.Empty(older.PageSizes);
    }
}
