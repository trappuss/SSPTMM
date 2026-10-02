using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// R17 (CLOSED-10, found by the stage-6 proof run): a mod may ADD a file directly in the install root
// (SVM's Greed.exe) or under EscapeFromTarkov_Data/Managed (Dynamic Maps' Unity.VectorGraphics.dll),
// but never replace one that is there, and a removal takes one back out only when its fingerprint
// proves it is the copy this app placed.
//
public class ResilienceNewFileOnlyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-newfile-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    private static int _nextId = 970_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    private const string Managed = "EscapeFromTarkov_Data/Managed/";

    public ResilienceNewFileOnlyTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write(Managed + "Assembly-CSharp.dll", "the game's own");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }

        foreach (var id in _ids)
        {
            var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, id.ToString());
            try { if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true); }
            catch (IOException) { }
        }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
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

    private ModInstallService Service(byte[]? archive = null) => new(
        new ModDownloadService(new HttpClient(new ArchiveHandler(archive ?? []))),
        _manifest,
        removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));

    private Task<ModInstallResult> Install(InstallTarget target, string version, params (string Path, string Content)[] files) =>
        Service(ArchiveFileTests.Zip(files))
            .InstallAsync(target, new ModVersion { Id = 1, Version = version, Link = "https://example.test/a" }, _install);

    private InstalledModRecord RecordOf(InstallTarget target) => _manifest.Load().Find(target.Id, false)!;

    // A Dynamic Maps / SVM shaped archive.
    private Task<ModInstallResult> InstallMapsLike(InstallTarget target, string version = "1.0.0", string vector = "vector graphics") =>
        Install(target, version,
            ("BepInEx/plugins/Maps/Maps.dll", "the plugin " + version),
            ("EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll", vector),
            ("Greed.exe", "config tool"));

    [Theory]
    [InlineData("Greed.exe", true)]
    [InlineData("EscapeFromTarkov.exe", true)]
    [InlineData("EscapeFromTarkov_Data/Managed/Unity.VectorGraphics.dll", true)]
    [InlineData("EscapeFromTarkov_Data/Managed/sub/x.dll", true)]
    [InlineData("EscapeFromTarkov_Data/Managed", false)]
    [InlineData("BepInEx/core/BepInEx.dll", false)]
    [InlineData("BepInEx/plugins/spt/spt-core.dll", false)]
    [InlineData("SPT/SPT.Server.exe", false)]
    [InlineData("SPT/SPT_Data/x.json", false)]
    [InlineData("MonoBleedingEdge/x.dll", false)]
    [InlineData("NLog/x.dll", false)]
    [InlineData("../Greed.exe", false)]
    public void Only_the_install_root_and_Managed_take_new_files(string path, bool newFileOnly)
    {
        Assert.Equal(newFileOnly, ProtectedInstallPaths.IsNewFileOnly(path));

        // Still protected for every caller that doesn't ask about new files.
        if (newFileOnly) Assert.True(ProtectedInstallPaths.IsProtected(path));
    }

    [Fact]
    public async Task A_new_file_in_Managed_or_the_root_is_placed_recorded_and_fingerprinted()
    {
        var target = NewTarget();
        var result = await InstallMapsLike(target);

        Assert.Equal("vector graphics", File.ReadAllText(Full(Managed + "Unity.VectorGraphics.dll")));
        Assert.Equal("config tool", File.ReadAllText(Full("Greed.exe")));
        Assert.Empty(result.SkippedProtected ?? []);

        var record = RecordOf(target);
        Assert.Contains(Managed + "Unity.VectorGraphics.dll", record.Files);
        Assert.Contains("Greed.exe", record.Files);
        Assert.NotNull(record.FingerprintFor(Managed + "Unity.VectorGraphics.dll"));
        Assert.NotNull(record.FingerprintFor("Greed.exe"));
    }

    [Fact]
    public async Task A_file_already_there_is_never_replaced_or_recorded()
    {
        var target = NewTarget();
        var result = await Install(target, "1.0.0",
            ("BepInEx/plugins/Patch/Patch.dll", "plugin"),
            (Managed + "Assembly-CSharp.dll", "a patched copy"),
            ("EscapeFromTarkov.exe", "not the game"));

        Assert.Equal("the game's own", File.ReadAllText(Full(Managed + "Assembly-CSharp.dll")));
        Assert.Equal("game", File.ReadAllText(Full("EscapeFromTarkov.exe")));
        Assert.Equal(
            new[] { "EscapeFromTarkov.exe", Managed + "Assembly-CSharp.dll" }.Order(),
            (result.SkippedProtected ?? []).Order());

        var record = RecordOf(target);
        Assert.DoesNotContain(Managed + "Assembly-CSharp.dll", record.Files);
        Assert.DoesNotContain("EscapeFromTarkov.exe", record.Files);
        Assert.Equal(0, result.OriginalsKept);
    }

    [Fact]
    public async Task Remove_takes_them_into_holding_and_Undo_puts_them_back()
    {
        var target = NewTarget();
        await InstallMapsLike(target);

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.False(File.Exists(Full(Managed + "Unity.VectorGraphics.dll")));
        Assert.False(File.Exists(Full("Greed.exe")));
        Assert.Equal("the game's own", File.ReadAllText(Full(Managed + "Assembly-CSharp.dll")));
        Assert.Equal("game", File.ReadAllText(Full("EscapeFromTarkov.exe")));
        Assert.Equal(3, result.FilesDeleted);
        Assert.Empty(result.RefusedFiles ?? []);

        var undo = Service().UndoRemoval(_install, result.HoldingFolder!);

        Assert.Empty(undo.Blocked);
        Assert.Equal("vector graphics", File.ReadAllText(Full(Managed + "Unity.VectorGraphics.dll")));
        Assert.Equal("config tool", File.ReadAllText(Full("Greed.exe")));
    }

    [Fact]
    public async Task Remove_leaves_one_that_changed_since_install()
    {
        var target = NewTarget();
        await InstallMapsLike(target);
        Write(Managed + "Unity.VectorGraphics.dll", "replaced by a game update");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal("replaced by a game update", File.ReadAllText(Full(Managed + "Unity.VectorGraphics.dll")));
        Assert.Contains(Managed + "Unity.VectorGraphics.dll", result.RefusedFiles ?? []);
        Assert.False(File.Exists(Full("Greed.exe")));
    }

    [Fact]
    public async Task Remove_leaves_one_a_record_from_before_v1_19_lists()
    {
        var target = NewTarget();
        await InstallMapsLike(target);

        // As a v1.18 record: same files, no fingerprints, no stamp.
        var manifest = _manifest.Load();
        var old = RecordOf(target);
        manifest.Mods.RemoveAll(m => m.ModId == old.ModId);
        manifest.Mods.Add(new InstalledModRecord
        {
            ModId = old.ModId, Name = old.Name, Version = old.Version, InstalledAt = old.InstalledAt, Files = [.. old.Files],
        });
        _manifest.Save(manifest);

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.True(File.Exists(Full(Managed + "Unity.VectorGraphics.dll")));
        Assert.True(File.Exists(Full("Greed.exe")));
        Assert.False(File.Exists(Full("BepInEx/plugins/Maps/Maps.dll")));
        Assert.Equal(2, result.RefusedFiles?.Count);
    }

    [Fact]
    public async Task An_update_replaces_its_own_proven_copy_and_holds_the_old_one()
    {
        var target = NewTarget();
        await InstallMapsLike(target, "1.0.0", "vector graphics v1");

        await InstallMapsLike(target, "2.0.0", "vector graphics v2");

        Assert.Equal("vector graphics v2", File.ReadAllText(Full(Managed + "Unity.VectorGraphics.dll")));
        Assert.Contains(Managed + "Unity.VectorGraphics.dll", RecordOf(target).Files);

        var held = RemovedMods.List(_install).Single(h => h.Log.Kind == RemovalKind.ReplacedByUpdate).Folder;
        Assert.Equal("vector graphics v1",
            File.ReadAllText(Path.Combine(held, RemovedMods.FilesFolder, "EscapeFromTarkov_Data", "Managed", "Unity.VectorGraphics.dll")));
    }

    [Fact]
    public async Task An_update_never_replaces_a_copy_that_changed_since_install()
    {
        var target = NewTarget();
        await InstallMapsLike(target, "1.0.0", "vector graphics v1");
        Write(Managed + "Unity.VectorGraphics.dll", "replaced by a game update");

        var result = await InstallMapsLike(target, "2.0.0", "vector graphics v2");

        Assert.Equal("replaced by a game update", File.ReadAllText(Full(Managed + "Unity.VectorGraphics.dll")));
        Assert.Contains(Managed + "Unity.VectorGraphics.dll", result.SkippedProtected ?? []);
        Assert.DoesNotContain(Managed + "Unity.VectorGraphics.dll", RecordOf(target).Files);
    }
}
