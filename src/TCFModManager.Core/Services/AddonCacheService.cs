using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// 
// Fetches every addon published on sp-mod.com page by page. Caching/reuse of the result is the
// caller's responsibility; this just performs the paginated fetch.
// 
public sealed class AddonCacheService(SpModApiClient spModApi)
{
    private const int PageSize = 50; // sp-mod.com API's per_page max.

    public async Task<List<Addon>> FetchAllAsync(
        IProgress<(int Loaded, int? Total)>? progress = null,
        CancellationToken ct = default)
    {
        var all = new List<Addon>();
        var page = 1;
        var failures = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            PagedResult<Addon> result;
            try
            {
                result = await spModApi.GetAddonsAsync(
                    new AddonsQuery
                    {
                        // Each addon's latest versions carry the download link, size and the
                        // parent-mod constraint, so the whole install path works off this one fetch.
                        Include = "versions",
                        // Fork (1.4.9), as TCF 2d53631: oldest first. By downloads, a mod whose count changed
                        // between two pages moved across the page boundary and was listed twice, or pushed
                        // another out of the list altogether; creation order only ever grows at the end.
                        Sort = "created_at",
                        Page = page,
                        PerPage = PageSize,
                    },
                    ct).ConfigureAwait(false);
            }
            catch (SpModApiRateLimitedException ex)
            {
                await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex) when (ModCacheService.IsPassing(ex, ct) && failures < ModCacheService.PageRetryDelays.Length)
            {
                // Fork (1.4.9): as the mod list does. One 502 here used to fail Browse's Refresh
                // after the mods had already been read again, and the page kept its old cards.
                var wait = ModCacheService.PageRetryDelays[failures++];
                AppLog.Info("Addons", $"page {page} failed ({ex.Message}); asking again in {wait.TotalSeconds:F0}s");
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }

            failures = 0;
            all.AddRange(result.Data);
            progress?.Report((all.Count, result.Meta?.Total));

            var isLastPage = result.Data.Count < PageSize
                || (result.Meta is { } meta && meta.LastPage > 0 && page >= meta.LastPage);
            if (isLastPage) break;

            page++;

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        AppLog.Info("Addons", $"fetched {all.Count} addon(s)");

        // Most downloaded first, as the list always was.
        return [.. all.DistinctBy(a => a.Id).OrderByDescending(a => a.Downloads ?? 0)];
    }
}
