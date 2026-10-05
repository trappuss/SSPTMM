using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// The listings that are a mod manager, never offered as a mod: subscribing to one would unzip the
// manager into the SPT folder.
public class SelfModTests
{
    [Theory]
    [InlineData(2945)] // TCF Mod Manager
    [InlineData(3111)] // SSPTMM
    public void ManagerListings_AreOwnListings(int id) => Assert.True(SelfMod.IsOwnListing(id));

    [Theory]
    [InlineData(236)]  // SVM
    [InlineData(29450)]
    [InlineData(311)]
    [InlineData(0)]
    public void OtherMods_AreNot(int id) => Assert.False(SelfMod.IsOwnListing(id));

    [Fact]
    public void ByString_MatchesExactlyOnly()
    {
        Assert.True(SelfMod.IsOwnListing("3111"));
        Assert.False(SelfMod.IsOwnListing(" 3111"));
        Assert.False(SelfMod.IsOwnListing(null));
    }
}
