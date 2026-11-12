using System.Net;
using System.Text.Json;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork (1.4.7): what a real Fika answered, kept as it came - a 4.1.6 server with Fika's server mod
// 2.4.2 (2026-11-12), asked on its own port. The Fika code here was first written from Fika's
// source; these are the shapes it then met. The settings file and both HTTP answers are the
// server's own, byte for byte (the settings file's API key taken out).
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

    // Answers the server's own bytes: zlib, as it sent them.
    private sealed class Answers(Dictionary<string, byte[]> byPath) : HttpMessageHandler
    {
        public List<string> Cookies { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Headers.TryGetValues("Cookie", out var cookies)) Cookies.AddRange(cookies);

            return Task.FromResult(byPath.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task The_mods_a_fika_server_lists_are_read_and_fika_is_one_of_them()
    {
        // /launcher/v2/mods as the server sent it. Keyed by name, the names written in PascalCase,
        // fields with nothing in them left out, and Fika's server mod named "server".
        var answer = Convert.FromBase64String(ModsAnswer);
        Assert.Equal(ModsAnswerText, SptLauncherApi.Decompress(answer));
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
        // /fika/profile/download for a profile just made, as the server sent it: the profile, and
        // no modData at all (not a null).
        var answer = Convert.FromBase64String(ProfileAnswer);
        Assert.Equal(ProfileAnswerText, SptLauncherApi.Decompress(answer));
        var server = new Answers(new() { [SptLauncherApi.FikaProfilePath] = answer });
        using var api = new SptLauncherApi("203.0.113.7:6969", server);

        var profile = await api.FikaProfileAsync("6af5d032ffed68874887dd51");

        Assert.StartsWith("{\"info\":{\"id\":\"6af5d032ffed68874887dd51\"", profile);
        Assert.Equal(["PHPSESSID=6af5d032ffed68874887dd51"], server.Cookies);
    }

    [Fact]
    public async Task An_id_fika_does_not_know_gets_an_empty_answer_and_no_profile()
    {
        // What the server sent for an id it has no profile for: 200 and nothing in the body.
        using var api = new SptLauncherApi("203.0.113.7:6969", new Answers(new() { [SptLauncherApi.FikaProfilePath] = [] }));

        Assert.Null(await api.FikaProfileAsync("0123456789abcdef01234567"));
    }

    // The server's answers, captured from a 4.1.6 server with Fika 2.4.2 and FIR Fence Purchases
    // (2026-11-12): the bytes it sent, and the JSON they hold.
    private const string ModsAnswer = "eNp9kMFOhDAQhl9l0zMt7sqJ24pBSdwNWdDEmD2UMkiVpU1bjAnBZ3cgxMVk9frNzJ//m54cwGrVWiBhT+LksIqhFbBKOyNqbsGOeKfKu06WJCQlMKes+mSVNNW4qH/2PLLnJ0y5GOKRbedqZXCcj/cInsBYqVoka7ZmV0gy7c7wK5jpPbepAc2dqAHvK95Y8MijaXCpdk7b0Pdfpau7ggl18qd4P0tzij2mGssWD1LA5Ep2SU4Gj1gwH2PsUjKW7/ysM28sDOZ5pFpnZNE5ZfBLL8el0oYFbHNR6fpPpaTF/ghlIRvpJMyh2OsWNLQlukywH/7R10a9gXC0wo7+WJRmU38aZTU3+tcLoojePNN9RLMtDfDXwzB8A0CorTs=";

    private const string ModsAnswerText = """
        {"Response":{"FIR Fence Purchases":{"ModGuid":"de.tosox.firfencepurchases","Name":"FIR Fence Purchases","Author":"Tosox","Version":"1.1.0","SptVersion":"~4.1.0","HasPrepatcher":false,"Url":"https://github.com/Tosox/SPT-FIRFencePurchases","License":"MIT"},"server":{"ModGuid":"Fika","Name":"server","Author":"Fika","Contributors":[],"Version":"2.4.2","SptVersion":"~4.1.3","HasPrepatcher":false,"Incompatibilities":[],"ModDependencies":{},"Url":"https://github.com/project-fika/Fika-Server-CSharp","License":"CC-BY-NC-SA-4.0"}}}
        """;

    private const string ProfileAnswer = "eNqljz9PAzEMxb+L5wy09I5rdyQqsTEwIAbjOL1IqRPlTxE69bvju3ISEwtDZD3rl+f3Jkg5Oh8YDhN4cXGZFg7Qo+vs3f3WObb9MDzs9FnbbcBAIbwc/2C2yuBssun3w27fGWiFs+BZr8AHC42ESZlPn3RTc2MDbH31URR4qSgWs4WrARoxI1XOZc6VzjSPghc86UdpIRh4FPqiEJMa4Lp78pZjq6t89WV89kX12/v1Fv//PmqkZIinxnM4lV7y0nqCEAl/2kgUhl9F1tXCl5ZRiG9+31Xygo0=";

    private const string ProfileAnswerText = """
        {"profile":{"info":{"id":"6af5d032ffed68874887dd51","scavId":"6af5d032ffed68874887dd52","aid":1698495,"username":"benchcap","wipe":true,"edition":"Standard"},"characters":{"pmc":{"savage":null,"Encyclopedia":null,"Hideout":null,"WishList":[]},"scav":{"savage":null,"Encyclopedia":null,"Hideout":null,"WishList":[]}},"dialogues":{},"inraid":{"location":"none","character":"none"},"insurance":[]}}
        """;

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
