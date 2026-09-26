using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SptUpgradeReportTests
{
    private static Mod Listing(int id, params (string Version, string Constraint)[] versions) => new()
    {
        Id = id,
        Name = "Mod " + id,
        Versions = [.. versions.Select((v, i) => new ModVersionSummary { Id = id * 100 + i, Version = v.Version, SptVersionConstraint = v.Constraint })],
    };

    private static readonly Mod[] Catalog =
    [
        Listing(1, ("1.0.0", "~4.0.0"), ("1.1.0", "~4.1.0")),      // an update for 4.1
        Listing(2, ("2.0.0", ">=4.0.0")),                          // already fine on 4.1
        Listing(3, ("3.0.0", "~4.0.0")),                           // nothing for 4.1 yet
        Listing(4, ("4.0.0", "not a constraint")),                 // cannot tell
    ];

    [Fact]
    public void EachInstalledModIsPlacedForTheTargetRelease()
    {
        var rows = SptUpgradeReport.Build(
            [
                new SptUpgradeInput("A", 1, "1.0.0"),
                new SptUpgradeInput("B", 2, "2.0.0.0"),
                new SptUpgradeInput("C", 3, "3.0.0"),
                new SptUpgradeInput("D", 4, "4.0.0"),
                new SptUpgradeInput("Hand", null, "0.1"),
            ],
            Catalog,
            "4.1.2");

        SptUpgradeRow Row(string name) => rows.Single(r => r.Name == name);

        Assert.Equal(SptUpgradeStanding.UpdateNeeded, Row("A").Standing);
        Assert.Equal("1.1.0", Row("A").Version);
        Assert.Equal(SptUpgradeStanding.Ready, Row("B").Standing);
        Assert.Equal(SptUpgradeStanding.NotYet, Row("C").Standing);
        Assert.Equal(SptUpgradeStanding.Unknown, Row("D").Standing);
        Assert.Equal(SptUpgradeStanding.Unknown, Row("Hand").Standing);

        // What stands in the way first.
        Assert.Equal(["C", "A", "D", "Hand", "B"], rows.Select(r => r.Name));
    }

    [Fact]
    public void OnTheReleaseAlreadyInstalled_TheInstalledVersionIsReady()
    {
        var rows = SptUpgradeReport.Build([new SptUpgradeInput("A", 1, "1.0.0")], Catalog, "4.0.13");

        Assert.Equal(SptUpgradeStanding.Ready, rows.Single().Standing);
    }

    [Fact]
    public void AnOlderVersionForTheTarget_IsNotOfferedAsAnUpdate()
    {
        var catalog = new[] { Listing(5, ("2.0.0", "~4.0.0"), ("1.9.0", ">=3.11.0")) };

        var row = SptUpgradeReport.Build([new SptUpgradeInput("E", 5, "2.0.0")], catalog, "4.1.0").Single();

        Assert.Equal(SptUpgradeStanding.NotYet, row.Standing);
    }

    [Fact]
    public void AnInstalledVersionNotAmongTheKnownOnes_CannotBeJudged()
    {
        var catalog = new[] { Listing(6, ("3.0.0", "~4.1.0")) };

        var row = SptUpgradeReport.Build([new SptUpgradeInput("F", 6, "1.0.0")], catalog, "4.0.13").Single();

        Assert.Equal(SptUpgradeStanding.Unknown, row.Standing);
    }
}
