using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SptReleasesTests
{
    // The real published list as of 2026-08-20, trimmed to what matters here.
    private static readonly List<SptRelease> Releases = SptReleases.FromApi(
    [
        new SptVersion { Version = "4.1.2" },
        new SptVersion { Version = "4.1.1" },
        new SptVersion { Version = "4.1.0" },
        new SptVersion { Version = "4.0.13" },
        new SptVersion { Version = "4.0.12" },
        new SptVersion { Version = "4.0.4" },
        new SptVersion { Version = "4.0.0" },
        new SptVersion { Version = "3.11.4" },
        new SptVersion { Version = "3.10.5" },
        new SptVersion { Version = "3.9.8" },
        new SptVersion { Version = "3.8.0" },
    ]);

    [Fact]
    public void FromApi_DropsReleasesBelowTheFloorAndSortsNewestFirst()
    {
        Assert.Equal("4.1.2", Releases[0].Label);
        Assert.DoesNotContain(Releases, r => r.Label == "3.9.8");
        Assert.DoesNotContain(Releases, r => r.Label == "3.8.0");
        Assert.Contains(Releases, r => r.Label == "3.10.5");
    }

    [Fact]
    public void Lines_AreDistinctAndNewestFirst() =>
        Assert.Equal([(4, 1), (4, 0), (3, 11), (3, 10)], SptReleases.Lines(Releases));

    [Theory]
    // ScopeRangefinder's "~4.0.4" spans 4.0.4 upward, and 4.0.13 is the newest release in it -
    // naming "4.0.4" was misleading because that is a boundary, not what anyone runs.
    [InlineData("~4.0.4", 4, 0, "4.0.13")]
    [InlineData("~4.1.0", 4, 1, "4.1.2")]
    [InlineData("4.0.*", 4, 0, "4.0.13")]
    [InlineData("~4.1", 4, 1, "4.1.2")]
    [InlineData("^4.0.13 <4.1.0", 4, 0, "4.0.13")]
    [InlineData("~3.11.0", 3, 11, "3.11.4")]
    public void NewestSupportedOnLine_NamesARealRelease(string constraint, int major, int minor, string expected)
    {
        var release = SptReleases.NewestSupportedOnLine([constraint], Releases, major, minor);

        Assert.NotNull(release);
        Assert.Equal(expected, release.Value.Label);
    }

    [Theory]
    [InlineData("~4.0.4", 4, 1)]
    [InlineData("~4.1.0", 4, 0)]
    [InlineData("~3.11.0", 4, 0)]
    public void NewestSupportedOnLine_IsNullWhenNothingOnThatLineQualifies(string constraint, int major, int minor) =>
        Assert.Null(SptReleases.NewestSupportedOnLine([constraint], Releases, major, minor));

    [Fact]
    public void NewestSupportedOnLine_CombinesEveryVersionOfTheMod()
    {
        // ScopeRangefinder ships ~4.0.4 and ~4.1.0 releases; each line resolves independently.
        string[] constraints = ["~4.0.4", "~4.1.0"];

        Assert.Equal("4.0.13", SptReleases.NewestSupportedOnLine(constraints, Releases, 4, 0)!.Value.Label);
        Assert.Equal("4.1.2", SptReleases.NewestSupportedOnLine(constraints, Releases, 4, 1)!.Value.Label);
    }

    [Fact]
    public void NewestSupportedOnLine_IgnoresAConstraintWithNoRealReleaseInRange()
    {
        // Nothing was ever published between 4.0.13 and 4.1.0.
        Assert.Null(SptReleases.NewestSupportedOnLine([">4.0.13 <4.1.0"], Releases, 4, 0));
    }

    [Theory]
    [InlineData("~3.10.0", true)]
    [InlineData("^3.11.0 <4.0.0", true)]
    [InlineData("~4.0.4", true)]
    [InlineData("~3.9.0", false)]
    [InlineData("<3.10.0", false)]
    [InlineData("~3.8.0", false)]
    public void ReachesFloor_KeepsOnlyModsThatStillRunOnASupportedRelease(string constraint, bool expected) =>
        Assert.Equal(expected, SptReleases.ReachesFloor([constraint]));

    [Fact]
    public void ReachesFloor_KeepsAModWhenAnySingleVersionQualifies() =>
        Assert.True(SptReleases.ReachesFloor(["~3.9.0", "~3.11.0"]));

    [Fact]
    public void ReachesFloor_GivesTheBenefitOfTheDoubtWhenNothingIsReadable() =>
        Assert.True(SptReleases.ReachesFloor([null, "", "nonsense"]));

    [Fact]
    public void Supported_ListsEveryRealReleaseAModRunsOn()
    {
        // "~4.0.4" is a real floor (2026-10-02): 4.0.0 is older than it, so isn't supported.
        var supported = SptReleases.Supported(["~4.0.4", "~4.1.0"], Releases).Select(r => r.Label).ToList();

        Assert.Equal(["4.1.2", "4.1.1", "4.1.0", "4.0.13", "4.0.12", "4.0.4"], supported);
    }
}
