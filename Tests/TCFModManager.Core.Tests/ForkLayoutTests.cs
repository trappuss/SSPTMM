using System.IO.Compression;
using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The two packagings the Steam fork reads that the original refuses (ArchiveLayout, "fork: two more
// layouts"): a wrapper folder with read-me files or pictures beside it, and BepInEx's own plugins/
// and patchers/ without the BepInEx folder around them. Both for an archive's entry list (a
// download-only save) and for an extracted folder (an install), which must agree; and installed for
// real, from an archive already on disk (InstallAsync's downloadedArchive, which Install from file
// and the download queue's kept archives use).
//
public class ForkLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-forklayout-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    private static int _nextId = 960_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public ForkLayoutTests()
    {
        _install = Path.Combine(_root, "install");
        Write(_install, "EscapeFromTarkov.exe", "game");
        Write(_install, "SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
    }

    public void Dispose()
    {
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

        GC.SuppressFinalize(this);
    }

    private static string Write(string root, string relative, string content)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static List<ArchiveFileEntry> Entries(params string[] paths) => [.. paths.Select(p => new ArchiveFileEntry(p, 10))];

    private static List<string> Placed(ArchivePlan plan) => [.. plan.Files.Select(f => f.Path).Order(StringComparer.Ordinal)];

    // ------------------------------------------------------------------ the entry list (download-only)

    [Fact]
    public void Plan_LooksThroughAWrapperWithReadMesAndPicturesBesideIt()
    {
        var plan = ArchiveLayout.Plan(Entries("README.md", "LICENSE", "cover.jpg", "MyMod-1.0/BepInEx/plugins/A.dll"), "");

        Assert.True(plan.Recognised);
        Assert.Equal(["BepInEx/plugins/A.dll"], Placed(plan));
    }

    [Fact]
    public void Plan_PutsBarePluginsAndPatchersUnderBepInEx_AndLeavesWhatIsBesideThemOut()
    {
        var plan = ArchiveLayout.Plan(Entries("plugins/A/A.dll", "patchers/A.Patcher.dll", "readme.txt", "preview.png"), "");

        Assert.True(plan.Recognised);
        Assert.Equal(["BepInEx/patchers/A.Patcher.dll", "BepInEx/plugins/A/A.dll"], Placed(plan));
    }

    [Fact]
    public void Plan_BarePluginsInsideAWrapper()
    {
        var plan = ArchiveLayout.Plan(Entries("README.md", "MyMod/plugins/A.dll"), "");

        Assert.Equal(["BepInEx/plugins/A.dll"], Placed(plan));
    }

    [Fact]
    public void Plan_PluginsWithAnotherFileBesideIt_IsNotGuessedAt()
    {
        Assert.False(ArchiveLayout.Plan(Entries("plugins/A.dll", "B.dll"), "").Recognised);
    }

    [Fact]
    public void Plan_ALoneDll_IsNotGuessedAt()
    {
        // Under SPT 4 a server mod is a DLL too: BepInEx\plugins would be a guess.
        Assert.False(ArchiveLayout.Plan(Entries("A.dll"), "").Recognised);
    }

    // ------------------------------------------------------------------ the extracted folder (install)

    [Fact]
    public void ContentRoot_AgreesWithThePlan()
    {
        var dir = Path.Combine(_root, "extracted");
        Write(dir, "readme.txt", "r");
        Write(dir, "cover.jpg", "c");
        Write(dir, "MyMod-1.0/MyMod/BepInEx/plugins/A.dll", "a");

        Assert.Equal(Path.Combine(dir, "MyMod-1.0", "MyMod"), ArchiveLayout.FindContentRoot(dir));
        Assert.Equal("MyMod-1.0/MyMod/", ArchiveLayout.FindContentPrefix(["readme.txt", "cover.jpg", "MyMod-1.0/MyMod/BepInEx/plugins/A.dll"]));
    }

    [Fact]
    public void ContentRoot_StopsAtABareBepInExFolder()
    {
        var dir = Path.Combine(_root, "extracted");
        Write(dir, "Wrapper/plugins/A.dll", "a");

        var root = ArchiveLayout.FindContentRoot(dir);

        Assert.Equal(Path.Combine(dir, "Wrapper"), root);
        Assert.True(ArchiveLayout.IsBareBepInEx(root));
    }

    [Theory]
    [InlineData("plugins/A/A.dll", "BepInEx/plugins/A/A.dll")]
    [InlineData(@"patchers\A.dll", "BepInEx/patchers/A.dll")]
    [InlineData("readme.txt", null)]
    public void MapBareBepInEx(string relative, string? expected) =>
        Assert.Equal(expected, ArchiveLayout.MapBareBepInEx(relative)?.Replace('\\', '/'));

    [Theory]
    [InlineData("README", true)]
    [InlineData("licence", true)]
    [InlineData("notes.TXT", true)]
    [InlineData("guide.pdf", true)]
    [InlineData("preview.png", false)]
    [InlineData("Mod.dll", false)]
    public void IsDocument(string name, bool expected) => Assert.Equal(expected, ArchiveLayout.IsDocument(name));

    // ------------------------------------------------------------------ installed for real

    private sealed class NoDownloads : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("an archive already on disk must not be downloaded");
    }

    private string ZipOnDisk(params (string Path, string Content)[] files)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        Directory.CreateDirectory(_root);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }

        return path;
    }

    private Task<ModInstallResult> InstallFromDisk(params (string Path, string Content)[] files)
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);

        var service = new ModInstallService(new ModDownloadService(new HttpClient(new NoDownloads())), _manifest);
        var archive = ZipOnDisk(files);

        return service.InstallAsync(
            new InstallTarget(id, false, $"Test {id}", null, null, null),
            new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" },
            _install,
            downloadedArchive: archive);
    }

    [Fact]
    public async Task AWrapperWithAReadMeBesideIt_IsInstalled_WithoutTheReadMe()
    {
        var result = await InstallFromDisk(("README.txt", "read me"), ("MyMod-1.0/BepInEx/plugins/Mod/mod.dll", "the mod"));

        Assert.Equal("the mod", File.ReadAllText(Path.Combine(_install, "BepInEx", "plugins", "Mod", "mod.dll")));
        Assert.False(File.Exists(Path.Combine(_install, "README.txt")));
        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], result.Record.Files);
    }

    [Fact]
    public async Task APluginsFolderWithoutBepInEx_GoesIntoBepInExPlugins()
    {
        var result = await InstallFromDisk(("plugins/Mod/mod.dll", "the mod"), ("preview.png", "cover"));

        Assert.Equal("the mod", File.ReadAllText(Path.Combine(_install, "BepInEx", "plugins", "Mod", "mod.dll")));
        Assert.False(File.Exists(Path.Combine(_install, "preview.png")));
        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], result.Record.Files);
    }

    [Fact]
    public async Task ALooseDll_IsRefused()
    {
        var failure = await Assert.ThrowsAsync<ModInstallException>(() => InstallFromDisk(("mod.dll", "x")));

        Assert.Equal(ModInstallFailure.UnrecognisedArchive, failure.Reason);
    }

    [Fact]
    public async Task TheArchiveOnDisk_IsLeftWhereItIs()
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        var archive = ZipOnDisk(("BepInEx/plugins/Mod/mod.dll", "the mod"));

        await new ModInstallService(new ModDownloadService(new HttpClient(new NoDownloads())), _manifest).InstallAsync(
            new InstallTarget(id, false, $"Test {id}", null, null, null),
            new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" },
            _install,
            downloadedArchive: archive);

        Assert.True(File.Exists(archive));
    }

    // ------------------------------------------------------------------ read-me files at the top

    [Fact]
    public void Plan_LeavesReadMesBesideBepInExOut()
    {
        var plan = ArchiveLayout.Plan(Entries("README.md", "LICENSE", "BepInEx/plugins/F/F.dll"), "");

        Assert.True(plan.Recognised);
        Assert.Equal(["BepInEx/plugins/F/F.dll"], Placed(plan));
    }

    [Fact]
    public async Task ReadMesBesideBepInEx_AreNotPutInTheSptFolder()
    {
        var result = await InstallFromDisk(("README.md", "read me"), ("LICENSE", "mit"), ("BepInEx/plugins/F/F.dll", "the mod"));

        Assert.False(File.Exists(Path.Combine(_install, "README.md")));
        Assert.False(File.Exists(Path.Combine(_install, "LICENSE")));
        Assert.Equal(["BepInEx/plugins/F/F.dll"], result.Record.Files);
        Assert.True(result.SkippedProtected is null or { Count: 0 });
    }

    [Fact]
    public void Plan_BareSptFolders_AreSkippedAsProtected()
    {
        var plan = ArchiveLayout.Plan(Entries("plugins/spt/spt-core.dll", "plugins/Mod/mod.dll"), "");

        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], Placed(plan));
    }

    // ------------------------------------------------------------------ profile backups

    [Fact]
    public async Task AnInstall_TakesACopyOfTheProfilesFirst_AndARefusedOneDoesNot()
    {
        var profiles = Path.Combine(_install, "SPT", "user", "profiles");
        Write(profiles, "abc.json", "{}");
        var backups = new ProfileBackups(Path.Combine(_root, "profile-backups"), keep: 5);
        var service = new ModInstallService(
            new ModDownloadService(new HttpClient(new NoDownloads())), _manifest, profileBackups: backups);

        Task<ModInstallResult> Install(params (string Path, string Content)[] files)
        {
            var id = Interlocked.Add(ref _nextId, 1);
            _ids.Add(id);
            return service.InstallAsync(
                new InstallTarget(id, false, $"Test {id}", null, null, null),
                new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" },
                _install,
                downloadedArchive: ZipOnDisk(files));
        }

        await Assert.ThrowsAsync<ModInstallException>(() => Install(("mod.dll", "x")));
        Assert.Empty(backups.List(_install));

        await Install(("BepInEx/plugins/Mod/mod.dll", "the mod"));
        Assert.Equal(ProfileBackups.BeforeInstall, Assert.Single(backups.List(_install)).Reason);
    }
}
