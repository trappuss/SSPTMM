using System.Net;
using System.Text;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The catalog fetch against an in-memory API: one failing page is asked for again rather than
// throwing every other page away; a refusal is not.
//
public class ModCacheServiceTests
{
    private sealed class Api(Func<int, int, HttpStatusCode> status) : HttpMessageHandler
    {
        private readonly Dictionary<int, int> _asked = [];

        public int Asked(int page) => _asked.GetValueOrDefault(page);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var page = int.Parse(query["page"]!);
            var attempt = _asked[page] = _asked.GetValueOrDefault(page) + 1;

            var code = status(page, attempt);
            if (code != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("{}") });

            // Two pages: fifty mods, then one.
            var count = page == 1 ? 50 : 1;
            var mods = string.Join(",", Enumerable.Range(0, count).Select(i => $"{{\"id\":{page * 100 + i},\"name\":\"M{page}-{i}\"}}"));
            var json = $"{{\"success\":true,\"data\":[{mods}],\"meta\":{{\"current_page\":{page},\"last_page\":2,\"total\":51}}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ModCacheService Service(Api api) =>
        new(new SpModApiClient(new HttpClient(api), new SpModApiOptions { BaseUrl = "http://sp-mod.test" }));

    [Fact]
    public async Task APageThatFailsOnce_IsAskedForAgain()
    {
        var before = ModCacheService.PageRetryDelays;
        ModCacheService.PageRetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];
        try
        {
            var api = new Api((page, attempt) => page == 2 && attempt == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.OK);

            var mods = await Service(api).FetchAllAsync();

            Assert.Equal(51, mods.Count);
            Assert.Equal(2, api.Asked(2));
        }
        finally
        {
            ModCacheService.PageRetryDelays = before;
        }
    }

    [Fact]
    public async Task ARefusal_IsNotAskedForAgain()
    {
        var api = new Api((page, _) => page == 2 ? HttpStatusCode.NotFound : HttpStatusCode.OK);

        await Assert.ThrowsAsync<SpModApiException>(() => Service(api).FetchAllAsync());
        Assert.Equal(1, api.Asked(2));
    }

    [Fact]
    public async Task APageThatKeepsFailing_GivesUpAfterAFewTries()
    {
        var before = ModCacheService.PageRetryDelays;
        ModCacheService.PageRetryDelays = [TimeSpan.Zero, TimeSpan.Zero];
        try
        {
            var api = new Api((page, _) => page == 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);

            await Assert.ThrowsAsync<SpModApiException>(() => Service(api).FetchAllAsync());
            Assert.Equal(3, api.Asked(2));
        }
        finally
        {
            ModCacheService.PageRetryDelays = before;
        }
    }
}
