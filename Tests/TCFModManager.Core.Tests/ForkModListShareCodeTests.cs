using System.IO.Compression;
using System.Text;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (SSPTMM): collections shared as a line of text - ModListShareCode.
public class ForkModListShareCodeTests
{
    private static ModList Sample()
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Fika night",
            Revision = 7,
            Origin = ModListOrigin.Local,
            Policy = ModListPolicy.Additive,
            SptVersion = "4.0.13",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        list.Entries.Add(new ModListEntry { Name = "SAIN", ModId = 2426, VersionId = 9001, Version = "4.4.3" });
        list.Entries.Add(new ModListEntry { Name = "Server half", ModId = 77, VersionId = 12, Version = "1.0.0", Scope = ModListEntryScope.Server });
        list.Entries.Add(new ModListEntry { Name = "An addon", ModId = 5, VersionId = 6, Version = "0.1", IsAddon = true });
        list.Entries.Add(new ModListEntry { Name = "Unpinned", ModId = 88 });
        list.Entries.Add(new ModListEntry { Name = "Hand-installed thing", Guid = "com.someone.thing" });
        return list;
    }

    [Fact]
    public void RoundTrip_KeepsIdentityAndEveryFetchableDetail()
    {
        var list = Sample();
        var read = ModListShareCode.Decode(ModListShareCode.Encode(list, "  Burningsbug350 "));

        Assert.True(read.Succeeded, read.Error);
        var back = read.List!;
        Assert.Equal(list.Id, back.Id);
        Assert.Equal(7, back.Revision);
        Assert.Equal("Fika night", back.Name);
        Assert.Equal(ModListOrigin.Imported, back.Origin);
        Assert.Equal(ModListPolicy.Additive, back.Policy);
        Assert.Equal("Burningsbug350", back.Source);
        Assert.Equal("4.0.13", back.SptVersion);
        Assert.Equal(5, back.Entries.Count);

        var sain = back.Entries[0];
        Assert.Equal((2426, 9001, "4.4.3"), (sain.ModId!.Value, sain.VersionId!.Value, sain.Version));
        Assert.Equal(ModListEntryScope.Everyone, sain.EffectiveScope);
        Assert.Equal("#2426", sain.Name);

        Assert.Equal(ModListEntryScope.Server, back.Entries[1].Scope);
        Assert.True(back.Entries[2].IsAddon);
        Assert.Equal((88, (int?)null, (string?)null), (back.Entries[3].ModId!.Value, back.Entries[3].VersionId, back.Entries[3].Version));

        // A mod sp-mod.com doesn't have travels by name alone.
        Assert.Equal("Hand-installed thing", back.Entries[4].Name);
        Assert.Null(back.Entries[4].ModId);
    }

    [Fact]
    public void ExclusiveListWithNoAuthor_ReadsBackThatWay()
    {
        var list = Sample();
        list.Policy = ModListPolicy.Exclusive;

        var back = ModListShareCode.Decode(ModListShareCode.Encode(list, null)).List!;

        Assert.Equal(ModListPolicy.Exclusive, back.Policy);
        Assert.Null(back.Source);
    }

    [Fact]
    public void Decode_FindsTheCodeInsideAChatMessage()
    {
        var code = ModListShareCode.Encode(Sample(), "me");
        var message = $"here's the pack for tonight:\n{code}\nlmk if it breaks";

        Assert.True(ModListShareCode.Contains(message));
        Assert.True(ModListShareCode.Decode(message).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("just some text")]
    [InlineData("SSPTMM1.")]
    public void NoCode_IsSaidSo(string? text)
    {
        Assert.False(ModListShareCode.Contains(text));
        Assert.False(ModListShareCode.Decode(text).Succeeded);
    }

    [Fact]
    public void ACutOffCode_IsRefused_NotHalfRead()
    {
        var code = ModListShareCode.Encode(Sample(), "me");
        var read = ModListShareCode.Decode(code[..(code.Length / 2)]);

        Assert.False(read.Succeeded);
    }

    [Fact]
    public void ACodeFromANewerFormat_IsRefusedWithThatReason()
    {
        var json = Encoding.UTF8.GetBytes("{\"f\":99,\"i\":\"" + Guid.NewGuid().ToString("N") + "\",\"r\":1,\"n\":\"x\",\"e\":[]}");
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true)) deflate.Write(json);
        var code = ModListShareCode.Prefix + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var read = ModListShareCode.Decode(code);

        Assert.False(read.Succeeded);
        Assert.Contains("newer version", read.Error);
    }

    [Fact]
    public void AnEntryOfTheWrongShape_IsSkipped_TheRestStillRead()
    {
        var json = Encoding.UTF8.GetBytes("{\"f\":1,\"i\":\"" + Guid.NewGuid().ToString("N") + "\",\"r\":2,\"n\":\"x\",\"e\":[[true],[12,34,\"1.0\"],{},[\"By hand\"]]}");
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, leaveOpen: true)) deflate.Write(json);
        var code = ModListShareCode.Prefix + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var read = ModListShareCode.Decode(code);

        Assert.True(read.Succeeded, read.Error);
        Assert.Equal(2, read.List!.Entries.Count);
        Assert.Equal(12, read.List.Entries[0].ModId);
        Assert.Equal("By hand", read.List.Entries[1].Name);
    }

    //
    // The reason the code leaves names out: a Discord message stops at 2000 characters. A real
    // 97-mod list measured about 1200; this one, with less repetition for deflate to find than a
    // real list has, still has to fit.
    //
    [Fact]
    public void ANinetySevenModList_FitsInADiscordMessage()
    {
        var random = new Random(97);
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "SPT 4.0.13 MO2",
            Revision = 12,
            SptVersion = "4.0.13",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        for (var i = 0; i < 97; i++)
        {
            list.Entries.Add(new ModListEntry
            {
                Name = "Mod " + i,
                ModId = random.Next(100, 3000),
                VersionId = random.Next(1000, 20000),
                Version = $"{random.Next(0, 5)}.{random.Next(0, 20)}.{random.Next(0, 10)}",
            });
        }

        var code = ModListShareCode.Encode(list, "WUVGAWORE");

        Assert.True(code.Length < 2000, $"{code.Length} characters");
        Assert.Equal(97, ModListShareCode.Decode(code).List!.Entries.Count);
    }
}
