using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class DependencyStatusResolverTests
{
    private static DependencyNode Node(bool conflict = false) => new() { Id = 902, Name = "BigBrain", Conflict = conflict };

    [Fact]
    public void Resolve_NotInstalledWhenNothingIsOnDisk() =>
        Assert.Equal(ModStatus.NotInstalled, DependencyStatusResolver.Resolve(Node(), null, "1.3.0"));

    [Fact]
    public void Resolve_InstalledWhenTheDiskVersionMatches() =>
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve(Node(), "1.3.0", "1.3.0"));

    // Newer than the newest version that fits means outside what the mod needing it accepts: CommonLib
    // 3.0.6 installed for a mod made for 2.x is not "installed and fine".
    [Fact]
    public void Resolve_TooNewWhenTheDiskVersionIsNewerThanTheNewestThatFits() =>
        Assert.Equal(ModStatus.TooNew, DependencyStatusResolver.Resolve(Node(), "1.4.0", "1.3.0"));

    [Fact]
    public void Resolve_TooNew_ForAVersionReadFromADll_OnlyAcrossAMajorVersion()
    {
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve(Node(), "1.4.0.0", "1.3.0", exactVersion: false));
        Assert.Equal(ModStatus.TooNew, DependencyStatusResolver.Resolve(Node(), "3.0.6.0", "2.5.0", exactVersion: false));
        Assert.Equal(ModStatus.TooNew, DependencyStatusResolver.Resolve(Node(), "0.9.0", "0.5.0", exactVersion: false));
    }

    [Fact]
    public void Resolve_InstalledWhenTheScannedVersionCarriesAnExtraZero() =>
        // The scanner reports a DLL's file version as "1.3.0.0" against a published "1.3.0".
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve(Node(), "1.3.0.0", "1.3.0"));

    [Fact]
    public void Resolve_UpdateAvailableWhenTheDiskVersionIsOlder() =>
        Assert.Equal(ModStatus.UpdateAvailable, DependencyStatusResolver.Resolve(Node(), "1.2.0", "1.3.0"));

    [Fact]
    public void Resolve_NoCompatibleVersionWhenNothingPublishedFitsAndItIsMissing() =>
        // latest_compatible_version comes back null when no release suits the installed SPT.
        Assert.Equal(ModStatus.NoCompatibleVersion, DependencyStatusResolver.Resolve(Node(), null, null));

    [Fact]
    public void Resolve_InstalledEvenWhenNoCompatibleVersionIsPublished() =>
        // Already on disk and nothing newer to move to - not a problem to flag.
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Resolve(Node(), "1.2.0", null));

    [Theory]
    [InlineData(null, "1.3.0")]
    [InlineData("1.2.0", "1.3.0")]
    [InlineData("1.3.0", "1.3.0")]
    public void Resolve_ConflictOutranksWhateverIsOnDisk(string? installed, string? required) =>
        Assert.Equal(ModStatus.Conflict, DependencyStatusResolver.Resolve(Node(conflict: true), installed, required));

    [Fact]
    public void Worst_IsInstalledForAnEmptyTree() =>
        Assert.Equal(ModStatus.Installed, DependencyStatusResolver.Worst([]));

    [Fact]
    public void Worst_PicksTheMostSevere() =>
        Assert.Equal(
            ModStatus.NotInstalled,
            DependencyStatusResolver.Worst([ModStatus.Installed, ModStatus.UpdateAvailable, ModStatus.NotInstalled]));

    [Fact]
    public void Worst_RanksConflictAboveEverythingElse() =>
        Assert.Equal(
            ModStatus.Conflict,
            DependencyStatusResolver.Worst([ModStatus.NotInstalled, ModStatus.Conflict, ModStatus.UpdateAvailable]));

    [Fact]
    public void Worst_IsInstalledWhenEverythingIsSatisfied() =>
        Assert.Equal(
            ModStatus.Installed,
            DependencyStatusResolver.Worst([ModStatus.Installed, ModStatus.Installed]));

    [Fact]
    public void Severity_OrdersMissingAboveOutdated() =>
        Assert.True(
            DependencyStatusResolver.Severity(ModStatus.NotInstalled)
            < DependencyStatusResolver.Severity(ModStatus.UpdateAvailable));
}
