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

    // ---- SPT's own backups (user/profiles/backups, one per server start) ----

    private void SptBackup(string stamp, string text)
    {
        var folder = Path.Combine(_profiles, "backups", stamp);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.json"), text);
    }

    [Fact]
    public void SptsOwnBackups_AreLeftOut_AndANewOneIsNotAChange()
    {
        Profile("a.json", "1");
        SptBackup("2026-09-25_23-23-04", "0");

        var backup = _backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall);

        Assert.NotNull(backup);
        Assert.Equal(1, backup.Files);
        using (var zip = ZipFile.OpenRead(backup.Path)) Assert.Equal(["a.json"], zip.Entries.Select(e => e.FullName));

        // The server started again and made another of its own: the profiles are as they were.
        SptBackup("2026-09-26_16-33-36", "1");
        Assert.Null(_backups.BackupIfChanged(_install, ProfileBackups.BeforeRemove));
    }

    [Fact]
    public void WhereSptKeepsItsBackups_IsReadFromItsConfig()
    {
        var configs = Path.Combine(_install, "SPT", "SPT_Data", "configs");
        Directory.CreateDirectory(configs);

        File.WriteAllText(Path.Combine(configs, "backup.json"), """{ "enabled": true, "directory": "./user/profiles/old copies", }""");
        Assert.Equal("old copies", ProfileBackups.SptOwnBackupFolder(_install));

        // Somewhere else entirely: nothing in the profiles folder is SPT's.
        File.WriteAllText(Path.Combine(configs, "backup.json"), """{ "directory": "./user/profile-backups" }""");
        Assert.Null(ProfileBackups.SptOwnBackupFolder(_install));
        SptBackup("x", "kept");
        Profile("a.json", "1");
        Assert.Equal(2, _backups.BackupIfChanged(_install, ProfileBackups.BeforeInstall)?.Files);

        // SPT 3 keeps its configs a folder deeper.
        File.Delete(Path.Combine(configs, "backup.json"));
        var spt3 = Path.Combine(_install, "SPT", "SPT_Data", "Server", "configs");
        Directory.CreateDirectory(spt3);
        File.WriteAllText(Path.Combine(spt3, "backup.json"), """{ "directory": "./user/profiles/spt3-backups" }""");
        Assert.Equal("spt3-backups", ProfileBackups.SptOwnBackupFolder(_install));
        File.Delete(Path.Combine(spt3, "backup.json"));

        // Unreadable: SPT's shipped default.
        File.WriteAllText(Path.Combine(configs, "backup.json"), "{ not json");
        Assert.Equal("backups", ProfileBackups.SptOwnBackupFolder(_install));
    }

    [Fact]
    public void RestoringAnOlderCopyThatHoldsSptsBackups_DoesNotWriteThemBack()
    {
        // A copy taken before they were left out.
        var folder = _backups.FolderFor(_install);
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "20260101-000000-000_install_abcdef123456.zip");
        using (var zip = ZipFile.Open(old, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("a.json").Open())) w.Write("old");
            using (var w = new StreamWriter(zip.CreateEntry("backups/2025-01-01_00-00-00/a.json").Open())) w.Write("ancient");
        }

        Profile("a.json", "new");
        var listed = Assert.Single(_backups.List(_install));
        Assert.Equal(1, listed.Files);

        _backups.Restore(listed, _install);

        Assert.Equal("old", ProfileText("a.json"));
        Assert.False(Directory.Exists(Path.Combine(_profiles, "backups")));
    }
}
