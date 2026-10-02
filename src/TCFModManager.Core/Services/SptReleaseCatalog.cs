using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.Core.Services;

// 
// The published SPT release list from sp-mod.com, cached to disk. Mod version constraints are
// resolved against this so the UI names releases that actually exist rather than the boundary
// versions the constraints are written in terms of.
// 
public sealed class SptReleaseCatalog(SpModApiClient api)
{
    private const int SchemaVersion = 1;
    private const int PageSize = 50;

    // Refetched when the cached copy is older than this; the list changes only when SPT ships.
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);

    private readonly string _filePath = Path.Combine(AppPaths.DataDirectory, "spt_versions.json");

    private List<SptRelease> _releases = [];

    // Known releases at or above the floor, newest first. Empty until loaded.
    public IReadOnlyList<SptRelease> Releases => _releases;

    // The distinct major.minor lines, newest first. Drives Browse's version filter.
    public IReadOnlyList<(int Major, int Minor)> Lines { get; private set; } = [];

    private sealed class CachedReleases
    {
        public int SchemaVersion { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public List<string> Versions { get; set; } = [];
    }

    // 
    // Loads from disk, refetching when the cache is missing or stale. A failed fetch keeps whatever
    // was cached, so the UI degrades to the last known list rather than to nothing.
    // 
    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        var cached = LoadFromDisk();
        if (cached is not null)
        {
            Apply(cached.Versions);
            if (DateTimeOffset.UtcNow - cached.FetchedAt < MaxAge) return;
        }

        try
        {
            var fetched = await FetchAllAsync(ct).ConfigureAwait(false);
            if (fetched.Count == 0) return;

            Apply(fetched.Select(r => r.Label).ToList());
            SaveToDisk(fetched);
            AppLog.Info("SptReleases", $"fetched {_releases.Count} release(s); lines {string.Join(", ", Lines.Select(l => $"{l.Major}.{l.Minor}"))}");
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException or TaskCanceledException)
        {
            // Keep the cached list; the next launch tries again.
            AppLog.Warn("SptReleases", $"refresh failed, using {_releases.Count} cached release(s): {ex.Message}");
        }
    }

    private async Task<List<SptRelease>> FetchAllAsync(CancellationToken ct)
    {
        var all = new List<SptVersion>();
        var page = 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var result = await api
                .GetSptVersionsAsync(new SptVersionsQuery { Page = page, PerPage = PageSize }, ct)
                .ConfigureAwait(false);

            all.AddRange(result.Data);

            var isLastPage = result.Data.Count < PageSize
                || (result.Meta is { } meta && meta.LastPage > 0 && page >= meta.LastPage);
            if (isLastPage) break;

            page++;
        }

        return SptReleases.FromApi(all);
    }

    private void Apply(List<string> labels)
    {
        _releases = SptReleases.FromApi(labels.Select(v => new SptVersion { Version = v }));
        Lines = SptReleases.Lines(_releases);
    }

    private CachedReleases? LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var data = JsonSerializer.Deserialize<CachedReleases>(File.ReadAllText(_filePath));
            if (data is null || data.SchemaVersion != SchemaVersion || data.Versions.Count == 0) return null;
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private void SaveToDisk(List<SptRelease> releases)
    {
        try
        {
            var data = new CachedReleases
            {
                SchemaVersion = SchemaVersion,
                FetchedAt = DateTimeOffset.UtcNow,
                Versions = releases.Select(r => r.Label).ToList(),
            };
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(data));
        }
        catch (IOException)
        {
            // A failed write just means the next launch refetches.
        }
    }
}
