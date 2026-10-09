using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// TEMPORARY, ADDED IN v1.5.0 - DELETE WHEN THE APP LEAVES BETA, along with
// AppPaths.MigrateLegacyConfigsFolder.
//
// Covers the one-shot move of a pre-v1.5.0 LegacyConfigs folder into Data\. Worth a test despite
// being throwaway: it runs once on somebody's machine, on a folder holding config files they chose
// to keep, and a silent failure would not be noticed until they went looking for them.
//
// These paths are relative to AppContext.BaseDirectory (the test assembly's own folder) rather than
// a temp directory, because that is what the method resolves - so each test clears both folders
// before and after itself.
//
// Run on its own, never beside another class: Clear deletes the whole of Data\LegacyConfigs, and
// that is where every install test's update copies a config aside before merging it. Deleted in
// between, the merge found its copy gone ("couldn't merge ... Could not find a part of the path")
// and ForkPrepatchInstallTests failed - in 3 of 15 runs of the suite, measured 2026-10-08.
//
[Collection(AloneWithLegacyConfigsCollection.Name)]
public class AppPathsTests : IDisposable
{
    private static readonly string OldFolder = Path.Combine(AppContext.BaseDirectory, "LegacyConfigs");

    public AppPathsTests() => Clear();

    public void Dispose() => Clear();

    private static void Clear()
    {
        if (Directory.Exists(OldFolder)) Directory.Delete(OldFolder, recursive: true);
        if (Directory.Exists(AppPaths.LegacyConfigsDirectory)) Directory.Delete(AppPaths.LegacyConfigsDirectory, recursive: true);
    }

    private static void WriteKeptConfig(string root, string name)
    {
        var path = Path.Combine(root, "20260101-120000_SomeMod", "user", "mods", "SomeMod", "config", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }

    [Fact]
    public void MigrateLegacyConfigsFolder_MovesAPreV150FolderIntoData()
    {
        WriteKeptConfig(OldFolder, "config.json");

        AppPaths.MigrateLegacyConfigsFolder();

        Assert.False(Directory.Exists(OldFolder));
        Assert.True(File.Exists(Path.Combine(
            AppPaths.LegacyConfigsDirectory, "20260101-120000_SomeMod", "user", "mods", "SomeMod", "config", "config.json")));
    }

    [Fact]
    public void MigrateLegacyConfigsFolder_DoesNothingWhenThereIsNoOldFolder()
    {
        AppPaths.MigrateLegacyConfigsFolder();

        Assert.False(Directory.Exists(AppPaths.LegacyConfigsDirectory));
    }

    [Fact]
    public void MigrateLegacyConfigsFolder_LeavesBothAloneWhenTheNewOneAlreadyExists()
    {
        // Merging two trees could overwrite a kept config, so neither side is touched.
        WriteKeptConfig(OldFolder, "old.json");
        WriteKeptConfig(AppPaths.LegacyConfigsDirectory, "new.json");

        AppPaths.MigrateLegacyConfigsFolder();

        Assert.True(File.Exists(Path.Combine(
            OldFolder, "20260101-120000_SomeMod", "user", "mods", "SomeMod", "config", "old.json")));
        Assert.True(File.Exists(Path.Combine(
            AppPaths.LegacyConfigsDirectory, "20260101-120000_SomeMod", "user", "mods", "SomeMod", "config", "new.json")));
    }

    [Fact]
    public void MigrateLegacyConfigsFolder_IsANoOpTheSecondTime()
    {
        WriteKeptConfig(OldFolder, "config.json");

        AppPaths.MigrateLegacyConfigsFolder();
        AppPaths.MigrateLegacyConfigsFolder();

        Assert.False(Directory.Exists(OldFolder));
        Assert.True(Directory.Exists(AppPaths.LegacyConfigsDirectory));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AloneWithLegacyConfigsCollection
{
    public const string Name = "Alone with Data\\LegacyConfigs";
}
