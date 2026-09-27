using System.Text.Json;
using TCFModManager.Core.Models;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// "Save as default" on Subscribed items and Browse, as settings.json keeps it: every field comes
// back, and a file from before a setting existed still opens the page at the app's own default.
//
public class PageDefaultsTests
{
    [Fact]
    public void InstalledDefaults_RoundTripEveryField()
    {
        var saved = new AppSettings
        {
            InstalledDefaults = new InstalledPageDefaults
            {
                ViewMode = "List",
                UpdateStatus = "NeedsUpdate",
                Enabled = "EnabledOnly",
                Category = "Bots",
                Group = "ungrouped",
                Sort = "AuthorAscending",
                GroupSort = "Category",
                Grouping = "Category",
                PageSize = 24,
                Attributes = ["FikaCompatible"],
            },
        };

        var read = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved))!.InstalledDefaults!;

        Assert.Equal("List", read.ViewMode);
        Assert.Equal("NeedsUpdate", read.UpdateStatus);
        Assert.Equal("EnabledOnly", read.Enabled);
        Assert.Equal("Bots", read.Category);
        Assert.Equal("ungrouped", read.Group);
        Assert.Equal("AuthorAscending", read.Sort);
        Assert.Equal("Category", read.GroupSort);
        Assert.Equal("Category", read.Grouping);
        Assert.Equal(24, read.PageSize);
        Assert.Equal(["FikaCompatible"], read.Attributes);
    }

    [Fact]
    public void InstalledDefaults_SavedBeforeGroupingExisted_HaveNoGrouping()
    {
        // Null is "not saved": the page opens Not grouped, as it always did.
        var read = JsonSerializer.Deserialize<AppSettings>(
            """{ "InstalledDefaults": { "ViewMode": "Cards", "GroupSort": "Manual", "PageSize": 12 } }""")!.InstalledDefaults!;

        Assert.Null(read.Grouping);
        Assert.Equal("Cards", read.ViewMode);
        Assert.Equal(12, read.PageSize);
        Assert.Empty(read.Attributes);
    }

    [Fact]
    public void NoDefaultsSaved_IsNull()
    {
        var read = JsonSerializer.Deserialize<AppSettings>("""{ "SptInstallPath": "C:\\SPT" }""")!;

        Assert.Null(read.InstalledDefaults);
        Assert.Null(read.BrowseDefaults);
    }
}
