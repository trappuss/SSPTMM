using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;
using static TCFModManager.Core.Tests.TestDlls;

namespace TCFModManager.Core.Tests;

//
// SPT 4 server mods say who they are in their DLL, not in a package.json. These DLLs are written
// in the shapes real mods compile to (see TestDlls); the expected values are what the SPT server
// itself logged for the real ones ("Mod: Dynamic Maps version: 1.2.1 (GUID: com.mpstark.dynamicmaps").
//
public class ServerModMetadataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcf-servermeta-" + Guid.NewGuid().ToString("N"));

    public ServerModMetadataTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Dll(string name = "Mod") => Path.Combine(_root, Guid.NewGuid().ToString("N"), name + ".dll");

    [Fact]
    public void ALiteralMetadataClass_IsReadInFull()
    {
        var path = Dll();
        ServerMod(path,
            new Str("ModGuid", "com.mpstark.dynamicmaps"),
            new Str("Name", "Dynamic Maps"),
            new Str("Author", "mpstark"),
            new Wrapped("Version", "Version", "1.2.1"),
            new Wrapped("SptVersion", "Range", "~4.1.0"),
            new Deps("ModDependencies", Indexer: false, ("com.wtt.commonlib", "~3.0.3"), ("com.x.other", ">=1.0.0")),
            new Str("License", "MIT"));

        var metadata = ServerModMetadataReader.Read(path);

        Assert.NotNull(metadata);
        Assert.Equal("com.mpstark.dynamicmaps", metadata.Guid);
        Assert.Equal("Dynamic Maps", metadata.Name);
        Assert.Equal("mpstark", metadata.Author);
        Assert.Equal("1.2.1", metadata.Version);
        Assert.Equal("~4.1.0", metadata.SptVersion);
        Assert.Equal(
            [new ModDependencyRef("com.wtt.commonlib", false), new ModDependencyRef("com.x.other", false)],
            metadata.Dependencies);
    }

    [Fact]
    public void SptFour0sBaseClass_IsReadTheSameWay()
    {
        var path = Dll();
        ServerMod(path, viaBaseClass: true, new Str("ModGuid", "com.old.mod"), new Wrapped("Version", "Version", "2.0.0"));

        var metadata = ServerModMetadataReader.Read(path);

        Assert.Equal("com.old.mod", metadata?.Guid);
        Assert.Equal("2.0.0", metadata?.Version);
    }

    [Fact]
    public void AVersionWorkedOutAtRunTime_IsNotGuessed()
    {
        // WTT Head Voice Selector builds its version from the assembly's own at run time.
        var path = Dll();
        ServerMod(path, new Str("ModGuid", "com.HeadVoiceSelector.Core"), new Computed("Version"));

        var metadata = ServerModMetadataReader.Read(path);

        Assert.Equal("com.HeadVoiceSelector.Core", metadata?.Guid);
        Assert.Null(metadata?.Version);
    }

    [Fact]
    public void OtherWaysOfWritingIt_AreReadToo()
    {
        var path = Dll();
        ServerMod(path,
            new Getter("ModGuid", "com.getter.mod"),
            new IntVersion("Version", 1, 4, 2, "beta"),
            new Wrapped("SptVersion", "Range", "~4.0.0", ViaParse: true),
            new Deps("ModDependencies", Indexer: true, ("com.dep.one", "^1.0.0")));

        var metadata = ServerModMetadataReader.Read(path);

        Assert.Equal("com.getter.mod", metadata?.Guid);
        Assert.Equal("1.4.2-beta", metadata?.Version);
        Assert.Equal("~4.0.0", metadata?.SptVersion);
        Assert.Equal([new ModDependencyRef("com.dep.one", false)], metadata?.Dependencies);
    }

    [Fact]
    public void ADllWithoutAMetadataClass_OrNotADllAtAll_GivesNothing()
    {
        var plugin = Dll();
        Plugin(plugin, "com.some.plugin", "Some", "1.0.0");
        var junk = Path.Combine(_root, "junk.dll");
        File.WriteAllText(junk, "not a dll");

        Assert.Null(ServerModMetadataReader.Read(plugin));
        Assert.Null(ServerModMetadataReader.Read(junk));
        Assert.Null(ServerModMetadataReader.Read(Path.Combine(_root, "missing.dll")));
    }

    // ---- the scan ----

    private string Install()
    {
        var install = Path.Combine(_root, "install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(install, "SPT_Runtime", "user", "mods"));
        Directory.CreateDirectory(Path.Combine(install, "BepInEx", "plugins"));
        File.WriteAllText(Path.Combine(install, "SPT_Runtime", "SPT.Server.exe"), "");
        return install;
    }

    private static string ServerFolder(string install, string folder) => Path.Combine(install, "SPT_Runtime", "user", "mods", folder);

    [Fact]
    public void AnSpt4ServerMod_IsScannedWithItsGuidVersionAuthorAndDependencies()
    {
        var install = Install();
        var folder = ServerFolder(install, "RCTA - Peely");
        ServerMod(Path.Combine(folder, "RCTA.Peely.dll"),
            new Str("ModGuid", "com.rcta.peely"),
            new Str("Author", "Hj"),
            new Wrapped("Version", "Version", "1.0.4"),
            new Deps("ModDependencies", Indexer: false, ("com.wtt.commonlib", "~3.0.3")));

        // A helper DLL beside it says nothing and is passed over.
        File.WriteAllText(Path.Combine(folder, "Helper.dll"), "not managed");

        var mod = Assert.Single(InstalledModScanner.Scan(install));

        Assert.Equal(InstalledModTarget.Server, mod.Target);
        Assert.Equal("RCTA - Peely", mod.Name);
        Assert.Equal("com.rcta.peely", mod.Guid);
        Assert.Equal(["com.rcta.peely"], mod.AllGuids);
        Assert.Equal("1.0.4", mod.Version);
        Assert.Equal("Hj", mod.Author);
        Assert.Equal([new ModDependencyRef("com.wtt.commonlib", false)], mod.Dependencies);
    }

    [Fact]
    public void APluginWithNoFileVersion_TakesItsBepInPluginVersion()
    {
        var install = Install();
        Plugin(Path.Combine(install, "BepInEx", "plugins", "ORBIT", "ORBIT.dll"), "com.chazut.orbit", "ORBIT", "2.0.0");

        var mod = Assert.Single(InstalledModScanner.Scan(install));

        Assert.Equal("com.chazut.orbit", mod.Guid);
        Assert.Equal("2.0.0", mod.Version);
    }

    [Fact]
    public void APlugin_KeepsBothItsFileVersionAndItsOwn()
    {
        var install = Install();
        Plugin(Path.Combine(install, "BepInEx", "plugins", "ORBIT", "ORBIT.dll"), "com.chazut.orbit", "ORBIT", "2.0.0", "2.0.0.42986");

        var mod = Assert.Single(InstalledModScanner.Scan(install));

        Assert.Equal("2.0.0.42986", mod.Version);
        Assert.Equal("2.0.0", mod.PluginVersion);
    }

    [Theory]
    // What is published decides, either way round.
    [InlineData("2.0.0.42986", "2.0.0", new[] { "2.0.0", "1.9.0" }, "2.0.0")]
    [InlineData("1.0.0.1", "1.0.0", new[] { "1.0.0.1", "1.0.0" }, "1.0.0.1")]
    [InlineData("1.2.0.0", "1.2.0", new[] { "1.2.0-beta" }, "1.2.0.0")]
    // Nothing published to go by: a build number on top gives way to the plugin's own.
    [InlineData("2.0.0.42986", "2.0.0", new string[0], "2.0.0")]
    [InlineData("1.3.0.0", "1.2.0", new string[0], "1.3.0.0")]
    [InlineData("1.2.0-beta", "1.2.0", new string[0], "1.2.0-beta")]
    public void WhichOfAPluginsTwoVersions_IsGoneBy(string file, string plugin, string[] published, string expected)
    {
        Assert.Equal(expected, InstalledModScanner.PluginVersionOf(file, plugin, published));
    }

    [Fact]
    public void AServerDependency_IsMetOnlyByAServerMod_AndAClientOneOnlyByAPlugin()
    {
        var install = Install();

        // SAIN's halves share a GUID; a plugin needing SAIN needs the plugin.
        ServerMod(Path.Combine(ServerFolder(install, "SAIN-Server"), "SAINServerMod.dll"),
            new Str("ModGuid", "me.sol.sain"), new Wrapped("Version", "Version", "4.5.1"));
        ServerMod(Path.Combine(ServerFolder(install, "WTT-ServerCommonLib"), "WTT-ServerCommonLib.dll"),
            new Str("ModGuid", "com.wtt.commonlib"), new Wrapped("Version", "Version", "3.0.6"));
        ServerMod(Path.Combine(ServerFolder(install, "Peely"), "Peely.dll"),
            new Str("ModGuid", "com.rcta.peely"),
            new Deps("ModDependencies", Indexer: false, ("com.wtt.commonlib", "~3.0.3"), ("me.sol.sain", "~4.5.0")));

        var scanned = InstalledModScanner.Scan(install);
        var graph = ModDependencyGraph.Build(scanned);
        var peely = scanned.Single(m => m.Name == "Peely");

        Assert.Empty(graph.MissingOf(peely));
        Assert.Equal(
            ["SAIN-Server", "WTT-ServerCommonLib"],
            graph.DependenciesOf(peely).Select(l => l.Dependency.Name).Order());

        // A client mod depending on me.sol.sain is not met by the server half.
        var orbit = new InstalledMod
        {
            Name = "ORBIT",
            Guid = "com.chazut.orbit",
            Target = InstalledModTarget.Client,
            FolderPath = Path.Combine(install, "BepInEx", "plugins", "ORBIT"),
            Dependencies = [new ModDependencyRef("me.sol.sain", false)],
        };

        var withPlugin = ModDependencyGraph.Build([.. scanned, orbit]);
        Assert.Equal(["me.sol.sain"], withPlugin.MissingOf(orbit).Select(m => m.Identifier));
        Assert.DoesNotContain(withPlugin.DependentsOf(scanned.Single(m => m.Name == "SAIN-Server")), l => l.Dependent == orbit);
    }

    // ---- archives ----

    [Fact]
    public async Task AnArchiveHoldingOnlyAnSpt4ServerMod_IsMatchedToItsListingByModGuid()
    {
        var source = Path.Combine(_root, "src-" + Guid.NewGuid().ToString("N"));
        ServerMod(Path.Combine(source, "user", "mods", "DynMaps", "DynMaps.dll"),
            new Str("ModGuid", "com.mpstark.dynamicmaps"), new Wrapped("Version", "Version", "1.2.1"));
        var zip = Path.Combine(_root, "dm.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(source, zip);

        var contents = await LocalArchive.InspectAsync(zip);
        var catalog = new[]
        {
            new Mod { Id = 7, Name = "Dynamic Maps", Guid = "com.mpstark.dynamicmaps", Versions = [new ModVersionSummary { Id = 1, Version = "1.2.1" }] },
            new Mod { Id = 8, Name = "Other", Guid = "com.other" },
        };

        Assert.Equal(7, LocalArchive.MatchIn(contents, catalog)?.Id);
        Assert.Equal("1.2.1", LocalArchive.VersionOf(contents, catalog[0]));

        // The local id is what it was before server GUIDs were read: the folder.
        Assert.Equal(
            LocalArchive.IdFor(new LocalArchiveContents(true, [], [], ["DynMaps"]), "dm"),
            LocalArchive.IdFor(contents, "dm"));
    }

    [Fact]
    public void AServerModWhoseGuidCannotBeRead_MeansNoServerDependencyIsCalledMissing()
    {
        var install = Install();
        ServerMod(Path.Combine(ServerFolder(install, "Lib"), "Lib.dll"), new Str("Name", "Lib"), new Computed("Version"));
        ServerMod(Path.Combine(ServerFolder(install, "User"), "User.dll"),
            new Str("ModGuid", "com.user"), new Deps("ModDependencies", Indexer: false, ("com.lib", "~1.0.0")));

        var scanned = InstalledModScanner.Scan(install);
        var graph = ModDependencyGraph.Build(scanned);
        var user = scanned.Single(m => m.Name == "User");

        Assert.True(scanned.Single(m => m.Name == "Lib").IdentityUnknown);
        Assert.Empty(graph.MissingOf(user));
        Assert.Equal(["com.lib"], graph.UnresolvedOf(user));
    }

    [Fact]
    public void TwoDescribedDllsInAFolder_NeitherNamedForIt_GiveNoGuid()
    {
        var install = Install();
        ServerMod(Path.Combine(ServerFolder(install, "Pack"), "A.dll"), new Str("ModGuid", "com.a"));
        ServerMod(Path.Combine(ServerFolder(install, "Pack"), "B.dll"), new Str("ModGuid", "com.b"));
        ServerMod(Path.Combine(ServerFolder(install, "Named"), "Helper.dll"), new Str("ModGuid", "com.helper"));
        ServerMod(Path.Combine(ServerFolder(install, "Named"), "Named.dll"), new Str("ModGuid", "com.named"));

        var scanned = InstalledModScanner.Scan(install);

        Assert.Null(scanned.Single(m => m.Name == "Pack").Guid);
        Assert.Equal("com.named", scanned.Single(m => m.Name == "Named").Guid);
    }

    [Fact]
    public void AnArchive_IsMatchedByItsPluginFirst_NotByAServerLibraryItBundles()
    {
        var contents = new LocalArchiveContents(true, [("com.mod.plugin", "1.0.0")], [], ["ModSrv", "LibSrv"],
            [("com.mod.plugin", "1.0.0"), ("com.shared.lib", "2.0.0")]);
        var catalog = new[]
        {
            new Mod { Id = 1, Name = "Mod", Guid = "com.mod.plugin" },
            new Mod { Id = 2, Name = "Shared lib", Guid = "com.shared.lib" },
        };

        Assert.Equal(1, LocalArchive.MatchIn(contents, catalog)?.Id);
    }
}
