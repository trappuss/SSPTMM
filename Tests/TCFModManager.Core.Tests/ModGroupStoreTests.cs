using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The groups on Subscribed items: what the Groups view, and Cards and List grouped by your groups,
// are built from. Everything goes through the file, as the page's own calls do - each one loads,
// changes and saves.
//
public class ModGroupStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _file;
    private readonly ModGroupStore _store;

    public ModGroupStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TCFModManagerModGroupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _file = Path.Combine(_directory, "mod_groups.json");
        _store = new ModGroupStore(_file);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private List<string> NamesInOrder() =>
        [.. _store.Load().Groups.OrderBy(g => g.SortOrder).Select(g => g.Name)];

    [Fact]
    public void NoFile_IsNoGroups()
    {
        var data = _store.Load();

        Assert.Empty(data.Groups);
        Assert.Empty(data.Assignments);
    }

    [Fact]
    public void AddGroup_GoesLastAndIsSaved()
    {
        var first = _store.AddGroup("Essentials");
        var second = _store.AddGroup("Visuals");

        Assert.NotEqual(first.Id, second.Id);
        Assert.True(second.SortOrder > first.SortOrder);
        Assert.Equal(["Essentials", "Visuals"], NamesInOrder());

        // Another store on the same file sees them: they were written, not just kept.
        Assert.Equal(2, new ModGroupStore(_file).Load().Groups.Count);
    }

    [Fact]
    public void AddGroup_AfterAMove_StillGoesLast()
    {
        var a = _store.AddGroup("A");
        _store.AddGroup("B");
        _store.Move(a.Id, 1);

        _store.AddGroup("C");

        Assert.Equal(["B", "A", "C"], NamesInOrder());
    }

    [Fact]
    public void Rename_ChangesOnlyThatGroup_AndAnUnknownIdChangesNothing()
    {
        var a = _store.AddGroup("A");
        _store.AddGroup("B");

        _store.RenameGroup(a.Id, "Renamed");
        _store.RenameGroup(Guid.NewGuid(), "Nobody");

        Assert.Equal(["Renamed", "B"], NamesInOrder());
    }

    [Fact]
    public void Move_SwapsWithTheNeighbour_AndDoesNothingPastEitherEnd()
    {
        var a = _store.AddGroup("A");
        var b = _store.AddGroup("B");
        var c = _store.AddGroup("C");

        _store.Move(c.Id, -1);
        Assert.Equal(["A", "C", "B"], NamesInOrder());

        _store.Move(a.Id, -1);
        _store.Move(b.Id, 1);
        Assert.Equal(["A", "C", "B"], NamesInOrder());

        _store.Move(Guid.NewGuid(), 1);
        Assert.Equal(["A", "C", "B"], NamesInOrder());
    }

    [Fact]
    public void SwapOrder_SwapsTwoGroupsPastAnyBetween_AndIgnoresAnUnknownOne()
    {
        // Cards and List grouped leave out an emptied group, so a move there swaps with the next
        // one shown, not the next one stored.
        var a = _store.AddGroup("A");
        _store.AddGroup("B");
        var c = _store.AddGroup("C");

        _store.SwapOrder(c.Id, a.Id);
        Assert.Equal(["C", "B", "A"], NamesInOrder());

        _store.SwapOrder(c.Id, Guid.NewGuid());
        _store.SwapOrder(c.Id, c.Id);
        Assert.Equal(["C", "B", "A"], NamesInOrder());
    }

    [Fact]
    public void AssignMod_KeysByNameWhateverItsCaseOrSpaces()
    {
        var group = _store.AddGroup("Bots");

        _store.AssignMod("  SAIN  ", group.Id);

        var data = _store.Load();
        Assert.Equal(group.Id, data.Assignments[ModGroupStore.KeyFor("sain")]);
        Assert.Equal("sain", ModGroupStore.KeyFor(" SAIN "));
    }

    [Fact]
    public void AssignMod_ToAnotherGroupMovesIt_AndNullMakesItUngrouped()
    {
        var bots = _store.AddGroup("Bots");
        var other = _store.AddGroup("Other");

        _store.AssignMod("SAIN", bots.Id);
        _store.AssignMod("sain", other.Id);
        Assert.Equal(other.Id, _store.Load().Assignments["sain"]);
        Assert.Single(_store.Load().Assignments);

        _store.AssignMod("SAIN", null);
        Assert.Empty(_store.Load().Assignments);
    }

    [Fact]
    public void DeleteGroup_SendsItsModsBackToUngrouped_AndLeavesTheRest()
    {
        var bots = _store.AddGroup("Bots");
        var tools = _store.AddGroup("Tools");
        _store.AssignMod("SAIN", bots.Id);
        _store.AssignMod("BigBrain", bots.Id);
        _store.AssignMod("Quick Sell", tools.Id);

        _store.DeleteGroup(bots.Id);

        var data = _store.Load();
        Assert.Equal(["Tools"], data.Groups.Select(g => g.Name));
        Assert.Equal(new Dictionary<string, Guid> { ["quick sell"] = tools.Id }, data.Assignments);
    }

    [Fact]
    public void SetCollapsed_IsKept()
    {
        var group = _store.AddGroup("Bots");

        _store.SetCollapsed(group.Id, true);
        Assert.True(new ModGroupStore(_file).Load().Groups.Single().IsCollapsed);

        _store.SetCollapsed(group.Id, false);
        Assert.False(new ModGroupStore(_file).Load().Groups.Single().IsCollapsed);
    }

    [Fact]
    public void ADamagedFile_ReadsAsNoGroups()
    {
        File.WriteAllText(_file, "{ this is not json");

        var data = _store.Load();

        Assert.Empty(data.Groups);
        Assert.Empty(data.Assignments);
    }
}
