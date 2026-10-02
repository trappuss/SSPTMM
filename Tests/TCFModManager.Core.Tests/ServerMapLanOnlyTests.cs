using System.Net;
using System.Text;
using TCFModManager.Core.ServerMap;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerMapLanOnlyTests
{
    private static readonly ServerMapEndpoint Keyed = new("86.140.28.231", 6969, "AABB", "KEY1-KEY2");

    private const string Refusal =
        """{"protocol":1,"error":"This server only answers machines on its own network.","reason":"lanOnly"}""";

    private static ServerMapClient Client(HttpStatusCode status, string body) =>
        ServerMapClient.TryCreate(Keyed, new Canned(status, body))!;

    [Fact]
    public async Task TheHandshakeSaysLanOnlyRatherThanNotAServerMap()
    {
        using var client = Client(HttpStatusCode.Forbidden, Refusal);
        Assert.Equal(ServerMapProblem.LanOnly, (await client.HelloAsync()).Problem);
    }

    [Fact]
    public async Task EveryKeyedRouteSaysLanOnlyRatherThanAKeyProblem()
    {
        using var client = Client(HttpStatusCode.Forbidden, Refusal);

        Assert.Equal(ServerMapProblem.LanOnly, (await client.ListAsync()).Problem);
        Assert.Equal(ServerMapProblem.LanOnly, (await client.ClientsAsync(null)).Problem);
        Assert.Equal(ServerMapProblem.LanOnly,
            (await client.ReportAsync(new MachineReport { ClientId = Guid.NewGuid().ToString(), InventoryHash = "x" })).Problem);
        Assert.Equal(ServerMapProblem.LanOnly, await client.WithdrawAsync(Guid.NewGuid().ToString()));
    }

    // A 403 that is not a LAN-only refusal still means what it meant before.
    [Fact]
    public async Task APlainForbiddenIsStillAKeyProblem()
    {
        using var client = Client(HttpStatusCode.Forbidden, """{"error":"nope"}""");

        Assert.Equal(ServerMapProblem.KeyRejected, (await client.ListAsync()).Problem);
        Assert.Equal(ServerMapProblem.NotServerMap, (await client.HelloAsync()).Problem);
    }

    [Fact]
    public async Task ABodyThatIsNotJsonIsNotALanOnlyRefusal()
    {
        using var client = Client(HttpStatusCode.Forbidden, "<html>forbidden</html>");
        Assert.Equal(ServerMapProblem.KeyRejected, (await client.ListAsync()).Problem);
    }

    // Stopping the reporter cancels its report. That is the caller walking away, not the server
    // being unreachable, so it surfaces as a cancellation rather than as a failed report.
    [Fact]
    public async Task ACancelledReportThrowsRatherThanReadingAsUnreachable()
    {
        using var client = ServerMapClient.TryCreate(Keyed, new Hangs())!;
        using var cts = new CancellationTokenSource();

        var report = client.ReportAsync(new MachineReport { ClientId = Guid.NewGuid().ToString(), InventoryHash = "x" }, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => report);
    }

    // A server that never answers still reads as unreachable: the HttpClient's own timeout is not
    // the caller cancelling.
    [Fact]
    public async Task ATimeoutStillReadsAsUnreachable()
    {
        using var client = ServerMapClient.TryCreate(Keyed, new Hangs(), TimeSpan.FromMilliseconds(100))!;

        var result = await client.ReportAsync(new MachineReport { ClientId = Guid.NewGuid().ToString(), InventoryHash = "x" });

        Assert.Equal(ServerMapProblem.Unreachable, result.Problem);
    }

    private sealed class Hangs : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class Canned(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

public class ServerMapServerSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tcfmm-lan-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, ServerMapServerSettings.FileName);

    public ServerMapServerSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void NoFileMeansOff() => Assert.False(ServerMapServerSettings.ReadLanOnly(File_));

    [Fact]
    public void SwitchingItOnAndOffRoundTrips()
    {
        Assert.True(ServerMapServerSettings.TryWriteLanOnly(File_, true));
        Assert.True(ServerMapServerSettings.ReadLanOnly(File_));

        Assert.True(ServerMapServerSettings.TryWriteLanOnly(File_, false));
        Assert.False(ServerMapServerSettings.ReadLanOnly(File_));
    }

    // Whatever else is in the file - a later setting, a hand edit - survives the switch.
    [Fact]
    public void OtherSettingsSurviveTheSwitch()
    {
        File.WriteAllText(File_, """{ "LanOnly": false, "somethingLater": 42 }""");

        ServerMapServerSettings.TryWriteLanOnly(File_, true);

        var text = File.ReadAllText(File_);
        Assert.Contains("\"somethingLater\": 42", text);
        Assert.Contains("\"lanOnly\": true", text);
        Assert.DoesNotContain("\"LanOnly\"", text);
    }

    [Fact]
    public void ABrokenHandEditIsReplacedRatherThanBlockingTheSwitch()
    {
        File.WriteAllText(File_, "{ lanOnly: tru");

        Assert.False(ServerMapServerSettings.ReadLanOnly(File_));
        Assert.True(ServerMapServerSettings.TryWriteLanOnly(File_, true));
        Assert.True(ServerMapServerSettings.ReadLanOnly(File_));
    }
}
