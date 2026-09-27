using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Where each file of an unpacked archive goes in the SPT folder - the ways authors package mods, and
// the cases that must not be guessed at. InstallPipelineTests installs the common layouts end to
// end; these pin down the edges of ArchiveLayout itself.
//
public class ArchiveLayoutTests : IDisposable
{
    private readonly string _root;

    public ArchiveLayoutTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TCFModManagerLayoutTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Files(params string[] paths)
    {
        foreach (var path in paths)
        {
            var full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, path);
        }
    }

    // What Read places, as install-relative paths with forward slashes, sorted.
    private List<string>? Placed() =>
        ArchiveLayout.Read(_root)?.Select(e => e.Relative.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList();

    [Fact]
    public void TheInstallsOwnLayout_IsPlacedAsItIs()
    {
        Files("BepInEx/plugins/A/A.dll", "BepInEx/config/com.a.cfg", "SPT/user/mods/a-server/A.Server.dll");

        Assert.Equal(["BepInEx/config/com.a.cfg", "BepInEx/plugins/A/A.dll", "SPT/user/mods/a-server/A.Server.dll"], Placed());
    }

    [Fact]
    public void ReadMesBesideTheInstallsLayout_AreLeftOut_ButPicturesArePlaced()
    {
        // A picture beside BepInEx/ could be meant for the game - nothing says otherwise.
        Files("BepInEx/plugins/A.dll", "README.md", "LICENSE", "CHANGELOG.txt", "icon.png");

        Assert.Equal(["BepInEx/plugins/A.dll", "icon.png"], Placed());
    }

    [Fact]
    public void AReadMeDeeperDown_IsPlaced()
    {
        // Only the top of the archive is for the person unpacking it; below that it is the mod's.
        Files("BepInEx/plugins/A/README.txt", "BepInEx/plugins/A/A.dll");

        Assert.Equal(["BepInEx/plugins/A/A.dll", "BepInEx/plugins/A/README.txt"], Placed());
    }

    [Fact]
    public void WrapperFolders_AreLookedThrough_WithReadMesAndPicturesBesideThem()
    {
        Files("readme.txt", "cover.jpg", "MyMod-1.0/MyMod/BepInEx/plugins/A.dll");

        Assert.Equal(["BepInEx/plugins/A.dll"], Placed());
        Assert.Equal(Path.Combine(_root, "MyMod-1.0", "MyMod"), ArchiveLayout.ContentRoot(_root));
    }

    [Fact]
    public void WrapperFolders_AreLookedThroughFourDeepAtMost()
    {
        Files("w1/w2/w3/w4/w5/BepInEx/plugins/A.dll");

        // Four wrappers looked through leaves the fifth, which is not the install's layout.
        Assert.Equal(Path.Combine(_root, "w1", "w2", "w3", "w4"), ArchiveLayout.ContentRoot(_root));
        Assert.Null(Placed());
    }

    [Fact]
    public void AFileThatIsNotAReadMe_BesideAFolder_StopsTheLookThrough()
    {
        // A DLL beside the folder means this level is content, not a wrapper - and it says nothing
        // about where it goes, so nothing is placed.
        Files("Extra.dll", "MyMod/BepInEx/plugins/A.dll");

        Assert.Equal(_root, ArchiveLayout.ContentRoot(_root));
        Assert.Null(Placed());
    }

    [Fact]
    public void PluginsAndPatchersOnTheirOwn_GoUnderBepInEx()
    {
        Files("plugins/A/A.dll", "patchers/A.Patcher.dll");

        Assert.Equal(["BepInEx/patchers/A.Patcher.dll", "BepInEx/plugins/A/A.dll"], Placed());
    }

    [Fact]
    public void PluginsInAWrapper_GoUnderBepInExToo()
    {
        Files("README.md", "MyMod/plugins/A.dll");

        Assert.Equal(["BepInEx/plugins/A.dll"], Placed());
    }

    [Fact]
    public void Plugins_WithALooseFileBesideIt_IsNotGuessedAt()
    {
        Files("plugins/A.dll", "B.dll");

        Assert.Null(Placed());
    }

    [Fact]
    public void Plugins_WithAPictureBesideIt_LeavesThePictureOut()
    {
        Files("plugins/A.dll", "preview.png");

        Assert.Equal(["BepInEx/plugins/A.dll"], Placed());
    }

    [Fact]
    public void ALoneDll_IsNotPlaced()
    {
        // Under SPT 4 a server mod is a DLL too: BepInEx\plugins would be a guess.
        Files("A.dll");

        Assert.Null(Placed());
    }

    [Fact]
    public void AnUnknownFolder_IsNotPlaced()
    {
        Files("Stuff/A.dll", "Other/B.dll");

        Assert.Null(Placed());
    }

    [Fact]
    public void AnEmptyArchive_PlacesNothing()
    {
        Assert.Null(Placed());
    }

    [Theory]
    [InlineData("README", true)]
    [InlineData("licence", true)]
    [InlineData("notes.TXT", true)]
    [InlineData("guide.pdf", true)]
    [InlineData("site.url", true)]
    [InlineData("preview.png", false)]
    [InlineData("Mod.dll", false)]
    [InlineData("config", false)]
    public void IsDocument(string name, bool expected) =>
        Assert.Equal(expected, ArchiveLayout.IsDocument(name));
}
