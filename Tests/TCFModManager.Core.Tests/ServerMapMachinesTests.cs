using System.Net;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerMapInventoryTests
{
    private static ModListCandidate Mod(string name, int? id = null, string? version = null, bool disabled = false,
        params string[] folders) =>
        new()
        {
            Name = name, ModId = id, Version = version, IsDisabled = disabled,
            Folders = folders.Length > 0 ? folders : [name.ToLowerInvariant()],
        };

    [Fact]
    public void HashIgnoresTheOrderTheScannerFoundThingsIn()
    {
        var a = new[] { Mod("SAIN", 1, "3.0.0"), Mod("Waypoints", 2, "1.2.0"), Mod("HandMade") };
        var b = new[] { a[2], a[0], a[1] };

        Assert.Equal(
            ServerMapMachines.HashOf(ServerMapMachines.InventoryOf(a)),
            ServerMapMachines.HashOf(ServerMapMachines.InventoryOf(b)));
    }

    [Fact]
    public void HashMovesWhenAVersionOrADisableChanges()
    {
        var baseline = ServerMapMachines.HashOf(ServerMapMachines.InventoryOf([Mod("SAIN", 1, "3.0.0")]));

        Assert.NotEqual(baseline, ServerMapMachines.HashOf(ServerMapMachines.InventoryOf([Mod("SAIN", 1, "3.0.1")])));
        Assert.NotEqual(baseline,
            ServerMapMachines.HashOf(ServerMapMachines.InventoryOf([Mod("SAIN", 1, "3.0.0", disabled: true)])));
    }

    [Fact]
    public void FoldersTravelAsNamesNeverPaths()
    {
        var inventory = ServerMapMachines.InventoryOf([Mod("X", folders: [@"C:\SPT\BepInEx\plugins\x-mod", "x-mod"])]);

        Assert.Equal(["x-mod"], inventory[0].Folders);
    }

    [Fact]
    public void AnAddonAndAModSharingANumberStayTwoThings()
    {
        var inventory = ServerMapMachines.InventoryOf(
        [
            Mod("Mod 116", 116),
            new ModListCandidate { Name = "Addon 116", ModId = 116, IsAddon = true, Folders = ["a"] },
        ]);

        var back = ServerMapMachines.CandidatesOf(inventory);

        Assert.Single(back, c => c.ModId == 116 && c.IsAddon);
        Assert.Single(back, c => c.ModId == 116 && !c.IsAddon);
    }

    [Fact]
    public void AnInventoryTurnsBackIntoWhatThePlannerMatchesOn()
    {
        var back = ServerMapMachines.CandidatesOf(ServerMapMachines.InventoryOf(
            [Mod("Hand installed", folders: ["hand-installed"])]));

        var match = new ModListMatch(back);

        Assert.Equal(0, match.IndexOf(new ModListEntry { Name = "Renamed since", Folders = ["hand-installed"] }));
    }
}

public class ServerMapPresenceTests
{
    private static MapMachine Seen(long seconds, bool inGame = false) =>
        new() { SecondsSinceSeen = seconds, GameRunning = inGame };

    [Theory]
    [InlineData(0, true, MapPresence.InGame)]
    [InlineData(150, true, MapPresence.InGame)]
    [InlineData(150, false, MapPresence.AppOpen)]
    [InlineData(151, true, MapPresence.Away)]
    [InlineData(86400, false, MapPresence.Away)]
    public void TwoAndAHalfMissedHeartbeatsIsAway(long seconds, bool inGame, MapPresence expected) =>
        Assert.Equal(expected, ServerMapMachines.PresenceOf(Seen(seconds, inGame), 60));

    //
    // A game flag left true by a machine that then went quiet is not somebody in a raid - the app
    // stopped reporting, most likely with the game still open, and "In game" three hours later
    // would be a lie.
    //
    [Fact]
    public void AStaleInGameFlagReadsAsAway() =>
        Assert.Equal(MapPresence.Away, ServerMapMachines.PresenceOf(Seen(3 * 3600, inGame: true), 60));

    [Fact]
    public void ANonsenseIntervalFallsBackToTheDefault() =>
        Assert.Equal(MapPresence.AppOpen, ServerMapMachines.PresenceOf(Seen(100), 0));
}

public class ServerMapStandingTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    private const ModListEntryScope ClientsAndHeadless = ModListEntryScope.Client | ModListEntryScope.Headless;

    private static ModListEntry Entry(string name, int id, string version, ModListEntryScope scope) =>
        new() { Name = name, ModId = id, Version = version, VersionId = id * 10, Scope = scope, Folders = [name.ToLowerInvariant()] };

    private static ModList Published(ModListOrigin origin)
    {
        var list = new ModList
        {
            Id = Guid.NewGuid(), Name = "Zero to Hero", Origin = origin, Policy = ModListPolicy.Exclusive,
            CreatedAt = Timestamp, UpdatedAt = Timestamp,
        };

        list.Entries.AddRange(
        [
            Entry("SAIN", 1, "3.0.0", ClientsAndHeadless),
            Entry("UIFixes", 2, "2.0.0", ModListEntryScope.Client),
            Entry("fika-server", 3, "1.0.0", ModListEntryScope.Server),
            Entry("HeadlessTweaks", 4, "1.0.0", ModListEntryScope.Headless),
        ]);

        return list;
    }

    private static ReportedMod Has(string name, int? id, string? version, bool disabled = false) =>
        new() { Name = name, ModId = id, Version = version, Disabled = disabled, Folders = [name.ToLowerInvariant()] };

    private static MapMachine Machine(bool hosts = false, bool plays = false, bool headless = false,
        params ReportedMod[] mods) =>
        new() { DisplayName = "Box", Hosts = hosts, Plays = plays, Headless = headless, Mods = [.. mods] };

    [Fact]
    public void APlayerIsComparedAgainstTheClientEntriesOnly()
    {
        var standing = ServerMapMachines.StandingOf(Published(ModListOrigin.Server),
            Machine(plays: true, mods: [Has("SAIN", 1, "2.9.0"), Has("Extra HUD", 99, "1.0.0")]))!;

        Assert.Equal(2, standing.Expected);
        Assert.Contains(standing.Behind, a => a.Name == "SAIN" && a.Kind == ModListActionKind.Update);
        Assert.Contains(standing.Behind, a => a.Name == "UIFixes" && a.Kind == ModListActionKind.Install);
        Assert.DoesNotContain(standing.Behind, a => a.Name == "fika-server");
        Assert.Equal(["Extra HUD"], standing.Extras.Select(m => m.Name));
    }

    [Fact]
    public void AHostThatDoesNotPlayOwesOnlyTheServerEntries()
    {
        var standing = ServerMapMachines.StandingOf(Published(ModListOrigin.Server),
            Machine(hosts: true, mods: [Has("fika-server", 3, "1.0.0")]))!;

        Assert.True(standing.UpToDate);
        Assert.Equal(1, standing.Expected);
    }

    [Fact]
    public void AHeadlessOwesItsOwnEntriesAndTheServerOnes()
    {
        var standing = ServerMapMachines.StandingOf(Published(ModListOrigin.Server), Machine(headless: true))!;

        Assert.Equal(
            ["fika-server", "HeadlessTweaks", "SAIN"],
            standing.Behind.Select(a => a.Name).Order());
    }

    //
    // The host holds its published list as its OWN (Local) copy, and a Local list applies whole.
    // Read that way, every player on the map would owe fika-server - so the map reads it as served.
    //
    [Fact]
    public void TheHostsOwnCopyOfTheListIsReadTheWayClientsReceiveIt()
    {
        var local = Published(ModListOrigin.Local);

        var standing = ServerMapMachines.StandingOf(local,
            Machine(plays: true, mods: [Has("SAIN", 1, "3.0.0"), Has("UIFixes", 2, "2.0.0")]))!;

        Assert.True(standing.UpToDate);
        Assert.Equal(ModListOrigin.Local, local.Origin);
    }

    [Fact]
    public void ADisabledModIsBehindAsAnEnable()
    {
        var standing = ServerMapMachines.StandingOf(Published(ModListOrigin.Server),
            Machine(plays: true, mods: [Has("SAIN", 1, "3.0.0", disabled: true), Has("UIFixes", 2, "2.0.0")]))!;

        Assert.Single(standing.Behind, a => a.Name == "SAIN" && a.Kind == ModListActionKind.Enable);
    }

    // Not known is not the same as has nothing - a card must not claim a machine lacks every mod.
    [Fact]
    public void NoInventoryYetMeansNoStanding() =>
        Assert.Null(ServerMapMachines.StandingOf(Published(ModListOrigin.Server), new MapMachine { Plays = true }));

    [Theory]
    [InlineData(false, false, false, ModListEntryScope.Client)]
    [InlineData(false, true, false, ModListEntryScope.Client)]
    [InlineData(false, false, true, ModListEntryScope.Headless)]
    [InlineData(true, false, false, ModListEntryScope.Server)]
    [InlineData(true, true, false, ModListEntryScope.Server | ModListEntryScope.Client)]
    [InlineData(true, true, true, ModListEntryScope.Everyone)]
    public void RolesBecomeAScope(bool hosts, bool plays, bool headless, ModListEntryScope expected) =>
        Assert.Equal(expected, ServerMapMachines.ScopeFor(hosts, plays, headless));
}

public class ServerMapReportingTests
{
    private static ServerMapEndpoint Pinned(string thumbprint) => new("192.168.1.111", 6969, thumbprint);

    [Fact]
    public void TheClientIdIsMadeOnceAndKept()
    {
        var settings = new ServerMapSettings();

        var first = ServerMapReporting.EnsureClientId(settings);

        Assert.Equal(first, ServerMapReporting.EnsureClientId(settings));
        Assert.True(Guid.TryParseExact(first, "D", out _));
    }

    [Fact]
    public void AHandEditedClientIdThatIsNotAGuidIsReplaced()
    {
        var settings = new ServerMapSettings { ClientId = "chris" };

        Assert.NotEqual("chris", ServerMapReporting.EnsureClientId(settings));
    }

    [Theory]
    [InlineData(null, "CORE-01")]
    [InlineData("   ", "CORE-01")]
    [InlineData("Chris\u0007 PC", "Chris PC")]
    public void TheDisplayNameDefaultsToTheMachineAndIsCleaned(string? chosen, string expected) =>
        Assert.Equal(expected,
            ServerMapReporting.DisplayNameOf(new ServerMapSettings { DisplayName = chosen }, "CORE-01"));

    [Fact]
    public void AnAbsurdlyLongNameIsCut() =>
        Assert.Equal(ServerMapReporting.MaxDisplayNameLength,
            ServerMapReporting.DisplayNameOf(new ServerMapSettings { DisplayName = new string('x', 500) }).Length);

    [Fact]
    public void ConsentIsAskedOncePerServer()
    {
        var settings = new ServerMapSettings();
        var server = Pinned("AABB");

        Assert.Equal(ReportConsentState.Unanswered, ServerMapReporting.ConsentFor(settings, server));

        ServerMapReporting.SetConsent(settings, server, allowed: true);
        ServerMapReporting.SetConsent(settings, server, allowed: true);

        Assert.Equal(ReportConsentState.Allowed, ServerMapReporting.ConsentFor(settings, server));
        Assert.Single(settings.ReportConsent);
    }

    //
    // The same server on its LAN and its WAN address presents the same certificate, so it is one
    // server and one question. A different certificate is somebody else.
    //
    [Fact]
    public void ConsentFollowsTheCertificateNotTheAddress()
    {
        var settings = new ServerMapSettings();
        ServerMapReporting.SetConsent(settings, Pinned("aabb"), allowed: true);

        Assert.Equal(ReportConsentState.Allowed,
            ServerMapReporting.ConsentFor(settings, new ServerMapEndpoint("86.140.28.231", 6969, "AABB")));
        Assert.Equal(ReportConsentState.Unanswered, ServerMapReporting.ConsentFor(settings, Pinned("CCDD")));
    }

    [Fact]
    public void ADeclineIsRememberedAndCanBeForgotten()
    {
        var settings = new ServerMapSettings();
        var server = Pinned("AABB");

        ServerMapReporting.SetConsent(settings, server, allowed: false);
        Assert.Equal(ReportConsentState.Declined, ServerMapReporting.ConsentFor(settings, server));

        ServerMapReporting.ForgetConsent(settings, server);
        Assert.Equal(ReportConsentState.Unanswered, ServerMapReporting.ConsentFor(settings, server));
    }

    [Fact]
    public void AHeartbeatCarriesTheHashButNotTheInventory()
    {
        var inventory = ServerMapMachines.InventoryOf([new ModListCandidate { Name = "SAIN", ModId = 1, Folders = ["sain"] }]);

        var heartbeat = ServerMapReporting.Build(new ServerMapSettings(), InstallRoles.Player | InstallRoles.Headless,
            hosts: false, gameRunning: true, "4.1.5", "1.17.0-beta", inventory, includeInventory: false, "CORE-01");

        Assert.Null(heartbeat.Mods);
        Assert.Equal(ServerMapMachines.HashOf(inventory), heartbeat.InventoryHash);
        Assert.True(heartbeat.Plays && heartbeat.Headless && heartbeat.GameRunning);
        Assert.DoesNotContain("\"mods\"", JsonSerializer.Serialize(heartbeat, ServerMapMachines.Json));
    }

    [Fact]
    public void ADedicatedHostClaimsNeitherPlayerRole()
    {
        var report = ServerMapReporting.Build(new ServerMapSettings(), InstallRoles.None, hosts: true,
            gameRunning: false, null, null, [], includeInventory: true);

        Assert.False(report.Plays || report.Headless);
        Assert.Equal(ModListEntryScope.Server, ServerMapMachines.ScopeFor(report.Hosts, report.Plays, report.Headless));
    }

    //
    // The lesson of the published mark: a field that has to persist is proven by a trip through
    // disk, not through a serializer call that happens to share its options.
    //
    [Fact]
    public void IdentityAndConsentSurviveSettingsJson()
    {
        var settings = new AppSettings();
        var id = ServerMapReporting.EnsureClientId(settings.ServerMap);
        settings.ServerMap.DisplayName = "Chris PC";
        ServerMapReporting.SetConsent(settings.ServerMap, Pinned("AABB"), allowed: true);

        var path = Path.Combine(Path.GetTempPath(), $"tcfmm-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            var back = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))!;

            Assert.Equal(id, back.ServerMap.ClientId);
            Assert.Equal("Chris PC", back.ServerMap.DisplayName);
            Assert.Equal(ReportConsentState.Allowed, ServerMapReporting.ConsentFor(back.ServerMap, Pinned("AABB")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ServerMapMapRoutesTests
{
    private static readonly ServerMapEndpoint Keyed = new("192.168.1.111", 6969, "AABB", "KEY1-KEY2");

    private static MachineReport Report(bool withMods) => new()
    {
        ClientId = Guid.NewGuid().ToString("D"),
        DisplayName = "Chris PC",
        Plays = true,
        InventoryHash = "abc",
        Mods = withMods ? [new ReportedMod { Name = "SAIN", ModId = 1, Version = "3.0.0" }] : null,
    };

    [Fact]
    public async Task AReportIsAPostOfCamelCaseJsonWithTheKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"protocol":1,"intervalSeconds":45,"resend":true}""");
        using var client = ServerMapClient.TryCreate(Keyed, handler)!;

        var result = await client.ReportAsync(Report(withMods: false));

        Assert.True(result.Succeeded);
        Assert.True(result.Resend);
        Assert.Equal(45, result.IntervalSeconds);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/tcfservermap/report", handler.Uri!.AbsolutePath);
        Assert.Equal("KEY1-KEY2", handler.Header(ServerMapEndpoint.KeyHeaderName));
        Assert.Contains("\"clientId\"", handler.Body);
        Assert.Contains("\"inventoryHash\":\"abc\"", handler.Body);
        Assert.DoesNotContain("\"mods\"", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ServerMapProblem.MapUnsupported)]
    [InlineData(HttpStatusCode.Unauthorized, ServerMapProblem.KeyRejected)]
    [InlineData(HttpStatusCode.Conflict, ServerMapProblem.ProtocolMismatch)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ServerMapProblem.NotServerMap)]
    [InlineData(HttpStatusCode.BadRequest, ServerMapProblem.Failed)]
    public async Task ARefusedReportSaysWhy(HttpStatusCode status, ServerMapProblem expected)
    {
        using var client = ServerMapClient.TryCreate(Keyed, new RecordingHandler(status, "{}"))!;

        Assert.Equal(expected, (await client.ReportAsync(Report(withMods: true))).Problem);
    }

    [Fact]
    public async Task NoKeyAtAllIsKeyRequiredNotRejected()
    {
        using var client = ServerMapClient.TryCreate(new ServerMapEndpoint("h", 6969, "AABB"),
            new RecordingHandler(HttpStatusCode.Unauthorized, "{}"))!;

        Assert.Equal(ServerMapProblem.KeyRequired, (await client.ClientsAsync(null)).Problem);
    }

    [Fact]
    public async Task TheMapNamesThisMachineOnlyByHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            {"protocol":1,"intervalSeconds":60,"clients":[
              {"isYou":false,"displayName":"Server box","hosts":true,"secondsSinceSeen":5,"mods":[{"name":"fika-server","modId":3}]},
              {"isYou":true,"displayName":"Chris PC","plays":true,"gameRunning":true,"secondsSinceSeen":12}
            ]}
            """);
        using var client = ServerMapClient.TryCreate(Keyed, handler)!;

        var result = await client.ClientsAsync("11111111-2222-3333-4444-555555555555");

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("11111111-2222-3333-4444-555555555555", handler.Header(ServerMapClient.ClientHeaderName));
        Assert.Equal(2, result.Machines.Count);
        Assert.Equal(3, result.Machines[0].Mods![0].ModId);
        Assert.Null(result.Machines[1].Mods);
        Assert.True(result.Machines[1].IsYou);
    }

    //
    // Another player on a newer build sending an inventory this one cannot read costs that one card
    // its mod list - never the whole map.
    //
    [Fact]
    public async Task OneUnreadableInventoryDoesNotBlankTheMap()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            {"protocol":1,"clients":[
              {"displayName":"Newer build","mods":{"format":"something else"}},
              {"displayName":"Chris PC","mods":[]}
            ]}
            """);
        using var client = ServerMapClient.TryCreate(Keyed, handler)!;

        var result = await client.ClientsAsync(null);

        Assert.Equal(2, result.Machines.Count);
        Assert.True(result.Machines[0].InventoryUnreadable);
        Assert.Null(result.Machines[0].Mods);
        Assert.Empty(result.Machines[1].Mods!);
        Assert.Equal(ServerMapMachines.DefaultIntervalSeconds, result.IntervalSeconds);
    }

    [Fact]
    public async Task WithdrawingSendsTheIdInTheBody()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"protocol":1,"removed":true}""");
        using var client = ServerMapClient.TryCreate(Keyed, handler)!;

        Assert.Equal(ServerMapProblem.None, await client.WithdrawAsync("11111111-2222-3333-4444-555555555555"));
        Assert.Equal("/tcfservermap/withdraw", handler.Uri!.AbsolutePath);
        Assert.Contains("\"clientId\":\"11111111-2222-3333-4444-555555555555\"", handler.Body);
    }

    [Fact]
    public async Task AnUnreachableServerIsUnreachable()
    {
        using var client = ServerMapClient.TryCreate(Keyed, new FailingHandler())!;

        Assert.Equal(ServerMapProblem.Unreachable, (await client.ReportAsync(Report(false))).Problem);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        private HttpRequestMessage? _request;

        public HttpMethod? Method => _request?.Method;

        public Uri? Uri => _request?.RequestUri;

        public string Body { get; private set; } = "";

        public string? Header(string name) =>
            _request is not null && _request.Headers.TryGetValues(name, out var values) ? values.First() : null;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _request = request;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no route to host");
    }
}
