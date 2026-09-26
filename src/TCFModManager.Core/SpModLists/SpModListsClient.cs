using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.SpModLists;

//
// Fetches sp-mod.com's list pages.
//
// The first page of sp-mod.com/lists, and any page of it, is a plain GET (?page=N). Searching and
// the SPT filter are not: the page does those through Livewire, posting the component's state and
// the change to the site's update address and reading back the component's new HTML. That is what
// this does too - the same request the page's own script sends, with the page's own token and
// cookies - because there is no other way to ask sp-mod.com for "lists matching mo2". Measured
// 2026-09-25: the update address carries a build hash ("livewire-6e3bcd93"), so it is read from the
// page each time rather than written here.
//
// One instance for the app: the cookies and the page state it holds are what let a search follow a
// search without fetching the page again every time.
//
public sealed partial class SpModListsClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _site;

    // The lists page's Livewire state, once read: the token, where updates go, and the component.
    private LivewirePage? _livewire;
    private readonly SemaphoreSlim _livewireGate = new(1, 1);

    public SpModListsClient(HttpMessageHandler? handler = null, SpModApiOptions? options = null)
    {
        options ??= new SpModApiOptions();
        _site = options.BaseUrl.TrimEnd('/');

        _ownsHttp = true;
        _http = new HttpClient(handler ?? new HttpClientHandler { CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = options.Timeout,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    /// <summary>A page of public lists, newest first - the only order sp-mod.com has. A search or
    /// SPT version id narrows them.</summary>
    public async Task<SpModListPage> BrowseAsync(int page = 1, string? search = null, int? sptVersionId = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        if (search is null && sptVersionId is null)
        {
            var html = await GetPageAsync($"{_site}/lists" + (page > 1 ? $"?page={page}" : string.Empty), ct).ConfigureAwait(false);
            _livewire = LivewirePage.Read(html) ?? _livewire;
            return SpModListParser.ParseBrowse(html);
        }

        await _livewireGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Once more with a fresh page when the first try is turned away: the token and the
            // component's checksum both expire with the site's session.
            for (var attempt = 0; ; attempt++)
            {
                if (_livewire is null || attempt > 0)
                {
                    var html = await GetPageAsync($"{_site}/lists", ct).ConfigureAwait(false);
                    _livewire = LivewirePage.Read(html)
                        ?? throw new SpModListsException(Strings.NotLivewire);
                }

                var result = await UpdateAsync(_livewire, page, search, sptVersionId, ct).ConfigureAwait(false);
                if (result is not null) return SpModListParser.ParseBrowse(result);
                if (attempt > 0) throw new SpModListsException(Strings.Refused);
            }
        }
        finally
        {
            _livewireGate.Release();
        }
    }

    /// <summary>A list's page, or null when sp-mod.com has no such list (or it is private).</summary>
    public async Task<SpModListDetails?> GetListAsync(int id, string slug, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(SpModListAddress.For(id, Uri.EscapeDataString(slug)), ct).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return SpModListParser.ParseList(html, id, slug);
    }

    private async Task<string> GetPageAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    // The component's HTML after the change, or null when the site refused the update (an expired
    // token: 419, or a checksum it no longer accepts).
    private Task<string?> UpdateAsync(LivewirePage page, int pageNumber, string? search, int? sptVersionId, CancellationToken ct)
    {
        // Always the page asked for, page 1 included: the state sent may be from a later page (a plain
        // ?page=N read last), and changing the search does not by itself go back to the first.
        var calls = new JsonArray(Call("gotoPage", pageNumber, "page"));

        var updates = new JsonObject
        {
            ["search"] = search ?? string.Empty,
            ["sptVersionId"] = sptVersionId is { } id ? JsonValue.Create(id) : null,
        };

        return PostAsync(page, updates, calls, $"{_site}/lists", ct);
    }

    private static JsonObject Call(string method, params object[] parameters)
    {
        var args = new JsonArray();
        foreach (var parameter in parameters) args.Add(JsonValue.Create(parameter));

        return new JsonObject
        {
            ["path"] = string.Empty,
            ["method"] = method,
            ["params"] = args,
            ["metadata"] = new JsonObject(),
        };
    }

    // The request the page's own script sends: the component's state, what changed, what to call.
    private async Task<string?> PostAsync(LivewirePage page, JsonObject updates, JsonArray calls, string referrer, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["_token"] = page.Token,
            ["components"] = new JsonArray(new JsonObject
            {
                ["snapshot"] = page.Snapshot,
                ["updates"] = updates,
                ["calls"] = calls,
            }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, page.UpdateUri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Livewire", "1");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Referrer = new Uri(referrer);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if ((int)response.StatusCode is 419 or 403 or 400 or 422 or 500)
        {
            AppLog.Info("Lists", $"sp-mod.com turned a page update away ({(int)response.StatusCode})");
            return null;
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            var html = JsonNode.Parse(json)?["components"]?[0]?["effects"]?["html"]?.GetValue<string>();
            if (html is null) AppLog.Info("Lists", "sp-mod.com answered a page update without the page");
            return html;
        }
        catch (JsonException ex)
        {
            AppLog.Info("Lists", $"sp-mod.com's answer to a page update was not readable: {ex.Message}");
            return null;
        }
    }

    //
    // A member's page: who they are, and their lists - which the page loads only when its Lists tab
    // comes into view, so they are asked for the same way (its lazy load, one request). Null when
    // sp-mod.com has no such member. A member whose lists could not be read comes back with none,
    // rather than not at all: the profile is still worth showing.
    //
    public async Task<SpModUserPage?> GetUserAsync(int id, CancellationToken ct = default)
    {
        var url = SpModListAddress.ForUser(id);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (SpModListParser.ParseUser(html, id) is not { } profile) return null;

        IReadOnlyList<SpModListSummary> lists = [];
        if (LivewirePage.Read(html, "user.show.lists-tab") is { LazyArgument: { } argument } tab)
        {
            var referrer = response.RequestMessage?.RequestUri?.ToString() ?? url;
            var tabHtml = await PostAsync(tab, new JsonObject(), new JsonArray(Call("__lazyLoad", argument)), referrer, ct).ConfigureAwait(false);
            if (tabHtml is not null) lists = SpModListParser.ParseUserLists(tabHtml, profile.Name);
        }
        else
        {
            AppLog.Info("Lists", $"member {id}'s page did not carry its lists tab");
        }

        return new SpModUserPage(profile, lists);
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _livewireGate.Dispose();
    }

    // What a Livewire update needs from the page it came from: the token, where updates go, one
    // component's state, and - for a component the page loads only once it scrolls into view -
    // the argument its lazy load is called with.
    internal sealed record LivewirePage(string Token, string UpdateUri, string Snapshot, string? LazyArgument = null)
    {
        public const string ListIndex = "pages::list.index";

        public static LivewirePage? Read(string html, string component = ListIndex)
        {
            var config = ScriptConfig().Match(html);
            if (!config.Success) return null;

            string? token, uri;
            try
            {
                var node = JsonNode.Parse(config.Groups[1].Value);
                token = node?["csrf"]?.GetValue<string>();
                uri = node?["uri"]?.GetValue<string>();
            }
            catch (JsonException)
            {
                return null;
            }

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(uri)) return null;

            foreach (Match snapshot in Snapshots().Matches(html))
            {
                var value = WebUtility.HtmlDecode(snapshot.Groups[1].Value);
                try
                {
                    if (JsonNode.Parse(value)?["memo"]?["name"]?.GetValue<string>() != component) continue;
                }
                catch (JsonException)
                {
                    continue;
                }

                // The lazy load's argument sits in the same tag, a little after the snapshot.
                var tagEnd = html.IndexOf('>', snapshot.Index + snapshot.Length);
                var tag = tagEnd < 0 ? string.Empty : html[(snapshot.Index + snapshot.Length)..tagEnd];
                var lazy = LazyLoad().Match(WebUtility.HtmlDecode(tag));

                return new LivewirePage(token, uri, value, lazy.Success ? lazy.Groups[1].Value : null);
            }

            return null;
        }
    }

    [GeneratedRegex(@"__lazyLoad\('([^']+)'\)")]
    private static partial Regex LazyLoad();

    [GeneratedRegex(@"livewireScriptConfig\s*=\s*(\{.*?\})\s*;?\s*</script>", RegexOptions.Singleline)]
    private static partial Regex ScriptConfig();

    [GeneratedRegex(@"wire:snapshot=""([^""]*)""")]
    private static partial Regex Snapshots();

    // Log-only wording: what the exception says is shown through the app's own sentence.
    private static class Strings
    {
        public const string NotLivewire = "the lists page did not carry what a search needs";
        public const string Refused = "sp-mod.com refused the search twice";
    }
}

/// <summary>sp-mod.com answered, but not with something a list could be read from.</summary>
public sealed class SpModListsException(string message) : Exception(message);
