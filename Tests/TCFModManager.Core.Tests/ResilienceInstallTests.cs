using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Stage 3 of CLOSED-10-TCFResilience-DESIGN.md: the install side. Each test runs a real InstallAsync
// against a throwaway SPT layout (EscapeFromTarkov.exe at the root, the server under SPT\), with the
// archive served from memory, and asserts on what is on disk and in the record afterwards.
//
public class ResilienceInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-install-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    // Ids no real catalog entry will collide with in the test Data folder.
    private static int _nextId = 970_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public ResilienceInstallTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }

        foreach (var id in _ids)
        {
            foreach (var key in new[] { id.ToString(), $"{id}-addon" })
            {
                var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, key);
                try
                {
                    if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true);
                    else if (File.Exists(kept)) File.Delete(kept);
                }
                catch (IOException) { }
            }
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

    private Task<ModInstallResult> Install(InstallTarget target, string version, params (string Path, string Content)[] files)
    {
        var service = new ModInstallService(
            new ModDownloadService(new HttpClient(new ArchiveHandler(ArchiveFileTests.Zip(files)))),
            _manifest);

        return service.InstallAsync(target, new ModVersion { Id = 1, Version = version, Link = "https://example.test/a" }, _install);
    }

    // --- Protected files (D3-D5, D7) -----------------------------------------------------------

    [Fact]
    public async Task ProtectedFilesInTheArchive_AreNeitherPlacedNorRecorded_AndAreReported()
    {
        Write("BepInEx/plugins/spt/spt-core.dll", "SPT's own");
        var target = NewTarget();

        var result = await Install(target, "1.0.0",
            ("BepInEx/plugins/spt/spt-core.dll", "an old copy"),
            ("BepInEx/core/BepInEx.dll", "an old BepInEx"),
            ("user/profiles/x.json", "someone's save"),
            ("BepInEx/plugins/Mod/mod.dll", "the mod"));

        Assert.Equal("SPT's own", File.ReadAllText(Full("BepInEx/plugins/spt/spt-core.dll")));
        Assert.False(File.Exists(Full("BepInEx/core/BepInEx.dll")));
        Assert.False(File.Exists(Full("SPT/user/profiles/x.json")));
        Assert.Equal("the mod", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));

        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], result.Record.Files);
        Assert.Equal(3, result.SkippedProtected!.Count);
        Assert.Contains("SPT/user/profiles/x.json", result.SkippedProtected);
    }

    // R17 replaced "never": a loose file is placed in the install root only where nothing is there.
    // Fork: read-mes, licences and pictures at the top are never placed at all (ArchiveLayout.
    // IsTopLevelBeside), so the files here that show R17 are ones the game could be reading.
    [Fact]
    public async Task LooseFilesBesideTheArchiveRoots_LandInTheInstallRootOnlyWhereNothingIsThere()
    {
        var target = NewTarget();
        Write("doorstop_config.ini", "SPT's doorstop");

        var result = await Install(target, "1.0.0",
            ("README.txt", "readme"),
            ("LICENSE", "the mod's licence"),
            ("extra.ini", "the mod's settings"),
            ("doorstop_config.ini", "the mod's doorstop"),
            ("BepInEx/plugins/Mod/mod.dll", "the mod"));

        Assert.False(File.Exists(Full("README.txt")));
        Assert.False(File.Exists(Full("LICENSE")));
        Assert.Equal("the mod's settings", File.ReadAllText(Full("extra.ini")));
        Assert.Equal("SPT's doorstop", File.ReadAllText(Full("doorstop_config.ini")));
        Assert.Equal(["BepInEx/plugins/Mod/mod.dll", "extra.ini"], result.Record.Files.Order());
        Assert.Equal(["doorstop_config.ini"], result.SkippedProtected);
    }

    // --- Fingerprints and stamp (D21, D17) -----------------------------------------------------

    [Fact]
    public async Task EveryPlacedFile_IsFingerprinted_AndTheRecordIsStamped()
    {
        var target = NewTarget();

        var result = await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("user/mods/ModServer/mod.dll", "server half"));

        var saved = Assert.Single(_manifest.Load().Mods);
        Assert.Equal(2, saved.Fingerprints.Count);
        Assert.True(saved.FingerprintFor("BepInEx/plugins/Mod/mod.dll")!.Matches(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.True(saved.FingerprintFor("SPT/user/mods/ModServer/mod.dll")!.Matches(Full("SPT/user/mods/ModServer/mod.dll")));
        Assert.Equal(InstallStamp.Of(_install), saved.InstallPath);
        Assert.Equal(saved.Fingerprints.Count, result.Record.Fingerprints.Count);
    }

    // --- Originals (D22) -----------------------------------------------------------------------

    [Fact]
    public async Task AFileNoRecordOwns_IsKeptBeforeItIsReplaced()
    {
        Write("BepInEx/plugins/Other/shared.dll", "the original");
        Write("BepInEx/plugins/Mod/mod.dll", "an earlier hand-installed copy");
        var target = NewTarget();

        var result = await Install(target, "1.0.0",
            ("BepInEx/plugins/Other/shared.dll", "replacement"),
            ("BepInEx/plugins/Mod/mod.dll", "the mod"));

        Assert.Equal("replacement", File.ReadAllText(Full("BepInEx/plugins/Other/shared.dll")));
        Assert.Equal(2, result.Record.Overwrote.Count);

        var shared = result.Record.Overwrote.Single(o => o.Path == "BepInEx/plugins/Other/shared.dll");
        Assert.Equal("the original", File.ReadAllText(Path.Combine(AppPaths.DataDirectory, shared.BackupPath)));
        Assert.False(shared.SameMod);

        var own = result.Record.Overwrote.Single(o => o.Path == "BepInEx/plugins/Mod/mod.dll");
        Assert.Equal("an earlier hand-installed copy", File.ReadAllText(Path.Combine(AppPaths.DataDirectory, own.BackupPath)));
        Assert.False(own.SameMod);
    }

    [Fact]
    public async Task AnEarlierCopyOfTheSameMod_ProvenByItsGuid_IsMarkedSameMod()
    {
        Directory.CreateDirectory(Full("BepInEx/plugins/Mod"));
        new ModMetadataFixture(ModMetadataFixture.Kind.Plain)
            .Plugin("com.test.samemod", "Mod", "0.9.0")
            .Write(Full("BepInEx/plugins/Mod"), "Mod.dll");

        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        var target = new InstallTarget(id, false, "Mod", "com.test.samemod", null, null);

        var result = await Install(target, "1.0.0", ("BepInEx/plugins/Mod/Mod.dll", "the new version"));

        Assert.True(Assert.Single(result.Record.Overwrote).SameMod);
    }

    [Fact]
    public async Task AFolderDeclaringADifferentGuid_IsNotTheSameMod()
    {
        Directory.CreateDirectory(Full("BepInEx/plugins/Parent"));
        new ModMetadataFixture(ModMetadataFixture.Kind.Plain)
            .Plugin("com.test.parent", "Parent", "1.0.0")
            .Write(Full("BepInEx/plugins/Parent"), "Parent.dll");

        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        var target = new InstallTarget(id, false, "Patch", "com.test.patch", null, null);

        var result = await Install(target, "1.0.0", ("BepInEx/plugins/Parent/Parent.dll", "patched"));

        Assert.False(Assert.Single(result.Record.Overwrote).SameMod);
    }

    [Fact]
    public async Task AnAddon_IsNeverTheSameMod()
    {
        Directory.CreateDirectory(Full("BepInEx/plugins/Parent"));
        new ModMetadataFixture(ModMetadataFixture.Kind.Plain)
            .Plugin("com.test.parent2", "Parent", "1.0.0")
            .Write(Full("BepInEx/plugins/Parent"), "Parent.dll");

        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        var target = new InstallTarget(id, true, "Addon", null, null, null);

        var result = await Install(target, "1.0.0", ("BepInEx/plugins/Parent/Parent.dll", "addon's copy"));

        Assert.False(Assert.Single(result.Record.Overwrote).SameMod);
    }

    [Fact]
    public async Task AFileInAnotherModsFolder_IsKeptAsNotItsOwn()
    {
        Write("BepInEx/plugins/Other/other.dll", "the other mod's");
        var target = NewTarget();

        var result = await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("BepInEx/config/shared.cfg", "the mod's settings"));

        Assert.Empty(result.Record.Overwrote);

        Write("BepInEx/config/other.cfg", "someone else's settings");
        var second = NewTarget();
        var result2 = await Install(second, "1.0.0",
            ("BepInEx/plugins/Second/s.dll", "second"),
            ("BepInEx/config/other.cfg", "replacement settings"));

        var kept = Assert.Single(result2.Record.Overwrote);
        Assert.Equal("BepInEx/config/other.cfg", kept.Path);
        Assert.False(kept.SameMod);
        Assert.Equal("someone else's settings", File.ReadAllText(Path.Combine(AppPaths.DataDirectory, kept.BackupPath)));
    }

    [Fact]
    public async Task AFileAnotherRecordOwns_IsNotTreatedAsAnOriginal()
    {
        var first = NewTarget();
        await Install(first, "1.0.0", ("BepInEx/plugins/Shared/lib.dll", "first's copy"));

        var second = NewTarget();
        var result = await Install(second, "1.0.0", ("BepInEx/plugins/Shared/lib.dll", "second's copy"));

        Assert.Empty(result.Record.Overwrote);
        Assert.Equal("second's copy", File.ReadAllText(Full("BepInEx/plugins/Shared/lib.dll")));
    }

    [Fact]
    public async Task AnUpdate_CarriesTheOriginalsForward_AndDoesntKeepItsOwnOldVersion()
    {
        Write("BepInEx/config/other.cfg", "someone else's settings");
        var target = NewTarget();

        await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "v1"),
            ("BepInEx/config/other.cfg", "v1 settings"));

        var result = await Install(target, "2.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "v2"),
            ("BepInEx/config/other.cfg", "v2 settings"));

        var kept = Assert.Single(result.Record.Overwrote);
        Assert.Equal("someone else's settings", File.ReadAllText(Path.Combine(AppPaths.DataDirectory, kept.BackupPath)));
        Assert.Equal("v2", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
    }

    [Fact]
    public async Task AnOriginalThatCantBeKept_StopsTheInstallBeforeAnythingChanges()
    {
        Write("BepInEx/config/other.cfg", "someone else's settings");
        var target = NewTarget();

        // A file where the mod's keep-folder has to go makes the copy impossible.
        var keepFolder = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, target.Id.ToString());
        Directory.CreateDirectory(Path.GetDirectoryName(keepFolder)!);
        File.WriteAllText(keepFolder, "in the way");

        var ex = await Assert.ThrowsAsync<ModInstallException>(() => Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("BepInEx/config/other.cfg", "replacement")));

        Assert.Equal(ModInstallFailure.OriginalNotKept, ex.Reason);
        Assert.Equal("BepInEx/config/other.cfg", ex.Folder);
        Assert.Equal("someone else's settings", File.ReadAllText(Full("BepInEx/config/other.cfg")));
        Assert.False(File.Exists(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Empty(_manifest.Load().Mods);
    }

    // --- Links (placement side) ----------------------------------------------------------------

    [Fact]
    public async Task InstallingThroughALinkedFolder_IsRefused_AndTheTargetIsUntouched()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "mod.dll"), "the user's own copy");
        Directory.CreateDirectory(Full("BepInEx/plugins"));
        Directory.CreateSymbolicLink(Full("BepInEx/plugins/Mod"), elsewhere);
        var target = NewTarget();

        var ex = await Assert.ThrowsAsync<ModInstallException>(() => Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "replacement")));

        Assert.Equal(ModInstallFailure.InstallThroughLink, ex.Reason);
        Assert.Equal("the user's own copy", File.ReadAllText(Path.Combine(elsewhere, "mod.dll")));
        Assert.Empty(_manifest.Load().Mods);
    }

    // --- Monitor mode (D6) ---------------------------------------------------------------------

    [Fact]
    public void ArchivePlan_LeavesProtectedFilesOut()
    {
        var plan = ArchiveLayout.Plan(
        [
            new ArchiveFileEntry("BepInEx/plugins/spt/spt-core.dll", 5),
            new ArchiveFileEntry("BepInEx/plugins/Mod/mod.dll", 7),
            new ArchiveFileEntry("README.txt", 3),
        ], "SPT");

        Assert.True(plan.Recognised);
        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], plan.Files.Select(f => f.Path));
    }

    [Fact]
    public void DownloadMatcher_DoesntWaitForProtectedFiles()
    {
        Write("BepInEx/plugins/Mod/mod.dll", "1234567");
        var download = new DownloadedModRecord
        {
            ModId = 1,
            Name = "Mod",
            Version = "1.0.0",
            DownloadedAt = DateTimeOffset.UtcNow,
            ArchivePath = "x.zip",
            ExpectedFolders = ["Mod"],
            ExpectedFiles =
            [
                new ExpectedFile("BepInEx/plugins/Mod/mod.dll", 7),
                new ExpectedFile("BepInEx/plugins/spt/spt-core.dll", 99),
            ],
        };

        Assert.Equal(DownloadMatchKind.Installed, DownloadMatcher.Check(download, _install, ["Mod"]).Kind);
    }

    [Fact]
    public void ConfirmDownload_FingerprintsFromDisk_StampsTheInstall_AndLeavesProtectedFilesOut()
    {
        Write("BepInEx/plugins/Mod/mod.dll", "on disk now");
        _manifest.Save(new ModInstallManifest
        {
            Mods =
            [
                new InstalledModRecord
                {
                    ModId = 1, Name = "Mod", Version = "1.0.0", InstalledAt = DateTimeOffset.UtcNow,
                    Files = ["BepInEx/plugins/Mod/mod.dll"],
                },
            ],
        });

        var record = _manifest.ConfirmDownload(new DownloadedModRecord
        {
            ModId = 1,
            Name = "Mod",
            Version = "1.1.0",
            DownloadedAt = DateTimeOffset.UtcNow,
            ArchivePath = "x.zip",
            ExpectedFolders = ["Mod"],
            ExpectedFiles =
            [
                new ExpectedFile("BepInEx/plugins/Mod/mod.dll", 11),
                new ExpectedFile("BepInEx/plugins/spt/spt-core.dll", 99),
            ],
        }, _install);

        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], record.Files);
        Assert.True(Assert.Single(record.Fingerprints).Matches(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal(InstallStamp.Of(_install), record.InstallPath);
    }
}
