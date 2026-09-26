using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Fetches the entire sp-mod.com catalog page by page. Caching/reuse of the result is the caller's
// responsibility; this just performs the paginated fetch.
// 
public sealed class ModCacheService(SpModApiClient spModApi)
{
    private const int PageSize = 50; // sp-mod.com API's per_page max.

    //
    // How long to wait before asking for a page again after a failure that may not happen twice - a
    // server error, a dropped connection, a timeout. One bad page used to throw the whole catalog
    // away, thirty-eight good pages with it.
    //
    public static TimeSpan[] PageRetryDelays { get; set; } =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    public async Task<List<Mod>> FetchAllAsync(
        IProgress<(int Loaded, int? Total)>? progress = null,
        CancellationToken ct = default)
    {
        var all = new List<Mod>();
        var page = 1;
        var dropped = 0;
        var failures = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            PagedResult<Mod> result;
            try
            {
                result = await spModApi.GetModsAsync(
                    new ModsQuery
                    {
                        // "versions" ensures each cached mod carries its latest version's SPT
                        // constraint/release number, used by Browse cards and the version filter.
                        Include = "category,versions",
                        Sort = "-downloads",
                        Page = page,
                        PerPage = PageSize,
                    },
                    ct).ConfigureAwait(false);
            }
            catch (SpModApiRateLimitedException ex)
            {
                // Back off as long as the API asks, then retry the same page.
                await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex) when (IsPassing(ex, ct) && failures < PageRetryDelays.Length)
            {
                var wait = PageRetryDelays[failures++];
                AppLog.Info("Catalog", $"page {page} failed ({ex.Message}); asking again in {wait.TotalSeconds:F0}s");
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }

            failures = 0;

            var kept = result.Data.Where(StillRunsOnASupportedRelease).ToList();
            dropped += result.Data.Count - kept.Count;
            all.AddRange(kept);
            progress?.Report((all.Count, result.Meta?.Total));

            var isLastPage = result.Data.Count < PageSize
                || (result.Meta is { } meta && meta.LastPage > 0 && page >= meta.LastPage);
            if (isLastPage) break;

            page++;

            // Small pause between pages to stay under the API's rate limit.
            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        AppLog.Info("Catalog", $"fetched {all.Count} mod(s); dropped {dropped} below SPT {SptReleases.Floor}");
        return all;
    }

    // A failure that may not happen again: a server error, a dropped or refused connection, a timeout
    // (not the caller cancelling). Not a refusal (4xx) or a response that did not read.
    private static bool IsPassing(Exception ex, CancellationToken ct) => ex switch
    {
        _ when ct.IsCancellationRequested => false,
        SpModApiException api => (int)api.StatusCode >= 500 || (int)api.StatusCode == 408,
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 || (int)code == 408,
        HttpRequestException => true,
        HttpIOException => true,
        TaskCanceledException => true,
        _ => false,
    };

    // 
    // Whether any of a mod's cached versions targets an SPT release at or above
    // <see cref="SptReleases.Floor"/>. Mods that only ever supported older releases are left out of
    // the catalog: nobody can install them, and they only pad out search and the version filter.
    // 
    private static bool StillRunsOnASupportedRelease(Mod mod)
    {
        var versions = mod.Versions ?? [];
        if (versions.Count == 0) return true;

        return SptReleases.ReachesFloor(versions.Select(v => v.SptVersionConstraint));
    }
}
