using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModConfigDiscoveryTests : IDisposable
{
    private readonly string _installRoot;

    public ModConfigDiscoveryTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerConfigDiscoveryTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_installRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
    }

    // BepInEx\config\<name>.cfg, optionally inside a subfolder of it.
    private string ClientConfig(string fileName, string? subFolder = null)
    {
        var folder = ModConfigDiscovery.ClientConfigFolder(_installRoot);
        if (subFolder is not null) folder = Path.Combine(folder, subFolder);

        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "[General]\nEnabled = true\n");
        return path;
    }

    private string ServerModFolder(string name, bool disabled = false)
    {
        var dir = Path.Combine(_installRoot, "user", disabled ? "mods.disabled" : "mods", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "package.json"), $$"""{ "name": "{{name}}" }""");
        return dir;
    }

    private static void WriteJson(string folder, params string[] segments)
    {
        var path = Path.Combine([folder, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }

    private static InstalledMod Client(string name, string? guid = null) => new()
    {
        Name = name,
        Guid = guid,
        Target = InstalledModTarget.Client,
        FolderPath = Path.Combine("BepInEx", "plugins", name),
    };

    // A mod folder registering several plugin GUIDs, as one shipping an API or config-UI assembly
    // alongside its own plugin does.
    private static InstalledMod ClientWithGuids(string name, params string[] guids) => new()
    {
        Name = name,
        Guid = guids.FirstOrDefault(),
        Guids = guids,
        Target = InstalledModTarget.Client,
        FolderPath = Path.Combine("BepInEx", "plugins", name),
    };

    private static InstalledMod Server(string name, string folderPath, bool disabled = false) => new()
    {
        Name = name,
        Target = InstalledModTarget.Server,
        FolderPath = folderPath,
        IsDisabled = disabled,
    };

    [Fact]
    public void Find_MatchesAClientConfigToThePluginGuidItIsNamedAfter()
    {
        ClientConfig("me.sol.sain.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]));

        Assert.Equal(ModConfigSource.Client, entry.Source);
        Assert.Equal("SAIN", entry.ModName);
        Assert.Equal("me.sol.sain", entry.ModGuid);
        Assert.Equal(ModConfigFormat.BepInExCfg, entry.Format);
        Assert.Equal("BepInEx/config/me.sol.sain.cfg", entry.DisplayPath);
    }

    //
    // A mod folder holding several plugin DLLs registers a GUID per DLL and gets a config file per
    // GUID. Taking only the folder's first GUID left every other file in it looking like it belonged
    // to nothing - real case: kmyuhkyuk-KmyTarkovApi ships KmyTarkovApi, KmyTarkovConfiguration,
    // KmyTarkovReflection and KmyTarkovUtils, and only the first was recognised.
    //
    // An SPT 4.x server half often declares the same GUID as its client half. A BepInEx config
    // file is always the client's, whichever half the scan happened to list first.
    [Fact]
    public void Find_ClientConfig_IsNeverClaimedByAServerModSharingTheGuid()
    {
        ClientConfig("com.blackdiv.tacticaltoaster.cfg");

        var server = new InstalledMod
        {
            Name = "BlackDivServer",
            Guid = "com.blackdiv.tacticaltoaster",
            Guids = ["com.blackdiv.tacticaltoaster"],
            Target = InstalledModTarget.Server,
            FolderPath = ServerModFolder("BlackDivServer"),
        };

        var entry = Assert.Single(
            ModConfigDiscovery.Find(_installRoot, [server, Client("BlackDiv", "com.blackdiv.tacticaltoaster")]),
            e => e.Format == ModConfigFormat.BepInExCfg);

        Assert.Equal(ModConfigSource.Client, entry.Source);
        Assert.Equal("BlackDiv", entry.ModName);
    }

    [Fact]
    public void Find_MatchesEveryGuidAModsFolderRegisters()
    {
        ClientConfig("com.kmyuhkyuk.KmyTarkovApi.cfg");
        ClientConfig("com.kmyuhkyuk.KmyTarkovConfiguration.cfg");

        var mod = ClientWithGuids(
            "kmyuhkyuk-KmyTarkovApi",
            "com.kmyuhkyuk.KmyTarkovApi",
            "com.kmyuhkyuk.KmyTarkovConfiguration");

        var entries = ModConfigDiscovery.Find(_installRoot, [mod]);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(ModConfigSource.Client, e.Source));
        Assert.All(entries, e => Assert.Equal("kmyuhkyuk-KmyTarkovApi", e.ModName));

        // Each file reports the GUID that actually wrote it, not the folder's primary one.
        Assert.Equal(
            ["com.kmyuhkyuk.KmyTarkovApi", "com.kmyuhkyuk.KmyTarkovConfiguration"],
            entries.Select(e => e.ModGuid).Order());
    }

    [Fact]
    public void Find_StillMatchesAModThatOnlyCarriesASingleGuid()
    {
        ClientConfig("me.sol.sain.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]));

        Assert.Equal("SAIN", entry.ModName);
    }

    [Fact]
    public void Find_FallsBackToTheModsOwnFolderNameForAPluginThatNamesItsFileAfterItself()
    {
        ClientConfig("Lots of Loot.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("LotsOfLoot", "wtf.archangel.lotsofloot")]));

        Assert.Equal(ModConfigSource.Client, entry.Source);
        Assert.Equal("LotsOfLoot", entry.ModName);

        // The matched mod's GUID comes along whichever tier found it - this file just wasn't named
        // after it.
        Assert.Equal("wtf.archangel.lotsofloot", entry.ModGuid);
    }

    [Fact]
    public void Find_LeavesAConfigUnmatchedWhenTwoModsNormalizeToTheSameName()
    {
        ClientConfig("some-mod.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("SomeMod"), Client("Some Mod")]));

        Assert.Equal(ModConfigSource.Unmatched, entry.Source);
        Assert.Null(entry.ModName);
    }

    [Fact]
    public void Find_MatchesASubfolderOfBepInExConfigNamedAfterTheMod()
    {
        ClientConfig("weapons.cfg", subFolder: "Realism");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("Realism", "com.fontaine.realism")]));

        Assert.Equal(ModConfigSource.Client, entry.Source);
        Assert.Equal("Realism", entry.ModName);
    }

    [Fact]
    public void Find_ReportsAConfigNoInstalledPluginClaimsRatherThanHidingIt()
    {
        ClientConfig("com.someone.removedmod.cfg");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]));

        Assert.Equal(ModConfigSource.Unmatched, entry.Source);
        Assert.Null(entry.ModName);
    }

    [Theory]
    [InlineData("BepInEx.cfg")]
    [InlineData("com.bepis.bepinex.configurationmanager.cfg")]
    public void Find_CallsBepInExsOwnConfigTheFrameworksRatherThanAMods(string fileName)
    {
        ClientConfig(fileName);

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, []));

        Assert.Equal(ModConfigSource.Framework, entry.Source);
        Assert.Null(entry.ModName);
    }

    [Fact]
    public void Find_TakesEveryJsonInsideAServerModsConfigFolder()
    {
        var folder = ServerModFolder("SVM");
        WriteJson(folder, "config", "config.json");
        WriteJson(folder, "config", "nested", "extra.json5");

        var entries = ModConfigDiscovery.Find(_installRoot, [Server("SVM", folder)]);

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(ModConfigSource.Server, e.Source));
        Assert.All(entries, e => Assert.Equal("SVM", e.ModName));
        Assert.All(entries, e => Assert.Equal(ModConfigFormat.Json, e.Format));
    }

    [Fact]
    public void Find_TakesAConventionallyNamedConfigSittingAtTheModsRoot()
    {
        var folder = ServerModFolder("Waypoints");
        WriteJson(folder, "config.json");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Server("Waypoints", folder)]));

        Assert.Equal("config.json", entry.FileName);
    }

    [Fact]
    public void Find_IgnoresAServerModsManifestAndItsOwnData()
    {
        var folder = ServerModFolder("BigMod");

        // package.json is the manifest, not a config, and a mod's data folder is full of JSON that
        // is content rather than settings - offering to edit an item table as if it were a config
        // is worse than not listing it at all.
        WriteJson(folder, "db", "items.json");
        WriteJson(folder, "src", "mod.json");
        WriteJson(folder, "node_modules", "something", "config", "config.json");

        Assert.Empty(ModConfigDiscovery.Find(_installRoot, [Server("BigMod", folder)]));
    }

    [Fact]
    public void Find_LooksInsideAConfigFolderNestedInTheModsSource()
    {
        var folder = ServerModFolder("Nested");
        WriteJson(folder, "src", "config", "values.json");

        Assert.Single(ModConfigDiscovery.Find(_installRoot, [Server("Nested", folder)]));
    }

    [Fact]
    public void Find_FindsADisabledServerModsConfigWhereItNowSitsAndSaysItIsDisabled()
    {
        var folder = ServerModFolder("Parked", disabled: true);
        WriteJson(folder, "config", "config.json");

        var entry = Assert.Single(ModConfigDiscovery.Find(_installRoot, [Server("Parked", folder, disabled: true)]));

        Assert.True(entry.IsModDisabled);
        Assert.Equal("user/mods.disabled/Parked/config/config.json", entry.DisplayPath);
    }

    [Fact]
    public void Find_OrdersModsBeforeTheFrameworkAndAnythingUnclaimedLast()
    {
        ClientConfig("me.sol.sain.cfg");
        ClientConfig("BepInEx.cfg");
        ClientConfig("com.someone.gone.cfg");

        var sources = ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]).Select(e => e.Source);

        Assert.Equal([ModConfigSource.Client, ModConfigSource.Framework, ModConfigSource.Unmatched], sources);
    }

    [Fact]
    public void Find_ReturnsNothingForAnInstallWithNoConfigsAtAll() =>
        Assert.Empty(ModConfigDiscovery.Find(_installRoot, [Client("SAIN", "me.sol.sain")]));
}
