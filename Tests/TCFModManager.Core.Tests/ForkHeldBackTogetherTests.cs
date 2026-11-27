using System.Net;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork (1.4.9), after TCF Mod Manager's HeldBackGroupTests (71708aa, MIT): held-back updates that
// can only go together, as ISB Aishi and ISB Special Forces on SPT 4.1.6.
public class ForkHeldBackTogetherTests
{
    private const int Aishi = 2478;
    private const int SpecialForces = 2485;
    private const int BigBrain = 902;

    private static readonly (int, string)[] Installed = [(Aishi, "2.0.6"), (SpecialForces, "2.0.3"), (BigBrain, "1.5.0")];

    private static string Updates(string updates = "", string blocked = "") => $$"""
        {"success":true,"data":{"spt_version":"4.1.6","updates":[{{updates}}],"blocked_updates":[{{blocked}}],"up_to_date":[],"incompatible_with_spt":[]} }
        """;

    private static string Update(int modId, string name, string from, string to) => $$"""
        {"current_version":{"id":1,"mod_id":{{modId}},"name":"{{name}}","version":"{{from}}"},"recommended_version":{"id":2,"version":"{{to}}","spt_versions":["4.1.6"]},"update_reason":"newer_version_available"}
        """;

    private const string AishiBlockedBySpecialForces = """
        {"current_version":{"id":14699,"mod_id":2478,"name":"ISB Aishi","version":"2.0.6"},
         "latest_version":{"id":16587,"version":"2.0.7","spt_versions":["4.1.6"]},
         "block_reason":"dependency_constraint_violation",
         "blocking_mods":[{"mod_id":2485,"mod_name":"ISB Special Forces","current_version":"2.0.3","constraint":"2.0.6","incompatible_with":"2.0.7"}]}
        """;

    private const string SpecialForcesChained = """
        {"current_version":{"id":15010,"mod_id":2485,"name":"ISB Special Forces","version":"2.0.3"},
         "latest_version":{"id":16588,"version":"2.0.4","spt_versions":["4.1.6"]},
         "block_reason":"chain_dependency_conflict"}
        """;

    private const string SpecialForcesTree = """
        {"success":true,"data":{"2485:2.0.4":[{"id":902,"name":"BigBrain","dependencies":[]},{"id":2478,"name":"ISB Aishi","dependencies":[]}]}}
        """;

    // Answers as sp-mod.com does (read again on 2026-11-27), by the versions asked about.
    private static string Live(Uri uri, bool specialForcesGoes = true)
    {
        if (uri.AbsolutePath.EndsWith("/mods/dependencies", StringComparison.Ordinal)) return SpecialForcesTree;

        var mods = RoutingHandler.Mods(uri);
        return (mods[Aishi], mods[SpecialForces]) switch
        {
            ("2.0.6", "2.0.3") => Updates(blocked: AishiBlockedBySpecialForces + "," + SpecialForcesChained),
            ("2.0.6", "2.0.4") => Updates(updates: Update(Aishi, "ISB Aishi", "2.0.6", "2.0.7")),
            ("2.0.7", "2.0.3") => specialForcesGoes
                ? Updates(updates: Update(SpecialForces, "ISB Special Forces", "2.0.3", "2.0.4"))
                : Updates(blocked: SpecialForcesChained),
            _ => Updates(),
        };
    }

    private static List<ModBlockedUpdateEntry> Blocked(string json) =>
        JsonSerializer.Deserialize<List<ModBlockedUpdateEntry>>(
            "[" + json + "]", new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    [Fact]
    public async Task Two_mods_pinned_to_each_other_go_together()
    {
        var handler = new RoutingHandler(uri => Live(uri));
        using var client = new SpModApiClient(new HttpClient(handler));

        var together = await new UpdateCheckService(client).HeldBackTogetherAsync(
            Installed, Blocked(AishiBlockedBySpecialForces + "," + SpecialForcesChained), "4.1.6");

        Assert.Equal([SpecialForces], together[Aishi]);
        Assert.Equal([Aishi], together[SpecialForces]);

        // The chained one's tree, then one confirmation per member.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(handler.Requests, r => r.AbsolutePath.EndsWith("/mods/dependencies") && RoutingHandler.Mods(r).Keys.SequenceEqual([SpecialForces]));
    }

    [Fact]
    public async Task A_member_sp_mod_will_not_confirm_leaves_both_held_back()
    {
        var handler = new RoutingHandler(uri => Live(uri, specialForcesGoes: false));
        using var client = new SpModApiClient(new HttpClient(handler));

        var together = await new UpdateCheckService(client).HeldBackTogetherAsync(
            Installed, Blocked(AishiBlockedBySpecialForces + "," + SpecialForcesChained), "4.1.6");

        Assert.Empty(together);
    }

    [Fact]
    public async Task Held_back_by_a_mod_that_is_not_moving_asks_nothing()
    {
        // Black and Blue pins CommonLib ~2.0.24 and has no update of its own.
        const string blocked = """
            {"current_version":{"id":2001,"mod_id":1500,"name":"CommonLib","version":"2.0.24"},
             "latest_version":{"id":2050,"version":"3.0.6","spt_versions":["4.1.6"]},
             "block_reason":"dependency_constraint_violation",
             "blocking_mods":[{"mod_id":1600,"mod_name":"Black and Blue","current_version":"1.0.0","constraint":"~2.0.24","incompatible_with":"3.0.6"}]},
            {"current_version":{"id":3001,"mod_id":1700,"name":"Uses CommonLib","version":"1.0.0"},
             "latest_version":{"id":3002,"version":"2.0.0","spt_versions":["4.1.6"]},
             "block_reason":"chain_dependency_conflict"}
            """;
        var handler = new RoutingHandler(_ => Updates());
        using var client = new SpModApiClient(new HttpClient(handler));

        var together = await new UpdateCheckService(client).HeldBackTogetherAsync(
            [(1500, "2.0.24"), (1600, "1.0.0"), (1700, "1.0.0")], Blocked(blocked), "4.1.6");

        Assert.Empty(together);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_failed_check_groups_nothing()
    {
        var handler = new RoutingHandler(uri => uri.AbsolutePath.EndsWith("/mods/dependencies", StringComparison.Ordinal)
            ? throw new HttpRequestException("offline")
            : Live(uri));
        using var client = new SpModApiClient(new HttpClient(handler));

        var together = await new UpdateCheckService(client).HeldBackTogetherAsync(
            Installed, Blocked(AishiBlockedBySpecialForces + "," + SpecialForcesChained), "4.1.6");

        Assert.Empty(together);
    }
}

// Answers each request by its URI and keeps every one it saw.
internal sealed class RoutingHandler(Func<Uri, string> answer) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    // The "mods" parameter as id -> version.
    public static Dictionary<int, string> Mods(Uri uri)
    {
        var query = Uri.UnescapeDataString(uri.Query.TrimStart('?'));
        var mods = query.Split('&').First(p => p.StartsWith("mods=", StringComparison.Ordinal))["mods=".Length..];
        return mods.Split(',')
            .Select(m => m.Split(':'))
            .ToDictionary(p => int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), p => p[1]);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(answer(request.RequestUri!), Encoding.UTF8, "application/json"),
        });
    }
}
