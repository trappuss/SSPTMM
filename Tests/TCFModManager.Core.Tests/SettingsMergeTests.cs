using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Two parts of the app that load the settings, change different things and save, in either order,
// both keep their change (SSPTMM 1.3.0). Before, the later save put back the earlier one's values.
//
public class SettingsMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ssptmm-settings-" + Guid.NewGuid().ToString("N"));
    private readonly string _file;

    public SettingsMergeTests()
    {
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private SettingsService Service() => new(_file);

    [Fact]
    public void OverlappingSaves_KeepBothChanges()
    {
        Service().Save(new AppSettings { SptInstallPath = @"C:\SPT" });

        var first = Service().Load();
        var second = Service().Load();

        first.CloseGameWithServer = true;
        second.SmoothScrolling = false;

        Service().Save(first);
        Service().Save(second);

        var saved = Service().Load();
        Assert.True(saved.CloseGameWithServer);
        Assert.False(saved.SmoothScrolling);
        Assert.Equal(@"C:\SPT", saved.SptInstallPath);
    }

    [Fact]
    public void TheSameValueChangedTwice_TheLaterSaveWins()
    {
        var first = Service().Load();
        var second = Service().Load();

        first.ShareName = "first";
        second.ShareName = "second";

        Service().Save(first);
        Service().Save(second);

        Assert.Equal("second", Service().Load().ShareName);
    }

    [Fact]
    public void NestedSettings_MergeValueByValue()
    {
        var first = Service().Load();
        var second = Service().Load();

        first.ServerMap.ShowPage = true;
        second.UpdateNotifications.Enabled = true;

        Service().Save(first);
        Service().Save(second);

        var saved = Service().Load();
        Assert.True(saved.ServerMap.ShowPage);
        Assert.True(saved.UpdateNotifications.Enabled);
    }

    [Fact]
    public void DictionaryKeys_AddedAndRemovedInDifferentCopies_BothApply()
    {
        var setup = Service().Load();
        setup.PageSizes["Browse"] = 30;
        Service().Save(setup);

        var first = Service().Load();
        var second = Service().Load();

        first.PageSizes.Remove("Browse");
        second.PageSizes["Installed"] = 50;

        Service().Save(first);
        Service().Save(second);

        var saved = Service().Load();
        Assert.False(saved.PageSizes.ContainsKey("Browse"));
        Assert.Equal(50, saved.PageSizes["Installed"]);
    }

    [Fact]
    public void ACopyNeverLoaded_IsWrittenWhole()
    {
        var loaded = Service().Load();
        loaded.ShareName = "kept?";
        Service().Save(loaded);

        Service().Save(new AppSettings { SptInstallPath = @"D:\Other" });

        var saved = Service().Load();
        Assert.Equal(@"D:\Other", saved.SptInstallPath);
        Assert.Null(saved.ShareName);
    }

    [Fact]
    public void SavingTheSameCopyTwice_OnlyTheLaterChangesGoOverOthers()
    {
        var mine = Service().Load();
        mine.ShareName = "mine";
        Service().Save(mine);

        var other = Service().Load();
        other.ShareName = "other";
        Service().Save(other);

        // mine saves again with an unrelated change: its ShareName is no longer a change of its own.
        mine.CloseGameWithServer = true;
        Service().Save(mine);

        var saved = Service().Load();
        Assert.Equal("other", saved.ShareName);
        Assert.True(saved.CloseGameWithServer);
    }

    [Fact]
    public void UnknownProperties_InTheFile_AreKept()
    {
        File.WriteAllText(_file, """{ "SptInstallPath": "C:\\SPT", "FromANewerVersion": 42 }""");

        var settings = Service().Load();
        settings.ShareName = "x";
        Service().Save(settings);

        Assert.Contains("FromANewerVersion", File.ReadAllText(_file));
    }

    // A value of the wrong type: Load falls back to defaults, and the next save repairs the file
    // rather than merging onto it and leaving the bad value in.
    [Fact]
    public void AFileThatDoesNotLoad_IsWrittenWhole()
    {
        File.WriteAllText(_file, """{ "SmoothScrolling": "not a bool", "SptInstallPath": "C:\\Old" }""");

        var settings = Service().Load();
        settings.SptInstallPath = @"D:\New";
        Service().Save(settings);

        var saved = Service().Load();
        Assert.Equal(@"D:\New", saved.SptInstallPath);
        Assert.True(saved.SmoothScrolling);
    }

    // The same key twice loads (the last wins) but can't be merged onto: written whole, no throw.
    [Fact]
    public void ADuplicateKey_IsWrittenWhole()
    {
        File.WriteAllText(_file, """{ "ShareName": "a", "ShareName": "b" }""");

        var settings = Service().Load();
        settings.SptInstallPath = @"D:\New";
        Service().Save(settings);

        var saved = Service().Load();
        Assert.Equal(@"D:\New", saved.SptInstallPath);
        Assert.Equal("b", saved.ShareName);
    }

    // Valid when loaded, then hand-edited into something that isn't: the merge result is checked.
    [Fact]
    public void AFileBrokenAfterLoad_IsWrittenWhole()
    {
        Service().Save(new AppSettings { ShareName = "kept" });
        var settings = Service().Load();

        File.WriteAllText(_file, """{ "SmoothScrolling": 5, "ShareName": "kept" }""");
        settings.SptInstallPath = @"D:\New";
        Service().Save(settings);

        var saved = Service().Load();
        Assert.Equal(@"D:\New", saved.SptInstallPath);
        Assert.Equal("kept", saved.ShareName);
    }
}
