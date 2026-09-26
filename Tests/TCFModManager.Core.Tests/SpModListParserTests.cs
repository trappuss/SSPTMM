using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TCFModManager.Core.SpModLists;
using Xunit;

namespace TCFModManager.Core.Tests;

// sp-mod.com's list pages as fetched on 2026-09-25 - see Fixtures/SpModLists.
public class SpModListParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpModLists", name));

    private static string SearchHtml(string name) =>
        JsonNode.Parse(Fixture(name))!["components"]![0]!["effects"]!["html"]!.GetValue<string>();

    [Fact]
    public void The_lists_page_gives_each_card_as_the_site_shows_it()
    {
        var page = SpModListParser.ParseBrowse(Fixture("lists.html"));

        Assert.Equal(12, page.Lists.Count);

        var first = page.Lists[0];
        Assert.Equal(126918, first.Id);
        Assert.Equal("grug-mp", first.Slug);
        Assert.Equal("Grug-MP", first.Title);
        Assert.Equal("4.1.6", first.SptVersion);
        Assert.Equal("Burningsbug350", first.Author);
        Assert.Equal(83, first.ItemCount);
        Assert.Null(first.Teaser);
        Assert.Null(first.Cover);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 16, 48, 50, TimeSpan.Zero), first.UpdatedAt);
        Assert.Equal("https://sp-mod.com/list/126918/grug-mp", first.Url);
    }

    [Fact]
    public void A_card_with_a_picture_and_a_description_keeps_both()
    {
        var card = SpModListParser.ParseBrowse(Fixture("lists.html")).Lists.Single(l => l.Id == 115945);

        Assert.Equal("https://files.sp-mod.com/mod-lists/0P7qZQWr2Ky71af2rTQkRqyRQEW6ynPgR0HGXDo7.png", card.Cover);
        Assert.StartsWith("if you want to see how does it look", card.Teaser);
        Assert.Equal(145, card.ItemCount);
        Assert.Equal("CMDR_Tank", card.Author);
    }

    [Fact]
    public void The_lists_page_says_how_many_there_are_and_how_many_pages()
    {
        var page = SpModListParser.ParseBrowse(Fixture("lists.html"));

        Assert.Equal(317, page.Total);
        Assert.Equal(1, page.Page);
        Assert.Equal(27, page.LastPage);
    }

    [Fact]
    public void The_spt_filter_choices_are_the_sites_ids()
    {
        var options = SpModListParser.ParseBrowse(Fixture("lists.html")).SptOptions;

        Assert.Equal(new SpModListSptOption(55, "4.1.6"), options[0]);
        Assert.Contains(options, o => o.Version == "4.0.13");
        Assert.All(options, o => Assert.True(o.Id > 0));
    }

    [Fact]
    public void A_search_answered_through_livewire_reads_the_same_way()
    {
        var page = SpModListParser.ParseBrowse(SearchHtml("search-mo2.json"));

        var only = Assert.Single(page.Lists);
        Assert.Equal(126737, only.Id);
        Assert.Equal("spt4013-mo2", only.Slug);
        Assert.Equal("4.0.13", only.SptVersion);
        Assert.Equal(1, page.Total);
        Assert.Equal(1, page.LastPage);
    }

    [Fact]
    public void A_search_with_no_matches_is_an_empty_page()
    {
        var page = SpModListParser.ParseBrowse(SearchHtml("search-none.json"));

        Assert.Empty(page.Lists);
        Assert.Equal(0, page.Total);
        Assert.NotEmpty(page.SptOptions);
    }

    [Fact]
    public void A_list_page_gives_its_header()
    {
        var list = SpModListParser.ParseList(Fixture("list-126737.html"), 126737, "spt4013-mo2")!;

        Assert.Equal("SPT4.0.13 MO2", list.Title);
        Assert.Equal("4.0.13", list.SptVersion);
        Assert.Equal("WUVGAWORE", list.Author);
        Assert.Equal("https://sp-mod.com/user/62485/wuvgawore", list.AuthorUrl);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 23, 42, 17, TimeSpan.Zero), list.UpdatedAt);
        Assert.Null(list.Cover);
        Assert.StartsWith("<h1>My Old Mod Organizer Mod-list for SPT 4.0.13</h1>", list.DescriptionHtml);
    }

    [Fact]
    public void A_list_page_gives_every_mod_and_addon_on_it()
    {
        var list = SpModListParser.ParseList(Fixture("list-126737.html"), 126737, "spt4013-mo2")!;

        // "96 mods · 1 addon", as the page's own header says.
        Assert.Equal(96, list.Items.Count);
        Assert.Equal(1, list.AddonCount);
        Assert.Equal(96, list.Items.Select(i => i.ModId).Distinct().Count());
        Assert.Equal(0, list.Unavailable);
    }

    [Fact]
    public void An_item_carries_its_version_author_and_badges()
    {
        var list = SpModListParser.ParseList(Fixture("list-126737.html"), 126737, "spt4013-mo2")!;

        var bigBrain = list.Items[0];
        Assert.Equal(902, bigBrain.ModId);
        Assert.Equal("bigbrain", bigBrain.Slug);
        Assert.Equal("BigBrain", bigBrain.Name);
        Assert.Equal("1.4.0", bigBrain.Version);
        Assert.Equal("4.0.13", bigBrain.SptVersion);
        Assert.Equal("DrakiaXYZ", bigBrain.Author);
        Assert.Equal(1_346_693, bigBrain.Downloads);
        Assert.True(bigBrain.IsDependency);
        Assert.False(bigBrain.IsIncompatible);
        Assert.Null(bigBrain.Note);
        Assert.Empty(bigBrain.Addons);

        var waypoints = list.Items[1];
        Assert.Equal("https://files.sp-mod.com/mods/1119.png", waypoints.Thumbnail);
    }

    [Fact]
    public void An_item_with_no_version_for_the_lists_spt_says_so()
    {
        var list = SpModListParser.ParseList(Fixture("list-126737.html"), 126737, "spt4013-mo2")!;

        var item = list.Items.Single(i => i.ModId == 2555);
        Assert.True(item.IsIncompatible);
        Assert.Equal("4.0.11", item.SptVersion);
    }

    [Fact]
    public void The_authors_notes_are_kept_on_their_items()
    {
        var list = SpModListParser.ParseList(Fixture("list-126737.html"), 126737, "spt4013-mo2")!;

        Assert.Equal("OPTIONAL", list.Items.Single(i => i.ModId == 2326).Note);
        Assert.Equal(7, list.Items.Count(i => i.Note is not null));
    }

    [Fact]
    public void Dependencies_the_list_satisfies_are_named()
    {
        var list = SpModListParser.ParseList(Fixture("list-126737.html"), 126737, "spt4013-mo2")!;

        var withTwo = list.Items.First(i => i.Dependencies.Count == 2);
        Assert.All(withTwo.Dependencies, d => Assert.Equal("On list", d.State));
        Assert.Contains(withTwo.Dependencies, d => d.Name == "BigBrain");
    }

    [Fact]
    public void An_addon_is_read_under_its_mod_and_not_as_the_mod()
    {
        var list = SpModListParser.ParseList(Fixture("list-126737.html"), 126737, "spt4013-mo2")!;

        var parent = list.Items.Single(i => i.Addons.Count > 0);
        Assert.Equal(2299, parent.ModId);
        Assert.NotEqual("Quick-Sell", parent.Name);

        var addon = Assert.Single(parent.Addons);
        Assert.Equal(1, addon.AddonId);
        Assert.Equal("quick-sell", addon.Slug);
        Assert.Equal("Quick-Sell", addon.Name);
        Assert.Equal("2.3.0", addon.Version);
        Assert.Equal("swiftxp", addon.Author);
        Assert.StartsWith("https://files.sp-mod.com/addons/", addon.Thumbnail);
    }

    [Fact]
    public void A_list_with_a_picture_has_it_as_its_cover()
    {
        var list = SpModListParser.ParseList(Fixture("list-115945.html"), 115945, "my-list")!;

        Assert.Equal("my list", list.Title);
        Assert.Equal("https://files.sp-mod.com/mod-lists/0P7qZQWr2Ky71af2rTQkRqyRQEW6ynPgR0HGXDo7.png", list.Cover);
        // "145 mods", two of them since taken down.
        Assert.Equal(143, list.Items.Count);
        Assert.Equal(2, list.Unavailable);
    }

    [Fact]
    public void A_page_without_a_list_is_not_one()
    {
        Assert.Null(SpModListParser.ParseList("<html><body><p>Not found</p></body></html>", 1, "x"));
    }

    [Theory]
    [InlineData("https://sp-mod.com/list/126737/spt4013-mo2", 126737, "spt4013-mo2")]
    [InlineData("sp-mod.com/list/126737/spt4013-mo2/", 126737, "spt4013-mo2")]
    [InlineData("https://sp-mod.com/list/126737/spt4013-mo2?tab=comments", 126737, "spt4013-mo2")]
    [InlineData("  https://www.sp-mod.com/list/5/a-b_c  ", 5, "a-b_c")]
    public void A_pasted_list_address_is_read(string text, int id, string slug)
    {
        Assert.True(SpModListAddress.TryParse(text, out var readId, out var readSlug));
        Assert.Equal(id, readId);
        Assert.Equal(slug, readSlug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://sp-mod.com/list/126737")]
    [InlineData("https://example.com/list/126737/spt4013-mo2")]
    [InlineData("https://sp-mod.com/mod/902/bigbrain")]
    public void Anything_else_is_not_a_list_address(string text)
    {
        Assert.False(SpModListAddress.TryParse(text, out _, out _));
    }

    // ------------------------------------------------------------------ the client

    // Answers the lists page to a GET and the search JSON to a POST, recording what was sent.
    private sealed class FakeSite(string page, string search, int firstPostStatus = 200) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        private int _posts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            if (request.Method == HttpMethod.Post)
            {
                Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
                var status = _posts++ == 0 ? (HttpStatusCode)firstPostStatus : HttpStatusCode.OK;
                return new HttpResponseMessage(status) { Content = new StringContent(search, Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(page, Encoding.UTF8, "text/html") };
        }
    }

    [Fact]
    public async Task Browsing_without_a_search_is_a_plain_page_fetch()
    {
        var site = new FakeSite(Fixture("lists.html"), Fixture("search-mo2.json"));
        using var client = new SpModListsClient(site);

        var page = await client.BrowseAsync(page: 3);

        Assert.Equal(12, page.Lists.Count);
        var request = Assert.Single(site.Requests);
        Assert.Equal("https://sp-mod.com/lists?page=3", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task A_search_posts_the_pages_own_state_and_the_change()
    {
        var site = new FakeSite(Fixture("lists.html"), Fixture("search-mo2.json"));
        using var client = new SpModListsClient(site);

        var page = await client.BrowseAsync(page: 2, search: "mo2", sptVersionId: 42);

        Assert.Equal(126737, Assert.Single(page.Lists).Id);

        var post = site.Requests.Single(r => r.Method == HttpMethod.Post);
        Assert.Equal("https://sp-mod.com/livewire-6e3bcd93/update", post.RequestUri!.ToString());
        Assert.True(post.Headers.Contains("X-Livewire"));

        var body = JsonNode.Parse(site.Bodies.Single())!;
        Assert.False(string.IsNullOrEmpty(body["_token"]!.GetValue<string>()));
        var component = body["components"]![0]!;
        Assert.Contains("pages::list.index", component["snapshot"]!.GetValue<string>());
        Assert.Equal("mo2", component["updates"]!["search"]!.GetValue<string>());
        Assert.Equal(42, component["updates"]!["sptVersionId"]!.GetValue<int>());
        Assert.Equal("gotoPage", component["calls"]![0]!["method"]!.GetValue<string>());
        Assert.Equal(2, component["calls"]![0]!["params"]![0]!.GetValue<int>());
    }

    [Fact]
    public async Task A_search_turned_away_reads_the_page_again_and_tries_once_more()
    {
        var site = new FakeSite(Fixture("lists.html"), Fixture("search-mo2.json"), firstPostStatus: 419);
        using var client = new SpModListsClient(site);

        var page = await client.BrowseAsync(search: "mo2");

        Assert.Single(page.Lists);
        Assert.Equal(2, site.Requests.Count(r => r.Method == HttpMethod.Get));
        Assert.Equal(2, site.Requests.Count(r => r.Method == HttpMethod.Post));
    }
}
