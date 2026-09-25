using System.Net;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

// 
// These tests use JSON fixtures captured from live calls against https://sp-mod.com on 2026-08-13,
// rather than the API doc's generated examples - a couple of fields (License.short_name,
// Category.name/color_class) only exist in the docs' fake data and are never actually returned,
// so asserting against real responses is what catches that.
// 
public class SpModApiClientTests
{
    private static SpModApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler));

    [Fact]
    public async Task GetModsAsync_BuildsExpectedQueryString()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"success":true,"data":[],"links":{},"meta":{}}""");
        using var client = CreateClient(handler);

        await client.GetModsAsync(new ModsQuery { SearchQuery = "raid time", FilterFeatured = true, PerPage = 25 });

        Assert.NotNull(handler.LastRequestUri);
        var query = handler.LastRequestUri!.Query;
        Assert.Contains("query=raid%20time", query);
        Assert.Contains("filter%5Bfeatured%5D=true", query);
        Assert.Contains("per_page=25", query);
    }

    // Live /mods/updates, 2026-09-25: CommonLib's update is held back because Black and Blue 1.0.0
    // needs CommonLib ~2.0.24, and Black and Blue's own update is held back in turn.
    private const string UpdatesFixture = """
        {"success":true,"data":{"spt_version":"4.1.6","updates":[],"blocked_updates":[{"current_version":{"id":14916,"mod_id":2310,"guid":"com.wtt.commonlib","name":"WTT - CommonLib","version":"3.0.5"},"latest_version":{"id":14931,"version":"3.0.6","spt_versions":["4.1.6","4.1.5","4.1.4","4.1.3"]},"block_reason":"dependency_constraint_violation","blocking_mods":[{"mod_id":3052,"mod_guid":"com.c11.blackandblue","mod_name":"Black and Blue","current_version":"1.0.0","constraint":"~2.0.24","incompatible_with":"3.0.6"}]},{"current_version":{"id":15571,"mod_id":3052,"guid":"com.c11.blackandblue","name":"Black and Blue","version":"1.0.0"},"latest_version":{"id":15572,"version":"3.0.0","spt_versions":["4.1.6"]},"block_reason":"chain_dependency_conflict"}],"up_to_date":[],"incompatible_with_spt":[]}}
        """;

    [Fact]
    public async Task GetModUpdatesAsync_ReadsHeldBackUpdatesAndWhatBlocksThem()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, UpdatesFixture);
        using var client = CreateClient(handler);

        var result = await client.GetModUpdatesAsync("2310:3.0.5,3052:1.0.0", "4.1.6");

        Assert.Contains("mods=2310%3A3.0.5%2C3052%3A1.0.0", handler.LastRequestUri!.Query);
        var commonLib = Assert.Single(result.BlockedUpdates, b => b.CurrentVersion?.ModId == 2310);
        Assert.Equal("3.0.6", commonLib.LatestVersion?.Version);
        Assert.Equal("dependency_constraint_violation", commonLib.BlockReason);
        var blocker = Assert.Single(commonLib.BlockingMods);
        Assert.Equal("Black and Blue", blocker.ModName);
        Assert.Equal("~2.0.24", blocker.Constraint);

        var chained = Assert.Single(result.BlockedUpdates, b => b.CurrentVersion?.ModId == 3052);
        Assert.Equal("chain_dependency_conflict", chained.BlockReason);
        Assert.Empty(chained.BlockingMods);
    }

    // Live /mod/791/versions/14862/file-tree, 2026-09-25 (SAIN 4.5.0).
    private const string FileTreeFixture = """
        {"success":true,"data":{"verified_at":"2026-08-20T17:36:45.000000Z","file_count":6,"truncated":false,"files":["BepInEx/plugins/SAIN/SAIN.Preset.Shared.dll","BepInEx/plugins/SAIN/SAIN.dll","SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/Data/NicknamePersonalities.json","SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/SAIN.Preset.Shared.dll","SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/SAINServerMod.dll","SPT_Runtime/user/mods/Solarint-SAIN-ServerMod/wwwroot/css/sain.css"]}}
        """;

    [Fact]
    public async Task GetModVersionFileTreeAsync_ReadsTheVerifiedFileList()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, FileTreeFixture);
        using var client = CreateClient(handler);

        var tree = await client.GetModVersionFileTreeAsync("791", "14862");

        Assert.EndsWith("/api/v0/mod/791/versions/14862/file-tree", handler.LastRequestUri!.AbsolutePath);
        Assert.NotNull(tree.VerifiedAt);
        Assert.Equal(tree.FileCount, tree.Files.Count);
        Assert.Contains(tree.Files, f => f.StartsWith("BepInEx/plugins/SAIN/", StringComparison.Ordinal));
        Assert.False(tree.Truncated);
    }

    [Fact]
    public async Task GetModsAsync_DeserializesRealListResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModsListFixture);
        using var client = CreateClient(handler);

        var result = await client.GetModsAsync();

        Assert.True(result.Success);
        Assert.Single(result.Data);
        var mod = result.Data[0];
        Assert.Equal(31, mod.Id);
        Assert.Equal("Scav Cat Trader Mod", mod.Name);
        Assert.Equal("scav-cat-trader-mod", mod.Slug);
        Assert.Equal(13548, mod.Downloads);
        Assert.Equal("DonutxLord", mod.Owner?.Name);
        Assert.Equal(1810, result.Meta?.LastPage);
    }

    [Fact]
    public async Task GetModAsync_DeserializesCategoryUsingTitleNotName()
    {
        // Live /mod/{id}?include=category returns {id, hub_id, title, slug, description} - not the
        // {id, name, slug, color_class} shape shown in some of the doc's generated examples.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDetailsFixture);
        using var client = CreateClient(handler);

        var mod = await client.GetModAsync("31", include: "versions,license,category");

        Assert.Equal("Traders", mod.Category?.Title);
        Assert.Equal("traders", mod.Category?.Slug);
    }

    [Fact]
    public async Task GetModAsync_DeserializesLicenseUsingNameAndLinkNotShortName()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDetailsFixture);
        using var client = CreateClient(handler);

        var mod = await client.GetModAsync("31", include: "versions,license,category");

        Assert.Equal("MIT License", mod.License?.Name);
        Assert.Equal("https://choosealicense.com/licenses/mit/", mod.License?.Link);
    }

    [Fact]
    public async Task GetModAsync_DeserializesEmbeddedVersionSummaries()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDetailsFixture);
        using var client = CreateClient(handler);

        var mod = await client.GetModAsync("31", include: "versions,license,category");

        Assert.NotNull(mod.Versions);
        Assert.Equal(2, mod.Versions!.Count);
        Assert.Equal("1.0.8", mod.Versions[0].Version);
    }

    [Fact]
    public async Task GetModUpdatesAsync_CategorizesRealResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModUpdatesFixture);
        using var client = CreateClient(handler);

        var result = await client.GetModUpdatesAsync("31:1.0.0", "4.0.10");

        Assert.Equal("4.0.10", result.SptVersion);
        Assert.Single(result.Updates);
        Assert.Empty(result.BlockedUpdates);
        Assert.Empty(result.UpToDate);
        Assert.Empty(result.IncompatibleWithSpt);

        var update = result.Updates[0];
        Assert.Equal(31, update.CurrentVersion?.ModId);
        Assert.Equal("1.0.0", update.CurrentVersion?.Version);
        Assert.Equal("1.0.8", update.RecommendedVersion?.Version);
        Assert.Equal("newer_version_available", update.UpdateReason);
    }

    //
    // Captured from https://sp-mod.com/api/v0/addon/123/versions?filter[version]=9.9.9 - a real
    // addon, a version nothing matches. Laravel's paginator has no first or last row to number on an
    // empty page and sends null for both, which a non-nullable int refuses outright:
    //
    //   The JSON value could not be converted to System.Int32. Path: $.meta.from
    //
    // That took down a 76-entry mod list apply whole, because ONE entry pinned a version that is no
    // longer published. The empty page is not an error - it is the answer.
    //
    [Fact]
    public async Task EmptyPage_HasNullFromAndTo_AndStillDeserializes()
    {
        const string emptyPage = """
            {"success":true,"data":[],"links":{"first":"https://sp-mod.com/api/v0/addon/123/versions?page=1","last":"https://sp-mod.com/api/v0/addon/123/versions?page=1","prev":null,"next":null},"meta":{"current_page":1,"from":null,"last_page":1,"path":"https://sp-mod.com/api/v0/addon/123/versions","per_page":5,"to":null,"total":0}}
            """;

        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, emptyPage);
        using var client = CreateClient(handler);

        var result = await client.GetAddonVersionsAsync("123", new AddonVersionsQuery { FilterVersion = "9.9.9" });

        Assert.True(result.Success);
        Assert.Empty(result.Data);
        Assert.Null(result.Meta?.From);
        Assert.Null(result.Meta?.To);
        Assert.Equal(0, result.Meta?.Total);
    }

    [Fact]
    public async Task GetModDependenciesAsync_KeyedByExactQueriedPair()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, ModDependenciesFixture);
        using var client = CreateClient(handler);

        var result = await client.GetModDependenciesAsync("31:1.0.8", "4.0.10");

        Assert.True(result.ContainsKey("31:1.0.8"));
        Assert.Empty(result["31:1.0.8"]);
    }

    [Fact]
    public async Task RateLimitedResponse_ThrowsWithRetryAfter()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.TooManyRequests,
            """{"success":false,"code":"RATE_LIMITED","message":"Too many requests. Retry after the number of seconds in the Retry-After header."}""",
            retryAfterSeconds: "30");
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SpModApiRateLimitedException>(() => client.GetModsAsync());

        Assert.Equal("RATE_LIMITED", exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task NotFoundResponse_ThrowsWithCodeAndMessage()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.NotFound,
            """{"success":false,"code":"NOT_FOUND","message":"Resource not found."}""");
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<SpModApiException>(() => client.GetModAsync("999999"));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("NOT_FOUND", exception.Code);
        Assert.Equal("Resource not found.", exception.Message);
    }

    // ---- Fixtures, captured from live https://sp-mod.com responses on 2026-08-13 -----------------

    private const string ModsListFixture = """
        {"success":true,"data":[{"id":31,"name":"Scav Cat Trader Mod","slug":"scav-cat-trader-mod","downloads":13548,"category_id":16,"owner":{"id":355,"name":"DonutxLord","profile_photo_url":"https://files.sp-mod.com/profile-photos/HbLCZMn9dHWo8M5j0uG452RjevMNWl64GC4TY9yM.png","cover_photo_url":"https://files.sp-mod.com/cover-photos/QhEdXITt9r8KicVKeRqp2wxE1KpttShD4LkKPm3X.png"},"additional_authors":[]}],"links":{"first":"https://sp-mod.com/api/v0/mods?page=1","last":"https://sp-mod.com/api/v0/mods?page=1810","prev":null,"next":"https://sp-mod.com/api/v0/mods?page=2"},"meta":{"current_page":1,"from":1,"last_page":1810,"path":"https://sp-mod.com/api/v0/mods","per_page":1,"to":1,"total":1810}}
        """;

    private const string ModDetailsFixture = """
        {"success":true,"data":{"id":31,"hub_id":76,"guid":"com.donut.scavcat","name":"Scav Cat Trader Mod","slug":"scav-cat-trader-mod","teaser":"Scav Cat sells cases.","thumbnail":"https://files.sp-mod.com/mods/76.jpg","downloads":13548,"favourites_count":9,"description":"<p>Cheap cases.</p>","detail_url":"https://sp-mod.com/mod/31/scav-cat-trader-mod","fika_compatibility":false,"featured":false,"contains_ads":false,"contains_ai_content":false,"custom_ai_disclosure":"","shows_profile_binding_notice":true,"cheat_notice":false,"category_id":16,"published_at":"2021-01-01T07:22:00.000000Z","created_at":"2021-01-01T07:22:08.000000Z","updated_at":"2026-07-28T07:26:05.000000Z","owner":{"id":355,"name":"DonutxLord","profile_photo_url":"https://files.sp-mod.com/profile-photos/x.png","cover_photo_url":"https://files.sp-mod.com/cover-photos/y.png"},"additional_authors":[],"versions":[{"id":12710,"hub_id":null,"version":"1.0.8","spt_version_constraint":" 4.0.10 ","downloads":4711,"published_at":"2025-12-29T20:52:00.000000Z"},{"id":11467,"hub_id":130,"version":"1.0.0","spt_version_constraint":"","downloads":306,"published_at":"2021-01-01T07:22:08.000000Z"}],"license":{"id":11,"hub_id":14,"name":"MIT License","link":"https://choosealicense.com/licenses/mit/","created_at":"2025-09-26T15:44:12.000000Z","updated_at":"2025-09-26T15:44:12.000000Z"},"category":{"id":16,"hub_id":29,"title":"Traders","slug":"traders","description":""}}}
        """;

    private const string ModUpdatesFixture = """
        {"success":true,"data":{"spt_version":"4.0.10","updates":[{"current_version":{"id":11467,"mod_id":31,"guid":"com.donut.scavcat","name":"Scav Cat Trader Mod","slug":"scav-cat-trader-mod","version":"1.0.0"},"recommended_version":{"id":12710,"version":"1.0.8","link":"https://sp-mod.com/mod/download/31/scav-cat-trader-mod/1.0.8","content_length":43815,"fika_compatibility":"unknown","spt_versions":["4.0.10"]},"update_reason":"newer_version_available"}],"blocked_updates":[],"up_to_date":[],"incompatible_with_spt":[]}}
        """;

    private const string ModDependenciesFixture = """
        {"success":true,"data":{"31:1.0.8":[]}}
        """;
}
