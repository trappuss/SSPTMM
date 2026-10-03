using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (SSPTMM): what changed between two copies of a collection - ModListDiff.
public class ForkModListDiffTests
{
    private static ModList List(params ModListEntry[] entries)
    {
        var list = new ModList { Id = Guid.Empty, Name = "x", CreatedAt = DateTimeOffset.UtcNow };
        list.Entries.AddRange(entries);
        return list;
    }

    private static ModListEntry Mod(int id, int versionId, string name = "", ModListEntryScope? scope = null) =>
        new() { Name = name.Length > 0 ? name : "Mod " + id, ModId = id, VersionId = versionId, Version = "1." + versionId, Scope = scope };

    [Fact]
    public void AddedRemovedAndChanged_AreEachNamed()
    {
        var before = List(Mod(1, 10), Mod(2, 20), Mod(3, 30));
        var after = List(Mod(1, 10), Mod(2, 21, "SAIN"), Mod(4, 40, "New one"));

        var diff = ModListDiff.Between(before, after);

        Assert.Equal(["New one"], diff.Added);
        Assert.Equal(["Mod 3"], diff.Removed);
        Assert.Equal(["SAIN"], diff.Changed);
        Assert.False(diff.IsEmpty);
    }

    [Fact]
    public void AScopeChange_IsAChange()
    {
        var diff = ModListDiff.Between(List(Mod(1, 10)), List(Mod(1, 10, scope: ModListEntryScope.Server)));

        Assert.Single(diff.Changed);
    }

    //
    // The case the diff exists for: a share code brings names from the catalog and no folders or
    // GUIDs, so against the stored copy everything but the mods themselves differs.
    //
    [Fact]
    public void NamesGuidsAndFolders_AreNotChanges()
    {
        var stored = List(new ModListEntry { Name = "SAIN - Solarint's AI", ModId = 7, VersionId = 70, Version = "4.4.3", Guid = "me.sol.sain", Folders = ["SAIN"] });
        var fromCode = List(new ModListEntry { Name = "#7", ModId = 7, VersionId = 70, Version = "4.4.3" });

        Assert.True(ModListDiff.Between(stored, fromCode).IsEmpty);
    }

    [Fact]
    public void News_IsANewCollection_ANewerRevision_OrTheSameOneChanged()
    {
        var held = List(Mod(1, 10));
        held.Revision = 4;

        var newer = List(Mod(1, 10)); newer.Revision = 5;
        var sameButChanged = List(Mod(1, 11)); sameButChanged.Revision = 4;
        var sameUnchanged = List(Mod(1, 10)); sameUnchanged.Revision = 4;
        var olderAndDifferent = List(Mod(2, 20)); olderAndDifferent.Revision = 2;

        Assert.True(ModListDiff.IsNews(null, newer));
        Assert.True(ModListDiff.IsNews(held, newer));
        Assert.True(ModListDiff.IsNews(held, sameButChanged));
        Assert.False(ModListDiff.IsNews(held, sameUnchanged));
        Assert.False(ModListDiff.IsNews(held, olderAndDifferent));
    }
}
