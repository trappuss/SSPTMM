using System.Globalization;
using System.Text;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Stage 2 of CLOSED-10-TCFResilience-DESIGN.md: how the app's own files are written and kept, and the
// record fields stages 3 and 4 fill in. Every test works in its own temp folder and checks the disk
// afterwards.
//
public class ResilienceRecordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-records-" + Guid.NewGuid().ToString("N"));

    public ResilienceRecordTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_root, name);

    private static string Stamp(DateTime time) => time.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

    // --- SafeFile.Write ------------------------------------------------------------------------

    [Fact]
    public void WriteText_WritesTheFileAndLeavesNoTemporaryBehind()
    {
        SafeFile.WriteText(P("a.json"), "{\"x\":1}");

        Assert.Equal("{\"x\":1}", File.ReadAllText(P("a.json")));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void WriteText_KeepsAByteOrderMarkWhenAskedTo()
    {
        SafeFile.WriteText(P("bom.json"), "{}", encoding: new UTF8Encoding(true));
        SafeFile.WriteText(P("nobom.json"), "{}");

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' }, File.ReadAllBytes(P("bom.json")));
        Assert.Equal(new byte[] { (byte)'{', (byte)'}' }, File.ReadAllBytes(P("nobom.json")));
    }

    [Fact]
    public void WriteText_WithBackups_CopiesTheOldFileOnce_ThenWaitsForTheInterval()
    {
        File.WriteAllText(P("store.json"), "{\"v\":1}");

        SafeFile.WriteText(P("store.json"), "{\"v\":2}", keepBackups: true);
        SafeFile.WriteText(P("store.json"), "{\"v\":3}", keepBackups: true);

        var backups = Directory.GetFiles(SafeFile.BackupFolderFor(P("store.json")));
        Assert.Single(backups);
        Assert.Equal("{\"v\":1}", File.ReadAllText(backups[0]));
        Assert.Equal("{\"v\":3}", File.ReadAllText(P("store.json")));
    }

    [Fact]
    public void WriteText_WithBackups_KeepsOnlyTheNewestFive()
    {
        var folder = SafeFile.BackupFolderFor(P("store.json"));
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 7; i++)
            File.WriteAllText(Path.Combine(folder, Stamp(DateTime.Now.AddDays(-10 + i)) + ".json"), $"{{\"old\":{i}}}");

        File.WriteAllText(P("store.json"), "{\"v\":1}");
        SafeFile.WriteText(P("store.json"), "{\"v\":2}", keepBackups: true);

        var kept = Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(SafeFile.BackupsKept, kept.Count);
        Assert.Equal("{\"v\":1}", File.ReadAllText(kept[^1]));
        Assert.DoesNotContain(kept, f => File.ReadAllText(f) == "{\"old\":0}");
    }

    [Fact]
    public void WriteText_WithBackups_NeverBacksUpADamagedFile()
    {
        File.WriteAllText(P("store.json"), "{ not json");

        SafeFile.WriteText(P("store.json"), "{}", keepBackups: true);

        Assert.False(Directory.Exists(SafeFile.BackupFolderFor(P("store.json")))
                     && Directory.EnumerateFiles(SafeFile.BackupFolderFor(P("store.json"))).Any());
    }

    // --- SafeFile.PreserveDamaged --------------------------------------------------------------

    [Fact]
    public void PreserveDamaged_KeepsOneCopyPerDistinctContent()
    {
        File.WriteAllText(P("store.json"), "{ broken");

        var first = SafeFile.PreserveDamaged(P("store.json"));
        var again = SafeFile.PreserveDamaged(P("store.json"));

        Assert.NotNull(first);
        Assert.Equal(first, again);
        Assert.Equal("{ broken", File.ReadAllText(first!));

        File.WriteAllText(P("store.json"), "{ broken differently");
        var second = SafeFile.PreserveDamaged(P("store.json"));

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(_root, "store.json.damaged-*").Length);
    }

    [Fact]
    public void PreserveDamaged_KeepsAtMostFive()
    {
        for (var i = 0; i < 8; i++)
            File.WriteAllText(P($"store.json.damaged-{Stamp(DateTime.Now.AddDays(-10 + i))}"), $"old {i}");

        File.WriteAllText(P("store.json"), "{ broken now");
        SafeFile.PreserveDamaged(P("store.json"));

        var copies = Directory.GetFiles(_root, "store.json.damaged-*");
        Assert.Equal(SafeFile.DamagedCopiesKept, copies.Length);
        Assert.Contains(copies, c => File.ReadAllText(c) == "{ broken now");
    }

    [Fact]
    public void PreserveDamaged_NothingToCopy_ReturnsNull() =>
        Assert.Null(SafeFile.PreserveDamaged(P("missing.json")));

    // --- Stores ---------------------------------------------------------------------------------

    [Fact]
    public void Manifest_ADamagedFileIsKept_AndTheNextSaveCantLoseIt()
    {
        const string damaged = "{ \"Mods\": [ { \"ModId\": 1, \"Name\": \"cut off";
        File.WriteAllText(P("installed-mods.json"), damaged);
        var service = new ModInstallManifestService(P("installed-mods.json"));

        Assert.Empty(service.Load().Mods);
        service.Save(new ModInstallManifest());

        var copy = Assert.Single(Directory.GetFiles(_root, "installed-mods.json.damaged-*"));
        Assert.Equal(damaged, File.ReadAllText(copy));
    }

    [Fact]
    public void ModLists_ADamagedFileIsKept()
    {
        File.WriteAllText(P("mod_lists.json"), "{ \"Lists\": [ oops");

        new ModListStore(P("mod_lists.json")).Load();

        Assert.Single(Directory.GetFiles(_root, "mod_lists.json.damaged-*"));
    }

    [Fact]
    public void DownloadLedger_ADamagedFileIsKept()
    {
        File.WriteAllText(P("downloads.json"), "[[[");

        new DownloadLedgerService(P("downloads.json")).Load();

        Assert.Single(Directory.GetFiles(_root, "downloads.json.damaged-*"));
    }

    [Fact]
    public void Manifest_SavesKeepABackupOfTheLastGoodFile()
    {
        var service = new ModInstallManifestService(P("installed-mods.json"));
        service.Save(new ModInstallManifest { Mods = [Record(1)] });
        service.Save(new ModInstallManifest { Mods = [Record(1), Record(2)] });

        var backup = Assert.Single(Directory.GetFiles(SafeFile.BackupFolderFor(P("installed-mods.json"))));
        Assert.Contains("\"ModId\": 1", File.ReadAllText(backup));
        Assert.DoesNotContain("\"ModId\": 2", File.ReadAllText(backup));
    }

    // --- Record fields ---------------------------------------------------------------------------

    [Fact]
    public void Record_NewFieldsRoundTrip()
    {
        var service = new ModInstallManifestService(P("installed-mods.json"));
        var record = new InstalledModRecord
        {
            ModId = 7,
            Name = "Seven",
            Version = "1.0.0",
            InstalledAt = DateTimeOffset.UtcNow,
            Files = ["BepInEx/plugins/Seven/s.dll"],
            Fingerprints = [new FileFingerprint("BepInEx/plugins/Seven/s.dll", 3, "ABC")],
            InstallPath = @"D:\Single Player Tarkov Server",
            Overwrote = [new OverwrittenFile("BepInEx/plugins/Other/o.dll", 9, "DEF", @"overwritten\7\o.dll")],
        };

        service.Save(new ModInstallManifest { Mods = [record] });
        var loaded = Assert.Single(service.Load().Mods);

        Assert.Equal(3, loaded.FingerprintFor("bepinex/PLUGINS/seven/S.DLL")!.Size);
        Assert.Equal(@"D:\Single Player Tarkov Server", loaded.InstallPath);
        Assert.Equal("DEF", Assert.Single(loaded.Overwrote).Sha256);
    }

    [Fact]
    public void Record_AFileWrittenBeforeTheNewFieldsStillLoads()
    {
        File.WriteAllText(P("installed-mods.json"), """
            { "Mods": [ { "ModId": 3, "Name": "Old", "Version": "1.0", "InstalledAt": "2026-08-01T00:00:00+00:00",
                          "Files": [ "BepInEx/plugins/Old/o.dll" ] } ] }
            """);

        var loaded = Assert.Single(new ModInstallManifestService(P("installed-mods.json")).Load().Mods);

        Assert.Empty(loaded.Fingerprints);
        Assert.Null(loaded.InstallPath);
        Assert.Empty(loaded.Overwrote);
        Assert.Null(loaded.FingerprintFor("BepInEx/plugins/Old/o.dll"));
    }

    [Fact]
    public void SetManualVersion_KeepsFingerprintsStampAndOriginals()
    {
        var service = new ModInstallManifestService(P("installed-mods.json"));
        service.Save(new ModInstallManifest
        {
            Mods =
            [
                new InstalledModRecord
                {
                    ModId = 5, Name = "Five", Version = "1.0.0", InstalledAt = DateTimeOffset.UtcNow,
                    Files = ["BepInEx/plugins/Five/f.dll"],
                    Fingerprints = [new FileFingerprint("BepInEx/plugins/Five/f.dll", 1, "AA")],
                    InstallPath = "X",
                    Overwrote = [new OverwrittenFile("a", 1, "BB", "c")],
                },
            ],
        });

        var updated = service.SetManualVersion(5, null, "Five", "1.0.1", null, []);

        Assert.Single(updated.Fingerprints);
        Assert.Equal("X", updated.InstallPath);
        Assert.Single(updated.Overwrote);
    }

    // --- FileFingerprint --------------------------------------------------------------------------

    [Fact]
    public void Fingerprint_MatchesOnlyTheExactBytes()
    {
        File.WriteAllBytes(P("mod.dll"), [1, 2, 3, 4]);
        var print = FileFingerprint.Compute(P("mod.dll"), "BepInEx/plugins/M/mod.dll")!;

        Assert.Equal(4, print.Size);
        Assert.Equal(64, print.Sha256.Length);
        Assert.True(print.Matches(P("mod.dll")));

        File.WriteAllBytes(P("mod.dll"), [1, 2, 3, 5]);
        Assert.False(print.Matches(P("mod.dll")));

        File.WriteAllBytes(P("mod.dll"), [1, 2, 3, 4, 5]);
        Assert.False(print.Matches(P("mod.dll")));

        File.Delete(P("mod.dll"));
        Assert.False(print.Matches(P("mod.dll")));
        Assert.Null(FileFingerprint.Compute(P("mod.dll"), "x"));
    }

    // --- Mod config saves (D20) -------------------------------------------------------------------

    [Fact]
    public void ConfigSave_RefusesWhenTheBackupCantBeMade_AndLeavesTheFile()
    {
        var install = P("install");
        var config = Path.Combine(install, "SPT", "user", "mods", "M", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "{\"a\":1}");

        // A file where the backup's timestamped folder has to go makes the copy impossible.
        var timestamp = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
        Directory.CreateDirectory(ModConfigStore.BackupDirectory);
        var blocker = Path.Combine(ModConfigStore.BackupDirectory, $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}");
        File.WriteAllText(blocker, "in the way");

        try
        {
            var loaded = ModConfigStore.Load(config);
            var result = ModConfigStore.Save(install, config, "{\"a\":2}", loaded, timestamp);

            Assert.Equal(ModConfigSaveOutcome.BackupFailed, result.Outcome);
            Assert.NotNull(result.Error);
            Assert.Equal("{\"a\":1}", File.ReadAllText(config));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void ConfigSave_TwoSavesInOneSecond_KeepBothBackups()
    {
        var install = P("install");
        var config = Path.Combine(install, "SPT", "user", "mods", "M", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "{\"a\":1}");
        var timestamp = new DateTimeOffset(2001, 2, 3, 4, 5, 7, TimeSpan.Zero);
        var stamp = $"{timestamp.ToLocalTime():yyyyMMdd-HHmmss}";

        try
        {
            var first = ModConfigStore.Save(install, config, "{\"a\":2}", ModConfigStore.Load(config), timestamp);
            var second = ModConfigStore.Save(install, config, "{\"a\":3}", first.Saved!, timestamp);

            Assert.Equal(ModConfigSaveOutcome.Saved, second.Outcome);
            Assert.Equal("{\"a\":1}", File.ReadAllText(first.BackupPath!));
            Assert.Equal("{\"a\":2}", File.ReadAllText(second.BackupPath!));
            Assert.Equal("{\"a\":3}", File.ReadAllText(config));
        }
        finally
        {
            foreach (var dir in Directory.GetDirectories(ModConfigStore.BackupDirectory, stamp + "*"))
                Directory.Delete(dir, recursive: true);
        }
    }

    private static InstalledModRecord Record(int id) => new()
    {
        ModId = id,
        Name = $"Mod {id}",
        Version = "1.0.0",
        InstalledAt = DateTimeOffset.UtcNow,
    };
}
