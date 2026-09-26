using System.IO.Compression;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The install pipeline, run for real: InstallAsync and UninstallAsync against a throwaway SPT folder,
// with archives built on the spot and everything the service writes kept inside the test's folder.
// Each test is one of the cases the round-14 audit reproduced.
//
public class InstallPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcf-install-" + Guid.NewGuid().ToString("N"));
    private readonly string _spt;
    private readonly ModInstallManifestService _manifest;
    private readonly ModInstallService _service;

    public InstallPipelineTests()
    {
        _spt = Path.Combine(_root, "SPT");
        Directory.CreateDirectory(Path.Combine(_spt, "BepInEx", "plugins"));
        Directory.CreateDirectory(Path.Combine(_spt, "BepInEx", "config"));

        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
        _service = new ModInstallService(
            new ModDownloadService(),
            _manifest,
            new ConfigCarryOver(new ConfigBaselineStore(Path.Combine(_root, "baselines")), Path.Combine(_root, "legacy"), new ModConfigOptionsStore(Path.Combine(_root, "options.json"))),
            new ConfigUpdateLog(Path.Combine(_root, "config_updates.json")),
            new ModConfigOptionsStore(Path.Combine(_root, "options.json")),
            Path.Combine(_root, "replaced"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Zip(string name, params (string Path, string Content)[] files)
    {
        var path = Path.Combine(_root, name + ".zip");
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(content);
        }

        return path;
    }

    private static ModVersion Version(int id, string version) => new() { Id = id, Version = version, Link = "http://unused" };

    private static InstallTarget Mod(int id, string name) => new(id, false, name, null, null, null);

    private string InSpt(string relative) => Path.Combine(_spt, relative.Replace('/', Path.DirectorySeparatorChar));

    private Task Install(int id, string name, string version, params (string, string)[] files) =>
        _service.InstallAsync(Mod(id, name), Version(id * 10, version), _spt, downloadedArchive: Zip($"{name}-{version}", files));

    private Task Remove(int id) =>
        _service.UninstallAsync(_spt, _manifest.Load().Mods.Single(m => m.ModId == id), ConfigAction.Keep);

    [Fact]
    public async Task RemovingOneMod_LeavesAFileAnotherModAlsoInstalled()
    {
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/A.dll", "a"), ("BepInEx/plugins/Shared.dll", "shared-a"));
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/B.dll", "b"), ("BepInEx/plugins/Shared.dll", "shared-b"));

        await Remove(2);

        // B had put its copy over A's, so A's copy comes back rather than nothing.
        Assert.Equal("shared-a", File.ReadAllText(InSpt("BepInEx/plugins/Shared.dll")));
        Assert.False(File.Exists(InSpt("BepInEx/plugins/B.dll")));
        Assert.True(File.Exists(InSpt("BepInEx/plugins/A.dll")));
    }

    [Fact]
    public async Task RemovingTheFirstOfTwoModsSharingAFile_LeavesTheOthersCopy()
    {
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/Shared.dll", "shared-a"));
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/Shared.dll", "shared-b"));

        await Remove(1);
        Assert.Equal("shared-b", File.ReadAllText(InSpt("BepInEx/plugins/Shared.dll")));

        // A is gone, so B going does not bring A's copy back.
        await Remove(2);
        Assert.False(File.Exists(InSpt("BepInEx/plugins/Shared.dll")));
    }

    [Fact]
    public async Task AFilePlacedByHand_IsPutBackWhenTheModThatReplacedItGoes()
    {
        File.WriteAllText(InSpt("BepInEx/plugins/Handmade.dll"), "HAND");

        await Install(7, "ModG", "1.0", ("BepInEx/plugins/Handmade.dll", "G"));
        Assert.Equal("G", File.ReadAllText(InSpt("BepInEx/plugins/Handmade.dll")));

        await Remove(7);
        Assert.Equal("HAND", File.ReadAllText(InSpt("BepInEx/plugins/Handmade.dll")));
    }

    [Fact]
    public async Task AFilePlacedByHand_SurvivesAnUpdateOfTheModThatReplacedIt()
    {
        File.WriteAllText(InSpt("BepInEx/plugins/Handmade.dll"), "HAND");

        await Install(7, "ModG", "1.0", ("BepInEx/plugins/Handmade.dll", "G1"));
        await Install(7, "ModG", "2.0", ("BepInEx/plugins/Handmade.dll", "G2"));
        Assert.Equal("G2", File.ReadAllText(InSpt("BepInEx/plugins/Handmade.dll")));

        await Remove(7);
        Assert.Equal("HAND", File.ReadAllText(InSpt("BepInEx/plugins/Handmade.dll")));
    }

    [Fact]
    public async Task ABepInExConfigInTheArchive_DoesNotReplaceTheUsersOwn_AndStaysAfterRemoval()
    {
        var config = InSpt("BepInEx/config/com.c.cfg");
        File.WriteAllText(config, "USER-TUNED");

        await Install(3, "ModC", "1.0", ("BepInEx/plugins/C.dll", "c"), ("BepInEx/config/com.c.cfg", "DEFAULT"));
        Assert.Equal("USER-TUNED", File.ReadAllText(config));

        await Remove(3);
        Assert.Equal("USER-TUNED", File.ReadAllText(config));
    }

    [Fact]
    public async Task ABepInExConfigInTheArchive_IsPlacedWhenThereIsNone()
    {
        await Install(3, "ModC", "1.0", ("BepInEx/plugins/C.dll", "c"), ("BepInEx/config/com.c.cfg", "DEFAULT"));
        Assert.Equal("DEFAULT", File.ReadAllText(InSpt("BepInEx/config/com.c.cfg")));
    }

    [Fact]
    public async Task ReadMeFiles_AreNotPlacedInTheSptFolder_AndAWrapperBesideOneIsLookedThrough()
    {
        await Install(4, "ModD", "1.0", ("README.txt", "r"), ("ModD-1.0/BepInEx/plugins/D.dll", "d"));
        Assert.True(File.Exists(InSpt("BepInEx/plugins/D.dll")));

        await Install(6, "ModF", "1.0", ("README.md", "r"), ("LICENSE", "l"), ("BepInEx/plugins/F.dll", "f"));
        Assert.True(File.Exists(InSpt("BepInEx/plugins/F.dll")));
        Assert.False(File.Exists(InSpt("README.md")));
        Assert.False(File.Exists(InSpt("LICENSE")));
    }

    [Fact]
    public async Task APluginsFolderWithoutBepInEx_GoesIntoBepInExPlugins()
    {
        await Install(8, "ModH", "1.0", ("plugins/H/H.dll", "h"));
        Assert.True(File.Exists(InSpt("BepInEx/plugins/H/H.dll")));
    }

    [Fact]
    public async Task ALooseDll_IsStillNotGuessedAt()
    {
        // Under SPT 4 a server mod is a DLL too; putting a lone one in BepInEx\plugins would be a guess.
        var failure = await Assert.ThrowsAsync<ModInstallException>(() => Install(5, "ModE", "1.0", ("E.dll", "e")));
        Assert.Equal(ModInstallFailure.UnrecognisedArchive, failure.Reason);
    }

    // What the app leaves behind if it stops half-way through an update of ModI to 2.0.
    private InstallJournal HalfDoneUpdate(bool absentWrittenDown)
    {
        var work = Path.Combine(_spt, ".tcfmm-work", "interrupted");
        Directory.CreateDirectory(work);
        var journal = InstallJournal.Begin(work, _spt, Mod(9, "ModI"));
        journal.Planned = ["BepInEx/plugins/I.dll", "BepInEx/plugins/I.New.dll", "BepInEx/plugins/Hand.dll"];
        journal.Save();

        // The previous version moved aside.
        Directory.CreateDirectory(Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins"));
        File.Move(InSpt("BepInEx/plugins/I.dll"), Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins", "I.dll"));
        File.Move(InSpt("BepInEx/plugins/I.Extra.dll"), Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins", "I.Extra.dll"));

        if (absentWrittenDown)
        {
            // Hand.dll copied aside, I.New.dll noted as absent, and then the new files placed.
            Directory.CreateDirectory(Path.GetDirectoryName(journal.BeforeCopyOf("BepInEx/plugins/Hand.dll"))!);
            File.Copy(InSpt("BepInEx/plugins/Hand.dll"), journal.BeforeCopyOf("BepInEx/plugins/Hand.dll"));
            journal.Absent = ["BepInEx/plugins/I.dll", "BepInEx/plugins/I.New.dll"];
            journal.Save();
            File.WriteAllText(InSpt("BepInEx/plugins/I.New.dll"), "N2");
            File.WriteAllText(InSpt("BepInEx/plugins/Hand.dll"), "H2");
        }

        return journal;
    }

    [Fact]
    public async Task AnInterruptedUpdate_IsPutBackByRecovery()
    {
        File.WriteAllText(InSpt("BepInEx/plugins/Hand.dll"), "HAND");
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"), ("BepInEx/plugins/I.Extra.dll", "E1"));
        var journal = HalfDoneUpdate(absentWrittenDown: true);

        var recovered = _service.RecoverInterruptedInstalls(_spt);

        Assert.Equal(["ModI"], recovered);
        Assert.Equal("I1", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
        Assert.Equal("E1", File.ReadAllText(InSpt("BepInEx/plugins/I.Extra.dll")));
        Assert.Equal("HAND", File.ReadAllText(InSpt("BepInEx/plugins/Hand.dll")));
        Assert.False(File.Exists(InSpt("BepInEx/plugins/I.New.dll")));
        Assert.False(Directory.Exists(journal.WorkDirectory));
        Assert.Equal("1.0", _manifest.Load().Mods.Single(m => m.ModId == 9).Version);
    }

    [Fact]
    public async Task AnUpdateStoppedBeforeItLookedAtAFile_LeavesThatFileAlone()
    {
        // The app stopped after moving the previous version aside, before it had looked at Hand.dll:
        // recovery must not delete what it never recorded as its own.
        File.WriteAllText(InSpt("BepInEx/plugins/Hand.dll"), "HAND");
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"), ("BepInEx/plugins/I.Extra.dll", "E1"));
        HalfDoneUpdate(absentWrittenDown: false);

        _service.RecoverInterruptedInstalls(_spt);

        Assert.Equal("HAND", File.ReadAllText(InSpt("BepInEx/plugins/Hand.dll")));
        Assert.Equal("I1", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
        Assert.Equal("E1", File.ReadAllText(InSpt("BepInEx/plugins/I.Extra.dll")));
    }

    [Fact]
    public async Task ANewInstall_PutsBackAHalfDoneOneFirst()
    {
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"), ("BepInEx/plugins/I.Extra.dll", "E1"));
        File.WriteAllText(InSpt("BepInEx/plugins/Hand.dll"), "HAND");
        HalfDoneUpdate(absentWrittenDown: true);

        // Installing anything puts the half-done one back before it starts.
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/A.dll", "a"));

        Assert.Equal("I1", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
        Assert.False(File.Exists(InSpt("BepInEx/plugins/I.New.dll")));
    }

    [Fact]
    public async Task AFailedUpdate_KeepsAFileAnotherModAlsoInstalled()
    {
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/Shared.dll", "shared-a"));
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/Shared.dll", "shared-b"));
        Directory.CreateDirectory(InSpt("BepInEx/plugins/Blocked.dll"));

        var failure = await Assert.ThrowsAsync<ModInstallException>(() =>
            Install(2, "ModB", "2.0", ("BepInEx/plugins/Shared.dll", "shared-b2"), ("BepInEx/plugins/Blocked.dll", "x")));

        Assert.Equal(ModInstallFailure.RolledBack, failure.Reason);
        Assert.Equal("shared-b", File.ReadAllText(InSpt("BepInEx/plugins/Shared.dll")));
    }

    [Fact]
    public async Task AModsOwnLeftoverFromAHandInstall_IsNotPutBackWhenItIsRemoved()
    {
        Directory.CreateDirectory(InSpt("BepInEx/plugins/M"));
        File.WriteAllText(InSpt("BepInEx/plugins/M/M.dll"), "M-v1-hand");

        await Install(11, "ModM", "2.0", ("BepInEx/plugins/M/M.dll", "M-v2"));
        await Remove(11);

        Assert.False(File.Exists(InSpt("BepInEx/plugins/M/M.dll")));
    }

    [Fact]
    public async Task RemovingAModAfterTheOwnerOfAFileItReplacedUpdatedIt_LeavesTheOwnersNewCopy()
    {
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/Lib.dll", "lib-b1"));
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/Lib.dll", "lib-a"));
        await Install(2, "ModB", "2.0", ("BepInEx/plugins/Lib.dll", "lib-b2"));

        await Remove(1);

        Assert.Equal("lib-b2", File.ReadAllText(InSpt("BepInEx/plugins/Lib.dll")));
    }

    [Fact]
    public async Task AnUpdate_LeavesNoWorkFolderOrJournalBehind()
    {
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"));
        await Install(9, "ModI", "2.0", ("BepInEx/plugins/I.dll", "I2"));

        Assert.Equal("I2", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
        var work = Path.Combine(_spt, ".tcfmm-work");
        Assert.True(!Directory.Exists(work) || Directory.GetFiles(work, InstallJournal.FileName, SearchOption.AllDirectories).Length == 0);
        Assert.Equal("2.0", _manifest.Load().Mods.Single(m => m.ModId == 9).Version);
    }

    [Fact]
    public async Task AnUpdateThatFailsPartWay_PutsThePreviousVersionBack()
    {
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"), ("BepInEx/plugins/Old.dll", "O1"));

        // Something in the way of one of the new files: placing it fails after others are placed.
        Directory.CreateDirectory(InSpt("BepInEx/plugins/Blocked.dll"));

        var failure = await Assert.ThrowsAsync<ModInstallException>(() =>
            Install(9, "ModI", "2.0", ("BepInEx/plugins/I.dll", "I2"), ("BepInEx/plugins/Blocked.dll", "B2"), ("BepInEx/plugins/New.dll", "N2")));

        Assert.Equal(ModInstallFailure.RolledBack, failure.Reason);
        Assert.Equal("I1", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
        Assert.Equal("O1", File.ReadAllText(InSpt("BepInEx/plugins/Old.dll")));
        Assert.False(File.Exists(InSpt("BepInEx/plugins/New.dll")));
        Assert.Equal("1.0", _manifest.Load().Mods.Single(m => m.ModId == 9).Version);
    }

    // ---------------------------------------------------------------- review round 2

    [Fact]
    public async Task APatchOverAnInstalledModsFile_IsUndoneWhenThePatchGoes()
    {
        await Install(20, "Parent", "1.0", ("BepInEx/plugins/Parent/Parent.dll", "p"), ("BepInEx/plugins/Parent/data.json", "orig"));
        await Install(21, "Patch", "1.0", ("BepInEx/plugins/Parent/data.json", "patched"));

        await Remove(21);

        Assert.Equal("orig", File.ReadAllText(InSpt("BepInEx/plugins/Parent/data.json")));
        Assert.Equal("p", File.ReadAllText(InSpt("BepInEx/plugins/Parent/Parent.dll")));
    }

    [Fact]
    public async Task APatchOverAHandInstalledModsFile_IsUndoneWhenThePatchGoes()
    {
        Directory.CreateDirectory(InSpt("BepInEx/plugins/Parent"));
        File.WriteAllText(InSpt("BepInEx/plugins/Parent/Parent.dll"), "p");
        File.WriteAllText(InSpt("BepInEx/plugins/Parent/data.json"), "orig");

        await Install(21, "Patch", "1.0", ("BepInEx/plugins/Parent/data.json", "patched"));
        await Remove(21);

        // The patch does not place Parent.dll, so the folder is not its own: what it replaced comes back.
        Assert.Equal("orig", File.ReadAllText(InSpt("BepInEx/plugins/Parent/data.json")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ModsStackedOnOneFile_EndWithTheOriginalInEitherRemovalOrder(bool lowerFirst)
    {
        File.WriteAllText(InSpt("BepInEx/plugins/H.dll"), "orig");
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/H.dll", "a"));
        await Task.Delay(20);
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/H.dll", "b"));

        if (lowerFirst)
        {
            await Remove(1);
            Assert.Equal("b", File.ReadAllText(InSpt("BepInEx/plugins/H.dll")));
            await Remove(2);
        }
        else
        {
            await Remove(2);
            Assert.Equal("a", File.ReadAllText(InSpt("BepInEx/plugins/H.dll")));
            await Remove(1);
        }

        Assert.Equal("orig", File.ReadAllText(InSpt("BepInEx/plugins/H.dll")));
    }

    [Fact]
    public async Task AModUpdatedOverTheModAboveIt_IsGoneWhenBothAre()
    {
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/Lib.dll", "lib-b1"));
        await Task.Delay(20);
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/Lib.dll", "lib-a"));
        await Task.Delay(20);
        await Install(2, "ModB", "2.0", ("BepInEx/plugins/Lib.dll", "lib-b2"));

        // Nothing was there before B: B's old version must not come back.
        await Remove(2);
        Assert.Equal("lib-a", File.ReadAllText(InSpt("BepInEx/plugins/Lib.dll")));
        await Remove(1);
        Assert.False(File.Exists(InSpt("BepInEx/plugins/Lib.dll")));
    }

    [Fact]
    public async Task AFileTheUpdateTurnedIntoAFolder_ComesBackWhenItFails()
    {
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"), ("BepInEx/plugins/Lib", "lib1"));

        // Writing the record fails, after every file is placed.
        Directory.CreateDirectory(Path.Combine(_root, "installed-mods.json.tmp"));

        var failure = await Assert.ThrowsAsync<ModInstallException>(() =>
            Install(9, "ModI", "2.0", ("BepInEx/plugins/I.dll", "I2"), ("BepInEx/plugins/Lib/inner.dll", "inner")));

        Assert.Equal(ModInstallFailure.RolledBack, failure.Reason);
        Assert.Equal("lib1", File.ReadAllText(InSpt("BepInEx/plugins/Lib")));
        Assert.Equal("I1", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
    }

    [Fact]
    public async Task AnUndoThatCannotFinish_KeepsTheWayBack_AndHoldsOtherChangesUntilItIsDone()
    {
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"));

        // An update stopped half-way whose previous file cannot go back: something now sits where it was.
        var work = Path.Combine(_spt, ".tcfmm-work", "stuck");
        var journal = InstallJournal.Begin(work, _spt, Mod(9, "ModI"));
        journal.Planned = ["BepInEx/plugins/I.dll"];
        Directory.CreateDirectory(Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins"));
        File.Move(InSpt("BepInEx/plugins/I.dll"), Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins", "I.dll"));
        Directory.CreateDirectory(InSpt("BepInEx/plugins/I.dll"));
        File.WriteAllText(InSpt("BepInEx/plugins/I.dll/in-the-way.txt"), "x");
        journal.Save();

        Assert.Empty(_service.RecoverInterruptedInstalls(_spt));
        Assert.True(File.Exists(Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins", "I.dll")));

        var refused = await Assert.ThrowsAsync<ModInstallException>(() => Install(1, "ModA", "1.0", ("BepInEx/plugins/A.dll", "a")));
        Assert.Equal(ModInstallFailure.EarlierInstallPending, refused.Reason);
        Assert.False(File.Exists(InSpt("BepInEx/plugins/A.dll")));

        // Once it is out of the way, the next change finishes the undo first.
        Directory.Delete(InSpt("BepInEx/plugins/I.dll"), recursive: true);
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/A.dll", "a"));

        Assert.Equal("I1", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
        Assert.False(Directory.Exists(work));
    }

    [Fact]
    public async Task APictureBesideAWrapperFolder_DoesNotStopTheInstall()
    {
        await Install(30, "ModP", "1.0", ("preview.png", "img"), ("ModP-1.0/BepInEx/plugins/P.dll", "p"));
        await Install(31, "ModQ", "1.0", ("icon.png", "img"), ("plugins/Q.dll", "q"));

        Assert.Equal("p", File.ReadAllText(InSpt("BepInEx/plugins/P.dll")));
        Assert.Equal("q", File.ReadAllText(InSpt("BepInEx/plugins/Q.dll")));
        Assert.False(File.Exists(InSpt("preview.png")));
        Assert.False(File.Exists(InSpt("icon.png")));
    }

    // ---------------------------------------------------------------- review round 3

    [Fact]
    public async Task UpdatingTheLowerOfTwoStackedMods_StillEndsWithTheOriginal()
    {
        File.WriteAllText(InSpt("BepInEx/plugins/H.dll"), "HAND");
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/H.dll", "a1"));
        await Task.Delay(20);
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/H.dll", "b"));
        await Task.Delay(20);
        await Install(1, "ModA", "2.0", ("BepInEx/plugins/H.dll", "a2"));

        await Remove(1);
        Assert.Equal("b", File.ReadAllText(InSpt("BepInEx/plugins/H.dll")));
        await Remove(2);
        Assert.Equal("HAND", File.ReadAllText(InSpt("BepInEx/plugins/H.dll")));
    }

    [Fact]
    public async Task UpdatingTheLowerModToAVersionWithoutTheFile_LeavesTheUpperModsFile()
    {
        File.WriteAllText(InSpt("BepInEx/plugins/H.dll"), "HAND");
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/H.dll", "a1"));
        await Task.Delay(20);
        await Install(2, "ModB", "1.0", ("BepInEx/plugins/H.dll", "b"));
        await Task.Delay(20);
        await Install(1, "ModA", "2.0", ("BepInEx/plugins/A.dll", "a2"));

        Assert.Equal("b", File.ReadAllText(InSpt("BepInEx/plugins/H.dll")));
        await Remove(2);
        Assert.Equal("HAND", File.ReadAllText(InSpt("BepInEx/plugins/H.dll")));
    }

    [Fact]
    public async Task AFinishedInstall_IsNeverUndone_EvenOnceItsRecordIsReplaced()
    {
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"));
        await Install(9, "ModI", "2.0", ("BepInEx/plugins/I.dll", "I2"));

        // A journal left by a finished install whose tidying did not happen (committed), for a record
        // that a later change has since replaced.
        var work = Path.Combine(_spt, ".tcfmm-work", "finished");
        var journal = InstallJournal.Begin(work, _spt, Mod(9, "ModI"));
        journal.Planned = ["BepInEx/plugins/I.dll"];
        journal.Absent = ["BepInEx/plugins/I.dll"];
        journal.Committed = true;
        Directory.CreateDirectory(Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins"));
        File.WriteAllText(Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins", "I.dll"), "stale");
        journal.Save();

        _service.RecoverInterruptedInstalls(_spt);

        Assert.Equal("I2", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
        Assert.False(Directory.Exists(work));
    }

    [Fact]
    public async Task DisablingOrApplyingAList_IsHeldWhileAnUndoIsPending()
    {
        await Install(9, "ModI", "1.0", ("BepInEx/plugins/I.dll", "I1"));
        var work = Path.Combine(_spt, ".tcfmm-work", "stuck");
        var journal = InstallJournal.Begin(work, _spt, Mod(9, "ModI"));
        journal.Planned = ["BepInEx/plugins/I.dll"];
        Directory.CreateDirectory(Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins"));
        File.Move(InSpt("BepInEx/plugins/I.dll"), Path.Combine(journal.PreviousDirectory, "BepInEx", "plugins", "I.dll"));
        Directory.CreateDirectory(InSpt("BepInEx/plugins/I.dll"));
        File.WriteAllText(InSpt("BepInEx/plugins/I.dll/in-the-way.txt"), "x");
        journal.Save();

        var refused = Assert.Throws<ModInstallException>(() => _service.EnsureNothingPending(_spt));
        Assert.Equal(ModInstallFailure.EarlierInstallPending, refused.Reason);

        Directory.Delete(InSpt("BepInEx/plugins/I.dll"), recursive: true);
        _service.EnsureNothingPending(_spt);
        Assert.Equal("I1", File.ReadAllText(InSpt("BepInEx/plugins/I.dll")));
    }

    [Fact]
    public async Task ARemovalAndAnInstall_DoNotWriteOverEachOthersRecords()
    {
        await Install(1, "ModA", "1.0", ("BepInEx/plugins/A.dll", "a"));
        var stale = _manifest.Load().Mods.Single(m => m.ModId == 1);

        // Both at once, many times over: each must end with its own result in the records.
        var tasks = new List<Task>();
        for (var n = 0; n < 5; n++)
        {
            var id = 100 + n;
            tasks.Add(Task.Run(() => Install(id, $"Mod{id}", "1.0", ($"BepInEx/plugins/M{id}.dll", "m"))));
        }

        tasks.Add(Task.Run(() => _service.UninstallAsync(_spt, stale, ConfigAction.Keep)));
        await Task.WhenAll(tasks);

        var mods = _manifest.Load().Mods;
        Assert.DoesNotContain(mods, m => m.ModId == 1);
        Assert.Equal(5, mods.Count(m => m.ModId >= 100));
    }
}
