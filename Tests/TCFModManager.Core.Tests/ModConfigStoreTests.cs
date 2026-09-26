using System.Text;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModConfigStoreTests : IDisposable
{
    private readonly string _installRoot;
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 25, 12, 15, 0, TimeSpan.Zero);

    public ModConfigStoreTests()
    {
        _installRoot = Path.Combine(Path.GetTempPath(), "TCFModManagerConfigStoreTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_installRoot);
    }

    //
    // Backups go to the app's own Data folder rather than anywhere under _installRoot, so for a test
    // run that folder is next to the test assembly and has to be cleared too. xunit builds a fresh
    // instance per test and runs a class's tests sequentially, so wiping the whole thing here gives
    // each test a clean backup folder rather than one shared across the class.
    //
    public void Dispose()
    {
        if (Directory.Exists(_installRoot)) Directory.Delete(_installRoot, recursive: true);
        if (Directory.Exists(ModConfigStore.BackupDirectory)) Directory.Delete(ModConfigStore.BackupDirectory, recursive: true);
    }

    private string WriteFile(string relative, string text, bool byteOrderMark = false)
    {
        var path = Path.Combine(_installRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(byteOrderMark));
        return path;
    }

    private ModConfigSaveResult Save(string path, string text, ModConfigDocument loaded, bool overwrite = false) =>
        ModConfigStore.Save(_installRoot, path, text, loaded, Timestamp, overwrite);

    [Fact]
    public void Load_ReadsTheTextAndNoticesThereIsNoByteOrderMark()
    {
        var path = WriteFile("BepInEx/config/some.cfg", "[General]\nEnabled = true\n");

        var loaded = ModConfigStore.Load(path);

        Assert.Equal("[General]\nEnabled = true\n", loaded.Text);
        Assert.False(loaded.HasByteOrderMark);
    }

    [Fact]
    public void Load_StripsAByteOrderMarkFromTheTextButRemembersItWasThere()
    {
        var path = WriteFile("user/mods/Some/config/config.json", "{ \"a\": 1 }", byteOrderMark: true);

        var loaded = ModConfigStore.Load(path);

        Assert.Equal("{ \"a\": 1 }", loaded.Text);
        Assert.True(loaded.HasByteOrderMark);
    }

    [Fact]
    public void Save_WritesTheFileAndCopiesTheOldContentsAside()
    {
        var path = WriteFile("BepInEx/config/some.cfg", "[General]\nEnabled = true\n");
        var loaded = ModConfigStore.Load(path);

        var result = Save(path, "[General]\nEnabled = false\n", loaded);

        Assert.True(result.Succeeded);
        Assert.Equal("[General]\nEnabled = false\n", File.ReadAllText(path));

        Assert.NotNull(result.BackupPath);
        Assert.Equal("[General]\nEnabled = true\n", File.ReadAllText(result.BackupPath!));

        // In the app's own Data folder, not the SPT install - but laid out at the file's own path
        // relative to the install, so a whole timestamped folder can still be copied back over an
        // SPT install to undo a round of edits.
        //
        // The folder is named in LOCAL time - it is something the user goes looking for by the clock
        // on their own wall - so the name is derived here rather than written out. Pinned to
        // "20260825-121500" this passed only where the machine running it was on UTC.
        Assert.Equal(
            Path.Combine(
                ModConfigStore.BackupDirectory,
                Timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmss"),
                "BepInEx",
                "config",
                "some.cfg"),
            result.BackupPath);

        Assert.StartsWith(AppPaths.DataDirectory, result.BackupPath!);
        Assert.DoesNotContain(_installRoot, result.BackupPath!);
    }

    [Fact]
    public void Save_HandsBackADocumentThatCanBeSavedOverAgain()
    {
        var path = WriteFile("BepInEx/config/some.cfg", "[General]\nEnabled = true\n");

        var first = Save(path, "[General]\nEnabled = false\n", ModConfigStore.Load(path));
        var second = Save(path, "[General]\nEnabled = true\n", first.Saved!);

        Assert.True(second.Succeeded);
        Assert.Equal("[General]\nEnabled = true\n", File.ReadAllText(path));
    }

    [Fact]
    public void Save_RefusesWhenTheFileChangedOnDiskAfterItWasLoaded()
    {
        var path = WriteFile("BepInEx/config/some.cfg", "[General]\nEnabled = true\n");
        var loaded = ModConfigStore.Load(path);

        File.WriteAllText(path, "[General]\nEnabled = maybe\n");
        File.SetLastWriteTimeUtc(path, loaded.LastWriteUtc.AddMinutes(1));

        var result = Save(path, "[General]\nEnabled = false\n", loaded);

        Assert.Equal(ModConfigSaveOutcome.ChangedOnDisk, result.Outcome);
        Assert.Equal("[General]\nEnabled = maybe\n", File.ReadAllText(path));
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public void Save_OverwritesAChangedFileOnceTheUserHasSaidTo()
    {
        var path = WriteFile("BepInEx/config/some.cfg", "[General]\nEnabled = true\n");
        var loaded = ModConfigStore.Load(path);

        File.WriteAllText(path, "[General]\nEnabled = maybe\n");
        File.SetLastWriteTimeUtc(path, loaded.LastWriteUtc.AddMinutes(1));

        var result = Save(path, "[General]\nEnabled = false\n", loaded, overwrite: true);

        Assert.True(result.Succeeded);
        Assert.Equal("[General]\nEnabled = false\n", File.ReadAllText(path));

        // What was actually on disk is what got backed up, not what was loaded earlier.
        Assert.Equal("[General]\nEnabled = maybe\n", File.ReadAllText(result.BackupPath!));
    }

    [Fact]
    public void Save_RefusesInvalidJsonAndLeavesTheFileAlone()
    {
        var path = WriteFile("user/mods/Some/config/config.json", "{ \"a\": 1 }");
        var loaded = ModConfigStore.Load(path);

        var result = Save(path, "{ \"a\": }", loaded);

        Assert.Equal(ModConfigSaveOutcome.Invalid, result.Outcome);
        Assert.Equal("{ \"a\": 1 }", File.ReadAllText(path));
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Save_KeepsAByteOrderMarkTheFileAlreadyHad()
    {
        var path = WriteFile("user/mods/Some/config/config.json", "{ \"a\": 1 }", byteOrderMark: true);
        var loaded = ModConfigStore.Load(path);

        Save(path, "{ \"a\": 2 }", loaded);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Fact]
    public void Save_DoesNotAddAByteOrderMarkToAFileThatHadNone()
    {
        var path = WriteFile("BepInEx/config/some.cfg", "[General]\nEnabled = true\n");
        var loaded = ModConfigStore.Load(path);

        Save(path, "[General]\nEnabled = false\n", loaded);

        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }

    [Fact]
    public void ValidateFor_NeverRejectsACfgFile() =>
        Assert.Null(ModConfigStore.ValidateFor("some.cfg", "this is not json at all"));

    [Theory]
    [InlineData("{ \"a\": 1 }")]
    [InlineData("{ /* a comment authors use to document the setting */ \"a\": 1 }")]
    [InlineData("{ \"a\": 1, }")]
    [InlineData("// leading comment\n{ \"a\": 1 }")]
    public void ValidateJson_AcceptsTheCommentsAndTrailingCommasServerModsActuallyShip(string text) =>
        Assert.Null(ModConfigStore.ValidateJson(text));

    [Fact]
    public void ValidateJson_NamesWhereTheTextStoppedMakingSense()
    {
        var error = ModConfigStore.ValidateJson("{\n  \"a\": 1\n  \"b\": 2\n}");

        Assert.NotNull(error);
        Assert.StartsWith("Line 3", error);

        // The position System.Text.Json appends to its own message is already being said more
        // readably by the line above it.
        Assert.DoesNotContain("LineNumber:", error);
    }

    [Fact]
    public void ValidateJson_RejectsAnEmptyFile() =>
        Assert.NotNull(ModConfigStore.ValidateJson("   "));

    [Fact]
    public void Backup_ReturnsNullForAFileThatIsNotThereYet() =>
        Assert.Null(ModConfigStore.Backup(_installRoot, Path.Combine(_installRoot, "missing.cfg"), Timestamp));

    [Fact]
    public void Backup_FallsBackToTheFileNameForSomethingOutsideTheInstall()
    {
        var outside = Path.Combine(Path.GetTempPath(), "TCFModManagerOutside_" + Guid.NewGuid() + ".cfg");
        File.WriteAllText(outside, "x");

        try
        {
            var backup = ModConfigStore.Backup(_installRoot, outside, Timestamp);

            Assert.NotNull(backup);
            Assert.Equal(Path.GetFileName(outside), Path.GetFileName(backup!));
            Assert.StartsWith(ModConfigStore.BackupDirectory, backup);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void TwoSavesInOneSecond_KeepTheBackupOfTheOriginal()
    {
        var path = WriteFile("BepInEx/config/twice.cfg", "original");
        var first = Save(path, "first edit", ModConfigStore.Load(path));
        var second = Save(path, "second edit", first.Saved!);

        Assert.Equal("original", File.ReadAllText(first.BackupPath!));
        Assert.Equal("first edit", File.ReadAllText(second.BackupPath!));
        Assert.NotEqual(first.BackupPath, second.BackupPath);
    }

    [Fact]
    public void AJson5File_IsSavedEvenThoughNoJsonReaderAcceptsIt()
    {
        var path = WriteFile("user/mods/SomeMod/config/config.json5", "{ enabled: true }");

        var result = Save(path, "{ enabled: false, 'name': 'x', }", ModConfigStore.Load(path));

        Assert.True(result.Succeeded);
        Assert.Equal("{ enabled: false, 'name': 'x', }", File.ReadAllText(path));
    }

    [Fact]
    public void BackupsOf_ListsTheCopiesKeptOfThatFile_NewestFirst()
    {
        var path = WriteFile("BepInEx/config/listed.cfg", "v0");
        var first = Save(path, "v1", ModConfigStore.Load(path));
        Save(path, "v2", first.Saved!);
        var other = WriteFile("BepInEx/config/other.cfg", "x");
        Save(other, "y", ModConfigStore.Load(other));

        var backups = ModConfigStore.BackupsOf(_installRoot, path);

        Assert.Equal(["v1", "v0"], backups.Select(b => File.ReadAllText(b.Path)));
    }
}
