using System.Net;
using System.Text.Json;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork (1.4.7): what a real Fika answered, kept as it came - a 4.1.6 server with Fika's server mod
// 2.4.2 (2026-11-12), asked on its own port. The Fika code here was first written from Fika's
// source; these are the shapes it then met.
//
public class ForkFikaRealShapesTests
{
    // fika.jsonc as Fika 2.4.2 writes it on its first start (the API key taken out).
    private const string RealConfig = """
        {
          "client": {
            "useBtr": true,
            "friendlyFire": true,
            "dynamicVExfils": false,
            "allowFreeCam": false,
            "allowSpectateFreeCam": false,
            "blacklistedItems": [],
            "forceSaveOnDeath": false,
            "mods": {
              "required": [],
              "optional": []
            },
            "useInertia": true,
            "sharedQuestProgression": false,
            "canEditRaidSettings": true,
            "enableTransits": true,
            "anyoneCanStartRaid": false,
            "allowNamePlates": true,
            "randomLabyrinthSpawns": false,
            "pmcFoundInRaid": false,
            "allowSpectateBots": false,
            "instantLoad": false,
            "fastLoad": false,
            "reviveConfig": {
              "enabled": false,
              "headshotKills": false,
              "grenadesKills": false,
              "allowLooting": false,
              "maxRevives": 0,
              "bleedoutTime": 60,
              "reviveTime": 5
            }
          },
          "server": {
            "SPT": {
              "http": {
                "ip": "127.0.0.1",
                "port": 6969,
                "backendIp": "127.0.0.1",
                "backendPort": 6969
              },
              "disableSPTChatBots": false
            },
            "webhook": {
              "enabled": false,
              "name": "Fika Server",
              "avatarUrl": "https://github.com/project-fika/Fika-Server-CSharp/blob/main/FikaWebApp/wwwroot/images/FIKA_LOGO.png?raw=true",
              "url": ""
            },
            "allowItemSending": true,
            "itemSendingStorageTime": 7,
            "sentItemsLoseFIR": true,
            "sessionTimeout": 5,
            "showDevProfile": true,
            "showNonStandardProfile": true,
            "logClientModsInConsole": false,
            "adminIds": [],
            "apiKey": ""
          },
          "natPunchServer": {
            "enable": false,
            "port": 6790
          },
          "headless": {
            "profiles": {
              "amount": 0,
              "aliases": {}
            },
            "scripts": {
              "generate": true,
              "forceIp": "https://127.0.0.1:6969"
            },
            "setLevelToAverageOfLobby": true,
            "restartAfterAmountOfRaids": 1
          }
        }
        """;

    [Fact]
    public void The_file_fika_writes_is_read()
    {
        var config = FikaServerConfig.Parse(RealConfig)!;

        Assert.Equal("127.0.0.1", config.Ip);
        Assert.Equal(6969, config.Port);
        Assert.True(config.OnlyThisPc);

        // Every switch the Play card offers is in it, once.
        foreach (var name in FikaConfigText.ClientSwitches)
        {
            Assert.NotNull(FikaConfigText.ReadSwitch(RealConfig, name));
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(RealConfig, "\"" + name + "\""));
        }

        Assert.True(FikaConfigText.ReadSwitch(RealConfig, "friendlyFire"));
        Assert.False(FikaConfigText.ReadSwitch(RealConfig, "sharedQuestProgression"));
    }

    [Fact]
    public void A_switch_changes_one_line_of_it_and_it_is_still_fikas_file()
    {
        var changed = FikaConfigText.SetSwitch(RealConfig, "friendlyFire", false)!;

        var before = RealConfig.Split('\n');
        var after = changed.Split('\n');
        Assert.Equal(before.Length, after.Length);
        Assert.Single(before.Zip(after), pair => pair.First != pair.Second);

        using var doc = JsonDocument.Parse(changed);
        Assert.False(doc.RootElement.GetProperty("client").GetProperty("friendlyFire").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("client").GetProperty("useInertia").GetBoolean());
    }

    [Fact]
    public void Let_other_pcs_in_changes_the_servers_address_and_no_other()
    {
        var changed = FikaConfigText.SetListenAddress(RealConfig, "0.0.0.0")!;

        using var doc = JsonDocument.Parse(changed);
        var http = doc.RootElement.GetProperty("server").GetProperty("SPT").GetProperty("http");
        Assert.Equal("0.0.0.0", http.GetProperty("ip").GetString());
        Assert.Equal("127.0.0.1", http.GetProperty("backendIp").GetString());
        Assert.Equal("https://127.0.0.1:6969", doc.RootElement.GetProperty("headless").GetProperty("scripts").GetProperty("forceIp").GetString());
        Assert.False(FikaServerConfig.Parse(changed)!.OnlyThisPc);
    }

    private sealed class Answers(Dictionary<string, string> byPath) : HttpMessageHandler
    {
        public List<string> Cookies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Headers.TryGetValues("Cookie", out var cookies)) Cookies.AddRange(cookies);

            return Task.FromResult(byPath.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SptLauncherApi.Compress(body)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task The_mods_a_fika_server_lists_are_read_and_fika_is_one_of_them()
    {
        // /launcher/v2/mods, as answered: keyed by name, and Fika's server mod is named "server".
        const string answer = """
            {"response":{"FIR Fence Purchases":{"modGuid":"de.tosox.firfencepurchases","name":"FIR Fence Purchases","author":"Tosox","contributors":null,"version":"1.1.0","sptVersion":"~4.1.0","hasPrepatcher":false,"incompatibilities":null,"modDependencies":null,"url":"https://github.com/Tosox/SPT-FIRFencePurchases","license":"MIT"},"server":{"modGuid":"Fika","name":"server","author":"Fika","contributors":null,"version":"2.4.2","sptVersion":"~4.1.3","hasPrepatcher":false,"incompatibilities":null,"modDependencies":null,"url":"https://github.com/project-fika/Fika-Server-CSharp","license":"CC BY-NC-SA 4.0"}}}
            """;
        using var api = new SptLauncherApi("203.0.113.7:6969", new Answers(new() { [SptLauncherApi.ModsPath] = answer }));

        var mods = await api.ModsAsync();

        Assert.Equal(2, mods.Count);
        var fika = Assert.Single(mods, m => m.ModGuid == "Fika");
        Assert.Equal("server", fika.Name);
        Assert.Equal("2.4.2", fika.Version);

        // In a collection the catalogue's names are used: sp-mod.com lists these two by these GUIDs.
        HostMods.CatalogLookup catalog = guid => guid.ToLowerInvariant() switch
        {
            "de.tosox.firfencepurchases" => (2371, "FIR Fence Purchases"),
            "fika" => (2357, "Project Fika - Server"),
            _ => null,
        };
        var candidates = HostMods.Candidates(mods, catalog);
        Assert.Equal(["FIR Fence Purchases", "Project Fika - Server"], candidates.Select(c => c.Name));
        Assert.All(candidates, c => Assert.NotNull(c.ModId));
    }

    [Fact]
    public async Task A_profile_comes_back_from_fika_asked_with_its_id_as_the_session()
    {
        // /fika/profile/download for a profile just made: the profile, and no mod data yet.
        const string answer = """
            {"profile":{"info":{"id":"6af54560f666896958ebe7ca","scavId":"6af54560f666896958ebe7cb","aid":1307922,"username":"benchuser","wipe":true,"edition":"Standard"},"characters":{"pmc":{},"scav":{}}}}
            """;
        var server = new Answers(new() { [SptLauncherApi.FikaProfilePath] = answer });
        using var api = new SptLauncherApi("203.0.113.7:6969", server);

        var profile = await api.FikaProfileAsync("6af54560f666896958ebe7ca");

        Assert.StartsWith("{\"info\":{\"id\":\"6af54560f666896958ebe7ca\"", profile);
        Assert.Equal(["PHPSESSID=6af54560f666896958ebe7ca"], server.Cookies);
    }

    // The lines Fika's game plugin logs, as they stand in Fika.Core.dll 2.4.6.
    [Theory]
    [InlineData("2 forbidden mod(s) are loaded, have the server host allow or remove the following mods: a.b, c.d", LogFindingKind.FikaForbiddenMods)]
    [InlineData("1 missing required mod(s), verify the following mods are present: com.fika.headless", LogFindingKind.FikaMissingMods)]
    [InlineData("3 mismatched mod(s) are loaded, verify the following mods are up to date with the server host: a, b, c", LogFindingKind.FikaMismatchedMods)]
    [InlineData("FikaModHandler::VerifyMods: Response was invalid!", LogFindingKind.FikaNoServerMod)]
    [InlineData("Server version check failed. Expected: >2.4.1, received: 2.3.0", LogFindingKind.FikaServerVersion)]
    public void Each_line_fikas_plugin_logs_has_its_card(string message, LogFindingKind kind)
    {
        var bepInEx = LogEntries.Parse(LogKind.BepInEx, "LogOutput.log", ["[Error  :FikaModHandler] " + message]);

        var found = LogDiagnoser.Diagnose(new LogFileSet(null, null, null, null, false), [], bepInEx, [], LogModLocator.From([])).Findings;

        Assert.Equal(kind, Assert.Single(found).Kind);
    }
}
