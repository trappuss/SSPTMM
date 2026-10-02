using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Stage 1 of CLOSED-10-TCFResilience-DESIGN.md: the rules every deletion and move inside an install
// goes through. Each test that touches the disk builds a throwaway install under the temp folder and
// asserts on what is still there afterwards, not on what the code says it did.
//
public class ResilienceGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-resilience-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;

    public ResilienceGuardTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_install);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string Write(string relative, string content = "x")
    {
        var full = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private ModInstallService Service() => new(
        new ModDownloadService(),
        new ModInstallManifestService(Path.Combine(_root, "installed-mods.json")),
        removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));

    private string Dir(string relative)
    {
        var full = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);
        return full;
    }

    // --- ProtectedInstallPaths (D2) -----------------------------------------------------------

    [Theory]
    // Loose files in the install root (R3)
    [InlineData("EscapeFromTarkov.exe")]
    [InlineData("UnityPlayer.dll")]
    [InlineData("winhttp.dll")]
    [InlineData("doorstop_config.ini")]
    [InlineData(".doorstop_version")]
    [InlineData("README.txt")]
    // Loose files in either server root (R3)
    [InlineData("SPT/SPT.Server.exe")]
    [InlineData("SPT/SPTarkov.Server.Core.dll")]
    [InlineData("SPT_Runtime/SPT.Launcher.exe")]
    [InlineData("SPT_Runtime/SPT.Server.Linux")]
    [InlineData("spt_runtime/sptlogger.json")]
    // BepInEx and SPT's client side
    [InlineData("BepInEx/plugins/spt/spt-core.dll")]
    [InlineData("BepInEx/plugins/spt/ConfigurationManager/ConfigurationManager.dll")]
    [InlineData("BepInEx/plugins/spt")]
    [InlineData("bepinex/PLUGINS/Spt/spt-common.dll")]
    [InlineData("BepInEx/patchers/spt-prepatch.dll")]
    [InlineData("BepInEx/patchers/aki-prepatch.dll")]
    [InlineData("BepInEx/core/BepInEx.dll")]
    [InlineData("BepInEx/config/BepInEx.cfg")]
    // Server data and the player's own files (R5)
    [InlineData("SPT/SPT_Data/database/globals.json")]
    [InlineData("SPT_Runtime/SPT_Data/x")]
    [InlineData("SPT/user/profiles/abc.json")]
    [InlineData("SPT_Runtime/user/credentials/x")]
    [InlineData("SPT/user/sptSettings/x")]
    [InlineData("SPT/user/mods")]
    [InlineData("SPT/user/somefile.json")]
    [InlineData("user/profiles/abc.json")]
    [InlineData("SPT_Data/database/x.json")]
    // The game (R2, R5)
    [InlineData("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")]
    [InlineData("MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll")]
    [InlineData("NLog/NLog.config")]
    // Anything that can't be read as a plain install-relative path
    [InlineData("")]
    [InlineData("../x")]
    [InlineData("BepInEx/../../x")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/x.dll")]
    [InlineData("C:\\Windows\\x.dll")]
    public void IsProtected_Protects(string path) =>
        Assert.True(ProtectedInstallPaths.IsProtected(path));

    [Theory]
    [InlineData("BepInEx/plugins/SAIN/SAIN.dll")]
    [InlineData("BepInEx/plugins/SomeMod.dll")]
    [InlineData("BepInEx/plugins/sptarkov-helper/x.dll")]
    [InlineData("BepInEx/plugins/SomeMod/spt/x.dll")]
    [InlineData("BepInEx/plugins.disabled/SomeMod/x.dll")]
    [InlineData("BepInEx/patchers/BlackDiv.dll")]
    [InlineData("BepInEx/patchers/FixPluginTypesSerialization.dll")]
    [InlineData("BepInEx/config/com.acidphantasm.stattrack.cfg")]
    [InlineData("SPT/user/mods/SomeMod/mod.dll")]
    [InlineData("SPT_Runtime/user/mods/SomeMod/config/config.json")]
    [InlineData("SPT/user/mods.disabled/SomeMod/mod.dll")]
    [InlineData("SPT/user/mods/SomeMod/SPT_Data/x.json")]
    [InlineData("user/mods/SomeMod/mod.dll")]
    [InlineData("EscapeFromTarkov_Data/Plugins/x86_64/graphics.dll")]
    [InlineData("EscapeFromTarkov_Data/StreamingAssets/Windows/assets/x.bundle")]
    [InlineData("./BepInEx/plugins/SAIN/SAIN.dll")]
    [InlineData("BepInEx\\plugins\\SAIN\\SAIN.dll")]
    public void IsProtected_LeavesModFilesOpen(string path) =>
        Assert.False(ProtectedInstallPaths.IsProtected(path));

    // --- CheckRecordedPath (D13) ---------------------------------------------------------------

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("BepInEx/../../outside.txt")]
    [InlineData("/outside.txt")]
    [InlineData("C:/outside.txt")]
    public void CheckRecordedPath_RefusesAnythingOutsideTheInstall(string relative)
    {
        Assert.Equal(PathRefusal.OutsideInstall, InstallPathGuard.CheckRecordedPath(_install, relative, out _));
    }

    [Fact]
    public void CheckRecordedPath_RefusesProtectedFiles()
    {
        Write("BepInEx/plugins/spt/spt-core.dll");
        Assert.Equal(PathRefusal.Protected,
            InstallPathGuard.CheckRecordedPath(_install, "BepInEx/plugins/spt/spt-core.dll", out _));
    }

    [Fact]
    public void CheckRecordedPath_PassesAModFileAndResolvesIt()
    {
        var full = Write("BepInEx/plugins/SomeMod/a.dll");

        Assert.Null(InstallPathGuard.CheckRecordedPath(_install, "BepInEx/plugins/SomeMod/a.dll", out var resolved));
        Assert.Equal(Path.GetFullPath(full), resolved);
    }

    [Fact]
    public void CheckRecordedPath_RefusesAFileReachedThroughALink()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "a.dll"), "keep me");
        Dir("BepInEx/plugins");
        Directory.CreateSymbolicLink(Path.Combine(_install, "BepInEx", "plugins", "LinkedMod"), elsewhere);

        Assert.Equal(PathRefusal.Link,
            InstallPathGuard.CheckRecordedPath(_install, "BepInEx/plugins/LinkedMod/a.dll", out _));
    }

    // --- CheckModFolder (D15) ------------------------------------------------------------------

    [Theory]
    [InlineData("BepInEx/plugins/SomeMod")]
    [InlineData("BepInEx/plugins/SomeMod.dll")]
    [InlineData("BepInEx/patchers/SomePatcher.dll")]
    [InlineData("BepInEx/plugins.disabled/SomeMod")]
    [InlineData("SPT/user/mods/SomeMod")]
    [InlineData("SPT_Runtime/user/mods/SomeMod")]
    [InlineData("SPT/user/mods.disabled/SomeMod")]
    [InlineData("user/mods/SomeMod")]
    public void CheckModFolder_PassesAModFolderInAContainer(string relative)
    {
        var path = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Null(InstallPathGuard.CheckModFolder(_install, path));
    }

    [Theory]
    [InlineData("BepInEx/plugins", PathRefusal.NotAModFolder)]
    [InlineData("BepInEx/patchers", PathRefusal.NotAModFolder)]
    [InlineData("BepInEx", PathRefusal.NotAModFolder)]
    [InlineData("SPT", PathRefusal.NotAModFolder)]
    [InlineData("SPT/user/mods", PathRefusal.NotAModFolder)]
    [InlineData("SPT/user", PathRefusal.NotAModFolder)]
    [InlineData("SPT/user/profiles", PathRefusal.NotAModFolder)]
    [InlineData("BepInEx/plugins/SomeMod/sub", PathRefusal.NotAModFolder)]
    [InlineData("BepInEx/config/SomeMod", PathRefusal.NotAModFolder)]
    [InlineData("BepInEx/plugins/spt", PathRefusal.Protected)]
    [InlineData("BepInEx/patchers/spt-prepatch.dll", PathRefusal.Protected)]
    public void CheckModFolder_RefusesEverythingElse(string relative, PathRefusal expected)
    {
        var path = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(expected, InstallPathGuard.CheckModFolder(_install, path));
    }

    [Fact]
    public void CheckModFolder_RefusesTheInstallRootAndAnotherInstall()
    {
        Assert.Equal(PathRefusal.OutsideInstall, InstallPathGuard.CheckModFolder(_install, _install));

        var other = Path.Combine(_root, "install-other", "BepInEx", "plugins", "SomeMod");
        Assert.Equal(PathRefusal.OutsideInstall, InstallPathGuard.CheckModFolder(_install, other));

        // A sibling whose name only starts the same way is not inside.
        var sibling = Path.Combine(_root, "install2", "BepInEx", "plugins", "SomeMod");
        Assert.Equal(PathRefusal.OutsideInstall, InstallPathGuard.CheckModFolder(_install, sibling));
    }

    [Fact]
    public void CheckModFolder_RefusesALinkedModFolder_AndRemoveLeavesItsTargetAlone()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        var precious = Path.Combine(elsewhere, "precious.dll");
        File.WriteAllText(precious, "keep me");
        Dir("BepInEx/plugins");
        var link = Path.Combine(_install, "BepInEx", "plugins", "LinkedMod");
        Directory.CreateSymbolicLink(link, elsewhere);

        Assert.Equal(PathRefusal.Link, InstallPathGuard.CheckModFolder(_install, link));

        var ex = Assert.Throws<ModInstallException>(() => Service().RemoveHandInstalled([link], _install, "Linked"));
        Assert.Equal(ModInstallFailure.RemovalRefused, ex.Reason);
        Assert.Equal(PathRefusal.Link, ex.Refusal);
        Assert.True(File.Exists(precious));
        Assert.True(Directory.Exists(link));
    }

    [Fact]
    public void CheckModFolder_RefusesAModFolderHoldingALink()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "precious.dll"), "keep me");
        var mod = Dir("BepInEx/plugins/SomeMod/deep");
        Directory.CreateSymbolicLink(Path.Combine(mod, "linked"), elsewhere);

        Assert.Equal(PathRefusal.Link,
            InstallPathGuard.CheckModFolder(_install, Path.Combine(_install, "BepInEx", "plugins", "SomeMod")));
    }

    [Fact]
    public void RemoveHandInstalled_RefusesAContainerAndLeavesIt()
    {
        Write("BepInEx/plugins/OtherMod/o.dll");
        var plugins = Path.Combine(_install, "BepInEx", "plugins");

        var ex = Assert.Throws<ModInstallException>(() => Service().RemoveHandInstalled([plugins], _install, "Plugins"));

        Assert.Equal(ModInstallFailure.RemovalRefused, ex.Reason);
        Assert.Equal(PathRefusal.NotAModFolder, ex.Refusal);
        Assert.True(File.Exists(Path.Combine(plugins, "OtherMod", "o.dll")));
    }

    [Fact]
    public void RemoveHandInstalled_MovesAModFolderIntoHolding()
    {
        Write("BepInEx/plugins/SomeMod/a.dll", "the mod");
        Write("BepInEx/plugins/OtherMod/o.dll");

        var held = Service().RemoveHandInstalled([Path.Combine(_install, "BepInEx", "plugins", "SomeMod")], _install, "Some Mod");

        Assert.False(Directory.Exists(Path.Combine(_install, "BepInEx", "plugins", "SomeMod")));
        Assert.True(File.Exists(Path.Combine(_install, "BepInEx", "plugins", "OtherMod", "o.dll")));
        Assert.Equal("the mod", File.ReadAllText(Path.Combine(held!, "files", "BepInEx", "plugins", "SomeMod", "a.dll")));
    }

    [Fact]
    public void RemoveHandInstalled_ChecksEveryPathBeforeMovingAny()
    {
        Write("BepInEx/plugins/SomeMod/a.dll");
        var good = Path.Combine(_install, "BepInEx", "plugins", "SomeMod");
        var bad = Path.Combine(_install, "BepInEx", "plugins");

        Assert.Throws<ModInstallException>(() => Service().RemoveHandInstalled([good, bad], _install, "Some Mod"));

        Assert.True(File.Exists(Path.Combine(good, "a.dll")));
    }

    [Fact]
    public void RemoveHandInstalled_RefusesWithNoInstallFolder()
    {
        var ex = Assert.Throws<ModInstallException>(() => Service().RemoveHandInstalled([_install], "", "x"));
        Assert.Equal(ModInstallFailure.NoInstallFolder, ex.Reason);
    }

    // --- MayRemoveEmptyFolder (D14) ------------------------------------------------------------

    [Theory]
    [InlineData("BepInEx/plugins/SomeMod", true)]
    [InlineData("BepInEx/plugins/SomeMod/sub", true)]
    [InlineData("SPT/user/mods/SomeMod", true)]
    [InlineData("SPT_Runtime/user/mods/SomeMod/config", true)]
    [InlineData("BepInEx/plugins", false)]
    [InlineData("BepInEx/patchers", false)]
    [InlineData("BepInEx", false)]
    [InlineData("BepInEx/config", false)]
    [InlineData("BepInEx/config/SomeMod", false)]
    [InlineData("SPT/user/mods", false)]
    [InlineData("SPT/user", false)]
    [InlineData("SPT", false)]
    [InlineData("user/mods", false)]
    [InlineData("EscapeFromTarkov_Data/StreamingAssets", false)]
    public void MayRemoveEmptyFolder_OnlyAtOrBelowAModFolder(string relative, bool expected)
    {
        var path = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(expected, InstallPathGuard.MayRemoveEmptyFolder(_install, path));
    }

    [Fact]
    public void MayRemoveEmptyFolder_NeverTheInstallRoot() =>
        Assert.False(InstallPathGuard.MayRemoveEmptyFolder(_install, _install));

    // --- FirstLink (D16) -----------------------------------------------------------------------

    [Fact]
    public void FirstLink_FindsALinkAnywhereInATree()
    {
        var extracted = Path.Combine(_root, "extracted");
        Directory.CreateDirectory(Path.Combine(extracted, "BepInEx", "plugins", "Mod"));
        File.WriteAllText(Path.Combine(extracted, "BepInEx", "plugins", "Mod", "a.dll"), "x");
        Assert.Null(InstallPathGuard.FirstLink(extracted));

        Directory.CreateSymbolicLink(Path.Combine(extracted, "BepInEx", "plugins", "Mod", "escape"), _root);

        Assert.Equal(
            Path.Combine("BepInEx", "plugins", "Mod", "escape"),
            InstallPathGuard.FirstLink(extracted));
    }

    [Fact]
    public void FirstLink_FindsAFileLink()
    {
        var extracted = Path.Combine(_root, "extracted");
        Directory.CreateDirectory(extracted);
        var target = Path.Combine(_root, "target.txt");
        File.WriteAllText(target, "x");
        File.CreateSymbolicLink(Path.Combine(extracted, "link.txt"), target);

        Assert.Equal("link.txt", InstallPathGuard.FirstLink(extracted));
    }

    // --- UninstallAsync end to end (D13 + D14) -------------------------------------------------

    [Fact]
    public async Task Uninstall_RemovesOnlyTheModsOwnFiles_AndKeepsTheContainers()
    {
        Write("BepInEx/plugins/spt/spt-core.dll", "SPT's");
        Write("SPT/user/profiles/me.json", "my save");
        Write("SPT/SPT.Server.dll", "server");
        Write("BepInEx/plugins/OnlyPlugin/p.dll");
        Write("SPT/user/mods/OnlyServerMod/mod.dll");
        Write("SPT/user/mods/OnlyServerMod/sub/data.bin");

        var outside = Path.Combine(_root, "outside.txt");
        File.WriteAllText(outside, "not the app's");

        var record = new InstalledModRecord
        {
            ModId = 1,
            Name = "Test",
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
            Files =
            [
                "BepInEx/plugins/OnlyPlugin/p.dll",
                "SPT/user/mods/OnlyServerMod/mod.dll",
                "SPT/user/mods/OnlyServerMod/sub/data.bin",
                "BepInEx/plugins/spt/spt-core.dll",
                "SPT/user/profiles/me.json",
                "SPT/SPT.Server.dll",
                "../outside.txt",
                "BepInEx/../../outside.txt",
            ],
        };

        var manifestPath = Path.Combine(_root, "installed-mods.json");
        var manifest = new ModInstallManifestService(manifestPath);
        manifest.Save(new ModInstallManifest { Mods = [record] });

        using var download = new ModDownloadService();
        var service = new ModInstallService(download, manifest);

        var result = await service.UninstallAsync(_install, record, ConfigAction.Delete);

        // The mod's own files and folders are gone.
        Assert.False(File.Exists(Path.Combine(_install, "BepInEx", "plugins", "OnlyPlugin", "p.dll")));
        Assert.False(Directory.Exists(Path.Combine(_install, "BepInEx", "plugins", "OnlyPlugin")));
        Assert.False(Directory.Exists(Path.Combine(_install, "SPT", "user", "mods", "OnlyServerMod")));

        // The containers it emptied are still there.
        Assert.True(Directory.Exists(Path.Combine(_install, "BepInEx", "plugins")));
        Assert.True(Directory.Exists(Path.Combine(_install, "SPT", "user", "mods")));

        // Nothing SPT's, nothing the player's, nothing outside the install.
        Assert.Equal("SPT's", File.ReadAllText(Path.Combine(_install, "BepInEx", "plugins", "spt", "spt-core.dll")));
        Assert.Equal("my save", File.ReadAllText(Path.Combine(_install, "SPT", "user", "profiles", "me.json")));
        Assert.Equal("server", File.ReadAllText(Path.Combine(_install, "SPT", "SPT.Server.dll")));
        Assert.Equal("not the app's", File.ReadAllText(outside));

        Assert.Equal(3, result.FilesDeleted);
        Assert.NotNull(result.RefusedFiles);
        Assert.Equal(5, result.RefusedFiles!.Count);
    }
}
