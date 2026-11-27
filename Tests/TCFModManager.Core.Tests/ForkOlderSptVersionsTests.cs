using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.9): the version for this install's SPT when it is further back than the ten the list carries.
public class ForkOlderSptVersionsTests
{
    private static Mod WithVersions(int id, int count, string constraint) => new()
    {
        Id = id,
        Versions = [.. Enumerable.Range(0, count).Select(i => new ModVersionSummary { Id = id * 100 + i, Version = $"2.0.{i}", SptVersionConstraint = constraint })],
    };

    [Fact]
    public void A_mod_whose_ten_are_all_for_a_newer_SPT_is_worth_asking_about() =>
        Assert.True(OlderSptVersions.WorthAsking(WithVersions(1, 10, "~4.1.0"), "3.11.4"));

    [Fact]
    public void Not_when_one_of_them_runs_here()
    {
        var mod = WithVersions(1, 10, "~4.1.0");
        mod.Versions![9].SptVersionConstraint = "~3.11.0";

        Assert.False(OlderSptVersions.WorthAsking(mod, "3.11.4"));
    }

    [Fact]
    public void Not_when_fewer_than_ten_came_since_that_is_all_there_are() =>
        Assert.False(OlderSptVersions.WorthAsking(WithVersions(1, 9, "~4.1.0"), "3.11.4"));

    [Fact]
    public void Not_a_mod_that_stopped_before_this_SPT_since_nothing_further_back_can_fit() =>
        Assert.False(OlderSptVersions.WorthAsking(WithVersions(1, 10, "~3.11.0"), "4.1.6"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a version")]
    public void Not_without_a_readable_SPT_version(string? spt) =>
        Assert.False(OlderSptVersions.WorthAsking(WithVersions(1, 10, "~4.1.0"), spt));

    [Fact]
    public void An_answer_is_added_once_and_marked()
    {
        var mod = WithVersions(7, 10, "~4.1.0");
        var answers = new OlderSptVersions.Answers();
        answers.Set(7, "3.11.4", new ModVersionSummary { Id = 5, Version = "1.9.0", SptVersionConstraint = "~3.11.0" }, DateTimeOffset.UtcNow);

        Assert.Equal(1, OlderSptVersions.Apply([mod], answers, "3.11.4"));
        Assert.Equal(0, OlderSptVersions.Apply([mod], answers, "3.11.4"));

        var added = Assert.Single(mod.Versions!, v => v.AddedForSpt);
        Assert.Equal("1.9.0", added.Version);

        // The newest is still the newest; the one that runs here is now among them.
        Assert.Equal("2.0.9", mod.Versions!.NewestFirst().First().Version);
        Assert.Contains(mod.Versions!, v => SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, "3.11.4") == true);
    }

    [Fact]
    public void An_answer_for_another_SPT_version_is_not_this_ones()
    {
        var mod = WithVersions(7, 10, "~4.1.0");
        var answers = new OlderSptVersions.Answers();
        answers.Set(7, "4.0.13", new ModVersionSummary { Id = 5, Version = "1.9.0" }, DateTimeOffset.UtcNow);

        Assert.Equal(0, OlderSptVersions.Apply([mod], answers, "3.11.4"));
    }

    [Fact]
    public void A_mod_is_asked_about_again_only_after_a_while()
    {
        var mod = WithVersions(7, 10, "~4.1.0");
        var answers = new OlderSptVersions.Answers();
        var now = DateTimeOffset.UtcNow;

        Assert.Single(OlderSptVersions.ToAsk([mod], answers, "3.11.4", now));

        // "It has none" is an answer too.
        answers.Set(7, "3.11.4", null, now);
        Assert.Empty(OlderSptVersions.ToAsk([mod], answers, "3.11.4", now.AddDays(1)));
        Assert.Single(OlderSptVersions.ToAsk([mod], answers, "3.11.4", now + OlderSptVersions.RecheckAfter + TimeSpan.FromHours(1)));
    }
}
