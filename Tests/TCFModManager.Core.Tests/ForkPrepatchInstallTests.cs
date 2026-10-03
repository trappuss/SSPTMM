using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork (SSPTMM): SPT 4.1's server enum prepatches, SPT_Runtime\user\patchers\<GUID>\*.json.
//
// Reported against Skills Extended 3.1.1: an update placed its client plugin, client patcher and
// server mod but left the old prepatch, and a removal took the same three and left the prepatch.
// The cause was ProtectedInstallPaths reading everything under user\ except user\mods as SPT's own:
// the prepatch was never placed, never recorded and never removed. The server then could not read
// the new skill in the end-of-raid payload and every raid's results were lost.
//
// Each test runs a real InstallAsync / UninstallAsync against a throwaway SPT 4.1 layout
// (EscapeFromTarkov.exe at the root, the server under SPT_Runtime\), the archive laid out as Skills
// Extended's own build script packs it.
//
public class ForkPrepatchInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-prepatch-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;

    // Ids in a range of their own, so Data\overwritten is never shared with another class's tests.
    private static int _nextId = 2_600_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    private const string Prepatch = "SPT_Runtime/user/patchers/com.cj.skillsextended/EnumExtensions.json";
    private const string OldPatch = """{"SkillTypes":{"Hacking":200}}""";
    private const string NewPatch = """{"SkillTypes":{"Hacking":200,"SignalsIntelligence":201}}""";

    public ForkPrepatchInstallTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT_Runtime/SPT.Server.exe", "server");
        Write("SPT_Runtime/user/profiles/me.json", "my save");
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
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // The catalog's GUID for Skills Extended, which is also its prepatch folder's name.
    private InstallTarget NewTarget(string? guid = "com.cj.skillsextended")
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return new InstallTarget(id, false, $"Skills Extended {id}", guid, null, null);
    }

    private sealed class ArchiveHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private ModInstallService Service(params (string Path, string Content)[] files) =>
        new(new ModDownloadService(new HttpClient(new ArchiveHandler(ArchiveFileTests.Zip(files)))), _manifest);

    // Skills Extended's four locations (Directory.Build.targets, GetSkillsExtendedPackageFiles).
    private static (string, string)[] Archive(string version, string patch) =>
    [
        ("BepInEx/plugins/SkillsExtended/SkillsExtended.dll", "plugin " + version),
        ("BepInEx/patchers/SkillsExtended.Client.Prepatch.dll", "client patcher " + version),
        ("SPT_Runtime/user/mods/SkillsExtended/SkillsExtended.Server.dll", "server " + version),
        (Prepatch, patch),
    ];

    private Task<ModInstallResult> Install(InstallTarget target, string version, string patch) =>
        Service(Archive(version, patch)).InstallAsync(
            target, new ModVersion { Id = 1, Version = version, Link = "https://example.test/a" }, _install);

    private InstalledModRecord RecordOf(InstallTarget target) => _manifest.Load().Mods.Single(target.Matches);

    [Fact]
    public async Task A_fresh_install_places_and_records_the_server_prepatch()
    {
        var target = NewTarget();

        var result = await Install(target, "3.1.1", NewPatch);

        Assert.Equal(NewPatch, File.ReadAllText(Full(Prepatch)));
        Assert.Contains(Prepatch, result.Record.Files);
        Assert.Empty(result.SkippedProtected!);
        Assert.Empty(result.NotAsInArchive);
    }

    // The check after install, on real installs: an update over a config the user changed merges it
    // on purpose and is not reported; an archive file that a protected path keeps out is named on its
    // own (SkippedProtected), not again as a mismatch.
    [Fact]
    public async Task The_check_after_install_reports_nothing_for_merged_configs_or_refused_files()
    {
        const string Config = "SPT_Runtime/user/mods/SkillsExtended/config/config.json";
        var target = NewTarget();

        await Service([.. Archive("3.0.0", OldPatch), (Config, "{ \"a\": 1, \"b\": 1 }")]).InstallAsync(
            target, new ModVersion { Id = 1, Version = "3.0.0", Link = "https://example.test/a" }, _install);
        Write(Config, "{ \"a\": 5, \"b\": 1 }");

        var update = await Service([.. Archive("3.1.1", NewPatch), (Config, "{ \"a\": 1, \"b\": 2, \"c\": 3 }"), ("SPT_Runtime/user/profiles/x.json", "not a mod's")]).InstallAsync(
            target, new ModVersion { Id = 1, Version = "3.1.1", Link = "https://example.test/a" }, _install);

        Assert.Contains("\"a\": 5", File.ReadAllText(Full(Config)));
        Assert.Contains("SPT_Runtime/user/profiles/x.json", update.SkippedProtected!);
        Assert.Empty(update.NotAsInArchive);
    }

    // Server configs placed as the archive has them are now checked too (they used to be skipped
    // with the merged ones). A first install (Added) and an update over an untouched config
    // (DefaultsUpdated) both leave the archive's copy, so neither is reported.
    [Fact]
    public async Task Server_configs_placed_as_the_archive_has_them_are_checked_and_pass()
    {
        const string Config = "SPT_Runtime/user/mods/SkillsExtended/config/config.json";
        var target = NewTarget();

        var first = await Service([.. Archive("3.0.0", OldPatch), (Config, "{ \"a\": 1 }")]).InstallAsync(
            target, new ModVersion { Id = 1, Version = "3.0.0", Link = "https://example.test/a" }, _install);

        Assert.Contains(first.Configs!.Files, f => f.Path == Config && f.Kind == ConfigOutcomeKind.Added);
        Assert.Empty(first.NotAsInArchive);

        var update = await Service([.. Archive("3.1.1", NewPatch), (Config, "{ \"a\": 2 }")]).InstallAsync(
            target, new ModVersion { Id = 1, Version = "3.1.1", Link = "https://example.test/a" }, _install);

        Assert.Contains(update.Configs!.Files, f => f.Path == Config && f.Kind == ConfigOutcomeKind.DefaultsUpdated);
        Assert.Equal("{ \"a\": 2 }", File.ReadAllText(Full(Config)));
        Assert.Empty(update.NotAsInArchive);
    }

    [Theory]
    [InlineData(ConfigOutcomeKind.Added, true)]
    [InlineData(ConfigOutcomeKind.Unchanged, true)]
    [InlineData(ConfigOutcomeKind.DefaultsUpdated, true)]
    [InlineData(ConfigOutcomeKind.Replaced, true)]
    [InlineData(ConfigOutcomeKind.Merged, false)]
    [InlineData(ConfigOutcomeKind.KeptMine, false)]
    [InlineData(ConfigOutcomeKind.NotUpdated, false)]
    [InlineData(ConfigOutcomeKind.Preserved, false)]
    [InlineData(ConfigOutcomeKind.Removed, false)]
    public void Only_outcomes_that_leave_the_archives_copy_are_checked_after_install(ConfigOutcomeKind kind, bool checkedAfter) =>
        Assert.Equal(checkedAfter, new ConfigFileOutcome { Path = "x/config.json", Kind = kind }.IsArchivesCopy);

    [Fact]
    public void Every_config_outcome_is_classified() =>
        Assert.Equal(9, Enum.GetValues<ConfigOutcomeKind>().Length);

    [Fact]
    public async Task An_update_replaces_the_server_prepatch_with_the_new_one()
    {
        var target = NewTarget();
        await Install(target, "3.0.0", OldPatch);

        await Install(target, "3.1.1", NewPatch);

        Assert.Equal(NewPatch, File.ReadAllText(Full(Prepatch)));
        Assert.Equal("plugin 3.1.1", File.ReadAllText(Full("BepInEx/plugins/SkillsExtended/SkillsExtended.dll")));
    }

    // The reported install: a stale prepatch no record owns (placed by hand, or before this fix).
    // The update puts the new one in, keeping the old one aside as it does any file it replaces.
    [Fact]
    public async Task An_update_over_a_prepatch_nothing_owns_still_puts_the_new_one_in()
    {
        Write(Prepatch, OldPatch);
        var target = NewTarget();

        var result = await Install(target, "3.1.1", NewPatch);

        Assert.Equal(NewPatch, File.ReadAllText(Full(Prepatch)));
        Assert.Contains(Prepatch, result.Record.Files);
    }

    // The user's install after the bug: the right prepatch on disk (written by hand), owned by no
    // record. Installing over it keeps it aside as usual - but as an earlier copy of the same mod,
    // proven by its folder being named after the mod's GUID, so removing the mod takes the prepatch
    // out instead of putting the kept copy back.
    [Fact]
    public async Task A_prepatch_nothing_owned_in_the_mods_own_GUID_folder_is_not_put_back_on_removal()
    {
        Write(Prepatch, OldPatch);
        var target = NewTarget();
        var installed = await Install(target, "3.1.1", NewPatch);

        Assert.True(installed.Record.Overwrote.Single(o => o.Path == Prepatch).SameMod);

        var removed = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.False(File.Exists(Full(Prepatch)));
        Assert.Equal(0, removed.OriginalsRestored);
    }

    // Without a GUID to compare there is no proof it was the same mod's, so the original rule stands:
    // what the install replaced is put back.
    [Fact]
    public async Task Without_a_GUID_a_replaced_prepatch_is_put_back_on_removal_as_before()
    {
        Write(Prepatch, OldPatch);
        var target = NewTarget(guid: null);
        await Install(target, "3.1.1", NewPatch);

        var removed = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal(OldPatch, File.ReadAllText(Full(Prepatch)));
        Assert.Equal(1, removed.OriginalsRestored);
    }

    [Theory]
    [InlineData("SPT_Runtime/user/patchers/com.cj.skillsextended/EnumExtensions.json", "com.cj.skillsextended")]
    [InlineData("user/patchers/com.mod/x.json", "com.mod")]
    [InlineData("SPT_Runtime/user/patchers/loose.json", null)]
    [InlineData("SPT_Runtime/user/mods/SkillsExtended/x.json", null)]
    [InlineData("BepInEx/patchers/SkillsExtended.Client.Prepatch.dll", null)]
    public void The_prepatch_folder_is_read_from_the_path(string path, string? expected) =>
        Assert.Equal(expected, InstallPathGuard.PrepatchFolderOf(path));

    [Fact]
    public async Task A_removal_takes_the_prepatch_and_its_folder_and_leaves_user_patchers_and_the_profiles()
    {
        Write("SPT_Runtime/user/patchers/some.other.mod/EnumExtensions.json", "another mod's");
        var target = NewTarget();
        await Install(target, "3.1.1", NewPatch);

        await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.False(File.Exists(Full(Prepatch)));
        Assert.False(Directory.Exists(Full("SPT_Runtime/user/patchers/com.cj.skillsextended")));
        Assert.Equal("another mod's", File.ReadAllText(Full("SPT_Runtime/user/patchers/some.other.mod/EnumExtensions.json")));
        Assert.Equal("my save", File.ReadAllText(Full("SPT_Runtime/user/profiles/me.json")));
        Assert.False(File.Exists(Full("BepInEx/patchers/SkillsExtended.Client.Prepatch.dll")));
    }

    [Theory]
    [InlineData("SPT_Runtime/user/patchers/com.cj.skillsextended/EnumExtensions.json", false)]
    [InlineData("SPT/user/patchers/com.mod/x.json", false)]
    [InlineData("user/patchers/com.mod/x.json", false)]
    [InlineData("SPT_Runtime/user/patchers", true)]
    [InlineData("SPT_Runtime/user/patchers/loose.json", false)]
    [InlineData("SPT_Runtime/user/profiles/abc.json", true)]
    [InlineData("SPT_Runtime/user/sptappdata/x", true)]
    public void Only_user_mods_and_user_patchers_are_open_under_user(string path, bool isProtected) =>
        Assert.Equal(isProtected, ProtectedInstallPaths.IsProtected(path));

    [Fact]
    public void Only_the_mods_own_folder_under_user_patchers_may_be_tidied_when_empty()
    {
        Assert.True(InstallPathGuard.MayRemoveEmptyFolder(_install, Full("SPT_Runtime/user/patchers/com.mod")));
        Assert.False(InstallPathGuard.MayRemoveEmptyFolder(_install, Full("SPT_Runtime/user/patchers")));
        Assert.False(InstallPathGuard.MayRemoveEmptyFolder(_install, Full("SPT_Runtime/user")));
        Assert.False(InstallPathGuard.MayRemoveEmptyFolder(_install, Full("SPT_Runtime/user/profiles")));
    }
}
