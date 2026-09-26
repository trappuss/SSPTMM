using System.IO.Compression;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ProfileBackupsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TCFModManagerProfileTests_" + Guid.NewGuid());
    private readonly string _install;
    private readonly string _profiles;
    private readonly ProfileBackups _backups;

    public ProfileBackupsTests()
    {
        _install = Path.Combine(_root, "SPT install");
        Directory.CreateDirectory(Path.Combine(_install, "SPT"));
        File.WriteAllText(Path.Combine(_install, "SPT", "SPT.Server.exe"), "");
        _profiles = Path.Combine(_install, "SPT", "user", "profiles");
        Directory.CreateDirectory(_profiles);
        _backups = new ProfileBackups(Path.Combine(_root, "backups"), keep: 3);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void Profile(string name, string text) => File.WriteAllText(Path.Combine(_profiles, name), text);

    private string ProfileText(string name) => File.ReadAllText(Path.Combine(_profiles, name));

    [Fact]
    public void TheProfilesFolder_IsUnderTheServersOwnFolder()
    {
        Assert.Equal(_profiles, ProfileBackups.ProfilesFolder(_install));
    }

    [Fact]
    public void ACopy_HoldsEveryProfile()
    {
        Profile("a.json", "{\"a\":1}");
        Profile("b.json", "{\"b\":2}");

        var backup = _backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall);

        Assert.NotNull(backup);
        Assert.Equal(2, backup.Files);
        Assert.Equal(ProfileBackups.BeforeInstall, backup.Reason);
        using var zip = ZipFile.OpenRead(backup.Path);
        Assert.Equal(["a.json", "b.json"], zip.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public void NoNewCopy_WhenNothingChangedSinceTheLast()
    {
        Profile("a.json", "1");

        Assert.NotNull(_backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall));
        Assert.Null(_backups.BackupIfChanged(_install, ProfileBackups.BeforeRemove));

        Profile("a.json", "2");
        Assert.NotNull(_backups.BackupIfChanged(_install, ProfileBackups.BeforeRemove));
        Assert.Equal(2, _backups.List(_install).Count);
    }

    [Fact]
    public void NoCopy_WhenThereAreNoProfiles()
    {
        Assert.Null(_backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall));
        Assert.Empty(_backups.List(_install));
    }

    [Fact]
    public void OnlyTheNewestFewAreKept()
    {
        for (var i = 0; i < 5; i++)
        {
            Profile("a.json", i.ToString());
            Assert.NotNull(_backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall));
        }

        var kept = _backups.List(_install);
        Assert.Equal(3, kept.Count);

        // The newest first, and it holds the latest content.
        using var zip = ZipFile.OpenRead(kept[0].Path);
        using var reader = new StreamReader(zip.GetEntry("a.json")!.Open());
        Assert.Equal("4", reader.ReadToEnd());
    }

    [Fact]
    public void PuttingBack_RestoresTheFiles_CopiesWhatWasThere_AndLeavesNewerProfiles()
    {
        Profile("a.json", "old");
        var backup = _backups.BackupIfChanged(_install, ProfileBackups.BeforeRemove)!;

        Profile("a.json", "broken");
        Profile("new.json", "made later");

        var before = _backups.Restore(backup, _install);

        Assert.Equal("old", ProfileText("a.json"));
        Assert.Equal("made later", ProfileText("new.json"));

        // What was there is itself a copy, so the restore can be undone.
        Assert.NotNull(before);
        Assert.Equal(ProfileBackups.BeforeRestore, before.Reason);
        _backups.Restore(before, _install);
        Assert.Equal("broken", ProfileText("a.json"));
    }

    [Fact]
    public void EachInstallKeepsItsOwnCopies()
    {
        Profile("a.json", "1");
        _backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall);

        var other = Path.Combine(_root, "Other install");
        Directory.CreateDirectory(Path.Combine(other, "SPT", "user", "profiles"));
        File.WriteAllText(Path.Combine(other, "SPT", "SPT.Server.exe"), "");

        Assert.Single(_backups.List(_install));
        Assert.Empty(_backups.List(other));
        Assert.NotEqual(_backups.FolderFor(_install), _backups.FolderFor(other));
    }

    [Fact]
    public void BackUpNow_TakesACopyEvenWhenNothingChanged()
    {
        Profile("a.json", "1");
        _backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall);

        var manual = _backups.BackupNow(_install);

        Assert.NotNull(manual);
        Assert.Equal(ProfileBackups.ByHand, manual.Reason);
        Assert.Equal(2, _backups.List(_install).Count);
    }

    [Fact]
    public void PuttingBackTheOldestCopy_WhenAllAreKept_StillWorks_AndKeepsIt()
    {
        for (var i = 0; i < 3; i++)
        {
            Profile("a.json", "v" + i);
            _backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall);
        }

        Profile("a.json", "now");
        var oldest = _backups.List(_install).Last();

        _backups.Restore(oldest, _install);

        Assert.Equal("v0", ProfileText("a.json"));
        Assert.Contains(_backups.List(_install), b => b.Path == oldest.Path);
    }
}
