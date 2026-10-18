using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.4): a friend's copy of your collection, come back - ModListCopyBack, and the share code
// carrying what it was copied from.
public class ForkModListCopyBackTests
{
    private static ModList List(ModListOrigin origin, params ModListEntry[] entries)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Squad pack",
            Origin = origin,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        list.Entries.AddRange(entries);
        return list;
    }

    private static ModList CopyOf(ModList parent, params ModListEntry[] entries)
    {
        var copy = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Squad pack (copy)",
            Origin = ModListOrigin.Imported,
            DerivedFrom = parent.Id,
            Source = "Alex",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        copy.Entries.AddRange(entries);
        return copy;
    }

    private static ModListEntry Mod(int id, string name, string? version = null) =>
        new() { Name = name, ModId = id, Version = version };

    [Fact]
    public void ACopyOfYourCollection_OffersOnlyTheModsYoursDoesntHave()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"), Mod(2, "Looting Bots"), Mod(3, "Realism"));
        var theirs = CopyOf(yours, Mod(1, "SAIN", "9.9"), Mod(2, "Looting Bots"), Mod(4, "Questing Bots"), Mod(5, "Fika"));

        var back = ModListCopyBack.For(theirs, [yours, theirs]);

        Assert.NotNull(back);
        Assert.Same(yours, back!.Yours);
        Assert.Equal(["Questing Bots", "Fika"], back.Adds.Select(e => e.Name));
        Assert.Equal(["Realism"], back.LeftOut.Select(e => e.Name));
    }

    [Fact]
    public void TakingSome_AddsThemSorted_AndRemovesNothing()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"), Mod(3, "Realism"));
        var theirs = CopyOf(yours, Mod(1, "SAIN"), Mod(4, "Questing Bots"), Mod(5, "Fika"));
        var back = ModListCopyBack.For(theirs, [yours])!;

        var entries = back.With([back.Adds[1]]);

        Assert.Equal(["Fika", "Realism", "SAIN"], entries.Select(e => e.Name));
    }

    [Fact]
    public void TheSameModTwiceInTheCopy_IsOfferedOnce()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"));
        var theirs = CopyOf(yours, Mod(4, "Questing Bots"), Mod(4, "Questing Bots"));

        Assert.Single(ModListCopyBack.For(theirs, [yours])!.Adds);
    }

    [Fact]
    public void AnAddonAndAModWithTheSameNumber_AreTwoThings()
    {
        var yours = List(ModListOrigin.Local, Mod(116, "A mod"));
        var theirs = CopyOf(yours, Mod(116, "A mod"), new ModListEntry { Name = "An addon", ModId = 116, IsAddon = true });

        Assert.Equal(["An addon"], ModListCopyBack.For(theirs, [yours])!.Adds.Select(e => e.Name));
    }

    [Fact]
    public void ACopyWithNothingNew_IsStillRecognised()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"));
        var back = ModListCopyBack.For(CopyOf(yours, Mod(1, "SAIN")), [yours]);

        Assert.NotNull(back);
        Assert.Empty(back!.Adds);
    }

    [Fact]
    public void ACopyOfAListThatIsntYours_IsAnOrdinaryArrival()
    {
        var friends = List(ModListOrigin.Imported, Mod(1, "SAIN"));
        Assert.Null(ModListCopyBack.For(CopyOf(friends, Mod(2, "Fika")), [friends]));
    }

    [Fact]
    public void ACopyOfACollectionNotHere_IsAnOrdinaryArrival()
    {
        var elsewhere = List(ModListOrigin.Local, Mod(1, "SAIN"));
        Assert.Null(ModListCopyBack.For(CopyOf(elsewhere, Mod(2, "Fika")), []));
    }

    [Fact]
    public void YourOwnCopyReadBack_KeepsTheClashItAlreadyHas()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"));
        var yourCopy = CopyOf(yours, Mod(2, "Fika"));
        var held = new ModList
        {
            Id = yourCopy.Id,
            Name = yourCopy.Name,
            Origin = ModListOrigin.Local,
            DerivedFrom = yours.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Null(ModListCopyBack.For(yourCopy, [yours, held]));
    }

    [Fact]
    public void ACollectionWithNoParent_IsAnOrdinaryArrival()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"));
        var other = List(ModListOrigin.Imported, Mod(2, "Fika"));
        Assert.Null(ModListCopyBack.For(other, [yours]));
    }

    [Fact]
    public void TheShareCode_CarriesWhatACopyWasCopiedFrom()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"));
        var copy = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Squad pack (copy)",
            Origin = ModListOrigin.Local,
            DerivedFrom = yours.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        copy.Entries.Add(Mod(2, "Fika"));

        var read = ModListShareCode.Decode(ModListShareCode.Encode(copy, "Alex"));

        Assert.True(read.Succeeded, read.Error);
        Assert.Equal(yours.Id, read.List!.DerivedFrom);
        Assert.Equal(["#2"], ModListCopyBack.For(read.List, [yours])!.Adds.Select(e => e.Name));
    }

    [Fact]
    public void ACodeOfACollectionThatIsntACopy_SaysNothingAboutAParent()
    {
        var yours = List(ModListOrigin.Local, Mod(1, "SAIN"));
        var code = ModListShareCode.Encode(yours, null);

        Assert.Null(ModListShareCode.Decode(code).List!.DerivedFrom);

        // And costs no characters for it.
        var withParent = new ModList
        {
            Id = yours.Id,
            Name = yours.Name,
            DerivedFrom = Guid.NewGuid(),
            CreatedAt = yours.CreatedAt,
        };
        withParent.Entries.AddRange(yours.Entries);
        Assert.True(ModListShareCode.Encode(withParent, null).Length > code.Length);
    }
}
