using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork: ModDependencyGraph.MissingOf, re-added on top of 1.19's graph for the Subscribed items
// cards' "needs X, which is not installed / is disabled" warning.
public class ForkMissingDependencyTests
{
    private static InstalledMod Client(string name, string guid, bool disabled = false, params ModDependencyRef[] dependencies) =>
        new()
        {
            Name = name,
            Guid = guid,
            Guids = [guid],
            Target = InstalledModTarget.Client,
            FolderPath = Path.Combine("C:", "SPT", "BepInEx", disabled ? "plugins.disabled" : "plugins", name),
            IsDisabled = disabled,
            Dependencies = dependencies,
        };

    // A version from package.json unless a file version is given (an SPT 4 mod read off its DLL).
    private static InstalledMod Server(string name, string? guid = null, string? fileVersion = null, bool disabled = false, params ModDependencyRef[] dependencies) =>
        new()
        {
            Name = name,
            Version = fileVersion ?? "1.0.0",
            Guid = guid,
            Guids = guid is null ? [] : [guid],
            FileVersion = fileVersion,
            Target = InstalledModTarget.Server,
            FolderPath = Path.Combine("C:", "SPT", "user", disabled ? "mods.disabled" : "mods", name),
            IsDisabled = disabled,
            Dependencies = dependencies,
        };

    [Fact]
    public void MissingOf_NamesHardDependenciesNothingEnabledProvides()
    {
        var disabledLibrary = Client("Library", "com.author.library", disabled: true);
        var consumer = Client("Consumer", "com.author.consumer", false,
            new ModDependencyRef("com.author.library", IsSoft: false),
            new ModDependencyRef("com.someone.absent", IsSoft: false),
            new ModDependencyRef("com.someone.optional", IsSoft: true));

        var missing = ModDependencyGraph.Build([disabledLibrary, consumer]).MissingOf(consumer);

        Assert.Equal(2, missing.Count);
        Assert.Same(disabledLibrary, missing.Single(m => m.Identifier == "com.author.library").DisabledProvider);
        Assert.Null(missing.Single(m => m.Identifier == "com.someone.absent").DisabledProvider);
    }

    [Fact]
    public void MissingOf_IsEmpty_WhenAnEnabledCopySitsBesideADisabledOne()
    {
        var enabled = Client("Library", "com.author.library");
        var disabled = Client("Library", "com.author.library", disabled: true);
        var consumer = Client("Consumer", "com.author.consumer", false, new ModDependencyRef("com.author.library", IsSoft: false));

        Assert.Empty(ModDependencyGraph.Build([enabled, disabled, consumer]).MissingOf(consumer));
    }

    [Fact]
    public void MissingOf_NeverNamesWhatTheModProvidesItself()
    {
        InstalledMod Copy(bool disabled) => new()
        {
            Name = "Kit",
            Guid = "com.author.kit",
            Guids = ["com.author.kit", "com.author.kit.api"],
            Target = InstalledModTarget.Client,
            FolderPath = Path.Combine("C:", "SPT", "BepInEx", disabled ? "plugins.disabled" : "plugins", "Kit"),
            IsDisabled = disabled,
            Dependencies = [new ModDependencyRef("com.author.kit.api", IsSoft: false)],
        };

        var enabled = Copy(false);

        Assert.Empty(ModDependencyGraph.Build([enabled, Copy(true)]).MissingOf(enabled));
    }

    [Fact]
    public void MissingOf_OnlyTheOtherSideProviding_IsMissing()
    {
        // SPT checks a server mod's dependencies against server mods only.
        var plugin = Client("Lib", "com.author.lib");
        var consumer = Server("Consumer", "com.author.consumer", dependencies: new ModDependencyRef("com.author.lib", IsSoft: false));

        Assert.Single(ModDependencyGraph.Build([plugin, consumer]).MissingOf(consumer));
    }

    [Fact]
    public void MissingOf_ServerSide_SaysNothing_WhileAServerModCouldNotBeIdentified()
    {
        var unreadable = Server("Mystery", guid: null, fileVersion: "1.0.0.0");
        var consumer = Server("Consumer", "com.author.consumer", dependencies: new ModDependencyRef("com.author.lib", IsSoft: false));

        var graph = ModDependencyGraph.Build([unreadable, consumer]);

        Assert.Empty(graph.MissingOf(consumer));
        Assert.Equal(["com.author.lib"], graph.UnresolvedOf(consumer));
    }

    [Fact]
    public void MissingOf_ServerSide_SaysNothing_WhileAServerModHasNoVersionOrGuidAtAll()
    {
        var unreadable = new InstalledMod
        {
            Name = "Mystery",
            Target = InstalledModTarget.Server,
            FolderPath = Path.Combine("C:", "SPT", "user", "mods", "Mystery"),
        };
        var consumer = Server("Consumer", "com.author.consumer", dependencies: new ModDependencyRef("com.author.lib", IsSoft: false));

        Assert.Empty(ModDependencyGraph.Build([unreadable, consumer]).MissingOf(consumer));
    }

    [Fact]
    public void MissingOf_ServerSide_KnownSpt3Mods_DoNotHideIt()
    {
        // An SPT 3 mod: no GUID, but its version came from package.json (no file version).
        var spt3 = Server("old-mod");
        var consumer = Server("Consumer", "com.author.consumer", dependencies: new ModDependencyRef("com.author.lib", IsSoft: false));

        Assert.Single(ModDependencyGraph.Build([spt3, consumer]).MissingOf(consumer));
    }
}
