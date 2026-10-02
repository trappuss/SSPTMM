using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SptVersionRangeTests
{
    [Theory]
    // Caret spans from its version up to the next major, so it covers every line above its own.
    [InlineData("^4.0.13", 4, 0, true)]
    [InlineData("^4.0.13", 4, 1, true)]
    [InlineData("^4.0.13", 3, 11, false)]
    // Tilde is confined to its own minor line - this is the case that made the old
    // "at or above" filter show 4.1-only mods under a 4.0 filter.
    [InlineData("~4.1.1", 4, 1, true)]
    [InlineData("~4.1.1", 4, 0, false)]
    [InlineData("~4.1.1", 4, 2, false)]
    // A tilde that doesn't start at .0 still covers its whole line.
    [InlineData("~4.1.2", 4, 1, true)]
    // Explicit upper bounds.
    [InlineData("^4.0.13 <4.1.0", 4, 0, true)]
    [InlineData("^4.0.13 <4.1.0", 4, 1, false)]
    [InlineData("^3.11.0 <4.0.0", 3, 11, true)]
    [InlineData("^3.11.0 <4.0.0", 4, 0, false)]
    // Single-sided bounds.
    [InlineData(">=3.9.0", 4, 0, true)]
    [InlineData(">=3.9.0", 3, 8, false)]
    [InlineData("<4.0.0", 3, 11, true)]
    [InlineData("<4.0.0", 4, 0, false)]
    // Bare version is exact.
    [InlineData("4.0.13", 4, 0, true)]
    [InlineData("4.0.13", 4, 1, false)]
    public void IntersectsReleaseLine_MatchesOnlyLinesTheConstraintAllows(
        string constraint, int major, int minor, bool expected) =>
        Assert.Equal(expected, SptVersionRange.IntersectsReleaseLine(constraint, major, minor));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a version")]
    public void IntersectsReleaseLine_IsFalseForAnUnreadableConstraint(string? constraint) =>
        // An unreadable constraint tells us nothing, so it matches no line. Deciding whether to
        // hide the mod is the caller's job - one unreadable version must not whitelist a mod
        // through every filter, which is how a 3.11-only mod ended up under a 4.0 filter.
        Assert.False(SptVersionRange.IntersectsReleaseLine(constraint, 4, 0));

    [Theory]
    // The Forge publishes wildcard constraints; "4.0.*" is the whole 4.0 line, not the exact
    // version 4.0.0, and reading it as exact made 4.0 mods look incompatible with SPT 4.0.13.
    [InlineData("4.0.*", "4.0.13", true)]
    [InlineData("4.0.*", "4.0.0", true)]
    [InlineData("4.0.*", "4.1.0", false)]
    [InlineData("4.x", "4.9.9", true)]
    [InlineData("4.x", "5.0.0", false)]
    [InlineData("*", "4.0.13", true)]
    public void Wildcards_AreTreatedAsRanges(string constraint, string version, bool expected) =>
        Assert.Equal(expected, SptVersionMatcher.IsSatisfiedBy(constraint, version));

    [Theory]
    [InlineData("4.0.*", 4, 0, true)]
    [InlineData("4.0.*", 4, 1, false)]
    [InlineData("~4.1", 4, 1, true)]
    [InlineData("~4.1", 4, 0, false)]
    public void Wildcards_IntersectOnlyTheirOwnLine(string constraint, int major, int minor, bool expected) =>
        Assert.Equal(expected, SptVersionRange.IntersectsReleaseLine(constraint, major, minor));

    [Fact]
    public void UnionForLine_DescribesOnlyWhatTheLineSupports()
    {
        // Task Search ships "4.0.*" and "~4.1" releases; each line describes itself.
        string[] constraints = ["4.0.*", "~4.1"];

        Assert.Equal("4.0.x", SptVersionRangeFormatter.Format(SptVersionRange.UnionForLine(constraints, 4, 0)!.Value));
        Assert.Equal("4.1.x", SptVersionRangeFormatter.Format(SptVersionRange.UnionForLine(constraints, 4, 1)!.Value));
    }

    [Fact]
    public void UnionForLine_IsNullWhenNoVersionTouchesTheLine()
    {
        // CameraShakeTweaker is 3.10/3.11 only and must not describe or match a 4.x line.
        string[] constraints = ["~3.10.0", "~3.11.0"];

        Assert.Null(SptVersionRange.UnionForLine(constraints, 4, 0));
        Assert.Null(SptVersionRange.UnionForLine(constraints, 4, 1));
        Assert.NotNull(SptVersionRange.UnionForLine(constraints, 3, 11));
    }

    [Fact]
    public void UnionForLine_MergesSeveralVersionsOnTheSameLine()
    {
        string[] constraints = ["^4.0.13 <4.1.0", "~4.0.0"];

        var union = SptVersionRange.UnionForLine(constraints, 4, 0);

        Assert.NotNull(union);
        Assert.Equal("4.0.x", SptVersionRangeFormatter.Format(union.Value));
    }

    [Fact]
    public void IntersectsReleaseLine_AgreesWithMatcherOnAConcreteVersionInTheLine()
    {
        // "~4.1.1" runs on 4.1.9 but not on any 4.0.x, and the line check must say the same.
        Assert.True(SptVersionMatcher.IsSatisfiedBy("~4.1.1", "4.1.9"));
        Assert.True(SptVersionRange.IntersectsReleaseLine("~4.1.1", 4, 1));

        Assert.False(SptVersionMatcher.IsSatisfiedBy("~4.1.1", "4.0.13"));
        Assert.False(SptVersionRange.IntersectsReleaseLine("~4.1.1", 4, 0));
    }

    [Theory]
    // The lower bound is honoured as written (2026-10-02): a server mod built against a newer patch
    // won't load on an older one - PityLoot "~4.1.6" on SPT 4.1.5. Later patches in the line pass.
    [InlineData("~4.1.6", "4.1.5", false)]
    [InlineData("~4.1.3", "4.1.1", false)]
    [InlineData("~4.1.3", "4.1.3", true)]
    [InlineData("~4.1.3", "4.1.9", true)]
    [InlineData("~4.1.3", "4.0.13", false)]
    [InlineData("~4.1.3", "4.2.0", false)]
    [InlineData("^4.0.13", "4.0.5", false)]
    [InlineData("^4.0.13", "4.1.5", true)]
    // But an EXPLICIT narrow range within one line is a real, deliberate exclusion several mods in
    // Chris's catalog use on purpose (e.g. HandsAreNotBusy, LetMeRightClick "broken on 4.1.3+";
    // "Temporary Fixes" "broken on 4.0.14+") and must keep failing outside that stated window.
    [InlineData(">=4.1.0 <4.1.3", "4.1.1", true)]
    [InlineData(">=4.1.0 <4.1.3", "4.1.3", false)]
    [InlineData(">=4.1.0 <4.1.3", "4.1.4", false)]
    [InlineData(">=4.0.0 <=4.0.13", "4.0.5", true)]
    [InlineData(">=4.0.0 <=4.0.13", "4.0.20", false)]
    // An open-ended lower bound is a real floor too.
    [InlineData(">=4.1.3", "4.1.1", false)]
    [InlineData(">=4.1.3", "4.1.3", true)]
    [InlineData(">=4.1.3", "4.1.9", true)]
    [InlineData(">=4.1.3", "4.0.13", false)]
    // No upper bound at all means exactly that - 4.2.0 and beyond satisfy it too, same as plain
    // ">=" always has (this half isn't the line-relaxation, it's ordinary ">=" semantics).
    [InlineData(">=4.1.3", "4.2.0", true)]
    public void IsSatisfiedBy_LowerBoundsAreFloorsAndExplicitNarrowRangesStayStrict(
        string constraint, string version, bool expected) =>
        Assert.Equal(expected, SptVersionMatcher.IsSatisfiedBy(constraint, version));

    [Fact]
    public void TryParse_ReturnsFalseForUnparsableInput()
    {
        Assert.False(SptVersionRange.TryParse("nonsense", out _));
        Assert.False(SptVersionRange.TryParse(null, out _));
    }

    [Theory]
    // A bare exact constraint is the release the author built against: it runs on that patch and
    // later ones of its own line, never an earlier patch or another line.
    [InlineData("4.1.2", "4.1.1", false)]
    [InlineData("4.1.2", "4.1.2", true)]
    [InlineData("4.1.2", "4.1.4", true)]
    [InlineData("4.1.2", "4.0.13", false)]
    [InlineData("4.1.2", "4.2.0", false)]
    public void IsSatisfiedBy_BareExactMatchesThatPatchAndLaterOnItsOwnLine(string constraint, string version, bool expected) =>
        Assert.Equal(expected, SptVersionMatcher.IsSatisfiedBy(constraint, version));

    [Fact]
    public void TryParse_IntersectsMultipleClauses()
    {
        Assert.True(SptVersionRange.TryParse("^4.0.13 <4.1.0", out var bounds));

        Assert.Equal(new Version(4, 0, 13, 0), bounds.Min);
        Assert.Equal(new Version(4, 1, 0, 0), bounds.MaxExclusive);
        Assert.False(bounds.MinExclusive);
    }

    [Theory]
    // How precisely the operand is written decides how wide it reaches. Both of these are live in
    // the catalog and both mean "any SPT 4": TCF Mod Manager's own listing publishes "~4", and
    // SPT Mod Manager publishes a bare "4". Read as the 4.0 line, each looked incompatible with
    // SPT 4.1 and was filtered out of the version list for anyone on it.
    [InlineData("~4", "4.0.13", true)]
    [InlineData("~4", "4.1.3", true)]
    [InlineData("~4", "3.11.4", false)]
    [InlineData("~4", "5.0.0", false)]
    [InlineData("4", "4.0.13", true)]
    [InlineData("4", "4.1.3", true)]
    [InlineData("4", "3.11.4", false)]
    [InlineData("4", "5.0.0", false)]
    // A minor written out still confines it to that line, which is the whole point of the distinction.
    [InlineData("~4.0", "4.0.13", true)]
    [InlineData("~4.0", "4.1.3", false)]
    [InlineData("4.0", "4.0.13", true)]
    [InlineData("4.0", "4.1.3", false)]
    // An explicit upper bound still wins over the wider tilde - this is the constraint on
    // HollywoodFX and 200-odd other version records, and it really is 4.0-only.
    [InlineData("~4 <4.1.0", "4.0.13", true)]
    [InlineData("~4 <4.1.0", "4.1.3", false)]
    public void IsSatisfiedBy_OperandPrecisionDecidesHowWideAClauseReaches(
        string constraint, string version, bool expected) =>
        Assert.Equal(expected, SptVersionMatcher.IsSatisfiedBy(constraint, version));

    [Theory]
    // "<=" reads the same precision: "<=4" allows all of 4.x.x, "<=4.0" all of 4.0.x, and
    // "<=4.0.13" stops at that release - which is the form the capped mods in the catalog use.
    [InlineData("<=4", "4.1.3", true)]
    [InlineData("<=4", "5.0.0", false)]
    [InlineData("<=4.0", "4.0.13", true)]
    [InlineData("<=4.0", "4.1.0", false)]
    [InlineData(">=4.0.0 <=4.0.13", "4.0.13", true)]
    [InlineData(">=4.0.0 <=4.0.13", "4.0.14", false)]
    public void IsSatisfiedBy_UpperBoundReadsOperandPrecisionToo(
        string constraint, string version, bool expected) =>
        Assert.Equal(expected, SptVersionMatcher.IsSatisfiedBy(constraint, version));

    [Fact]
    public void TryParse_BareMajorSpansTheWholeMajorLine()
    {
        Assert.True(SptVersionRange.TryParse("4", out var bounds));

        Assert.Equal(new Version(4, 0, 0, 0), bounds.Min);
        Assert.Equal(new Version(5, 0, 0, 0), bounds.MaxExclusive);

        // Not recorded as an exact match: Contains reads an exact as "this major.minor line",
        // which would put a bare "4" back on the 4.0 line only.
        Assert.Null(bounds.Exact);
    }

    [Theory]
    // The line checks the Browse SPT filter runs on have to agree with the compatibility answer above.
    [InlineData("~4", 4, 0, true)]
    [InlineData("~4", 4, 1, true)]
    [InlineData("~4", 3, 11, false)]
    [InlineData("4", 4, 1, true)]
    [InlineData("4", 5, 0, false)]
    public void IntersectsReleaseLine_AgreesWithBareAndTildeMajors(
        string constraint, int major, int minor, bool expected) =>
        Assert.Equal(expected, SptVersionRange.IntersectsReleaseLine(constraint, major, minor));
}
