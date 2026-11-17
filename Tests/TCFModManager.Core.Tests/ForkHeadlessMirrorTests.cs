using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.8): a headless client in a folder of its own, kept in step by copying (HeadlessMirror).
public sealed class ForkHeadlessMirrorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 6, 40, 7, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ssptmm-headless-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly string _headless;

    public ForkHeadlessMirrorTests()
    {
        _install = Path.Combine(_root, "SPT");
        _headless = Path.Combine(_root, "Headless");
        Directory.CreateDirectory(Path.Combine(_install, "BepInEx", "plugins"));
        Directory.CreateDirectory(Path.Combine(_install, "BepInEx", "patchers"));
        Directory.CreateDirectory(Path.Combine(_headless, "BepInEx", "plugins"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string root, string relative, string text = "x")
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string Plugin(string name) => Path.Combine(_install, "BepInEx", "plugins", name);

    [Fact]
    public void Folders_and_loose_files_are_copied_and_the_rest_of_the_headless_folder_is_left()
    {
        Write(_install, @"BepInEx\plugins\SAIN\SAIN.dll", "sain");
        Write(_install, @"BepInEx\plugins\SAIN\Presets\hard.json", "{}");
        Write(_install, @"BepInEx\plugins\Loose.dll", "loose");
        Write(_install, @"BepInEx\patchers\Skills.Prepatch.dll", "patch");
        Write(_headless, @"BepInEx\plugins\Fika.Headless.dll", "fika");

        var result = HeadlessMirror.Sync(_install, _headless,
            [Plugin("SAIN"), Plugin("Loose.dll"), Path.Combine(_install, "BepInEx", "patchers", "Skills.Prepatch.dll")], Now);

        Assert.Null(result.Problem);
        Assert.Equal(["SAIN", "Loose.dll", "Skills.Prepatch.dll"], result.Copied);
        Assert.Equal("sain", File.ReadAllText(Path.Combine(_headless, "BepInEx", "plugins", "SAIN", "SAIN.dll")));
        Assert.True(File.Exists(Path.Combine(_headless, "BepInEx", "plugins", "SAIN", "Presets", "hard.json")));
        Assert.Equal("patch", File.ReadAllText(Path.Combine(_headless, "BepInEx", "patchers", "Skills.Prepatch.dll")));
        Assert.Equal("fika", File.ReadAllText(Path.Combine(_headless, "BepInEx", "plugins", "Fika.Headless.dll")));

        // Again, with nothing changed: nothing is copied twice.
        var again = HeadlessMirror.Sync(_install, _headless, [Plugin("SAIN"), Plugin("Loose.dll")], Now);
        Assert.Empty(again.Copied);
        Assert.Equal(["SAIN", "Loose.dll"], again.Unchanged);
    }

    [Fact]
    public void What_it_placed_and_is_no_longer_wanted_is_removed_and_nothing_else_is()
    {
        Write(_install, @"BepInEx\plugins\SAIN\SAIN.dll");
        Write(_install, @"BepInEx\plugins\Waypoints\Waypoints.dll");
        Write(_headless, @"BepInEx\plugins\ByHand\ByHand.dll");

        HeadlessMirror.Sync(_install, _headless, [Plugin("SAIN"), Plugin("Waypoints")], Now);
        var result = HeadlessMirror.Sync(_install, _headless, [Plugin("SAIN")], Now);

        Assert.Equal(["Waypoints"], result.Removed);
        Assert.False(Directory.Exists(Path.Combine(_headless, "BepInEx", "plugins", "Waypoints")));
        Assert.True(Directory.Exists(Path.Combine(_headless, "BepInEx", "plugins", "SAIN")));
        Assert.True(File.Exists(Path.Combine(_headless, "BepInEx", "plugins", "ByHand", "ByHand.dll")));
    }

    [Fact]
    public void A_folder_it_did_not_place_is_set_aside_and_one_it_did_is_replaced()
    {
        Write(_install, @"BepInEx\plugins\SAIN\SAIN.dll", "new");
        Write(_headless, @"BepInEx\plugins\SAIN\SAIN.dll", "by hand, older");

        var first = HeadlessMirror.Sync(_install, _headless, [Plugin("SAIN")], Now);

        Assert.Equal(["SAIN"], first.Copied);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_headless, "BepInEx", "plugins", "SAIN", "SAIN.dll")));
        var aside = Directory.GetFiles(Path.Combine(_headless, HeadlessMirror.SetAsideFolder), "SAIN.dll", SearchOption.AllDirectories);
        Assert.Equal("by hand, older", File.ReadAllText(Assert.Single(aside)));

        // An update in the install: its own copy is replaced, with nothing more set aside.
        Write(_install, @"BepInEx\plugins\SAIN\SAIN.dll", "newer still");
        var second = HeadlessMirror.Sync(_install, _headless, [Plugin("SAIN")], Now.AddMinutes(5));

        Assert.Equal(["SAIN"], second.Copied);
        Assert.Equal("newer still", File.ReadAllText(Path.Combine(_headless, "BepInEx", "plugins", "SAIN", "SAIN.dll")));
        Assert.Single(Directory.GetFiles(Path.Combine(_headless, HeadlessMirror.SetAsideFolder), "SAIN.dll", SearchOption.AllDirectories));
    }

    [Fact]
    public void Only_what_sits_directly_in_plugins_or_patchers_of_the_install_is_taken()
    {
        Write(_install, @"BepInEx\plugins\SAIN\SAIN.dll");
        var server = Write(_install, @"SPT_Runtime\user\mods\SAIN\SAIN.Server.dll");
        var off = Write(_install, @"BepInEx\plugins.disabled\Off\Off.dll");
        var elsewhere = Write(_root, @"Other\BepInEx\plugins\Else\Else.dll");

        var result = HeadlessMirror.Sync(_install, _headless,
            [Path.GetDirectoryName(server)!, Path.GetDirectoryName(off)!, Path.GetDirectoryName(elsewhere)!,
             Path.Combine(_install, "BepInEx", "plugins"), Path.Combine(_install, "BepInEx", "plugins", "SAIN", "SAIN.dll")], Now);

        Assert.Empty(result.Copied);
        Assert.Empty(result.Failed);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_headless, "BepInEx", "plugins")));
    }

    [Fact]
    public void A_folder_that_cannot_be_a_headless_client_is_refused_before_anything_is_copied()
    {
        Write(_install, @"BepInEx\plugins\SAIN\SAIN.dll");

        Assert.Equal(HeadlessMirrorProblem.NoFolder, HeadlessMirror.Sync(_install, Path.Combine(_root, "gone"), [Plugin("SAIN")], Now).Problem);
        Assert.Equal(HeadlessMirrorProblem.SameAsInstall, HeadlessMirror.Sync(_install, _install + Path.DirectorySeparatorChar, [Plugin("SAIN")], Now).Problem);

        var bare = Path.Combine(_root, "Bare");
        Directory.CreateDirectory(bare);
        Assert.Equal(HeadlessMirrorProblem.NotAGame, HeadlessMirror.Sync(_install, bare, [Plugin("SAIN")], Now).Problem);
    }

    [Fact]
    public void A_headless_folder_whose_plugins_folder_is_a_link_is_refused()
    {
        Write(_install, @"BepInEx\plugins\SAIN\SAIN.dll");

        var linked = Path.Combine(_root, "Linked");
        Directory.CreateDirectory(Path.Combine(linked, "BepInEx"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(linked, "BepInEx", "plugins"), Path.Combine(_install, "BepInEx", "plugins"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Making a link needs a right this account doesn't have; the check can't be shown here.
        }

        Assert.Equal(HeadlessMirrorProblem.Link, HeadlessMirror.Sync(_install, linked, [Plugin("SAIN")], Now).Problem);
        Assert.Single(Directory.GetFileSystemEntries(Path.Combine(_install, "BepInEx", "plugins")));
    }
}
