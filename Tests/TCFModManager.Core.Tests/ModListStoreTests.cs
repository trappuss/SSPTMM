using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ModListStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly ModListStore _store;
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    public ModListStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TCFModManagerModListTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _store = new ModListStore(Path.Combine(_directory, "mod_lists.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static ModList NewList(string name, ModListOrigin origin = ModListOrigin.Local, params ModListEntry[] entries) =>
        Snapshot(name, isSnapshot: false, updatedAt: Timestamp, origin: origin, entries: entries);

    private static ModList Snapshot(
        string name,
        bool isSnapshot = true,
        DateTimeOffset? updatedAt = null,
        ModListOrigin origin = ModListOrigin.Local,
        params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = name,
            Origin = origin,
            IsSnapshot = isSnapshot,
            CreatedAt = Timestamp,
            UpdatedAt = updatedAt ?? Timestamp,
        };

        list.Entries.AddRange(entries);
        return list;
    }

    private static ModListEntry Entry(string name, int? modId = null, int? versionId = null) =>
        new() { Name = name, ModId = modId, VersionId = versionId };

    [Fact]
    public void Load_ReturnsEmptyWhenFileMissing()
    {
        var data = _store.Load();

        Assert.Empty(data.Lists);
        Assert.Null(data.ActiveListId);
    }

    [Fact]
    public void Load_ReturnsEmptyWhenFileIsCorrupt()
    {
        File.WriteAllText(_store.FilePath, "{ not json");

        Assert.Empty(_store.Load().Lists);
    }

    [Fact]
    public void Add_RoundTripsThroughDisk()
    {
        var list = _store.Add(NewList("Fika night", ModListOrigin.Local, Entry("Realism", 1263, 55)));

        var loaded = new ModListStore(_store.FilePath).Find(list.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Fika night", loaded!.Name);
        Assert.Equal(ModListOrigin.Local, loaded.Origin);
        var entry = Assert.Single(loaded.Entries);
        Assert.Equal(1263, entry.ModId);
        Assert.Equal(55, entry.VersionId);
        Assert.True(entry.IsPinned);
    }

    [Fact]
    public void Add_ReplacesAListWithTheSameId()
    {
        var list = _store.Add(NewList("Fika night"));

        var newer = new ModList
        {
            Id = list.Id,
            Name = "Fika night",
            Revision = 2,
            Origin = ModListOrigin.Imported,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        _store.Add(newer);

        Assert.Single(_store.Load().Lists);
        Assert.Equal(2, _store.Find(list.Id)!.Revision);
    }

    //
    // The operator pointing the app at their own server, which is the ordinary case for anyone who
    // hosts and plays on one machine.
    //
    // What comes back off /list is their own published list, same id, marked Server on the way in
    // because that is where it arrived from. Storing it replaced the local original with a read-only
    // copy of itself, and "Make a copy" became the only way back into a list they wrote. The local
    // one is the master; the served copy is dropped and the caller is handed what is stored.
    //
    [Fact]
    public void Add_NeverLetsAServedListReplaceTheLocalOneItCameFrom()
    {
        var mine = _store.Add(NewList("Fika night", ModListOrigin.Local, Entry("Realism", 1263)));

        var served = new ModList
        {
            Id = mine.Id,
            Name = "Fika night",
            Revision = 9,
            Origin = ModListOrigin.Server,
            Source = "127.0.0.1:6969",
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        var stored = _store.Add(served);

        Assert.Equal(ModListOrigin.Local, stored.Origin);
        Assert.True(stored.IsEditable);
        Assert.Single(_store.Load().Lists);

        var held = _store.Find(mine.Id)!;
        Assert.Equal(ModListOrigin.Local, held.Origin);
        Assert.Equal(1, held.Revision);
        Assert.Single(held.Entries);
    }

    // The other direction is untouched: a served list this install does not already own is stored,
    // and a newer revision of it still replaces the older one.
    [Fact]
    public void Add_StillUpsertsAServedListThisMachineDoesNotOwn()
    {
        var served = _store.Add(NewList("The server", ModListOrigin.Server));

        var newer = new ModList
        {
            Id = served.Id,
            Name = "The server",
            Revision = 4,
            Origin = ModListOrigin.Server,
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };

        _store.Add(newer);

        Assert.Single(_store.Load().Lists);
        Assert.Equal(4, _store.Find(served.Id)!.Revision);
    }

    //
    // THE BUG THIS EXISTS FOR: the published mark was a [JsonIgnore]'d flag on ModList, and the
    // share file and this store serialise the SAME objects - so the attribute that correctly kept
    // the mark out of an exported list also kept it out of mod_lists.json. Publish set it, saved
    // nothing, and the page reloaded a moment later with no mark: the "Serving" badge never
    // appeared and the app forgot which list it served the instant it was told.
    //
    // A round trip through disk is the only thing that catches it. The old test asserted the mark
    // did not travel in a share file, which passed, and was the half that already worked.
    //
    [Fact]
    public void SetPublished_SurvivesAReload()
    {
        var list = _store.Add(NewList("Fika night"));

        // A fresh instance off disk, not the one passed in - the store never hands back its caller's.
        Assert.Equal(list.Id, _store.SetPublished(list.Id)!.Id);
        Assert.Equal(list.Id, _store.Load().PublishedListId);

        // A second store over the same file - what the next Refresh, and the next launch, both see.
        var reopened = new ModListStore(_store.FilePath);

        Assert.Equal(list.Id, reopened.Load().PublishedListId);
        Assert.Equal(list.Id, reopened.GetPublished()!.Id);
    }

    // Exactly one, because the server serves exactly one file.
    [Fact]
    public void SetPublished_MovesTheMarkRatherThanAddingASecond()
    {
        var first = _store.Add(NewList("Fika night"));
        var second = _store.Add(NewList("Zero to hero"));

        _store.SetPublished(first.Id);
        _store.SetPublished(second.Id);

        Assert.Equal(second.Id, _store.Load().PublishedListId);
        Assert.Equal(second.Id, _store.GetPublished()!.Id);
    }

    [Fact]
    public void SetPublished_RefusesAListThisStoreDoesNotHold()
    {
        Assert.Null(_store.SetPublished(Guid.NewGuid()));
        Assert.Null(_store.Load().PublishedListId);
    }

    // The pointer goes; the file already in the server's config folder is not this store's to
    // withdraw.
    [Fact]
    public void Delete_ClearsThePublishedPointer()
    {
        var list = _store.Add(NewList("Fika night"));
        _store.SetPublished(list.Id);

        _store.Delete(list.Id);

        Assert.Null(_store.Load().PublishedListId);
        Assert.Null(_store.GetPublished());
    }

    [Fact]
    public void ReplaceEntries_SwapsTheContentsAndLeavesTheRevisionAlone()
    {
        var list = _store.Add(NewList("Fika night", ModListOrigin.Local, Entry("Realism", 1263)));

        var updated = _store.ReplaceEntries(list.Id, [Entry("Realism", 1263), Entry("SAIN", 2426)]);

        Assert.NotNull(updated);
        Assert.Equal(2, _store.Find(list.Id)!.Entries.Count);

        // One rule for all three ways of editing a list: the revision counts applies, not edits.
        Assert.Equal(1, updated!.Revision);
    }

    [Fact]
    public void ReplaceEntries_RefusesAnImportedList()
    {
        var list = _store.Add(NewList("From Dave", ModListOrigin.Imported, Entry("Realism", 1263)));

        Assert.Null(_store.ReplaceEntries(list.Id, [Entry("SAIN", 2426)]));
        Assert.Equal("Realism", Assert.Single(_store.Find(list.Id)!.Entries).Name);
    }

    [Fact]
    public void Rename_RefusesAServedList()
    {
        var list = _store.Add(NewList("Dave's server", ModListOrigin.Server));

        _store.Rename(list.Id, "Mine now");

        Assert.Equal("Dave's server", _store.Find(list.Id)!.Name);
    }

    [Fact]
    public void Fork_CopiesAnImportedListIntoAnEditableOne()
    {
        var source = _store.Add(NewList("From Dave", ModListOrigin.Imported, Entry("Realism", 1263, 55)));

        var fork = _store.Fork(source.Id, "From Dave (mine)", Timestamp);

        Assert.NotEqual(source.Id, fork.Id);
        Assert.Equal(ModListOrigin.Local, fork.Origin);
        Assert.Equal(source.Id, fork.DerivedFrom);
        Assert.Equal(1, fork.Revision);
        Assert.Equal(1263, Assert.Single(fork.Entries).ModId);
        Assert.True(fork.IsEditable);

        Assert.Equal(2, _store.Load().Lists.Count);
        Assert.Equal("From Dave", _store.Find(source.Id)!.Name);
    }

    [Fact]
    public void Fork_DoesNotCarryTheServerAddressAcross()
    {
        var served = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Dave's server",
            Origin = ModListOrigin.Server,
            Source = "86.140.28.231:6969",
            CreatedAt = Timestamp,
            UpdatedAt = Timestamp,
        };
        served.Entries.Add(Entry("Realism", 1263, 55));
        _store.Add(served);

        var fork = _store.Fork(served.Id, "Dave's server (mine)", Timestamp);

        Assert.Null(fork.Source);
        Assert.Null(_store.Find(fork.Id)!.Source);
        Assert.Equal("86.140.28.231:6969", _store.Find(served.Id)!.Source);
    }

    [Fact]
    public void SetPinned_StoresLowercasedKeysOnce()
    {
        _store.SetPinned(["SVM-Client", "svm-server"], pinned: true);
        _store.SetPinned(["svm-client"], pinned: true);

        Assert.Equal(["svm-client", "svm-server"], _store.Load().NeverAutoDisable);
        Assert.True(_store.GetPins().Contains("SVM-SERVER"));
    }

    [Fact]
    public void SetPinned_FalseRemovesTheKeys()
    {
        _store.SetPinned(["svm-client", "svm-server", "sain"], pinned: true);

        _store.SetPinned(["SVM-Client", "svm-server"], pinned: false);

        Assert.Equal(["sain"], _store.Load().NeverAutoDisable);
    }

    [Fact]
    public void SetActive_IgnoresAnUnknownList()
    {
        _store.SetActive(Guid.NewGuid());

        Assert.Null(_store.Load().ActiveListId);
    }

    [Fact]
    public void SetActive_ThenGetActiveReturnsTheList()
    {
        var list = _store.Add(NewList("Fika night"));

        _store.SetActive(list.Id);

        Assert.Equal(list.Id, _store.GetActive()!.Id);
    }

    [Fact]
    public void Delete_ClearsTheActivePointerWhenItWasTheActiveList()
    {
        var list = _store.Add(NewList("Fika night"));
        _store.SetActive(list.Id);

        _store.Delete(list.Id);

        var data = _store.Load();
        Assert.Empty(data.Lists);
        Assert.Null(data.ActiveListId);
    }

    [Fact]
    public void Delete_LeavesADifferentActiveListAlone()
    {
        var active = _store.Add(NewList("Fika night"));
        var other = _store.Add(NewList("Solo"));
        _store.SetActive(active.Id);

        _store.Delete(other.Id);

        Assert.Equal(active.Id, _store.Load().ActiveListId);
    }

    [Fact]
    public void ASnapshotGoesToItsOwnSlotRatherThanTheList()
    {
        var snapshot = _store.Add(Snapshot("basic test"));

        var data = _store.Load();
        Assert.Empty(data.Lists);
        Assert.Equal(snapshot.Id, data.Snapshot!.Id);
        Assert.Equal(snapshot.Id, _store.GetSnapshot()!.Id);
    }

    [Fact]
    public void EachApplyOverwritesTheOneSnapshot()
    {
        _store.Add(Snapshot("basic test"));
        var second = _store.Add(Snapshot("Fika night"));

        Assert.Equal("Fika night", _store.GetSnapshot()!.Name);
        Assert.Equal(second.Id, _store.GetSnapshot()!.Id);
        Assert.Empty(_store.Load().Lists);
    }

    [Fact]
    public void SetSnapshotNullClearsIt()
    {
        _store.Add(Snapshot("basic test"));

        _store.SetSnapshot(null);

        Assert.Null(_store.GetSnapshot());
    }

    //
    // A file written before the single slot existed holds one snapshot per apply, each named after
    // the last. The newest fills the slot and the rest go.
    //
    [Fact]
    public void OldSnapshotListsAreLiftedOutOfTheListOnLoad()
    {
        var data = new ModListData();
        data.Lists.Add(NewList("basic test"));
        data.Lists.Add(Snapshot("Before basic test", updatedAt: Timestamp));
        data.Lists.Add(Snapshot("Before Before basic test", updatedAt: Timestamp.AddMinutes(5)));
        data.Lists.Add(Snapshot("Before Before Before basic test", updatedAt: Timestamp.AddMinutes(2)));
        _store.Save(data);

        var loaded = new ModListStore(_store.FilePath).Load();

        Assert.Equal("basic test", Assert.Single(loaded.Lists).Name);
        Assert.Equal("Before Before basic test", loaded.Snapshot!.Name);
    }

    [Fact]
    public void AnOldSnapshotDoesNotDisplaceOneAlreadyInTheSlot()
    {
        var data = new ModListData { Snapshot = Snapshot("kept") };
        data.Lists.Add(Snapshot("stale"));
        _store.Save(data);

        var loaded = new ModListStore(_store.FilePath).Load();

        Assert.Equal("kept", loaded.Snapshot!.Name);
        Assert.Empty(loaded.Lists);
    }

    [Fact]
    public void AnActivePointerAtALiftedSnapshotIsCleared()
    {
        var snapshot = Snapshot("Before basic test");
        var data = new ModListData { ActiveListId = snapshot.Id };
        data.Lists.Add(snapshot);
        _store.Save(data);

        Assert.Null(new ModListStore(_store.FilePath).Load().ActiveListId);
    }

    [Fact]
    public void Unresolved_ListsOnlyEntriesWithNoModId()
    {
        var list = NewList(
            "Mixed",
            ModListOrigin.Local,
            Entry("Realism", 1263, 55),
            Entry("FixPluginTypesSerialization"));

        Assert.Equal("FixPluginTypesSerialization", Assert.Single(list.Unresolved).Name);
    }

    [Fact]
    public void BumpRevision_CountsAnApplyOfALocalList()
    {
        var list = _store.Add(NewList("Fika night", ModListOrigin.Local, Entry("Realism", 1)));

        _store.ReplaceEntries(list.Id, [Entry("Realism", 1), Entry("SAIN", 2)]);
        var applied = _store.BumpRevision(list.Id);

        Assert.NotNull(applied);
        Assert.Equal(2, _store.Find(list.Id)!.Revision);
    }

    [Fact]
    public void BumpRevision_LeavesSomeoneElsesNumberingAlone()
    {
        // Their next revision has to still look newer than this copy of it.
        var list = _store.Add(NewList("Their list", ModListOrigin.Imported, Entry("Realism", 1)));

        Assert.Null(_store.BumpRevision(list.Id));
        Assert.Equal(1, _store.Find(list.Id)!.Revision);
    }

    // ---------------------------------------------------------------- importing over a list here

    private ModList ReadBack(ModList list)
    {
        var file = Path.Combine(_directory, "export.json");
        ModListFile.Save(list, file);
        return ModListFile.Load(file).List!;
    }

    [Fact]
    public void ImportingYourOwnListFromAFile_IsAClash_NotAQuietReplace()
    {
        var mine = NewList("Mine", ModListOrigin.Local, new ModListEntry { Name = "SAIN" });
        _store.Add(mine);

        var incoming = ReadBack(mine);

        Assert.Equal(ModListOrigin.Imported, incoming.Origin);
        Assert.Equal(ModListImportClash.YourOwnList, _store.ClashFor(incoming));
    }

    [Fact]
    public void AnOlderFileOfAReceivedList_IsAClash_ANewerOneIsNot()
    {
        var received = NewList("Theirs", ModListOrigin.Imported);
        received.Revision = 3;
        _store.Add(received);

        var older = ReadBack(received);
        older.Revision = 2;
        var newer = ReadBack(received);
        newer.Revision = 4;

        Assert.Equal(ModListImportClash.OlderThanStored, _store.ClashFor(older));
        Assert.Equal(ModListImportClash.None, _store.ClashFor(newer));
        Assert.Equal(ModListImportClash.None, _store.ClashFor(NewList("Unknown", ModListOrigin.Imported)));
    }

    [Fact]
    public void ASeparateImport_LeavesYourListAsItWas()
    {
        var mine = NewList("Mine", ModListOrigin.Local, new ModListEntry { Name = "SAIN" });
        _store.Add(mine);

        var incoming = ReadBack(mine);
        var copy = ModListStore.AsSeparateList(incoming, "Mine (imported)");
        _store.Add(copy);

        var lists = new ModListStore(_store.FilePath).Load().Lists;
        Assert.Equal(2, lists.Count);
        Assert.True(lists.Single(l => l.Id == mine.Id).IsEditable);

        var stored = lists.Single(l => l.Id != mine.Id);
        Assert.Equal("Mine (imported)", stored.Name);
        Assert.Equal(mine.Id, stored.DerivedFrom);
        Assert.Equal(ModListOrigin.Imported, stored.Origin);
        Assert.Equal("SAIN", Assert.Single(stored.Entries).Name);
    }
}
