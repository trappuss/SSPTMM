using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModDependencyGraphTests
{
    private static InstalledMod Client(string name, string guid, bool disabled = false, params ModDependencyRef[] dependencies) =>
        new()
        {
            Name = name,
            Guid = guid,
            Target = InstalledModTarget.Client,
            FolderPath = Path.Combine("C:", "SPT", "BepInEx", disabled ? "plugins.disabled" : "plugins", name),
            IsDisabled = disabled,
            Dependencies = dependencies,
        };

    private static InstalledMod Server(string name, bool disabled = false, params ModDependencyRef[] dependencies) =>
        new()
        {
            Name = name,
            Target = InstalledModTarget.Server,
            FolderPath = Path.Combine("C:", "SPT", "user", disabled ? "mods.disabled" : "mods", name),
            IsDisabled = disabled,
            Dependencies = dependencies,
        };

    [Fact]
    public void DependentsOf_MatchesAClientModByItsBepInPluginGuid()
    {
        var library = Client("Library", "com.author.library");
        var consumer = Client("Consumer", "com.author.consumer", false, new ModDependencyRef("com.author.library", IsSoft: false));

        var graph = ModDependencyGraph.Build([library, consumer]);

        var link = Assert.Single(graph.DependentsOf(library));
        Assert.Same(consumer, link.Dependent);
        Assert.False(link.IsSoft);
    }

    //
    // A mod folder holding several plugin DLLs registers a GUID per DLL, and what other mods depend
    // on is usually the API assembly's GUID rather than the folder's primary one. Matching only the
    // primary reported the dependency as unresolved and left the dependant out of the disable
    // cascade, with the mod providing it installed the whole time.
    //
    [Fact]
    public void DependentsOf_MatchesAnyGuidTheModsFolderRegisters()
    {
        var library = new InstalledMod
        {
            Name = "author-Toolkit",
            Guid = "com.author.toolkit",
            Guids = ["com.author.toolkit", "com.author.toolkit.api"],
            Target = InstalledModTarget.Client,
            FolderPath = Path.Combine("C:", "SPT", "BepInEx", "plugins", "author-Toolkit"),
        };

        var consumer = Client("Consumer", "com.author.consumer", false, new ModDependencyRef("com.author.toolkit.api", IsSoft: false));

        var graph = ModDependencyGraph.Build([library, consumer]);

        Assert.Same(consumer, Assert.Single(graph.DependentsOf(library)).Dependent);
        Assert.Empty(graph.UnresolvedOf(consumer));
    }

    [Fact]
    public void DependentsOf_MatchesAServerModByItsPackageName()
    {
        var library = Server("SharedTools");
        var consumer = Server("Consumer", false, new ModDependencyRef("SharedTools", IsSoft: false));

        var graph = ModDependencyGraph.Build([library, consumer]);

        var link = Assert.Single(graph.DependentsOf(library));
        Assert.Same(consumer, link.Dependent);
    }

    [Fact]
    public void UnresolvedOf_ListsDependenciesNothingInstalledProvides()
    {
        var consumer = Client("Consumer", "com.author.consumer", false, new ModDependencyRef("com.someone.missing", IsSoft: false));

        var graph = ModDependencyGraph.Build([consumer]);

        Assert.Equal("com.someone.missing", Assert.Single(graph.UnresolvedOf(consumer)));
        Assert.Empty(graph.DependenciesOf(consumer));
    }

    [Fact]
    public void DisableImpact_FollowsTheChainOutward()
    {
        var library = Client("Library", "com.author.library");
        var middle = Client("Middle", "com.author.middle", false, new ModDependencyRef("com.author.library", IsSoft: false));
        var outer = Client("Outer", "com.author.outer", false, new ModDependencyRef("com.author.middle", IsSoft: false));

        var graph = ModDependencyGraph.Build([library, middle, outer]);

        var impact = graph.DisableImpact([library]);

        Assert.Equal(2, impact.Count);
        Assert.Contains(impact, l => ReferenceEquals(l.Dependent, middle));
        Assert.Contains(impact, l => ReferenceEquals(l.Dependent, outer));
    }

    [Fact]
    public void DisableImpact_ExcludesTheRootsAndAlreadyDisabledDependents()
    {
        var library = Client("Library", "com.author.library");
        var offline = Client("Offline", "com.author.offline", true, new ModDependencyRef("com.author.library", IsSoft: false));

        var graph = ModDependencyGraph.Build([library, offline]);

        Assert.Empty(graph.DisableImpact([library]));
    }

    [Fact]
    public void DisableImpact_KeepsSoftAndHardApart()
    {
        var library = Client("Library", "com.author.library");
        var optional = Client("Optional", "com.author.optional", false, new ModDependencyRef("com.author.library", IsSoft: true));

        var graph = ModDependencyGraph.Build([library, optional]);

        var link = Assert.Single(graph.DisableImpact([library]));
        Assert.True(link.IsSoft);
    }

    [Fact]
    public void DisableImpact_SurvivesADependencyCycle()
    {
        var first = Client("First", "com.author.first", false, new ModDependencyRef("com.author.second", IsSoft: false));
        var second = Client("Second", "com.author.second", false, new ModDependencyRef("com.author.first", IsSoft: false));

        var graph = ModDependencyGraph.Build([first, second]);

        var link = Assert.Single(graph.DisableImpact([first]));
        Assert.Same(second, link.Dependent);
    }

    [Fact]
    public void EnableRequirements_ListsDisabledDependenciesTheChainNeeds()
    {
        var library = Client("Library", "com.author.library", disabled: true);
        var middle = Client("Middle", "com.author.middle", true, new ModDependencyRef("com.author.library", IsSoft: false));
        var outer = Client("Outer", "com.author.outer", true, new ModDependencyRef("com.author.middle", IsSoft: false));

        var graph = ModDependencyGraph.Build([library, middle, outer]);

        var required = graph.EnableRequirements([outer]);

        Assert.Equal(2, required.Count);
        Assert.Contains(required, l => ReferenceEquals(l.Dependency, middle));
        Assert.Contains(required, l => ReferenceEquals(l.Dependency, library));
    }

    [Fact]
    public void EnableRequirements_IgnoresDependenciesAlreadyEnabled()
    {
        var library = Client("Library", "com.author.library");
        var consumer = Client("Consumer", "com.author.consumer", true, new ModDependencyRef("com.author.library", IsSoft: false));

        var graph = ModDependencyGraph.Build([library, consumer]);

        Assert.Empty(graph.EnableRequirements([consumer]));
    }

    [Fact]
    public void Build_IgnoresAModDependingOnItself()
    {
        var mod = Client("Solo", "com.author.solo", false, new ModDependencyRef("com.author.solo", IsSoft: false));

        var graph = ModDependencyGraph.Build([mod]);

        Assert.Empty(graph.DependentsOf(mod));
        Assert.Empty(graph.DependenciesOf(mod));
    }

    private static InstalledMod Spt4Server(string name, string guid, params ModDependencyRef[] dependencies) =>
        new()
        {
            Name = name,
            Guid = guid,
            Guids = [guid],
            Target = InstalledModTarget.Server,
            FolderPath = Path.Combine("C:", "SPT", "SPT", "user", "mods", name),
            Dependencies = dependencies,
        };

    [Fact]
    public void DependentsOf_MatchesAnSpt4ServerModByItsDeclaredGuid()
    {
        var library = Spt4Server("WTT-ServerCommonLib", "com.wtt.commonlib");
        var consumer = Spt4Server("BlackDivServer", "com.blackdiv.tacticaltoaster",
            new ModDependencyRef("com.wtt.commonlib", IsSoft: false, ">=2.0.0"));

        var graph = ModDependencyGraph.Build([library, consumer]);

        Assert.Same(consumer, Assert.Single(graph.DependentsOf(library)).Dependent);
        Assert.Empty(graph.UnresolvedOf(consumer));
    }

    // SPT 4.x mods commonly give both halves one GUID. A server mod's dependency is on the other
    // mod's server half, and a plugin's on its client half - never across.
    [Fact]
    public void Dependency_OnAGuidBothHalvesShare_ResolvesToTheDependantsOwnSide()
    {
        var libraryClient = Client("WTT-ClientCommonLib", "com.wtt.commonlib");
        var libraryServer = Spt4Server("WTT-ServerCommonLib", "com.wtt.commonlib");
        var serverConsumer = Spt4Server("BlackDivServer", "com.blackdiv.tacticaltoaster",
            new ModDependencyRef("com.wtt.commonlib", IsSoft: false));
        var clientConsumer = Client("BlackDiv", "com.blackdiv.tacticaltoaster", false,
            new ModDependencyRef("com.wtt.commonlib", IsSoft: false));

        var graph = ModDependencyGraph.Build([libraryClient, libraryServer, serverConsumer, clientConsumer]);

        Assert.Same(libraryServer, Assert.Single(graph.DependenciesOf(serverConsumer)).Dependency);
        Assert.Same(libraryClient, Assert.Single(graph.DependenciesOf(clientConsumer)).Dependency);
    }

    // Seen on the real 4.0 install: Foldables' plugin depends on "com.tyfon.uifixes", which only UI
    // Fixes' SERVER half declares. BepInEx can't satisfy that from a server mod, so it stays
    // unresolved rather than tying the plugin to the server half in the disable cascade.
    [Fact]
    public void Dependency_WithNoMatchOnItsOwnSide_StaysUnresolved()
    {
        var serverOnly = Spt4Server("Tyfon.UIFixes.Server", "com.tyfon.uifixes");
        var clientConsumer = Client("ozen-Foldables", "com.ozen.foldables", false,
            new ModDependencyRef("com.tyfon.uifixes", IsSoft: true));

        var graph = ModDependencyGraph.Build([serverOnly, clientConsumer]);

        Assert.Empty(graph.DependenciesOf(clientConsumer));
        Assert.Empty(graph.DependentsOf(serverOnly));
        Assert.Equal(["com.tyfon.uifixes"], graph.UnresolvedOf(clientConsumer));
    }
}
