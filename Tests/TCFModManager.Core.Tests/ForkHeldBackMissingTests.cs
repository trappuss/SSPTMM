using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.9, hotfix): an update held back for a mod it needs, as ECOT 2.0.2 on SPT 4.1.6 - it needs
// WTT - CommonLib 3.0.8, which sp-mod.com lists for SPT 4.1.3 only (read on 2026-11-27).
public class ForkHeldBackMissingTests
{
    private const int Ecot = 2195;

    private const string EcotMissing = """
        {"current_version":{"id":15704,"mod_id":2195,"name":"ECOT","version":"2.0.1"},
         "latest_version":{"id":17141,"version":"2.0.2","spt_versions":["4.1.6"]},
         "block_reason":"missing_dependency"}
        """;

    private const string EcotTree = """
        {"success":true,"data":{"2195:2.0.2":[
          {"id":1263,"name":"Epic's All in One","latest_compatible_version":{"id":17118,"version":"5.0.4"},"conflict":false,"dependencies":[
            {"id":2310,"name":"WTT - CommonLib","latest_compatible_version":{"id":16720,"version":"3.0.7"},"conflict":true,"dependencies":[]}]},
          {"id":2310,"name":"WTT - CommonLib","latest_compatible_version":null,"conflict":true,"dependencies":[]}]}}
        """;

    private static List<ModBlockedUpdateEntry> Blocked(string json) =>
        JsonSerializer.Deserialize<List<ModBlockedUpdateEntry>>(
            "[" + json + "]", new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    [Fact]
    public async Task The_mod_with_no_fitting_version_is_named()
    {
        var handler = new RoutingHandler(_ => EcotTree);
        using var client = new SpModApiClient(new HttpClient(handler));

        var missing = await new UpdateCheckService(client).MissingForAsync(Blocked(EcotMissing), "4.1.6");

        Assert.Equal(["WTT - CommonLib"], missing[Ecot]);

        // Asked about the release held back, not the one installed.
        Assert.Equal("2.0.2", RoutingHandler.Mods(Assert.Single(handler.Requests))[Ecot]);
    }

    [Fact]
    public async Task An_update_held_back_for_another_reason_asks_nothing()
    {
        var handler = new RoutingHandler(_ => EcotTree);
        using var client = new SpModApiClient(new HttpClient(handler));

        var missing = await new UpdateCheckService(client).MissingForAsync(
            Blocked(EcotMissing.Replace("missing_dependency", "chain_dependency_conflict", StringComparison.Ordinal)), "4.1.6");

        Assert.Empty(missing);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_failed_read_names_nothing()
    {
        var handler = new RoutingHandler(_ => throw new HttpRequestException("offline"));
        using var client = new SpModApiClient(new HttpClient(handler));

        Assert.Empty(await new UpdateCheckService(client).MissingForAsync(Blocked(EcotMissing), "4.1.6"));
    }

    [Fact]
    public void A_held_back_mod_has_its_listing_read_again()
    {
        var check = new InstalledUpdateCheck([7], [8]) { Blocked = Blocked(EcotMissing) };

        Assert.Equal([7, 8, Ecot], check.ToRefetch);
    }
}
