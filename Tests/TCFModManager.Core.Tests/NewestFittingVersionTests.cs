using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// An addon on a collection gets the newest version that fits its parent (1.3.0, from TCF 448af87).
public class NewestFittingVersionTests
{
    private static AddonVersion Addon(string version, string? parent, int day) => new()
    {
        Version = version,
        ModVersionConstraint = parent,
        PublishedAt = new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void ForParent_TakesTheNewestTheParentSatisfies()
    {
        var versions = new[] { Addon("1.0.0", "^1.0.0", 1), Addon("2.0.0", "^2.0.0", 5) };

        Assert.Equal("1.0.0", NewestFittingVersion.ForParent(versions, "1.4.0")?.Version);
        Assert.Equal("2.0.0", NewestFittingVersion.ForParent(versions, "2.1.0")?.Version);
        Assert.Null(NewestFittingVersion.ForParent(versions, "3.0.0"));
    }

    [Fact]
    public void ForParent_DoesntRuleOutAVersionWithNoConstraint() =>
        Assert.Equal("1.1.0", NewestFittingVersion.ForParent([Addon("1.0.0", "^1.0.0", 1), Addon("1.1.0", null, 2)], "1.4.0")?.Version);

    [Fact]
    public void ForParent_TakesTheNewest_WhenTheParentsVersionIsUnknown() =>
        Assert.Equal("2.0.0", NewestFittingVersion.ForParent([Addon("1.0.0", "^1.0.0", 1), Addon("2.0.0", "^2.0.0", 2)], null)?.Version);

    [Fact]
    public void ForParent_NothingPublished_IsNull() =>
        Assert.Null(NewestFittingVersion.ForParent([], "1.0.0"));
}
