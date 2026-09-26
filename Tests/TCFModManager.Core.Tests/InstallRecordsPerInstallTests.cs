using System.IO.Compression;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Each SPT install keeps its own install records (and copies of the files its installs replaced);
// the single list kept before is shared out the first time each install is opened, by which
// install the records' files are in.
//
public class InstallRecordsPerInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcf-records-" + Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly string _a;
    private readonly string _b;

    public InstallRecordsPerInstallTests()
    {
        _data = Path.Combine(_root, "Data");
        _a = Path.Combine(_root, "SPT A");
        _b = Path.Combine(_root, "SPT B");
        foreach (var install in new[] { _a, _b })
        {
            Directory.CreateDirectory(Path.Combine(install, "BepInEx", "plugins"));
            Directory.CreateDirectory(Path.Combine(install, "SPT", "user", "mods"));
            File.WriteAllText(Path.Combine(install, "SPT", "SPT.Server.exe"), "");
        }

        Directory.CreateDirectory(_data);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static void Put(string install, string relative, string text = "x")
    {
        var full = Path.Combine(install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static InstalledModRecord Record(int id, params string[] files) => new()
    {
        ModId = id,
        Name = "Mod " + id,
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UtcNow,
        Files = [.. files],
        Folders = InstalledModFolders.FromPlacedFiles([.. files]),
    };

    private void Legacy(params InstalledModRecord[] records) =>
        File.WriteAllText(Path.Combine(_data, "installed-mods.json"),
            JsonSerializer.Serialize(new ModInstallManifest { Mods = [.. records] }));

    private List<int> LegacyIds() =>
        JsonSerializer.Deserialize<ModInstallManifest>(File.ReadAllText(Path.Combine(_data, "installed-mods.json")))!
            .Mods.Select(m => m.ModId).Order().ToList();

    [Fact]
    public void TheOldList_IsSharedOutByWhichInstallTheFilesAreIn()
    {
        Put(_a, "BepInEx/plugins/ModOne/One.dll");
        Put(_b, "BepInEx/plugins/ModTwo/Two.dll");
        Legacy(
            Record(1, "BepInEx/plugins/ModOne/One.dll"),
            Record(2, "BepInEx/plugins/ModTwo/Two.dll"),
            Record(3, "BepInEx/plugins/Gone/Gone.dll"));

        // A copy of a file mod 1 replaced, kept under the old shared folder.
        var oldCopy = Path.Combine(_data, "ReplacedFiles", "mod-1", "BepInEx", "plugins", "shared.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(oldCopy)!);
        File.WriteAllText(oldCopy, "under mod 1");

        var service = new ModInstallManifestService(_data);

        Assert.Equal([1], service.Load(_a).Mods.Select(m => m.ModId));
        Assert.Equal([2, 3], LegacyIds());
        Assert.False(File.Exists(oldCopy));
        Assert.Equal("under mod 1", File.ReadAllText(
            Path.Combine(service.ReplacedFilesRootFor(_a), "mod-1", "BepInEx", "plugins", "shared.dll")));

        Assert.Equal([2], service.Load(_b).Mods.Select(m => m.ModId));
        Assert.Equal([3], LegacyIds());

        // Once only: a later look at A takes nothing more, even with mod 3's file there now.
        Put(_a, "BepInEx/plugins/Gone/Gone.dll");
        Assert.Equal([1], service.Load(_a).Mods.Select(m => m.ModId));
        Assert.Equal([3], LegacyIds());
    }

    [Fact]
    public void ADisabledModsRecord_AndAVersionConfirmedByHand_GoWithTheirInstall()
    {
        // Disabled: its file is in the ".disabled" folder beside where it was placed.
        Put(_a, "BepInEx/plugins.disabled/Off/Off.dll");

        // Confirmed by hand: no files, just the folder it was confirmed for.
        Put(_a, "BepInEx/plugins/ByHand/ByHand.dll");
        var byHand = new InstalledModRecord
        {
            ModId = 5, Name = "By hand", Version = "2.0.0", InstalledAt = DateTimeOffset.UtcNow,
            IsAppManaged = false, Folders = ["ByHand"],
        };

        Legacy(Record(4, "BepInEx/plugins/Off/Off.dll"), byHand);

        var service = new ModInstallManifestService(_data);

        Assert.Equal([], service.Load(_b).Mods.Select(m => m.ModId));
        Assert.Equal([4, 5], service.Load(_a).Mods.Select(m => m.ModId).Order());
    }

    [Fact]
    public async Task AModInstalledOnOneInstall_IsNotRecordedOnAnother()
    {
        var service = new ModInstallManifestService(_data);
        var installer = new ModInstallService(new ModDownloadService(), service,
            new ConfigCarryOver(new ConfigBaselineStore(Path.Combine(_root, "baselines")), Path.Combine(_root, "legacy"), new ModConfigOptionsStore(Path.Combine(_root, "options.json"))),
            new ConfigUpdateLog(Path.Combine(_root, "config_updates.json")),
            new ModConfigOptionsStore(Path.Combine(_root, "options.json")));

        var zip = Path.Combine(_root, "mod.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("BepInEx/plugins/Mod/Mod.dll").Open());
            writer.Write("mod");
        }

        // The same mod, already there by hand on B.
        Put(_b, "BepInEx/plugins/Mod/Mod.dll", "by hand on B");

        await installer.InstallAsync(
            new InstallTarget(7, false, "Mod", null, null, null),
            new ModVersion { Id = 70, Version = "1.2.0", Link = "https://example.invalid/mod.zip" },
            _a,
            downloadedArchive: zip);

        Assert.Equal("1.2.0", Assert.Single(service.Load(_a).Mods).Version);
        Assert.Empty(service.Load(_b).Mods);
        Assert.Equal("by hand on B", File.ReadAllText(Path.Combine(_b, "BepInEx", "plugins", "Mod", "Mod.dll")));

        // Each install's records name it.
        Assert.Equal(_a, File.ReadAllText(Path.Combine(service.FolderFor(_a), "install.txt")));
    }

    [Fact]
    public void NoInstall_NoRecords()
    {
        Legacy(Record(1, "BepInEx/plugins/ModOne/One.dll"));

        Assert.Empty(new ModInstallManifestService(_data).Load(null).Mods);
        Assert.Equal([1], LegacyIds());
    }

    [Fact]
    public void TheSameInstallWrittenDifferently_IsTheSameRecords()
    {
        var service = new ModInstallManifestService(_data);

        Assert.Equal(service.FolderFor(_a), service.FolderFor(_a + Path.DirectorySeparatorChar));
        Assert.NotEqual(service.FolderFor(_a), service.FolderFor(_b));
    }
}
