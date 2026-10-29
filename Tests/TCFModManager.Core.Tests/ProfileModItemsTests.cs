using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork (1.4.5): what a profile holds from a mod, read before the mod goes - ProfileModItems, and
// the Fika status the filters read off a catalog listing.
//
public class ProfileModItemsTests : IDisposable
{
    private const string BaseItem = "5447a9cd4bdc2dbd208b4567";
    private const string GunId = "6a0000000000000000000001";
    private const string ScopeId = "6a0000000000000000000002";
    private const string TraderId = "6a00000000000000000000aa";
    private const string BaseTrader = "54cb50c76803fa8b248b4571";

    private readonly string _install = Path.Combine(Path.GetTempPath(), "SsptmmProfileItems_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_install)) Directory.Delete(_install, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string Profile(string nickname, params string[] tpls) => $$"""
        {
          "info": { "id": "p1", "username": "user" },
          "characters": {
            "pmc": {
              "Info": { "Nickname": "{{nickname}}", "Level": 10, "Side": "Usec" },
              "Inventory": { "items": [ {{string.Join(",", tpls.Select((t, i) => $$"""{ "_id": "i{{i}}", "_tpl": "{{t}}" }"""))}} ] },
              "TradersInfo": { "{{BaseTrader}}": {}, "{{TraderId}}": {}, "ragfair": {} }
            }
          },
          "dialogues": { "d": { "messages": [ { "items": { "data": [ { "_id": "m", "_tpl": "{{GunId}}" } ] } } ] } }
        }
        """;

    [Fact]
    public void A_profile_holds_what_SPTs_own_database_doesnt_have_counting_every_copy()
    {
        using var doc = JsonDocument.Parse(Profile("Trapp", BaseItem, GunId, ScopeId, GunId));

        var content = ProfileModItems.FromJson(doc.RootElement, new HashSet<string> { BaseItem }, new HashSet<string> { BaseTrader })!;

        Assert.Equal("Trapp", content.ProfileName);
        Assert.Equal(3, content.Items[GunId]); // two in the stash, one in the mail
        Assert.Equal(1, content.Items[ScopeId]);
        Assert.False(content.Items.ContainsKey(BaseItem));
        Assert.Equal([TraderId], content.Traders);
    }

    [Fact]
    public void Ids_are_found_as_text_and_as_UTF16_strings_in_a_DLL()
    {
        var ids = new HashSet<string>();
        var bytes = Encoding.UTF8.GetBytes($"{{\"_id\":\"{GunId}\"}} x")
            .Concat(new byte[] { 0, 1 })
            .Concat(Encoding.Unicode.GetBytes(ScopeId.ToUpperInvariant()))
            .ToArray();

        ProfileModItems.CollectIds(bytes, ids);

        Assert.Contains(GunId, ids);
        Assert.Contains(ScopeId, ids);
    }

    [Fact]
    public void A_longer_run_of_hex_is_not_an_id()
    {
        var ids = new HashSet<string>();
        ProfileModItems.CollectIds(Encoding.ASCII.GetBytes(GunId + "ff " + new string('a', 23)), ids);
        Assert.Empty(ids);
    }

    [Fact]
    public void An_item_another_staying_mod_also_carries_is_not_lost()
    {
        var profile = new ProfileModContent("Trapp", new Dictionary<string, int> { [GunId] = 2, [ScopeId] = 1 }, new HashSet<string> { TraderId });

        var losses = ProfileModItems.Losses(
            [profile],
            [("Guns", new HashSet<string> { GunId, ScopeId, TraderId })],
            new HashSet<string> { ScopeId });

        var loss = Assert.Single(losses);
        Assert.Equal(("Trapp", "Guns", 2, 1), (loss.ProfileName, loss.ModName, loss.Items, loss.Traders));
    }

    [Fact]
    public void Check_reads_the_install_and_names_the_mod_that_carries_the_items()
    {
        var server = Path.Combine(_install, "SPT_Runtime");
        Directory.CreateDirectory(Path.Combine(server, "SPT_Data", "database", "templates"));
        Directory.CreateDirectory(Path.Combine(server, "SPT_Data", "database", "traders", BaseTrader));
        Directory.CreateDirectory(Path.Combine(server, "user", "profiles"));
        File.WriteAllText(Path.Combine(server, "SPT.Server.exe"), "");
        File.WriteAllText(Path.Combine(server, "SPT_Data", "database", "templates", "items.json"), $$"""{ "{{BaseItem}}": {} }""");
        File.WriteAllText(Path.Combine(server, "user", "profiles", "p1.json"), Profile("Trapp", BaseItem, GunId, ScopeId));

        var guns = Path.Combine(server, "user", "mods", "Guns");
        Directory.CreateDirectory(Path.Combine(guns, "db"));
        File.WriteAllText(Path.Combine(guns, "db", "items.json"), $$"""{ "{{GunId}}": {}, "{{TraderId}}": {} }""");
        File.WriteAllBytes(Path.Combine(guns, "Guns.bundle"), Encoding.ASCII.GetBytes(ScopeId)); // never searched

        var other = Path.Combine(server, "user", "mods", "Other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "x.json"), "{}");

        // The mod's client half names its guns too - and can't add them, so it neither hides them
        // nor is warned about.
        var client = Path.Combine(_install, "BepInEx", "plugins", "GunsClient");
        Directory.CreateDirectory(client);
        File.WriteAllText(Path.Combine(client, "guns.json"), $$"""["{{GunId}}"]""");

        var losses = ProfileModItems.Check(_install, [("Guns", [guns, client])]);
        var loss = Assert.Single(losses);
        Assert.Equal(("Trapp", "Guns", 2, 1), (loss.ProfileName, loss.ModName, loss.Items, loss.Traders));

        Assert.Empty(ProfileModItems.Check(_install, [("Other", [other])]));
        Assert.Empty(ProfileModItems.Check(_install, [("GunsClient", [client])]));
        Assert.Single(ProfileModItems.Check(_install, [("Guns", [guns])]));
    }

    [Fact]
    public void An_install_without_SPTs_item_list_says_nothing()
    {
        Directory.CreateDirectory(Path.Combine(_install, "SPT_Runtime", "user", "profiles"));
        File.WriteAllText(Path.Combine(_install, "SPT_Runtime", "SPT.Server.exe"), "");
        File.WriteAllText(Path.Combine(_install, "SPT_Runtime", "user", "profiles", "p1.json"), Profile("Trapp", GunId));

        Assert.Empty(ProfileModItems.Read(_install));
    }

    [Theory]
    [InlineData(true, "incompatible", FikaStatus.Compatible)]
    [InlineData(false, "incompatible", FikaStatus.Incompatible)]
    [InlineData(false, "unknown", FikaStatus.Unknown)]
    [InlineData(false, null, FikaStatus.Unknown)]
    public void Fika_status_is_the_flag_then_the_newest_version(bool flagged, string? newest, FikaStatus expected)
    {
        var mod = new Mod
        {
            FikaCompatibility = flagged,
            Versions =
            [
                new ModVersionSummary { Version = "1.0.0", PublishedAt = DateTimeOffset.UtcNow.AddDays(-9), FikaCompatibility = "compatible" },
                new ModVersionSummary { Version = "2.0.0", PublishedAt = DateTimeOffset.UtcNow, FikaCompatibility = newest },
            ],
        };

        Assert.Equal(expected, mod.Fika);
    }
}
