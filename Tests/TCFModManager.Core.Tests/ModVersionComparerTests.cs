using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModVersionComparerTests
{
    [Theory]
    [InlineData("1.0.0", "1.1.0", true)]
    [InlineData("1.1.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.0", "1.10.0", true)] // numeric, not lexicographic, comparison
    [InlineData("v1.0.0", "v1.1.0", true)] // leading "v" tolerated on both sides
    [InlineData("1.0", "1.0.1", true)] // missing segments default to 0
    [InlineData("1.2.0-beta", "1.3.0", true)]
    [InlineData("1.2.0-beta", "1.2.0", true)] // a pre-release sorts below its release
    [InlineData("1.2.0", "1.2.0-beta", false)]
    [InlineData("1.2.0-beta.2", "1.2.0-beta.10", true)] // numeric parts compare as numbers
    [InlineData("1.2.0-alpha", "1.2.0-beta", true)]
    [InlineData("1.2.0-beta", "1.2.0-beta", false)]
    [InlineData("1.2.0-beta.1", "1.2.0-beta", false)] // more parts outrank fewer
    [InlineData("1.2.0.0", "1.2.0", false)] // a DLL's four-part version is the same release
    [InlineData("1.2.0+build5", "1.2.0", false)] // build metadata ignored
    [InlineData("1.2.0", "1.2.0-hotfix", true)] // a fix after the release, whatever SemVer says
    [InlineData("1.2.0-hotfix", "1.2.0-hotfix2", true)]
    [InlineData("1.2.0-fix1", "1.2.0", false)]
    [InlineData("1.2.0-beta", "1.2.0-hotfix", true)]
    [InlineData("1.2.0-hotfix", "1.3.0-beta", true)]
    [InlineData("1.2.0-hotfix9", "1.2.0-hotfix10", true)] // the number glued to the word, as a number
    [InlineData("1.2.0-fix2", "1.2.0-fix10", true)]
    [InlineData("1.2.0-beta2", "1.2.0-beta10", true)]
    public void IsUpdateAvailable_ComparesNumerically(string installed, string latest, bool expected)
    {
        Assert.Equal(expected, ModVersionComparer.IsUpdateAvailable(installed, latest));
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.0", null)]
    [InlineData(null, null)]
    [InlineData("not-a-version", "1.0.0")]
    [InlineData("1.0.0", "not-a-version")]
    public void IsUpdateAvailable_ReturnsNullWhenEitherSideIsUnknown(string? installed, string? latest)
    {
        Assert.Null(ModVersionComparer.IsUpdateAvailable(installed, latest));
    }

    [Theory]
    [InlineData("1.2.0.0", "1.2.0-beta", true)] // a DLL cannot carry the label
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.0-beta", "1.2.0", false)]
    [InlineData("1.2.0-beta", "1.2.0-beta", true)]
    [InlineData("1.2.1.0", "1.2.0", false)]
    public void IsSameRelease(string installed, string published, bool expected) =>
        Assert.Equal(expected, ModVersionComparer.IsSameRelease(installed, published));

    [Fact]
    public void BestSameRelease_PrefersTheExactVersion_ThenThePlainRelease()
    {
        Assert.Equal("1.2.0-beta", ModVersionComparer.BestSameRelease("1.2.0-beta", ["1.2.0-hotfix", "1.2.0", "1.2.0-beta"]));
        Assert.Equal("1.2.0", ModVersionComparer.BestSameRelease("1.2.0.0", ["1.2.0-hotfix", "1.2.0", "1.2.0-beta"]));
        Assert.Equal("1.2.0-hotfix", ModVersionComparer.BestSameRelease("1.2.0.0", ["1.2.0-hotfix", "1.3.0"]));
        Assert.Null(ModVersionComparer.BestSameRelease("1.2.0.0", ["1.3.0"]));
    }
}
