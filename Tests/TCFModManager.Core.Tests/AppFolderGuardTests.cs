using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The app's own folder (PathRefusal.AppFolder) when the app is not in a folder of its own. v1.19.0
// treated everything below the exe's folder as the app's, so an exe run straight from the SPT root
// refused every file of every install, update and removal. Each test points the guard at an app
// folder of its own choosing through InstallPathGuard.AppDirectoryForTests.
//
public class AppFolderGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-appfolder-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    private static int _nextId = 980_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public AppFolderGuardTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
    }

    public void Dispose()
    {
        InstallPathGuard.AppDirectoryForTests.Value = null;

        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }

        foreach (var id in _ids)
        {
            var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, id.ToString());
            try
            {
                if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true);
            }
            catch (IOException) { }
        }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private string Write(string relative, string content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private void AppAt(string folder) => InstallPathGuard.AppDirectoryForTests.Value = folder;

    private InstallTarget NewTarget()
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return new InstallTarget(id, false, $"Test {id}", null, null, null);
    }

    private sealed class ArchiveHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private ModInstallService Service(params (string Path, string Content)[] files) => new(
        new ModDownloadService(new HttpClient(new ArchiveHandler(ArchiveFileTests.Zip(files)))),
        _manifest,
        removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));

    private Task<ModInstallResult> Install(InstallTarget target, string version, params (string Path, string Content)[] files) =>
        Service(files).InstallAsync(target, new ModVersion { Id = 1, Version = version, Link = "https://example.test/a" }, _install);

    // --- The guard ------------------------------------------------------------------------------

    [Theory]
    [InlineData("BepInEx/plugins/Mod/mod.dll")]
    [InlineData("SPT/user/mods/Mod/mod.dll")]
    [InlineData("BepInEx/config/com.mod.cfg")]
    public void AppInTheInstallRoot_DoesNotClaimTheMods(string relative)
    {
        AppAt(_install);

        Assert.Null(InstallPathGuard.CheckRecordedPath(_install, relative, out _));
    }

    [Theory]
    [InlineData("SSPTMM.exe")]
    [InlineData("SSPTMM.pdb")]
    [InlineData("Licenses/SharpCompress-LICENSE.txt")]
    [InlineData("TCFModManager.exe")]
    [InlineData("TCFModManager.pdb")]
    [InlineData("Data/installed-mods.json")]
    [InlineData("Staging/a.zip")]
    [InlineData(".tcfmm-update/payload/TCFModManager.exe")]
    [InlineData("LegacyConfigs/x.json")]
    [InlineData("verbose")]
    public void AppInTheInstallRoot_StillClaimsItsOwnItems(string relative)
    {
        AppAt(_install);

        Assert.Equal(PathRefusal.AppFolder, InstallPathGuard.CheckPlacedPath(_install, relative, out _));
    }

    [Fact]
    public void AppInTheServerFolder_DoesNotClaimTheServerMods()
    {
        AppAt(Full("SPT"));

        Assert.Null(InstallPathGuard.CheckRecordedPath(_install, "SPT/user/mods/Mod/mod.dll", out _));
        Assert.Equal(PathRefusal.AppFolder, InstallPathGuard.CheckRecordedPath(_install, "SPT/Data/x.json", out _));
    }

    [Fact]
    public void AppAboveTheInstall_DoesNotClaimAnythingInIt()
    {
        AppAt(_root);

        Assert.Null(InstallPathGuard.CheckRecordedPath(_install, "BepInEx/plugins/Mod/mod.dll", out _));
        Assert.Null(InstallPathGuard.CheckRecordedPath(_install, "BepInEx/plugins/Data/mod.dll", out _));
    }

    // The documented layout keeps v1.19.0's rule: the whole folder is the app's, whatever is in it.
    [Fact]
    public void AppInAFolderOfItsOwn_ClaimsTheWholeFolder()
    {
        AppAt(Full("TCFModManager"));

        Assert.Equal(PathRefusal.AppFolder, InstallPathGuard.CheckPlacedPath(_install, "TCFModManager/anything.txt", out _));
        Assert.Equal(PathRefusal.AppFolder, InstallPathGuard.CheckPlacedPath(_install, "TCFModManager", out _));
        Assert.Null(InstallPathGuard.CheckRecordedPath(_install, "BepInEx/plugins/Mod/mod.dll", out _));
        Assert.Null(InstallPathGuard.CheckRecordedPath(_install, "BepInEx/plugins/Data/mod.dll", out _));
    }

    [Fact]
    public void AppInTheInstallRoot_ModFoldersCanBeRemovedWhole()
    {
        AppAt(_install);
        Write("BepInEx/plugins/Mod/mod.dll", "the mod");

        Assert.Null(InstallPathGuard.CheckModFolder(_install, Full("BepInEx/plugins/Mod")));
        Assert.True(InstallPathGuard.MayRemoveEmptyFolder(_install, Full("BepInEx/plugins/Mod")));
    }

    // --- Installing -----------------------------------------------------------------------------

    [Fact]
    public async Task AppInTheInstallRoot_InstallsUpdatesAndRemoves()
    {
        AppAt(_install);
        var target = NewTarget();

        var first = await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "v1"),
            ("user/mods/ModServer/mod.dll", "server v1"));

        Assert.Equal(2, first.Record.Files.Count);
        Assert.Empty(first.SkippedProtected!);
        Assert.Empty(first.SkippedAppFolder!);
        Assert.Equal("v1", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));

        var update = await Install(target, "1.1.0",
            ("BepInEx/plugins/Mod/mod.dll", "v2"),
            ("user/mods/ModServer/mod.dll", "server v2"));

        Assert.Equal(2, update.Record.Files.Count);
        Assert.Equal("v2", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("server v2", File.ReadAllText(Full("SPT/user/mods/ModServer/mod.dll")));

        var removal = await Service().UninstallAsync(_install, update.Record);

        Assert.Empty(removal.FailedFiles);
        Assert.False(File.Exists(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.False(File.Exists(Full("SPT/user/mods/ModServer/mod.dll")));
    }

    [Fact]
    public async Task AFileLandingOnTheAppsOwnItems_IsLeftOut_AndReportedApartFromSptsFiles()
    {
        AppAt(_install);
        Write("Data/settings.json", "the app's settings");
        var target = NewTarget();

        var result = await Install(target, "1.0.0",
            ("Data/settings.json", "the mod's file"),
            ("BepInEx/plugins/Mod/mod.dll", "the mod"));

        Assert.Equal("the app's settings", File.ReadAllText(Full("Data/settings.json")));
        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], result.Record.Files);
        Assert.Equal(["Data/settings.json"], result.SkippedAppFolder);
        Assert.Empty(result.SkippedProtected!);
    }

    // --- Nothing to place -----------------------------------------------------------------------

    [Fact]
    public async Task AnArchiveWhoseEveryFileIsRefused_FailsWithNothingChanged()
    {
        var target = NewTarget();

        var ex = await Assert.ThrowsAsync<ModInstallException>(() => Install(target, "1.0.0",
            ("BepInEx/core/BepInEx.dll", "an old BepInEx"),
            ("SPT/SPT.Server.exe", "an old server")));

        Assert.Equal(ModInstallFailure.NothingToPlace, ex.Reason);
        Assert.Equal(2, ex.TotalFiles);
        Assert.NotNull(ex.ArchiveEntry);
        Assert.Equal("server", File.ReadAllText(Full("SPT/SPT.Server.exe")));
        Assert.Empty(_manifest.Load().Mods);
    }

    // The v1.19.0 damage: an update that placed nothing swapped the record for an empty one.
    [Fact]
    public async Task AnUpdateWhoseEveryFileIsRefused_LeavesThePreviousVersionAndItsRecord()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "v1"));

        await Assert.ThrowsAsync<ModInstallException>(() => Install(target, "1.1.0",
            ("BepInEx/core/BepInEx.dll", "an old BepInEx")));

        var saved = Assert.Single(_manifest.Load().Mods);
        Assert.Equal("1.0.0", saved.Version);
        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], saved.Files);
        Assert.Equal("v1", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
    }
}
