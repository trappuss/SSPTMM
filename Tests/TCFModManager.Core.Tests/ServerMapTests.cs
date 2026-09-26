using System.Net;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerMapEndpointTests
{
    [Theory]
    [InlineData("192.168.1.111", 6969, "https://192.168.1.111:6969/")]
    [InlineData("spt.example.com", 6969, "https://spt.example.com:6969/")]
    [InlineData("  192.168.1.111  ", 6969, "https://192.168.1.111:6969/")]
    public void BuildsABaseUri(string host, int port, string expected)
    {
        Assert.True(new ServerMapEndpoint(host, port).TryGetBaseUri(out var uri));
        Assert.Equal(expected, uri.ToString());
    }

    //
    // UriBuilder does not bracket an IPv6 literal for you - "::1" comes out as "https://::1:6969",
    // which does not parse. An address handed out for a server reached over the internet can easily
    // be IPv6, so this is not a curiosity.
    //
    [Theory]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("fd00::1")]
    public void BracketsAnIPv6Literal(string host)
    {
        Assert.True(new ServerMapEndpoint(host, 6969).TryGetBaseUri(out var uri));
        Assert.Contains("[", uri.ToString());
        Assert.Equal(6969, uri.Port);
    }

    [Theory]
    [InlineData("", 6969)]
    [InlineData("   ", 6969)]
    [InlineData("192.168.1.111", 0)]
    [InlineData("192.168.1.111", 70000)]
    [InlineData("192.168.1.111", -1)]
    public void RefusesAnUnusableEndpoint(string host, int port) =>
        Assert.False(new ServerMapEndpoint(host, port).TryGetBaseUri(out _));

    [Fact]
    public void HasPinIsFalseUntilOneIsRecorded()
    {
        Assert.False(new ServerMapEndpoint("h", 1).HasPin);
        Assert.False(new ServerMapEndpoint("h", 1, "   ").HasPin);
        Assert.True(new ServerMapEndpoint("h", 1, "AABB").HasPin);
    }
}

//
// The pin decision, tested as the pure function it is. SPT's certificate fails the chain and
// hostname checks at any remote address - always - so the thumbprint is the entire check. Getting
// this wrong either locks everyone out or trusts anything that answers.
//
public class ServerCertificatePinTests
{
    private const string A = "6D6B779B72514954";
    private const string B = "0000000000000000";

    [Fact]
    public void NoPinYet_IsTrustOnFirstUse()
    {
        Assert.Equal(PinVerdict.FirstUse, ServerCertificatePin.Decide(null, A));
        Assert.Equal(PinVerdict.FirstUse, ServerCertificatePin.Decide("", A));
        Assert.Equal(PinVerdict.FirstUse, ServerCertificatePin.Decide("   ", A));
    }

    [Fact]
    public void SameThumbprint_Matches() => Assert.Equal(PinVerdict.Match, ServerCertificatePin.Decide(A, A));

    // Thumbprints get copied by hand and between tools; casing must not decide access.
    [Fact]
    public void CasingDoesNotDecideAccess() =>
        Assert.Equal(PinVerdict.Match, ServerCertificatePin.Decide(A.ToLowerInvariant(), A.ToUpperInvariant()));

    [Fact]
    public void DifferentThumbprint_IsAMismatch() =>
        Assert.Equal(PinVerdict.Mismatch, ServerCertificatePin.Decide(A, B));

    [Fact]
    public void NoCertificateAtAll_IsNeverTrusted()
    {
        Assert.Equal(PinVerdict.NoCertificate, ServerCertificatePin.Decide(A, null));
        Assert.Equal(PinVerdict.NoCertificate, ServerCertificatePin.Decide(null, null));
    }

    [Theory]
    [InlineData(PinVerdict.FirstUse, true)]
    [InlineData(PinVerdict.Match, true)]
    [InlineData(PinVerdict.Mismatch, false)]
    [InlineData(PinVerdict.NoCertificate, false)]
    public void OnlyFirstUseAndMatchAreAccepted(PinVerdict verdict, bool accepted) =>
        Assert.Equal(accepted, ServerCertificatePin.Accepts(verdict));
}

public class ServerHelloTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // The exact body the spike server returned, so the parser is held to the real wire shape rather
    // than to one written to match it.
    private const string RealBody = """
        {
          "protocol": 1,
          "modVersion": "0.1.0",
          "serverName": "CORE-01",
          "requiresKey": false,
          "hasList": false,
          "capabilities": [ "hello", "echo" ],
          "payloadPath": "G:\\Single Player Tarkov 4.1\\TCFModManager\\ServerMap\\payload\\TCFMM.ServerMap.Payload.dll"
        }
        """;

    [Fact]
    public void ParsesTheHandshakeTheSpikeServerActuallySent()
    {
        var hello = JsonSerializer.Deserialize<ServerHello>(RealBody, Json)!;

        Assert.Equal(1, hello.Protocol);
        Assert.Equal("0.1.0", hello.ModVersion);
        Assert.Equal("CORE-01", hello.ServerName);
        Assert.False(hello.RequiresKey);
        Assert.False(hello.HasList);
        Assert.Null(hello.ListRevision);
        Assert.True(hello.Supports("hello"));
        Assert.True(hello.Supports("HELLO"));
        Assert.False(hello.Supports("list"));
    }

    // Unknown fields must not throw - a newer server carries more than this build knows about, and
    // payloadPath above is already one of them.
    [Fact]
    public void IgnoresFieldsItDoesNotKnow()
    {
        var hello = JsonSerializer.Deserialize<ServerHello>(
            """{ "protocol": 1, "somethingNew": { "a": 1 }, "capabilities": [] }""", Json)!;

        Assert.Equal(1, hello.Protocol);
    }

    [Fact]
    public void CapabilitiesDefaultToEmptyRatherThanNull()
    {
        var hello = JsonSerializer.Deserialize<ServerHello>("""{ "protocol": 1 }""", Json)!;

        Assert.NotNull(hello.Capabilities);
        Assert.False(hello.Supports("hello"));
    }
}

//
// The response-handling half, over a stub handler. Every branch below is something a user will
// actually hit: a plain SPT server on the address, a stub whose payload is gone, a server newer
// than this build, and a machine that is simply off.
//
public class ServerMapClientTests
{
    private static readonly ServerMapEndpoint Endpoint = new("192.168.1.111", 6969);

    private static async Task<ServerHelloProbe> Probe(HttpStatusCode status, string body)
    {
        using var client = ServerMapClient.TryCreate(Endpoint, new StubHandler(status, body))!;
        return await client.HelloAsync();
    }

    [Fact]
    public void TryCreateRefusesAnAddressItCannotDial() =>
        Assert.Null(ServerMapClient.TryCreate(new ServerMapEndpoint("", 6969)));

    [Fact]
    public void TryCreateBuildsAClientForAUsableAddress()
    {
        using var client = ServerMapClient.TryCreate(Endpoint);
        Assert.NotNull(client);
    }

    [Fact]
    public async Task AHandshakeIsReadBackWhole()
    {
        var probe = await Probe(HttpStatusCode.OK,
            """{ "protocol": 1, "serverName": "CORE-01", "hasList": true, "listRevision": 7 }""");

        Assert.True(probe.Found);
        Assert.Equal(ServerMapProblem.None, probe.Problem);
        Assert.Equal("CORE-01", probe.Hello!.ServerName);
        Assert.Equal(7, probe.Hello.ListRevision);
    }

    //
    // A plain SPT server 404s the route and a stub whose payload folder has been emptied 503s it.
    // Both mean "no server map here" and neither deserves an error the user has to interpret.
    //
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnUnhappyStatusIsNotAServerMap(HttpStatusCode status)
    {
        var probe = await Probe(status, "");

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.NotServerMap, probe.Problem);
        Assert.Equal((int)status, probe.StatusCode);
    }

    // Some other service on that port answering 200 - the port is a user setting and gets mistyped.
    [Theory]
    [InlineData("<html>hello</html>")]
    [InlineData("")]
    [InlineData("""{ "message": "pong" }""")]
    public async Task A200ThatIsNotAHandshakeIsNotAServerMap(string body) =>
        Assert.Equal(ServerMapProblem.NotServerMap, (await Probe(HttpStatusCode.OK, body)).Problem);

    //
    // A server newer than this build is refused rather than half-understood, and the version it
    // speaks is carried out so the App can say which side needs updating.
    //
    [Fact]
    public async Task AProtocolThisBuildDoesNotSpeakIsRefusedAndNamed()
    {
        var probe = await Probe(HttpStatusCode.OK, """{ "protocol": 99, "serverName": "CORE-01" }""");

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.ProtocolMismatch, probe.Problem);
        Assert.Equal(99, probe.ServerProtocol);
    }

    [Fact]
    public async Task TheEndpointIsCarriedBackOnEveryOutcome()
    {
        Assert.Equal(Endpoint, (await Probe(HttpStatusCode.OK, """{ "protocol": 1 }""")).Endpoint);
        Assert.Equal(Endpoint, (await Probe(HttpStatusCode.NotFound, "")).Endpoint);
    }

    [Fact]
    public async Task AThrowingTransportIsReportedAsUnreachableNotThrown()
    {
        using var client = ServerMapClient.TryCreate(Endpoint, new ThrowingHandler())!;

        var probe = await client.HelloAsync();

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.Unreachable, probe.Problem);
        Assert.NotNull(probe.Error);
    }

    //
    // Nothing is listening on port 1, so this is the one test that goes through the real handler -
    // the pinning callback, UseProxy = false and all. It must come back as Unreachable rather than
    // throwing, and must not be mistaken for a rejected certificate.
    //
    [Fact]
    public async Task ARealSocketThatGoesNowhereIsReportedNotThrown()
    {
        using var client = ServerMapClient.TryCreate(
            new ServerMapEndpoint("127.0.0.1", 1), TimeSpan.FromSeconds(5))!;

        var probe = await client.HelloAsync();

        Assert.False(probe.Found);
        Assert.Equal(ServerMapProblem.Unreachable, probe.Problem);
        Assert.NotNull(probe.Error);
    }

    // Asks for the handshake route and nothing else, so a stub server's CanHandle prefix test is
    // being given what it was written against.
    [Fact]
    public async Task AsksForTheHandshakeRoute()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "protocol": 1 }""");
        using var client = ServerMapClient.TryCreate(Endpoint, handler)!;

        await client.HelloAsync();

        Assert.Equal("https://192.168.1.111:6969/tcfservermap/hello", handler.LastRequestUri?.ToString());
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no route to host");
    }
}

public class ServerMapSettingsTests
{
    //
    // Off by default, like the Mod footprint page. The map needs an SPT server running the Server
    // Map mod at the other end, which most installs will not have - so nobody gets a page that is
    // empty for them without asking for it.
    //
    [Fact]
    public void AFreshSettingsFileHasTheServerMapOffAndUnconfiguredOnTheDefaultPort()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.ServerMap);
        Assert.False(settings.ServerMap.ShowPage);
        Assert.False(settings.ServerMap.IsConfigured);
        Assert.Equal(ServerMapEndpoint.DefaultPort, settings.ServerMap.Port);
        Assert.Null(settings.ServerMap.PinnedThumbprint);
    }

    [Fact]
    public void ConfiguredSettingsBecomeAnEndpointThatCanBeDialled()
    {
        var settings = new ServerMapSettings { Host = "192.168.1.111", PinnedThumbprint = "AABB" };

        var endpoint = settings.ToEndpoint();

        Assert.True(settings.IsConfigured);
        Assert.True(endpoint.HasPin);
        Assert.True(endpoint.TryGetBaseUri(out var uri));
        Assert.Equal("https://192.168.1.111:6969/", uri.ToString());
    }

    // Round-trips through the same serializer settings.json uses, so an upgrading install keeps the
    // pin it already recorded rather than silently re-arming trust-on-first-use.
    [Fact]
    public void SurvivesARoundTripThroughJson()
    {
        var settings = new AppSettings
        {
            ServerMap = new ServerMapSettings
            {
                ShowPage = true,
                Host = "spt.example.com",
                Port = 7000,
                PinnedThumbprint = "AABB",
            },
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        Assert.True(restored.ServerMap.ShowPage);
        Assert.Equal("spt.example.com", restored.ServerMap.Host);
        Assert.Equal(7000, restored.ServerMap.Port);
        Assert.Equal("AABB", restored.ServerMap.PinnedThumbprint);
    }

    //
    // An install upgrading from a build with no ServerMap key must not land with a null here - and
    // neither must one whose hand-edited settings.json says so outright, which is a thing that
    // happens to a file the Options page invites people to open.
    //
    [Theory]
    [InlineData("""{ "SptInstallPath": "C:\\SPT" }""")]
    [InlineData("""{ "ServerMap": null }""")]
    public void SettingsWithNoUsableServerMapSectionStillLoad(string json)
    {
        var restored = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.NotNull(restored.ServerMap);
        Assert.False(restored.ServerMap.ShowPage);
        Assert.False(restored.ServerMap.IsConfigured);
        Assert.Equal(ServerMapEndpoint.DefaultPort, restored.ServerMap.Port);
    }

    //
    // Turning the page off must not throw away the address or the recorded certificate: switching it
    // back on has to land where it was, not re-arm trust-on-first-use as though the server had never
    // been seen.
    //
    [Fact]
    public void SwitchingThePageOffKeepsTheAddressAndThePin()
    {
        var settings = new ServerMapSettings
        {
            ShowPage = true,
            Host = "127.0.0.1",
            PinnedThumbprint = "AABB",
        };

        settings.ShowPage = false;

        Assert.True(settings.IsConfigured);
        Assert.Equal("AABB", settings.ToEndpoint().PinnedThumbprint);
    }

    // The address Fika defaults to, and the one this was first tested against.
    [Fact]
    public void LoopbackIsADialableAddress()
    {
        var endpoint = new ServerMapSettings { Host = "127.0.0.1" }.ToEndpoint();

        Assert.True(endpoint.TryGetBaseUri(out var uri));
        Assert.Equal("https://127.0.0.1:6969/", uri.ToString());
    }
}

//
// Fetching the list a server publishes. The server serves the operator's own .tcfmodlist file
// verbatim, so what arrives here is byte-for-byte the format a list emailed between two people is -
// which is the whole point, and what these tests hold it to.
//
public class ServerMapListTests
{
    private static readonly ServerMapEndpoint Endpoint = new("192.168.1.111", 6969);

    // A real export, as ModListFile.Write produces it.
    private static string Published(string name = "Fika night", int revision = 7) =>
        ModListFile.Write(
            new ModList
            {
                Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                Name = name,
                Revision = revision,
                Policy = ModListPolicy.Exclusive,
                SptVersion = "4.1.5",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch,
                Entries =
                {
                    new ModListEntry { Name = "SAIN", ModId = 2426, VersionId = 5, Version = "3.2.1" },
                    new ModListEntry { Name = "A GitHub-only mod" },
                },
            },
            author: "Somebody Else");

    private static async Task<ServerMapListResult> Fetch(HttpStatusCode status, string body)
    {
        using var client = ServerMapClient.TryCreate(Endpoint, new ListStubHandler(status, body))!;
        return await client.ListAsync();
    }

    [Fact]
    public async Task AServedListIsReadByTheSameParserAFileIs()
    {
        var result = await Fetch(HttpStatusCode.OK, Published());

        Assert.True(result.Found);
        Assert.Equal(ServerMapProblem.None, result.Problem);
        Assert.Equal("Fika night", result.List!.Name);
        Assert.Equal(7, result.List.Revision);
        Assert.Equal(2, result.List.Entries.Count);
        Assert.Single(result.List.Unresolved);
    }

    //
    // Origin decides what the app is allowed to do with it: a served list is read-only and editing
    // it forks, exactly like an imported one, and nothing else in the app has to special-case it.
    //
    [Fact]
    public async Task AServedListIsMarkedAsComingFromAServerAndIsNotEditable()
    {
        var result = await Fetch(HttpStatusCode.OK, Published());

        Assert.Equal(ModListOrigin.Server, result.List!.Origin);
        Assert.False(result.List.IsEditable);
    }

    //
    // Where you got it beats who wrote it. The export above names an author; for a served list the
    // address is what the user recognises and can go back to, and a stranger's name against a list
    // your own server hands you is worse than useless.
    //
    [Fact]
    public async Task TheSourceIsTheServerAddressNotTheAuthorWhoExportedIt()
    {
        var result = await Fetch(HttpStatusCode.OK, Published());

        Assert.Equal("192.168.1.111:6969", result.List!.Source);
    }

    //
    // A 404 here means the server runs the mod and publishes nothing - a normal thing for a server
    // to say. It only means "wrong address" on /hello, which is why the two have separate problems.
    //
    [Fact]
    public async Task NoListPublishedIsItsOwnAnswerNotAFailure()
    {
        var result = await Fetch(HttpStatusCode.NotFound, """{ "error": "no list" }""");

        Assert.False(result.Found);
        Assert.Equal(ServerMapProblem.NoList, result.Problem);
        Assert.Equal(404, result.StatusCode);
    }

    // A list this build cannot read is named as such, in the parser's own words, rather than coming
    // back as a bare failure the user cannot act on.
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "schemaVersion": 1, "app": "x" }""")]
    [InlineData("""{ "schemaVersion": 99, "list": { "id": "0f8fad5b-d9cb-469f-a165-70867728950e", "name": "x" } }""")]
    public async Task AListThatWillNotParseSaysWhy(string body)
    {
        var result = await Fetch(HttpStatusCode.OK, body);

        Assert.False(result.Found);
        Assert.Equal(ServerMapProblem.ListUnreadable, result.Problem);
        Assert.False(string.IsNullOrWhiteSpace(result.ParseError));
    }

    [Fact]
    public async Task AServerErrorIsNotMistakenForNoList()
    {
        var result = await Fetch(HttpStatusCode.InternalServerError, "");

        Assert.Equal(ServerMapProblem.Failed, result.Problem);
        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    public async Task AnUnreachableServerIsReportedNotThrown()
    {
        using var client = ServerMapClient.TryCreate(Endpoint, new ListThrowingHandler())!;

        var result = await client.ListAsync();

        Assert.False(result.Found);
        Assert.Equal(ServerMapProblem.Unreachable, result.Problem);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task AsksForTheListRoute()
    {
        var handler = new ListStubHandler(HttpStatusCode.OK, Published());
        using var client = ServerMapClient.TryCreate(Endpoint, handler)!;

        await client.ListAsync();

        Assert.Equal("https://192.168.1.111:6969/tcfservermap/list", handler.LastRequestUri?.ToString());
    }

    //
    // The handshake carries the revision so the list is only fetched when it moves. This is the
    // round trip that has to hold for that to be safe: the number on /hello is the number on the
    // list /list returns.
    //
    [Fact]
    public async Task TheRevisionOnTheHandshakeIsTheRevisionOnTheList()
    {
        using var helloClient = ServerMapClient.TryCreate(
            Endpoint, new ListStubHandler(HttpStatusCode.OK,
                """{ "protocol": 1, "hasList": true, "listRevision": 7, "listName": "Fika night" }"""))!;

        var hello = await helloClient.HelloAsync();
        var list = await Fetch(HttpStatusCode.OK, Published());

        Assert.Equal(hello.Hello!.ListRevision, list.List!.Revision);
        Assert.Equal(hello.Hello.ListName, list.List.Name);
    }

    private sealed class ListStubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ListThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("no route to host");
    }
}

//
// The shared key, from the client's side. The server's own generation and comparison live in the
// payload (ServerMapKey) and are exercised by a harness; what matters here is that the key travels,
// and that a refusal is turned into the right thing to tell the user.
//
public class ServerMapKeyTests
{
    private const string Key = "K7Q2-9FJM-3XBA-TW4N-P6HD-R2VC";

    private static ServerMapEndpoint Endpoint(string? key = null) =>
        new("192.168.1.111", 6969, PinnedThumbprint: null, SharedKey: key);

    [Fact]
    public void AnEndpointKnowsWhetherItHasAKey()
    {
        Assert.False(Endpoint().HasKey);
        Assert.False(Endpoint("   ").HasKey);
        Assert.True(Endpoint(Key).HasKey);
    }

    // Never on the handshake, which does not need it; on the list once the server has answered it.
    [Fact]
    public async Task TheKeyIsSentOnceTheServerHasAnsweredTheHandshake()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{ "protocol": 1 }""");
        using var client = ServerMapClient.TryCreate(Endpoint(Key), handler)!;

        await client.HelloAsync();
        await client.ListAsync();

        Assert.Equal([null, Key], handler.SeenKeys);
    }

    // A list asked for straight away is preceded by the handshake, so the key still goes only to a
    // server that answered as one.
    [Fact]
    public async Task AListAskedForFirst_ShakesHandsBeforeSendingTheKey()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{ "protocol": 1 }""");
        using var client = ServerMapClient.TryCreate(Endpoint(Key), handler)!;

        await client.ListAsync();

        Assert.Equal([null, Key], handler.SeenKeys);
    }

    // Something at that address that is not a server map is never handed the key.
    [Fact]
    public async Task TheKeyIsNeverSentToSomethingThatIsNotAServerMap()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound, "<html>not us</html>");
        using var client = ServerMapClient.TryCreate(Endpoint(Key), handler)!;

        await client.HelloAsync();
        await client.ListAsync();

        Assert.All(handler.SeenKeys, k => Assert.Null(k));
    }

    // A pinned server is known before any handshake: TLS checks the pin before a request goes out.
    [Fact]
    public async Task APinnedServerGetsTheKeyWithoutAHandshake()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{ "protocol": 1 }""");
        using var client = ServerMapClient.TryCreate(
            new ServerMapEndpoint("192.168.1.111", 6969, PinnedThumbprint: "AB12", SharedKey: Key), handler)!;

        await client.ListAsync();

        Assert.Equal([Key], handler.SeenKeys);
    }

    [Fact]
    public async Task NoHeaderIsSentWhenNoKeyIsSet()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{ "protocol": 1 }""");
        using var client = ServerMapClient.TryCreate(Endpoint(), handler)!;

        await client.HelloAsync();

        Assert.Single(handler.SeenKeys);
        Assert.Null(handler.SeenKeys[0]);
    }

    // Whitespace around a pasted key is the most likely way one arrives, and it must not be the
    // reason a connection fails.
    [Fact]
    public async Task APastedKeyIsTrimmed()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, """{ "protocol": 1 }""");
        using var client = ServerMapClient.TryCreate(Endpoint($"  {Key}\t"), handler)!;

        await client.HelloAsync();
        await client.ListAsync();

        Assert.Equal(Key, handler.SeenKeys[1]);
    }

    //
    // One status code, two situations, two different next actions: ask someone for a key, versus the
    // key you were given is wrong. Collapsing them would leave the user with nothing to do.
    //
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARefusalWithNoKeySetMeansOneIsNeeded(HttpStatusCode status)
    {
        using var client = ServerMapClient.TryCreate(Endpoint(), new CapturingHandler(status, ""))!;

        var result = await client.ListAsync();

        Assert.False(result.Found);
        Assert.Equal(ServerMapProblem.KeyRequired, result.Problem);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ARefusalWithAKeySetMeansTheKeyIsWrong(HttpStatusCode status)
    {
        using var client = ServerMapClient.TryCreate(Endpoint(Key), new CapturingHandler(status, ""))!;

        var result = await client.ListAsync();

        Assert.False(result.Found);
        Assert.Equal(ServerMapProblem.KeyRejected, result.Problem);
    }

    // The handshake stays open, so it must never come back as a key problem - that is what lets a
    // client tell "wrong address" from "right address, no key".
    [Fact]
    public async Task TheHandshakeIsNeverAKeyProblem()
    {
        using var client = ServerMapClient.TryCreate(
            Endpoint(), new CapturingHandler(HttpStatusCode.Unauthorized, ""))!;

        var probe = await client.HelloAsync();

        Assert.Equal(ServerMapProblem.NotServerMap, probe.Problem);
    }

    [Fact]
    public void SettingsCarryTheKeyOntoTheEndpointAndBack()
    {
        var settings = new ServerMapSettings { Host = "192.168.1.111", SharedKey = Key };

        Assert.True(settings.HasKey);
        Assert.Equal(Key, settings.ToEndpoint().SharedKey);

        var restored = JsonSerializer.Deserialize<AppSettings>(
            JsonSerializer.Serialize(new AppSettings { ServerMap = settings }))!;

        Assert.Equal(Key, restored.ServerMap.SharedKey);
    }

    private sealed class CapturingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<string?> SeenKeys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SeenKeys.Add(request.Headers.TryGetValues(ServerMapEndpoint.KeyHeaderName, out var values)
                ? values.FirstOrDefault()
                : null);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}

//
// Reading the key of a server running on this machine. The operator generated it by starting their
// own server and already owns the file; making them go and find it to paste back into the window in
// front of them is busywork, and this is what avoids it.
//
public class ServerMapKeyFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smkey-" + Guid.NewGuid().ToString("N"));

    private const string Key = "D7SM-YSW3-PQJV-KM5W-CQQF-VHD5";

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A temp folder left behind is not worth failing a test run over.
        }
    }

    private string WriteKey(string contents, params string[] under)
    {
        var directory = Path.Combine([_root, .. under, "TCFModManager", "ServerMap", "config"]);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, ServerMapKeyFile.FileName);
        File.WriteAllText(path, contents);
        return path;
    }

    private string Dir(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void FindsTheKeyBesideTheInstallRoot()
    {
        var expected = WriteKey(Key);

        Assert.True(ServerMapKeyFile.TryFind(_root, out var found, appDirectory: Dir("nothing-here")));
        Assert.Equal(expected, found);
    }

    //
    // The app is normally at <SPT root>\TCFModManager\, so the search has to climb: from the app's
    // own folder the config folder is a sibling, not a child.
    //
    [Fact]
    public void ClimbsFromAFolderInsideTheInstall()
    {
        WriteKey(Key);
        var deep = Dir("SPT_Runtime", "user", "mods");

        Assert.True(ServerMapKeyFile.TryFind(deep, out _, appDirectory: Dir("nothing-here")));
    }

    [Fact]
    public void FindsNothingOnAMachineThatIsNotTheServer()
    {
        var nowhere = Dir("nothing-here");

        Assert.False(ServerMapKeyFile.TryFind(Dir("elsewhere"), out _, appDirectory: nowhere));
        Assert.False(ServerMapKeyFile.TryFind(null, out _, appDirectory: nowhere));
        Assert.False(ServerMapKeyFile.TryFind("   ", out _, appDirectory: nowhere));
    }

    //
    // Read exactly as written. The server normalises dashes and case away when comparing, so
    // reformatting here would only make what the app shows differ from what the operator sees in
    // the file and pastes to a friend.
    //
    [Theory]
    [InlineData("D7SM-YSW3-PQJV-KM5W-CQQF-VHD5\r\n")]
    [InlineData("  D7SM-YSW3-PQJV-KM5W-CQQF-VHD5  ")]
    public void ReadsTheKeyVerbatimApartFromSurroundingWhitespace(string contents)
    {
        var path = WriteKey(contents);

        Assert.True(ServerMapKeyFile.TryRead(path, out var key));
        Assert.Equal(Key, key);
    }

    // An empty file is not a key of "" - that is the value that would match nothing and confuse
    // everything downstream.
    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public void AnEmptyKeyFileIsNotAKey(string contents)
    {
        var path = WriteKey(contents);

        Assert.False(ServerMapKeyFile.TryRead(path, out _));
    }

    [Fact]
    public void TryReadLocalPrefersTheAppsOwnFolderThenTheConfiguredInstall()
    {
        WriteKey(Key);

        // From a folder that knows nothing, the configured install path is what finds it.
        Assert.Equal(Key, ServerMapKeyFile.TryReadLocal(_root, appDirectory: Dir("unrelated")));

        // And from the app's own folder, without any install path configured at all.
        Assert.Equal(Key, ServerMapKeyFile.TryReadLocal(null, appDirectory: Path.Combine(_root, "TCFModManager")));
    }

    [Fact]
    public void TryReadLocalIsNullWhenThisMachineRunsNoServer() =>
        Assert.Null(ServerMapKeyFile.TryReadLocal(Dir("no-server"), appDirectory: Dir("no-server")));
}

//
// Computed properties were being written into every file this app produces, because
// System.Text.Json serialises get-only properties by default. Nothing ever read them back.
//
public class SerializedShapeTests
{
    [Fact]
    public void SettingsDoNotCarryComputedProperties()
    {
        var json = JsonSerializer.Serialize(new AppSettings
        {
            ServerMap = new ServerMapSettings { Host = "127.0.0.1", SharedKey = "AABB" },
        });

        Assert.DoesNotContain("IsConfigured", json);
        Assert.DoesNotContain("HasKey", json);
        Assert.Contains("SharedKey", json);
    }

    //
    // "Unresolved" was the expensive one: an IEnumerable of entries, so every exported list carried
    // a second full copy of each unresolved entry. "IsEditable" was the misleading one - a served
    // list said true while the app reading it correctly treats it as read-only.
    //
    [Fact]
    public void AnExportedListDoesNotCarryComputedProperties()
    {
        var json = ModListFile.Write(new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Fika night",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Entries =
            {
                new ModListEntry { Name = "SAIN", ModId = 2426, VersionId = 5 },
                new ModListEntry { Name = "A GitHub-only mod" },
            },
        });

        Assert.DoesNotContain("IsEditable", json);
        Assert.DoesNotContain("Unresolved", json);
        Assert.DoesNotContain("IsPinned", json);
        Assert.DoesNotContain("IsResolved", json);
    }

    // Dropping them is not a schema change: an older app never set them either, so a file without
    // them means exactly what a file with them meant.
    [Fact]
    public void AListStillRoundTripsWithoutThem()
    {
        var original = new ModList
        {
            Id = Guid.NewGuid(),
            Name = "Fika night",
            Revision = 4,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Entries = { new ModListEntry { Name = "SAIN", ModId = 2426, VersionId = 5 } },
        };

        var restored = ModListFile.Read(ModListFile.Write(original)).List!;

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(4, restored.Revision);
        Assert.True(restored.Entries[0].IsPinned);
        Assert.False(restored.IsEditable);
    }
}

//
// Where the operator's key and published list live.
//
// They moved in v1.12.0 from TCFModManager\ServerMap\config\ to TCFModManager\Data\ServerMap\. The
// old folder looked like part of the mod, so a hand-deploy that replaced TCFModManager\ took the
// key with it and every player holding that key was locked out with nothing to say why. Data\ is
// the folder every deploy already knows to keep.
//
// The server mod derives the same path independently (PublishedModList.ConfigDirectory, two levels
// up from its payload folder) because it cannot reference this project. PreferredIsUnderData below
// pins the shape both sides have to agree on - if one moves, that test is the tripwire.
//
public class ServerMapConfigFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smcfg-" + Guid.NewGuid().ToString("N"));

    private const string Key = "D7SM-YSW3-PQJV-KM5W-CQQF-VHD5";

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // A temp folder left behind is not worth failing a test run over.
        }
    }

    private string Current() => Make("TCFModManager", "Data", "ServerMap");

    private string Legacy() => Make("TCFModManager", "ServerMap", "config");

    private string Make(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Elsewhere() => Make("not-a-server");

    [Fact]
    public void PreferredIsUnderData()
    {
        Assert.Equal(
            Path.Combine(_root, "TCFModManager", "Data", "ServerMap"),
            ServerMapConfigFolder.Preferred(_root));
    }

    [Fact]
    public void FindsTheDataFolder()
    {
        var expected = Current();

        Assert.True(ServerMapConfigFolder.TryFind(_root, out var found, appDirectory: Elsewhere()));
        Assert.Equal(expected, found);
    }

    //
    // An install that has not been migrated yet keeps working. Without this the server would find no
    // key, mint a second one, and lock out everybody already holding the first.
    //
    [Fact]
    public void FallsBackToTheLegacyFolder()
    {
        var expected = Legacy();

        Assert.True(ServerMapConfigFolder.TryFind(_root, out var found, appDirectory: Elsewhere()));
        Assert.Equal(expected, found);
    }

    //
    // A half-migrated install has both. The answer has to be the one the app writes to, or the two
    // halves read different files and the server serves a list nobody is editing.
    //
    [Fact]
    public void PrefersTheDataFolderWhenBothExist()
    {
        var expected = Current();
        Legacy();

        Assert.True(ServerMapConfigFolder.TryFind(_root, out var found, appDirectory: Elsewhere()));
        Assert.Equal(expected, found);
    }

    [Fact]
    public void MigrationMovesTheKeyAndTheList()
    {
        var legacy = Legacy();
        File.WriteAllText(Path.Combine(legacy, "servermap-key.txt"), Key);
        File.WriteAllText(Path.Combine(legacy, "published.tcfmodlist"), "{}");

        ServerMapConfigFolder.MigrateLegacyFolder(_root, appDirectory: Elsewhere());

        var moved = Path.Combine(_root, "TCFModManager", "Data", "ServerMap");
        Assert.Equal(Key, File.ReadAllText(Path.Combine(moved, "servermap-key.txt")));
        Assert.True(File.Exists(Path.Combine(moved, "published.tcfmodlist")));

        Assert.False(File.Exists(Path.Combine(legacy, "servermap-key.txt")));
    }

    //
    // Never over the top of one already there. Two of these means somebody moved files by hand, and
    // the one in the new place is the one both halves read - overwriting it would swap the running
    // server's key for an older one nobody is using.
    //
    [Fact]
    public void MigrationKeepsTheFileAlreadyInTheNewPlace()
    {
        var legacy = Legacy();
        File.WriteAllText(Path.Combine(legacy, "servermap-key.txt"), "OLD1-OLD1-OLD1-OLD1-OLD1-OLD1");

        var current = Current();
        File.WriteAllText(Path.Combine(current, "servermap-key.txt"), Key);

        ServerMapConfigFolder.MigrateLegacyFolder(_root, appDirectory: Elsewhere());

        Assert.Equal(Key, File.ReadAllText(Path.Combine(current, "servermap-key.txt")));
    }

    //
    // Only the two files this app knows the meaning of. The legacy folder sits beside the payload
    // and may hold anything; carrying the tree wholesale would take things that are not ours.
    //
    [Fact]
    public void MigrationLeavesAnythingElseBehind()
    {
        var legacy = Legacy();
        File.WriteAllText(Path.Combine(legacy, "servermap-key.txt"), Key);
        File.WriteAllText(Path.Combine(legacy, "notes.txt"), "someone else's");

        ServerMapConfigFolder.MigrateLegacyFolder(_root, appDirectory: Elsewhere());

        Assert.True(File.Exists(Path.Combine(legacy, "notes.txt")));
    }

    [Fact]
    public void MigrationDoesNothingOnAMachineThatIsNotTheServer()
    {
        ServerMapConfigFolder.MigrateLegacyFolder(_root, appDirectory: Elsewhere());

        Assert.False(Directory.Exists(Path.Combine(_root, "TCFModManager", "Data", "ServerMap")));
    }
}
