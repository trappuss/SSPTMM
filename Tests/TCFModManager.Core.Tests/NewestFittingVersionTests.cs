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

    // Fork (1.4.9): WTT - Content Backport, as sp-mod.com listed it on 2026-11-27. 2.0.6 was edited
    // after 2.0.7 came out and is dated a minute later; its file was gone from its host (404).
    [Fact]
    public void NewestFirst_GoesByVersionNumber_NotByPublishDate()
    {
        var versions = new[]
        {
            new TCFModManager.Core.Models.ModVersionSummary { Id = 16702, Version = "2.0.6", PublishedAt = DateTimeOffset.Parse("2026-11-13T16:48:00Z") },
            new TCFModManager.Core.Models.ModVersionSummary { Id = 16722, Version = "2.0.7", PublishedAt = DateTimeOffset.Parse("2026-11-13T16:47:00Z") },
            new TCFModManager.Core.Models.ModVersionSummary { Id = 15862, Version = "2.0.5", PublishedAt = DateTimeOffset.Parse("2026-10-10T04:50:00Z") },
        };

        Assert.Equal(["2.0.7", "2.0.6", "2.0.5"], versions.NewestFirst().Select(v => v.Version));
    }

    [Fact]
    public void NewestFirst_ReadsNumbersAsNumbers_AndPutsWhatItCannotReadLast()
    {
        var versions = new[] { "1.0.9", "banana", "1.0.10", null, "1.0.2" }
            .Select((v, i) => new TCFModManager.Core.Models.ModVersionSummary { Id = i, Version = v, PublishedAt = DateTimeOffset.UnixEpoch.AddDays(v is "banana" ? 900 : i) })
            .ToList();

        Assert.Equal(["1.0.10", "1.0.9", "1.0.2"], versions.NewestFirst().Take(3).Select(v => v.Version));
    }

    [Fact]
    public void NewestFirst_OrdersTheSameNumberByDate()
    {
        var versions = new[]
        {
            new TCFModManager.Core.Models.ModVersionSummary { Id = 1, Version = "1.0.0", PublishedAt = DateTimeOffset.UnixEpoch },
            new TCFModManager.Core.Models.ModVersionSummary { Id = 2, Version = "1.0.0", PublishedAt = DateTimeOffset.UnixEpoch.AddDays(3) },
        };

        Assert.Equal(2, versions.NewestFirst().First().Id);
    }
}
