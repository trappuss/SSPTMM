using System.IO.Compression;
using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork (SSPTMM): an archive's empty folders. SVM 2.2.3 ships an empty Presets\ that its Greed.exe
// requires; the install placed files only, so Greed said "couldn't find Preset folder" until it made
// one itself. Tested with the archive's real folder entries, on an SPT 4.1 layout.
//
public class ForkEmptyFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-emptydirs-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;
    private static int _nextId = 2_700_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    private const string Svm = "SPT_Runtime/user/mods/[SVM] Server Value Modifier";

    public ForkEmptyFoldersTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(_install, "SPT_Runtime"));
        File.WriteAllText(Path.Combine(_install, "EscapeFromTarkov.exe"), "game");
        File.WriteAllText(Path.Combine(_install, "SPT_Runtime", "SPT.Server.exe"), "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        foreach (var id in _ids)
        {
            var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, id.ToString());
            try { if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true); } catch (IOException) { }
        }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    // A zip with real folder entries ("name/") as well as files, as SVM's is.
    private static byte[] Zip(string[] folders, params (string Path, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var folder in folders) zip.CreateEntry(folder.TrimEnd('/') + "/");
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed class Handler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private ModInstallService Service(byte[] archive) => new(new ModDownloadService(new HttpClient(new Handler(archive))), _manifest);

    private InstallTarget NewTarget()
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return new InstallTarget(id, false, $"SVM {id}", null, null, null);
    }

    private static byte[] SvmArchive() => Zip(
        [
            "SPT_Runtime/",
            "SPT_Runtime/user/",
            "SPT_Runtime/user/mods/",
            Svm + "/",
            Svm + "/Presets/",
            Svm + "/Loader/",
            "SPT_Runtime/user/cache/",
            "BepInEx/",
            "BepInEx/plugins/",
        ],
        ("Greed.exe", "greed"),
        (Svm + "/ServerValueModifier.dll", "dll"),
        (Svm + "/Loader/loader.json", "{\"CurrentlySelectedPreset\":\"null\"}"));

    [Fact]
    public void The_empty_folders_in_an_archive_are_read_from_its_folder_entries()
    {
        var path = Path.Combine(_root, "svm.zip");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(path, SvmArchive());

        Assert.Equal(
            ["BepInEx/plugins", "SPT_Runtime/user/cache", Svm + "/Presets"],
            ModInstallService.EmptyFoldersIn(path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task An_install_creates_an_empty_folder_inside_the_mods_folder_and_nowhere_else()
    {
        var target = NewTarget();
        await Service(SvmArchive()).InstallAsync(
            target, new ModVersion { Id = 1, Version = "2.2.3", Link = "https://example.test/a" }, _install);

        Assert.True(Directory.Exists(Full(Svm + "/Presets")));
        Assert.False(Directory.Exists(Full("SPT_Runtime/user/cache")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins")));
    }

    [Fact]
    public async Task A_removal_takes_the_empty_folders_it_created_and_the_mods_folder_with_them()
    {
        var target = NewTarget();
        await Service(SvmArchive()).InstallAsync(
            target, new ModVersion { Id = 1, Version = "2.2.3", Link = "https://example.test/a" }, _install);

        var record = _manifest.Load().Mods.Single(target.Matches);
        await Service([]).UninstallAsync(_install, record, ConfigAction.Delete);

        Assert.False(Directory.Exists(Full(Svm)));
        Assert.True(Directory.Exists(Full("SPT_Runtime/user/mods")));
    }

    [Fact]
    public async Task Removing_an_addon_that_shares_the_mods_folder_leaves_the_mods_empty_folders()
    {
        var svm = NewTarget();
        await Service(SvmArchive()).InstallAsync(
            svm, new ModVersion { Id = 1, Version = "2.2.3", Link = "https://example.test/a" }, _install);

        var addon = new InstallTarget(Interlocked.Add(ref _nextId, 1), true, "SVM extra", null, null, null);
        _ids.Add(addon.Id);
        await Service(Zip([], (Svm + "/Loader/extra.json", "{}"))).InstallAsync(
            addon, new ModVersion { Id = 2, Version = "1.0.0", Link = "https://example.test/b" }, _install);

        var record = _manifest.Load().Mods.Single(addon.Matches);
        await Service([]).UninstallAsync(_install, record, ConfigAction.Delete);

        Assert.True(Directory.Exists(Full(Svm + "/Presets")));
        Assert.False(File.Exists(Full(Svm + "/Loader/extra.json")));
    }
}
