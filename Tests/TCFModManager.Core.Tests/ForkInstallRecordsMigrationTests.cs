using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork: the per-install record lists (Data\InstallRecords\<key>\) moved into 1.19's single list.
public class ForkInstallRecordsMigrationTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "tcfmm-forkrec-" + Guid.NewGuid().ToString("N"));

    public ForkInstallRecordsMigrationTests() => Directory.CreateDirectory(_data);

    public void Dispose()
    {
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }

    private void ForkList(string key, string install, string json)
    {
        var folder = Path.Combine(_data, ForkInstallRecordsMigration.FolderName, key);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "install.txt"), install);
        File.WriteAllText(Path.Combine(folder, "installed-mods.json"), json);
    }

    private static string Record(int id, string name, string version) =>
        $$"""{ "ModId": {{id}}, "Name": "{{name}}", "Version": "{{version}}", "InstalledAt": "2026-09-27T10:00:00+00:00", "Files": ["BepInEx/plugins/{{name}}/{{name}}.dll"], "Replaced": [] }""";

    private ModInstallManifest Global() => new ModInstallManifestService(Path.Combine(_data, "installed-mods.json")).Load();

    [Fact]
    public void Records_AreMovedIn_StampedWithTheirInstall_AndTheFolderIsKeptAside()
    {
        var install = Path.Combine(_data, "SPT");
        ForkList("aaa", install, $$"""{ "Mods": [ {{Record(1, "One", "1.0.0")}} ] }""");

        Assert.Equal(1, ForkInstallRecordsMigration.Run(_data, install));

        var record = Assert.Single(Global().Mods);
        Assert.Equal("One", record.Name);
        Assert.Equal(InstallStamp.Of(install), record.InstallPath);
        Assert.Equal(["BepInEx/plugins/One/One.dll"], record.Files);
        Assert.False(Directory.Exists(Path.Combine(_data, ForkInstallRecordsMigration.FolderName)));
        Assert.True(File.Exists(Path.Combine(_data, ForkInstallRecordsMigration.MovedFolderName, "aaa", "installed-mods.json")));
    }

    [Fact]
    public void TheCurrentInstall_WinsAMod_AnotherInstallAlsoHas()
    {
        var current = Path.Combine(_data, "SPT-A");
        var other = Path.Combine(_data, "SPT-B");
        ForkList("bbb", other, $$"""{ "Mods": [ {{Record(5, "Shared", "2.0.0")}}, {{Record(6, "OnlyB", "1.0.0")}} ] }""");
        ForkList("aaa", current, $$"""{ "Mods": [ {{Record(5, "Shared", "3.0.0")}} ] }""");

        Assert.Equal(2, ForkInstallRecordsMigration.Run(_data, current));

        var mods = Global().Mods;
        Assert.Equal(2, mods.Count);
        Assert.Equal("3.0.0", mods.Single(m => m.ModId == 5).Version);
        Assert.Equal(InstallStamp.Of(current), mods.Single(m => m.ModId == 5).InstallPath);
        Assert.Equal(InstallStamp.Of(other), mods.Single(m => m.ModId == 6).InstallPath);
    }

    [Fact]
    public void TheCurrentInstalls_Record_ReplacesAnUnstampedOneWaitingInTheOldList()
    {
        var install = Path.Combine(_data, "SPT");
        File.WriteAllText(Path.Combine(_data, "installed-mods.json"), $$"""{ "Mods": [ {{Record(1, "One", "0.9.0")}} ] }""");
        ForkList("aaa", install, $$"""{ "Mods": [ {{Record(1, "One", "1.0.0")}} ] }""");

        ForkInstallRecordsMigration.Run(_data, install);

        Assert.Equal("1.0.0", Assert.Single(Global().Mods).Version);
    }

    [Fact]
    public void NoForkFolder_DoesNothing()
    {
        Assert.Equal(0, ForkInstallRecordsMigration.Run(_data, Path.Combine(_data, "SPT")));
        Assert.False(File.Exists(Path.Combine(_data, "installed-mods.json")));
    }
}
