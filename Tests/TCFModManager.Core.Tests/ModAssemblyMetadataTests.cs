using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;
using static TCFModManager.Core.Tests.ModMetadataFixture;

namespace TCFModManager.Core.Tests;

public sealed class ModAssemblyMetadataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tcfmm-metadata-" + Guid.NewGuid().ToString("N"));

    public ModAssemblyMetadataTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModMetadata", name);

    // What "Version Version { get; init; } = new(1, 1, 0);" compiles to: the optional pre-release and
    // build arguments are passed as nulls.
    private static Il SetIntVersion(Il il, int major, int minor, int patch, string? preRelease = null)
    {
        il.This().Int(major).Int(minor).Int(patch);
        if (preRelease is null) il.Null(); else il.Str(preRelease);
        return il.Null().NewVersionFromInts().Set("Version");
    }

    [Fact]
    public void ReadServer_Spt40Record_WithStringVersion()
    {
        var dll = new ModMetadataFixture(Kind.Spt40)
            .Constructor(il => il
                .SetString("ModGuid", "com.example.mod")
                .SetString("Name", "Example Mod")
                .SetString("Author", "Someone")
                .This().Null().Set("Contributors")
                .SetVersion("Version", "1.4.2")
                .SetRange("SptVersion", "~4.0.0")
                .This().Null().Set("ModDependencies")
                .SetString("License", "MIT"))
            .Write(_dir);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.example.mod", metadata.Guid);
        Assert.Equal("Example Mod", metadata.Name);
        Assert.Equal("Someone", metadata.Author);
        Assert.Equal("1.4.2", metadata.Version);
        Assert.Equal("~4.0.0", metadata.SptVersion);
        Assert.Empty(metadata.Dependencies);
    }

    // The shape WTT - Black Division 1.1.0 ships: the version built from three integers, which a
    // search of the DLL's strings cannot find, and a dependency dictionary of GUID -> range.
    [Fact]
    public void ReadServer_IntegerVersion_AndDependencyDictionary()
    {
        var dll = new ModMetadataFixture(Kind.Spt40)
            .Constructor(il =>
            {
                il.SetString("ModGuid", "com.blackdiv.tacticaltoaster");
                SetIntVersion(il, 1, 1, 0);
                il.SetRange("SptVersion", "~4.0.0");
                il.This().NewDictionary()
                    .Dup().Str("com.morebotsapi.tacticaltoaster").Str(">=2.0.0").Int(0).NewRange().DictionaryAdd()
                    .Dup().Str("com.wtt.commonlib").Str(">=2.0.0").Int(0).NewRange().DictionaryAdd()
                    .Dup().Str("com.wtt.contentbackport").Str(">=1.0.0").Int(0).NewRange().DictionaryAdd()
                    .Set("ModDependencies");
            })
            .Write(_dir);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.blackdiv.tacticaltoaster", metadata.Guid);
        Assert.Equal("1.1.0", metadata.Version);
        Assert.Equal(
            [
                new ModDependencyRef("com.morebotsapi.tacticaltoaster", false, ">=2.0.0"),
                new ModDependencyRef("com.wtt.commonlib", false, ">=2.0.0"),
                new ModDependencyRef("com.wtt.contentbackport", false, ">=1.0.0"),
            ],
            metadata.Dependencies);
    }

    [Fact]
    public void ReadServer_IntegerVersion_WithPreRelease()
    {
        var dll = new ModMetadataFixture(Kind.Spt41)
            .Constructor(il => SetIntVersion(il.SetString("ModGuid", "com.example.pre"), 2, 0, 0, "beta.3"))
            .Write(_dir);

        Assert.Equal("2.0.0-beta.3", ModAssemblyMetadata.ReadServer(dll)?.Version);
    }

    [Fact]
    public void ReadServer_DependencyDictionary_IndexerInitialiser()
    {
        var dll = new ModMetadataFixture(Kind.Spt41)
            .Constructor(il => il
                .SetString("ModGuid", "com.example.indexer")
                .This().NewDictionary()
                    .Dup().Str("com.wtt.commonlib").Str("~2.0.0").Int(0).NewRange().DictionarySetItem()
                    .Set("ModDependencies"))
            .Write(_dir);

        Assert.Equal(
            [new ModDependencyRef("com.wtt.commonlib", false, "~2.0.0")],
            ModAssemblyMetadata.ReadServer(dll)?.Dependencies);
    }

    [Fact]
    public void ReadServer_Spt41Interface()
    {
        var dll = new ModMetadataFixture(Kind.Spt41)
            .Constructor(il => il
                .SetString("ModGuid", "com.example.spt41")
                .SetVersion("Version", "0.3.0")
                .SetRange("SptVersion", ">=4.1.0 <4.2.0")
                .This().Int(0).Set("HasPrepatcher"))
            .Write(_dir);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.example.spt41", metadata.Guid);
        Assert.Equal("0.3.0", metadata.Version);
        Assert.Equal(">=4.1.0 <4.2.0", metadata.SptVersion);
    }

    // What "public string ModGuid => "x";" compiles to: a getter that loads the value and returns.
    [Fact]
    public void ReadServer_ExpressionBodiedProperties()
    {
        var dll = new ModMetadataFixture(Kind.Spt41)
            .Constructor(_ => { })
            .Getter("ModGuid", il => il.Str("com.example.getters"))
            .Getter("Name", il => il.Str("Getters"))
            .Getter("Version", il => il.Int(3).Int(2).Int(1).Null().Null().NewVersionFromInts())
            .Getter("SptVersion", il => il.Str("~4.1.0").Int(0).NewRange())
            .Write(_dir);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Equal("com.example.getters", metadata.Guid);
        Assert.Equal("Getters", metadata.Name);
        Assert.Equal("3.2.1", metadata.Version);
        Assert.Equal("~4.1.0", metadata.SptVersion);
    }

    [Fact]
    public void ReadServer_ComputedValues_StayUnknown()
    {
        var dll = new ModMetadataFixture(Kind.Spt41)
            .StaticStringMethod("Build")
            .Constructor(il => il
                .This().CallStatic("Build").Set("ModGuid")
                .SetString("Name", "Computed")
                .This().CallStatic("Build").Int(0).NewVersionFromString().Set("Version")
                .SetRange("SptVersion", "~4.1.0"))
            .Write(_dir);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Guid);
        Assert.Null(metadata.Version);
        Assert.Equal("Computed", metadata.Name);
        Assert.Equal("~4.1.0", metadata.SptVersion);
    }

    // A record has a second, compiler-generated constructor that copies every field from another
    // instance. It must not wipe what the real constructor set, whichever order they are read in.
    [Fact]
    public void ReadServer_CopyConstructor_LeavesValuesAlone()
    {
        var dll = new ModMetadataFixture(Kind.Spt40)
            .CopyConstructor("ModGuid", "Version")
            .Constructor(il => il.SetString("ModGuid", "com.example.copy").SetVersion("Version", "1.0.0"))
            .CopyConstructor("ModGuid", "Version")
            .Write(_dir);

        var metadata = ModAssemblyMetadata.ReadServer(dll);

        Assert.Equal("com.example.copy", metadata?.Guid);
        Assert.Equal("1.0.0", metadata?.Version);
    }

    [Fact]
    public void ReadServer_NoMetadataType_IsNull()
    {
        var dll = new ModMetadataFixture(Kind.Plain)
            .Constructor(il => il.SetString("ModGuid", "com.not.a.mod"))
            .Write(_dir);

        Assert.Null(ModAssemblyMetadata.ReadServer(dll));
    }

    [Fact]
    public void ReadServer_NotManagedCode_IsNull()
    {
        var path = Path.Combine(_dir, "native.dll");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x01, 0x02]);

        Assert.Null(ModAssemblyMetadata.ReadServer(path));
        Assert.True(ModAssemblyMetadata.ReadPlugin(path).IsEmpty);
    }

    [Fact]
    public void ReadServer_MissingFile_IsNull() =>
        Assert.Null(ModAssemblyMetadata.ReadServer(Path.Combine(_dir, "missing.dll")));

    // Real compiler output from Chris's own server mods (MIT). Configurable Softcore's record was
    // never bumped past 0.0.1, which is exactly what it declares.
    [Theory]
    [InlineData("ConfigurableSoftcore.Server.dll", "com.thecrimsonfuckr.configurablesoftcore", "Configurable Softcore", "0.0.1", "~4.0.0")]
    [InlineData("TCFModSync.Server.dll", "com.thecrimsonfuckr.tcfmodsync", "TCF-ModSync", "2.0.0", "~4.0.0")]
    [InlineData("TCFMM.ServerMap.Stub.Spt40.dll", "com.thecrimsonfuckr.servermap", "TCFMM Server Map", "0.2.0", ">=4.0.0 <4.1.0")]
    [InlineData("TCFMM.ServerMap.Stub.Spt41.dll", "com.thecrimsonfuckr.servermap", "TCFMM Server Map", "0.2.1", ">=4.1.0 <4.2.0")]
    public void ReadServer_RealServerMods(string file, string guid, string name, string version, string sptVersion)
    {
        var metadata = ModAssemblyMetadata.ReadServer(Fixture(file));

        Assert.NotNull(metadata);
        Assert.Equal(guid, metadata.Guid);
        Assert.Equal(name, metadata.Name);
        Assert.Equal("TheCrimsonFuckr", metadata.Author);
        Assert.Equal(version, metadata.Version);
        Assert.Equal(sptVersion, metadata.SptVersion);
        Assert.Empty(metadata.Dependencies);
    }

    [Fact]
    public void ReadPlugin_ReadsGuidNameVersionAndDependencies()
    {
        var dll = new ModMetadataFixture(Kind.Plain)
            .Constructor(_ => { })
            .Plugin("com.blackdiv.tacticaltoaster", "BlackDiv", "1.1.0")
            .Dependency("com.wtt.commonlib", flags: 1)
            .Dependency("com.optional.thing", flags: 2)
            .Dependency("com.morebotsapi.tacticaltoaster", "2.0.0")
            .Write(_dir);

        var metadata = ModAssemblyMetadata.ReadPlugin(dll);

        Assert.Equal("com.blackdiv.tacticaltoaster", metadata.Guid);
        Assert.Equal("BlackDiv", metadata.Name);
        Assert.Equal("1.1.0", metadata.Version);
        Assert.Equal(
            [
                new ModDependencyRef("com.morebotsapi.tacticaltoaster", false, ">=2.0.0"),
                new ModDependencyRef("com.optional.thing", true),
                new ModDependencyRef("com.wtt.commonlib", false),
            ],
            metadata.Dependencies.OrderBy(d => d.Identifier, StringComparer.Ordinal));
        Assert.Null(metadata.SptVersion);
    }

    [Fact]
    public void ReadPlugin_NoPluginAttribute_IsEmpty()
    {
        var dll = new ModMetadataFixture(Kind.Plain).Constructor(_ => { }).Write(_dir);

        Assert.True(ModAssemblyMetadata.ReadPlugin(dll).IsEmpty);
    }

    [Fact]
    public void ReadPlugin_ServerModDll_HasNoPlugin()
    {
        Assert.Null(ModAssemblyMetadata.ReadPlugin(Fixture("ConfigurableSoftcore.Server.dll")).Guid);
    }
}
