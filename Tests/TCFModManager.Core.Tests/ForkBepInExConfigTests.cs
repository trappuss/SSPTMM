using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork: a plugin's settings in BepInEx\config are the user's. An archive's copy never goes over
// one that is already there - except the copy this app placed, untouched - and a removal leaves a
// changed one where it is (ConfigCarryOver.Prepare, ModInstallService.KeepSettingsPrints).
//
public class ForkBepInExConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-cfg-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    // Ids no real catalog entry will collide with in the test Data folder.
    // Fork: each class that keeps files under Data\overwritten has its own 100,000 ids, so two classes
    // running at once never share (and clean up) the same folder there.
    private static int _nextId = 2_500_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public ForkBepInExConfigTests()
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

    private ModInstallService Service() => new(new ModDownloadService(new HttpClient()), _manifest);

    [Fact]
    public async Task AnUpdate_LeavesSettingsTheUserChanged_AndTheyStillReadAsChanged()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "v1"), ("BepInEx/config/mod.cfg", "defaults v1"));
        Write("BepInEx/config/mod.cfg", "tuned");

        var v2 = await Install(target, "2.0.0", ("BepInEx/plugins/Mod/mod.dll", "v2"), ("BepInEx/config/mod.cfg", "defaults v2"));

        Assert.Equal("tuned", File.ReadAllText(Full("BepInEx/config/mod.cfg")));
        Assert.Equal("v2", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal(["BepInEx/config/mod.cfg"], v2.KeptSettings);
        Assert.Contains("BepInEx/config/mod.cfg", v2.Record.Files);
        Assert.Empty(v2.Record.Overwrote);

        // A second update still sees the user's copy as theirs, not as what this app placed.
        var v3 = await Install(target, "3.0.0", ("BepInEx/plugins/Mod/mod.dll", "v3"), ("BepInEx/config/mod.cfg", "defaults v3"));
        Assert.Equal("tuned", File.ReadAllText(Full("BepInEx/config/mod.cfg")));
        Assert.Equal(["BepInEx/config/mod.cfg"], v3.KeptSettings);

        // And unsubscribing leaves it where it is.
        var removed = await Service().UninstallAsync(_install, _manifest.Load().Find(target.Id, false)!);
        Assert.Equal("tuned", File.ReadAllText(Full("BepInEx/config/mod.cfg")));
        Assert.Contains("BepInEx/config/mod.cfg", removed.KeptChanged);
        Assert.False(File.Exists(Full("BepInEx/plugins/Mod/mod.dll")));
    }

    [Fact]
    public async Task AnUpdate_ReplacesTheDefaultsThisAppPlaced_WhenNobodyChangedThem()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "v1"), ("BepInEx/config/mod.cfg", "defaults v1"));

        var v2 = await Install(target, "2.0.0", ("BepInEx/plugins/Mod/mod.dll", "v2"), ("BepInEx/config/mod.cfg", "defaults v2"));

        Assert.Equal("defaults v2", File.ReadAllText(Full("BepInEx/config/mod.cfg")));
        Assert.Empty(v2.KeptSettings);
        Assert.True(v2.Record.FingerprintFor("BepInEx/config/mod.cfg")!.Matches(Full("BepInEx/config/mod.cfg")));
    }

    [Fact]
    public async Task AFirstInstall_LeavesSettingsThatWereAlreadyThere_AndDoesntClaimThem()
    {
        Write("BepInEx/config/mod.cfg", "written by BepInEx, tuned");
        var target = NewTarget();

        var result = await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "v1"), ("BepInEx/config/mod.cfg", "defaults"));

        Assert.Equal("written by BepInEx, tuned", File.ReadAllText(Full("BepInEx/config/mod.cfg")));
        Assert.Equal(["BepInEx/config/mod.cfg"], result.KeptSettings);
        Assert.DoesNotContain("BepInEx/config/mod.cfg", result.Record.Files);
        Assert.Empty(result.Record.Overwrote);

        await Service().UninstallAsync(_install, _manifest.Load().Find(target.Id, false)!);
        Assert.Equal("written by BepInEx, tuned", File.ReadAllText(Full("BepInEx/config/mod.cfg")));
    }

    [Fact]
    public async Task SettingsThatArentThereYet_ArePlacedAndRecorded()
    {
        var target = NewTarget();

        var result = await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "v1"), ("BepInEx/config/mod.cfg", "defaults"));

        Assert.Equal("defaults", File.ReadAllText(Full("BepInEx/config/mod.cfg")));
        Assert.Empty(result.KeptSettings);
        Assert.Contains("BepInEx/config/mod.cfg", result.Record.Files);
    }

    [Fact]
    public void TheClashWarning_LeavesSettingsOut()
    {
        Write("BepInEx/config/mod.cfg", "the user's");
        Write("BepInEx/plugins/Hand/hand.dll", "by hand");

        var clashes = ModFileConflicts.Find(
            _install, ["BepInEx/config/mod.cfg", "BepInEx/plugins/Hand/hand.dll"],
            new InstallTarget(1, false, "Test", null, null, null), []);

        Assert.Equal(["BepInEx/plugins/Hand/hand.dll"], clashes.Select(c => c.Path));
    }
}
