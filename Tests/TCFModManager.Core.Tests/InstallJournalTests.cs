using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// An install cut off part-way is put on record, as incomplete, at the next start (1.3.0).
public class InstallJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ssptmm-journal-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;
    private static int _nextId = 970_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public InstallJournalTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "data", "installed-mods.json"));
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

    private string Write(string relative, string content)
    {
        var full = Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private int NewId()
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return id;
    }

    private string JournalDir => InstallJournal.DirectoryFor(_manifest);

    private InstallJournal.Entry Entry(int id, params string[] planned) => new()
    {
        ModId = id,
        Name = "Cut Off",
        Version = "2.0.0",
        InstallPath = _install,
        StartedAt = DateTimeOffset.UtcNow,
        Planned = [.. planned],
    };

    [Fact]
    public void ALeftoverJournal_PutsWhatIsOnDiskOnRecord_AsIncomplete()
    {
        var id = NewId();
        InstallJournal.Begin(_manifest, Entry(id, "BepInEx/plugins/CutOff/a.dll", "BepInEx/plugins/CutOff/b.dll"));
        Write("BepInEx/plugins/CutOff/a.dll", "placed");

        var recovered = InstallJournal.RecoverAll(_manifest);

        var found = Assert.Single(recovered);
        Assert.Equal("Cut Off", found.Name);
        Assert.Equal(1, found.FilesOnRecord);

        var record = Assert.Single(_manifest.Load().Mods);
        Assert.Equal(id, record.ModId);
        Assert.True(record.Incomplete);
        Assert.Equal(["BepInEx/plugins/CutOff/a.dll"], record.Files);
        Assert.Empty(Directory.EnumerateFiles(JournalDir));
    }

    [Fact]
    public void ItReplacesThePreviousVersionsRecord()
    {
        var id = NewId();
        var manifest = _manifest.Load();
        manifest.Mods.Add(new InstalledModRecord
        {
            ModId = id, Name = "Cut Off", Version = "1.0.0", InstalledAt = DateTimeOffset.UtcNow,
            Files = ["BepInEx/plugins/CutOff/old.dll"],
        });
        _manifest.Save(manifest);
        InstallJournal.Begin(_manifest, Entry(id, "BepInEx/plugins/CutOff/a.dll"));
        Write("BepInEx/plugins/CutOff/a.dll", "placed");

        InstallJournal.RecoverAll(_manifest);

        var record = Assert.Single(_manifest.Load().Mods);
        Assert.Equal("2.0.0", record.Version);
        Assert.True(record.Incomplete);
    }

    [Fact]
    public void PathsOutsideTheInstall_AreNotRecorded()
    {
        var id = NewId();
        InstallJournal.Begin(_manifest, Entry(id, "BepInEx/plugins/CutOff/a.dll", "../outside.txt", "SPT/SPT.Server.exe"));
        Write("BepInEx/plugins/CutOff/a.dll", "placed");

        InstallJournal.RecoverAll(_manifest);

        Assert.Equal(["BepInEx/plugins/CutOff/a.dll"], Assert.Single(_manifest.Load().Mods).Files);
    }

    [Fact]
    public void ADamagedJournal_IsSetAside_AndNothingIsRecorded()
    {
        Directory.CreateDirectory(JournalDir);
        File.WriteAllText(Path.Combine(JournalDir, "broken.json"), "{ not json");

        Assert.Empty(InstallJournal.RecoverAll(_manifest));
        Assert.Empty(_manifest.Load().Mods);
        Assert.False(File.Exists(Path.Combine(JournalDir, "broken.json")));
    }

    // A planned file that was there before and is untouched - the user's config, say - isn't claimed.
    [Fact]
    public void AnUnchangedFileThatWasAlreadyThere_IsNotClaimed()
    {
        var id = NewId();
        Write("BepInEx/config/cut.off.cfg", "the user's");
        InstallJournal.Begin(_manifest, Entry(id, "BepInEx/config/cut.off.cfg", "BepInEx/plugins/CutOff/a.dll"));
        Write("BepInEx/plugins/CutOff/a.dll", "placed");

        InstallJournal.RecoverAll(_manifest);

        Assert.Equal(["BepInEx/plugins/CutOff/a.dll"], Assert.Single(_manifest.Load().Mods).Files);
    }

    // Cut off while the previous version was being removed: what is left of it stays on record.
    [Fact]
    public void WhatIsLeftOfThePreviousVersion_StaysOnRecord_WithItsFingerprint()
    {
        var id = NewId();
        var old = Write("BepInEx/plugins/CutOff/old.dll", "old");
        var print = FileFingerprint.Compute(old, "BepInEx/plugins/CutOff/old.dll")!;
        var manifest = _manifest.Load();
        manifest.Mods.Add(new InstalledModRecord
        {
            ModId = id, Name = "Cut Off", Version = "1.0.0", InstalledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Files = ["BepInEx/plugins/CutOff/old.dll", "BepInEx/plugins/CutOff/gone.dll"],
            Fingerprints = [print],
            InstallPath = InstallStamp.Of(_install),
        });
        _manifest.Save(manifest);
        InstallJournal.Begin(_manifest, Entry(id, "BepInEx/plugins/CutOff/a.dll"));

        InstallJournal.RecoverAll(_manifest);

        var record = Assert.Single(_manifest.Load().Mods);
        Assert.True(record.Incomplete);
        Assert.Equal("1.0.0", record.Version); // nothing of 2.0.0 placed yet
        Assert.Equal(["BepInEx/plugins/CutOff/old.dll"], record.Files);
        Assert.Equal(print, Assert.Single(record.Fingerprints));
    }

    // The new version uses the same paths as the old: cut off before any was overwritten, what is on
    // disk is still the old version - its label and fingerprint stay. One overwritten makes it the new.
    [Fact]
    public void OverlappingPaths_UntouchedKeepTheOldLabel_OverwrittenTakeTheNew()
    {
        var id = NewId();
        var path = "BepInEx/plugins/CutOff/same.dll";
        var full = Write(path, "old");
        var print = FileFingerprint.Compute(full, path)!;
        var manifest = _manifest.Load();
        manifest.Mods.Add(new InstalledModRecord
        {
            ModId = id, Name = "Cut Off", Version = "1.0.0", InstalledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Files = [path], Fingerprints = [print], InstallPath = InstallStamp.Of(_install),
        });
        _manifest.Save(manifest);
        var journal = InstallJournal.Begin(_manifest, Entry(id, path));

        var untouched = InstallJournal.RecoverOne(journal, _manifest);
        var record = Assert.Single(_manifest.Load().Mods);
        Assert.NotNull(untouched);
        Assert.Equal("1.0.0", record.Version);
        Assert.Equal([path], record.Files);
        Assert.Equal(print, Assert.Single(record.Fingerprints));

        // Again, with the file replaced by the new version's.
        manifest = _manifest.Load();
        manifest.Mods.Clear();
        manifest.Mods.Add(new InstalledModRecord
        {
            ModId = id, Name = "Cut Off", Version = "1.0.0", InstalledAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Files = [path], Fingerprints = [print], InstallPath = InstallStamp.Of(_install),
        });
        _manifest.Save(manifest);
        journal = InstallJournal.Begin(_manifest, Entry(id, path));
        File.WriteAllText(full, "new version, longer");

        InstallJournal.RecoverOne(journal, _manifest);
        record = Assert.Single(_manifest.Load().Mods);
        Assert.Equal("2.0.0", record.Version);
        Assert.Equal([path], record.Files);
        Assert.Empty(record.Fingerprints);
    }

    // The install finished and only its journal stayed behind: the finished record is left alone.
    [Fact]
    public void AJournalLeftAfterTheInstallFinished_ChangesNothing()
    {
        var id = NewId();
        var entry = Entry(id, "BepInEx/plugins/CutOff/a.dll");
        entry.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        InstallJournal.Begin(_manifest, entry);
        Write("BepInEx/plugins/CutOff/a.dll", "placed");
        var manifest = _manifest.Load();
        manifest.Mods.Add(new InstalledModRecord
        {
            ModId = id, Name = "Cut Off", Version = "2.0.0", InstalledAt = DateTimeOffset.UtcNow,
            Files = ["BepInEx/plugins/CutOff/a.dll"], InstallPath = InstallStamp.Of(_install),
        });
        _manifest.Save(manifest);

        Assert.Empty(InstallJournal.RecoverAll(_manifest));
        Assert.False(Assert.Single(_manifest.Load().Mods).Incomplete);
        Assert.Empty(Directory.EnumerateFiles(JournalDir, "*.json"));
    }

    [Fact]
    public void AJournalWithNoInstallPath_IsSetAside()
    {
        Directory.CreateDirectory(JournalDir);
        File.WriteAllText(Path.Combine(JournalDir, "odd.json"),
            """{ "ModId": 1, "Name": "x", "Version": "1", "InstallPath": null, "Planned": null }""");

        Assert.Empty(InstallJournal.RecoverAll(_manifest));
        Assert.Empty(_manifest.Load().Mods);
        Assert.Empty(Directory.EnumerateFiles(JournalDir, "*.json"));
    }

    [Fact]
    public void NoJournal_NothingHappens() => Assert.Empty(InstallJournal.RecoverAll(_manifest));

    private sealed class ArchiveHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public async Task AFinishedInstall_LeavesNoJournal()
    {
        var service = new ModInstallService(
            new ModDownloadService(new HttpClient(new ArchiveHandler(ArchiveFileTests.Zip(("BepInEx/plugins/Mod/mod.dll", "v1"))))),
            _manifest,
            removedMods: new RemovedMods(() => RemovedModsRetention.UntilCleared));
        var target = new InstallTarget(NewId(), false, "Finished", null, null, null);

        await service.InstallAsync(target, new ModVersion { Id = 1, Version = "1.0.0", Link = "https://example.test/a" }, _install);

        Assert.False(Directory.Exists(JournalDir) && Directory.EnumerateFiles(JournalDir).Any());
        Assert.False(Assert.Single(_manifest.Load().Mods).Incomplete);
    }
}
