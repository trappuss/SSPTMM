using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Stage 4 of CLOSED-10-TCFResilience-DESIGN.md: the removal engine. Mods are installed with a real
// InstallAsync into a throwaway SPT layout, removed, updated and put back, and every test asserts on
// the disk - the install, the holding folder and Data - not on what the code reports.
//
public class ResilienceRemovalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tcfmm-removal-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly ModInstallManifestService _manifest;
    private RemovedModsRetention _retention = RemovedModsRetention.UntilCleared;

    private static int _nextId = 960_000 + Random.Shared.Next(0, 10_000) * 10;
    private readonly List<int> _ids = [];

    public ResilienceRemovalTests()
    {
        _install = Path.Combine(_root, "install");
        Write("EscapeFromTarkov.exe", "game");
        Write("SPT/SPT.Server.exe", "server");
        _manifest = new ModInstallManifestService(Path.Combine(_root, "installed-mods.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }

        foreach (var id in _ids)
        {
            foreach (var key in new[] { id.ToString(), $"{id}-addon" })
            {
                var kept = Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, key);
                try { if (Directory.Exists(kept)) Directory.Delete(kept, recursive: true); }
                catch (IOException) { }
            }
        }
    }

    private string Full(string relative) => Path.Combine(_install, relative.Replace('/', Path.DirectorySeparatorChar));

    private string Write(string relative, string content)
    {
        var full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private InstallTarget NewTarget(string? guid = null)
    {
        var id = Interlocked.Add(ref _nextId, 1);
        _ids.Add(id);
        return new InstallTarget(id, false, $"Test {id}", guid, null, null);
    }

    private sealed class ArchiveHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    private ModInstallService Service(byte[]? archive = null) => new(
        new ModDownloadService(new HttpClient(new ArchiveHandler(archive ?? []))),
        _manifest,
        removedMods: new RemovedMods(() => _retention));

    private Task<ModInstallResult> Install(InstallTarget target, string version, params (string Path, string Content)[] files) =>
        Service(ArchiveFileTests.Zip(files))
            .InstallAsync(target, new ModVersion { Id = 1, Version = version, Link = "https://example.test/a" }, _install);

    private InstalledModRecord RecordOf(InstallTarget target) =>
        _manifest.Load().Find(target.Id, target.IsAddon)!;

    private static string Held(string folder, string relative) =>
        Path.Combine(folder, RemovedMods.FilesFolder, relative.Replace('/', Path.DirectorySeparatorChar));

    // --- Remove: nothing is deleted (D23) --------------------------------------------------------

    [Fact]
    public async Task Remove_MovesTheModsFilesIntoHolding_AndWritesTheTrail()
    {
        var target = NewTarget();
        await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("user/mods/ModServer/server.dll", "server half"));

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.False(File.Exists(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));
        Assert.True(Directory.Exists(Full("BepInEx/plugins")));
        Assert.True(Directory.Exists(Full("SPT/user/mods")));

        Assert.NotNull(result.HoldingFolder);
        Assert.Equal("the mod", File.ReadAllText(Held(result.HoldingFolder!, "BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("server half", File.ReadAllText(Held(result.HoldingFolder!, "SPT/user/mods/ModServer/server.dll")));
        Assert.Equal(2, result.FilesDeleted);

        var log = RemovedMods.ReadLog(result.HoldingFolder!)!;
        Assert.Equal(RemovalKind.AppInstalled, log.Kind);
        Assert.Equal(2, log.Entries.Count(e => e.Outcome == RemovalOutcome.Moved));
        Assert.NotNull(log.Record);
        Assert.Null(_manifest.Load().Find(target.Id, false));
    }

    [Fact]
    public async Task Remove_LeavesAFileThatChangedSinceInstall()
    {
        var target = NewTarget();
        await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("BepInEx/plugins/Mod/settings.txt", "as shipped"));
        File.WriteAllText(Full("BepInEx/plugins/Mod/settings.txt"), "edited by the user");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal("edited by the user", File.ReadAllText(Full("BepInEx/plugins/Mod/settings.txt")));
        Assert.False(File.Exists(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal(["BepInEx/plugins/Mod/settings.txt"], result.KeptChanged);
    }

    [Fact]
    public async Task Remove_LeavesAFileAnotherModsRecordLists()
    {
        var first = NewTarget();
        var second = NewTarget();
        await Install(first, "1.0.0", ("BepInEx/plugins/Shared/lib.dll", "same bytes"), ("BepInEx/plugins/First/f.dll", "first"));
        await Install(second, "1.0.0", ("BepInEx/plugins/Shared/lib.dll", "same bytes"), ("BepInEx/plugins/Second/s.dll", "second"));

        var result = await Service().UninstallAsync(_install, RecordOf(first), ConfigAction.Delete);

        Assert.True(File.Exists(Full("BepInEx/plugins/Shared/lib.dll")));
        Assert.Equal(["BepInEx/plugins/Shared/lib.dll"], result.KeptOwned);
        Assert.False(File.Exists(Full("BepInEx/plugins/First/f.dll")));
    }

    [Fact]
    public async Task Remove_RefusesARecordFromAnotherInstall_AndTouchesNothing()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"));
        var record = RecordOf(target);
        var foreign = new InstalledModRecord
        {
            ModId = record.ModId, Name = record.Name, Version = record.Version, InstalledAt = record.InstalledAt,
            Files = record.Files, Fingerprints = record.Fingerprints,
            InstallPath = Path.Combine(_root, "another-install"),
        };

        var ex = await Assert.ThrowsAsync<ModInstallException>(() => Service().UninstallAsync(_install, foreign, ConfigAction.Delete));

        Assert.Equal(ModInstallFailure.RecordFromAnotherInstall, ex.Reason);
        Assert.True(File.Exists(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.False(Directory.Exists(RemovedMods.Root(_install)));
    }

    // --- Originals (D22, R16) --------------------------------------------------------------------

    [Fact]
    public async Task Remove_PutsBackWhatTheModReplaced()
    {
        Write("BepInEx/config/other.cfg", "someone else's settings");
        var target = NewTarget();
        await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("BepInEx/config/other.cfg", "the mod's version"));

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal("someone else's settings", File.ReadAllText(Full("BepInEx/config/other.cfg")));
        Assert.Equal(1, result.OriginalsRestored);
        Assert.False(Directory.Exists(Path.Combine(AppPaths.DataDirectory, ModInstallService.OverwrittenDirectoryName, target.Id.ToString())));
        Assert.Single(Directory.GetFiles(Path.Combine(result.HoldingFolder!, RemovedMods.OriginalsFolder), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Remove_DoesntPutBackAnEarlierCopyOfTheSameMod()
    {
        Directory.CreateDirectory(Full("BepInEx/plugins/Mod"));
        new ModMetadataFixture(ModMetadataFixture.Kind.Plain)
            .Plugin("com.test.removal.samemod", "Mod", "0.9.0")
            .Write(Full("BepInEx/plugins/Mod"), "Mod.dll");
        var target = NewTarget("com.test.removal.samemod");
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/Mod.dll", "the new version"));

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.False(File.Exists(Full("BepInEx/plugins/Mod/Mod.dll")));
        Assert.Equal(0, result.OriginalsRestored);
        var log = RemovedMods.ReadLog(result.HoldingFolder!)!;
        Assert.Contains(log.Entries, e => e.Outcome == RemovalOutcome.OriginalHeldSameMod);
        Assert.Single(Directory.GetFiles(Path.Combine(result.HoldingFolder!, RemovedMods.OriginalsFolder), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Remove_DoesntPutAnOriginalOverSomethingThatsThere()
    {
        Write("BepInEx/config/other.cfg", "someone else's settings");
        var target = NewTarget();
        await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("BepInEx/config/other.cfg", "the mod's version"));
        File.WriteAllText(Full("BepInEx/config/other.cfg"), "changed since");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal("changed since", File.ReadAllText(Full("BepInEx/config/other.cfg")));
        var log = RemovedMods.ReadLog(result.HoldingFolder!)!;
        Assert.Contains(log.Entries, e => e.Outcome == RemovalOutcome.OriginalNotRestoredOccupied);
    }

    // --- Update (D23 on the removal half) --------------------------------------------------------

    [Fact]
    public async Task Update_HoldsThePreviousVersion_IncludingAChangedFileItReplaces()
    {
        var target = NewTarget();
        await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "v1"),
            ("BepInEx/plugins/Mod/notes.txt", "v1 notes"),
            ("BepInEx/plugins/Mod/only-in-v1.txt", "gone in v2"));
        File.WriteAllText(Full("BepInEx/plugins/Mod/notes.txt"), "edited");
        File.WriteAllText(Full("BepInEx/plugins/Mod/only-in-v1.txt"), "edited and not replaced");

        await Install(target, "2.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "v2"),
            ("BepInEx/plugins/Mod/notes.txt", "v2 notes"));

        Assert.Equal("v2 notes", File.ReadAllText(Full("BepInEx/plugins/Mod/notes.txt")));
        Assert.Equal("edited and not replaced", File.ReadAllText(Full("BepInEx/plugins/Mod/only-in-v1.txt")));

        var (folder, log) = Assert.Single(RemovedMods.List(_install));
        Assert.Equal(RemovalKind.ReplacedByUpdate, log.Kind);
        Assert.Equal("edited", File.ReadAllText(Held(folder, "BepInEx/plugins/Mod/notes.txt")));
        Assert.Equal("v1", File.ReadAllText(Held(folder, "BepInEx/plugins/Mod/mod.dll")));
        Assert.Null(RemovedMods.LatestUndoable(_install));
    }

    // --- Undo (D28) ------------------------------------------------------------------------------

    [Fact]
    public async Task Undo_PutsEverythingBackByteForByte()
    {
        Write("BepInEx/config/other.cfg", "someone else's settings");
        var target = NewTarget();
        await Install(target, "1.0.0",
            ("BepInEx/plugins/Mod/mod.dll", "the mod"),
            ("BepInEx/config/other.cfg", "the mod's version"));
        var before = RecordOf(target);
        var keptCopy = Path.Combine(AppPaths.DataDirectory, Assert.Single(before.Overwrote).BackupPath);

        var removal = await Service().UninstallAsync(_install, before, ConfigAction.Delete);
        var undo = Service().UndoRemoval(_install, removal.HoldingFolder!);

        Assert.True(undo.Ran);
        Assert.Empty(undo.Blocked);
        Assert.Equal("the mod", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("the mod's version", File.ReadAllText(Full("BepInEx/config/other.cfg")));
        Assert.Equal("someone else's settings", File.ReadAllText(keptCopy));
        Assert.NotNull(_manifest.Load().Find(target.Id, false));
        Assert.False(Directory.Exists(removal.HoldingFolder));
    }

    [Fact]
    public async Task Undo_NeverOverwrites_AndKeepsTheHoldingFolderWhenBlocked()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"));
        var removal = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);
        Write("BepInEx/plugins/Mod/mod.dll", "something new");

        var undo = Service().UndoRemoval(_install, removal.HoldingFolder!);

        Assert.Equal(["BepInEx/plugins/Mod/mod.dll"], undo.Blocked);
        Assert.Equal("something new", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("the mod", File.ReadAllText(Held(removal.HoldingFolder!, "BepInEx/plugins/Mod/mod.dll")));
    }

    [Fact]
    public void Undo_OfAHandInstalledRemoval_PutsTheFolderBack()
    {
        Write("BepInEx/plugins/Hand/h.dll", "by hand");
        var held = Service().RemoveHandInstalled([Full("BepInEx/plugins/Hand")], _install, "Hand");

        Assert.False(Directory.Exists(Full("BepInEx/plugins/Hand")));

        var undo = Service().UndoRemoval(_install, held!);

        Assert.True(undo.Ran);
        Assert.Equal("by hand", File.ReadAllText(Full("BepInEx/plugins/Hand/h.dll")));
    }

    [Fact]
    public async Task Undo_RefusesARemovalFromAnotherInstallOrOneAlreadyUndone()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"));
        var removal = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.False(Service().UndoRemoval(Path.Combine(_root, "elsewhere"), removal.HoldingFolder!).Ran);

        Assert.True(Service().UndoRemoval(_install, removal.HoldingFolder!).Ran);
        Assert.False(Service().UndoRemoval(_install, removal.HoldingFolder!).Ran);
    }

    // --- Chris's 2026-10-01 Windows test: a file the user added, several removals -----------------

    [Fact]
    public async Task Remove_SaysWhenTheModsFolderStays_ForAFileItDidntInstall()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"), ("BepInEx/plugins/Mod/README.md", "readme"));
        Write("BepInEx/plugins/Mod/TEST.md", "the user's own note");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal("the user's own note", File.ReadAllText(Full("BepInEx/plugins/Mod/TEST.md")));
        Assert.Equal([new FolderLeft("BepInEx/plugins/Mod", 1)], result.FoldersLeft);
        Assert.Equal(["BepInEx/plugins/Mod"], RemovedMods.ReadLog(result.HoldingFolder!)!.FoldersLeft);
    }

    [Fact]
    public async Task Remove_DoesntReportAFolderLeftOnlyForAChangedFile()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"), ("BepInEx/plugins/Mod/notes.txt", "as shipped"));
        Write("BepInEx/plugins/Mod/notes.txt", "edited");

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal(["BepInEx/plugins/Mod/notes.txt"], result.KeptChanged);
        Assert.Empty(result.FoldersLeft);
    }

    [Fact]
    public async Task Undoable_ListsEveryRemoval_AndAnyCanBeUndoneFirst()
    {
        var first = NewTarget();
        var second = NewTarget();
        await Install(first, "1.0.0", ("BepInEx/plugins/First/f.dll", "first"));
        await Install(second, "1.0.0", ("BepInEx/plugins/Second/s.dll", "second"));
        var a = await Service().UninstallAsync(_install, RecordOf(first), ConfigAction.Delete);
        var b = await Service().UninstallAsync(_install, RecordOf(second), ConfigAction.Delete);

        Assert.Equal([b.HoldingFolder, a.HoldingFolder], RemovedMods.Undoable(_install).Select(u => u.Folder));

        Assert.Empty(Service().UndoRemoval(_install, a.HoldingFolder!).Blocked);

        Assert.Equal("first", File.ReadAllText(Full("BepInEx/plugins/First/f.dll")));
        Assert.False(File.Exists(Full("BepInEx/plugins/Second/s.dll")));
        Assert.Equal([b.HoldingFolder], RemovedMods.Undoable(_install).Select(u => u.Folder));
        Assert.Equal(b.HoldingFolder, RemovedMods.LatestUndoable(_install)?.Folder);
    }

    [Fact]
    public async Task Undo_InEitherOrder_PutsTheModAndTheLeftoverFolderBackTogether()
    {
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"));
        Write("BepInEx/plugins/Mod/TEST.md", "the user's own note");
        var modRemoval = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);
        var leftoverRemoval = Service().RemoveHandInstalled([Full("BepInEx/plugins/Mod")], _install, "Mod");
        Assert.False(Directory.Exists(Full("BepInEx/plugins/Mod")));

        // The older one first: the mod's file recreates the folder, then the leftover merges into it.
        Assert.Empty(Service().UndoRemoval(_install, modRemoval.HoldingFolder!).Blocked);
        Assert.Empty(Service().UndoRemoval(_install, leftoverRemoval!).Blocked);

        Assert.Equal("the mod", File.ReadAllText(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.Equal("the user's own note", File.ReadAllText(Full("BepInEx/plugins/Mod/TEST.md")));
        Assert.False(Directory.Exists(modRemoval.HoldingFolder));
        Assert.False(Directory.Exists(leftoverRemoval));
    }

    [Fact]
    public void Undo_MergingAFolder_NeverOverwritesAFileThatIsThere()
    {
        Write("BepInEx/plugins/Hand/a.txt", "held a");
        Write("BepInEx/plugins/Hand/b.txt", "held b");
        var held = Service().RemoveHandInstalled([Full("BepInEx/plugins/Hand")], _install, "Hand");
        Write("BepInEx/plugins/Hand/a.txt", "new a");

        var undo = Service().UndoRemoval(_install, held!);

        Assert.Equal(["BepInEx/plugins/Hand/a.txt"], undo.Blocked);
        Assert.Equal("new a", File.ReadAllText(Full("BepInEx/plugins/Hand/a.txt")));
        Assert.Equal("held b", File.ReadAllText(Full("BepInEx/plugins/Hand/b.txt")));
        Assert.True(Directory.Exists(held));
    }

    [Fact]
    public async Task Undo_TidiesTheEmptiedKeptConfigsFolder()
    {
        var target = NewTarget();
        await Install(target, "1.0.0",
            ("user/mods/CfgMod/mod.dll", "server"),
            ("user/mods/CfgMod/config/config.json", "{ \"a\": 1 }"));
        var removal = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Keep);
        Assert.NotNull(removal.ConfigsFolder);
        Assert.True(Directory.Exists(removal.ConfigsFolder));

        Assert.Empty(Service().UndoRemoval(_install, removal.HoldingFolder!).Blocked);

        Assert.Equal("{ \"a\": 1 }", File.ReadAllText(Full("SPT/user/mods/CfgMod/config/config.json")));
        Assert.False(Directory.Exists(removal.ConfigsFolder));
        Assert.True(Directory.Exists(AppPaths.LegacyConfigsDirectory));
    }

    // --- Retention (D27) -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteStraightAway_LeavesNothingHeld_AndDeletesOnlyInsideTheHoldingFolder()
    {
        _retention = RemovedModsRetention.DeleteStraightAway;
        Write("BepInEx/plugins/Other/o.dll", "another mod");
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"));

        var result = await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Null(result.HoldingFolder);
        Assert.Empty(RemovedMods.List(_install));
        Assert.False(File.Exists(Full("BepInEx/plugins/Mod/mod.dll")));
        Assert.True(File.Exists(Full("BepInEx/plugins/Other/o.dll")));
        Assert.True(File.Exists(Full("EscapeFromTarkov.exe")));
    }

    [Fact]
    public async Task Prune_ClearsOnlyRemovalsOlderThanTheSetting()
    {
        _retention = RemovedModsRetention.FourteenDays;
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"));
        await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        var removals = new RemovedMods(() => _retention);
        Assert.Equal(0, removals.Prune(_install, DateTimeOffset.Now.AddDays(13)));
        Assert.Single(RemovedMods.List(_install));

        Assert.Equal(1, removals.Prune(_install, DateTimeOffset.Now.AddDays(15)));
        Assert.Empty(RemovedMods.List(_install));
    }

    [Fact]
    public async Task UntilCleared_KeepsEverything_UntilClear()
    {
        _retention = RemovedModsRetention.UntilCleared;
        var target = NewTarget();
        await Install(target, "1.0.0", ("BepInEx/plugins/Mod/mod.dll", "the mod"));
        await Service().UninstallAsync(_install, RecordOf(target), ConfigAction.Delete);

        Assert.Equal(0, new RemovedMods(() => _retention).Prune(_install, DateTimeOffset.Now.AddYears(5)));
        Assert.True(RemovedMods.Size(_install) > 0);

        Assert.Equal(1, RemovedMods.Clear(_install));
        Assert.Empty(RemovedMods.List(_install));
        Assert.True(File.Exists(Full("EscapeFromTarkov.exe")));
    }

    [Fact]
    public void DeleteHeld_RefusesAnythingOutsideTheHoldingFolder()
    {
        Directory.CreateDirectory(RemovedMods.Root(_install));

        Assert.False(RemovedMods.DeleteHeld(_install, Full("BepInEx")));
        Assert.False(RemovedMods.DeleteHeld(_install, _install));
        Assert.False(RemovedMods.DeleteHeld(_install, RemovedMods.Root(_install)));
        Assert.True(File.Exists(Full("EscapeFromTarkov.exe")));
    }

    [Fact]
    public void DeleteHeld_LeavesAHeldFolderThatHoldsALink()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "precious.txt"), "keep me");
        var held = Path.Combine(RemovedMods.Root(_install), "20260101-000000-000_x");
        Directory.CreateDirectory(held);
        Directory.CreateSymbolicLink(Path.Combine(held, "link"), elsewhere);

        Assert.False(RemovedMods.DeleteHeld(_install, held));
        Assert.True(File.Exists(Path.Combine(elsewhere, "precious.txt")));
    }
}
