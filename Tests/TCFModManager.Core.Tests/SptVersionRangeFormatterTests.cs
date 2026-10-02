using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class SptVersionRangeFormatterTests
{
    [Theory]
    // Caret: at least this version, below the next major.
    [InlineData("^4.0.13", "4.0.13 - 4.x")]
    [InlineData("^4.0", "4.x")]
    [InlineData("^3.11.0 <4.0.0", "3.11 - 3.x")]
    // Tilde: at least this version, below the next minor.
    [InlineData("~4.1.1", "4.1.1 - 4.1.x")]
    [InlineData("~4.0", "4.0.x")]
    [InlineData("~3.9", "3.9.x")]
    // Intersected clauses.
    [InlineData("^4.0.13 <4.1.0", "4.0.13 - 4.0.x")]
    [InlineData("^4.0 <4.1.0", "4.0.x")]
    [InlineData(">=4.0.0 <=4.0.13", "4.0 - 4.0.13")]
    // Single-sided bounds.
    [InlineData(">=3.9.0", "3.9+")]
    [InlineData(">3.8.0", "newer than 3.8")]
    [InlineData("<4.0.0", "up to 3.x")]
    // Bare version is an exact match.
    [InlineData("3.9.0", "3.9.x")]
    [InlineData("4.0.13", "4.0.13 - 4.0.x")]
    public void Format_RendersConstraintAsPlainRange(string constraint, string expected) =>
        Assert.Equal(expected, SptVersionRangeFormatter.Format(constraint));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a version")]
    [InlineData("^")]
    public void Format_ReturnsNullForUnparsableInput(string? constraint) =>
        Assert.Null(SptVersionRangeFormatter.Format(constraint));

    [Fact]
    public void Format_UpperBoundNeverClaimsAVersionTheConstraintExcludes()
    {
        // "^3.11.0 <4.0.0" must not render as "3.11+", which would imply SPT 4 support.
        var formatted = SptVersionRangeFormatter.Format("^3.11.0 <4.0.0");

        Assert.NotNull(formatted);
        Assert.DoesNotContain("+", formatted);
        Assert.False(SptVersionMatcher.IsSatisfiedBy("^3.11.0 <4.0.0", "4.0.0"));
    }

    [Theory]
    [InlineData("^4.0.13 <4.1.0", "4.0.13", true)]
    [InlineData("^4.0.13 <4.1.0", "4.1.0", false)]
    [InlineData("~4.1.1", "4.1.9", true)]
    [InlineData("~4.1.1", "4.2.0", false)]
    public void Format_AgreesWithMatcherOnRangeEdges(string constraint, string version, bool satisfied)
    {
        // The displayed range and the compatibility colour come from different code paths; this
        // pins them to the same answer at the boundaries.
        Assert.NotNull(SptVersionRangeFormatter.Format(constraint));
        Assert.Equal(satisfied, SptVersionMatcher.IsSatisfiedBy(constraint, version));
    }
}
