using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Serialization;

namespace TCFModManager.Core.SpModApi;

// Typed client for the sp-mod.com /api/v0 mod/addon catalog API.
public sealed class SpModApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public SpModApiClient(HttpClient? httpClient = null, SpModApiOptions? options = null)
    {
        options ??= new SpModApiOptions();
        _ownsHttpClient = httpClient is null;
        // Compressed: the catalog is some forty pages of JSON, about a quarter the size gzipped.
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        });
        _http.BaseAddress = new Uri(options.BaseUrl);
        _http.Timeout = options.Timeout;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    // ---- General ----------------------------------------------------------------------------

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        var data = await GetSingleAsync<PingResponse>("/api/v0/ping", null, ct).ConfigureAwait(false);
        return data?.Message == "pong";
    }

    // ---- Mods ---------------------------------------------------------------------------------

    public Task<PagedResult<Mod>> GetModsAsync(ModsQuery? query = null, CancellationToken ct = default) =>
        GetPagedAsync<Mod>("/api/v0/mods", query, ct);

    public async Task<Mod> GetModAsync(string modId, string? fields = null, string? include = null, CancellationToken ct = default)
    {
        var query = new QueryParametersShim(fields, include);
        return await GetSingleAsync<Mod>($"/api/v0/mod/{Uri.EscapeDataString(modId)}", query, ct).ConfigureAwait(false)
               ?? throw new SpModApiException(HttpStatusCode.OK, null, "Mod response had no data.");
    }

    public Task<PagedResult<ModVersion>> GetModVersionsAsync(string modId, ModVersionsQuery? query = null, CancellationToken ct = default) =>
        GetPagedAsync<ModVersion>($"/api/v0/mod/{Uri.EscapeDataString(modId)}/versions", query, ct);

    // Checks a set of installed mods for updates against a target SPT version.
    // <param name="mods">Comma-separated identifier:version pairs, e.g. "5:1.2.0,com.example.mod:2.0.5".</param>
    // <param name="sptVersion">Target SPT version; must match a published version exactly.</param>
    public async Task<ModUpdateCheckResult> GetModUpdatesAsync(string mods, string sptVersion, CancellationToken ct = default)
    {
        var query = new KeyValuePair<string, string?>[] { new("mods", mods), new("spt_version", sptVersion) };
        return await GetSingleAsync<ModUpdateCheckResult>("/api/v0/mods/updates", query, ct).ConfigureAwait(false)
               ?? throw new SpModApiException(HttpStatusCode.OK, null, "Update check response had no data.");
    }

    // Resolves the dependency tree for one or more mod versions, keyed by "identifier:version".
    public async Task<Dictionary<string, List<DependencyNode>>> GetModDependenciesAsync(string mods, string sptVersion, CancellationToken ct = default)
    {
        var query = new KeyValuePair<string, string?>[] { new("mods", mods), new("spt_version", sptVersion) };
        return await GetSingleAsync<Dictionary<string, List<DependencyNode>>>("/api/v0/mods/dependencies", query, ct).ConfigureAwait(false)
               ?? [];
    }

    public Task<FileTree> GetModVersionFileTreeAsync(string modId, string versionId, CancellationToken ct = default) =>
        GetSingleRequiredAsync<FileTree>(
            $"/api/v0/mod/{Uri.EscapeDataString(modId)}/versions/{Uri.EscapeDataString(versionId)}/file-tree", null, ct);

    // ---- Addons -------------------------------------------------------------------------------

    public Task<PagedResult<Addon>> GetAddonsAsync(AddonsQuery? query = null, CancellationToken ct = default) =>
        GetPagedAsync<Addon>("/api/v0/addons", query, ct);

    public Task<Addon> GetAddonAsync(string addonId, string? fields = null, string? include = null, CancellationToken ct = default) =>
        GetSingleRequiredAsync<Addon>($"/api/v0/addon/{Uri.EscapeDataString(addonId)}", new QueryParametersShim(fields, include), ct);

    public Task<PagedResult<AddonVersion>> GetAddonVersionsAsync(string addonId, AddonVersionsQuery? query = null, CancellationToken ct = default) =>
        GetPagedAsync<AddonVersion>($"/api/v0/addon/{Uri.EscapeDataString(addonId)}/versions", query, ct);

    public Task<FileTree> GetAddonVersionFileTreeAsync(string addonId, string versionId, CancellationToken ct = default) =>
        GetSingleRequiredAsync<FileTree>(
            $"/api/v0/addon/{Uri.EscapeDataString(addonId)}/versions/{Uri.EscapeDataString(versionId)}/file-tree", null, ct);

    // Resolves the mods required by one or more addon versions.
    public async Task<Dictionary<string, List<DependencyNode>>> GetAddonDependenciesAsync(string addons, string sptVersion, CancellationToken ct = default)
    {
        var query = new KeyValuePair<string, string?>[] { new("addons", addons), new("spt_version", sptVersion) };
        return await GetSingleAsync<Dictionary<string, List<DependencyNode>>>("/api/v0/addons/dependencies", query, ct).ConfigureAwait(false)
               ?? [];
    }

    // ---- Mod categories -------------------------------------------------------------------------

    public Task<PagedResult<ModCategory>> GetModCategoriesAsync(ModCategoriesQuery? query = null, CancellationToken ct = default) =>
        GetPagedAsync<ModCategory>("/api/v0/mod-categories", query, ct);

    public Task<ModCategory> GetModCategoryAsync(string idOrSlug, string? fields = null, CancellationToken ct = default) =>
        GetSingleRequiredAsync<ModCategory>(
            $"/api/v0/mod-categories/{Uri.EscapeDataString(idOrSlug)}",
            fields is null ? null : new KeyValuePair<string, string?>[] { new("fields", fields) }, ct);

    // ---- SPT versions ---------------------------------------------------------------------------

    public Task<PagedResult<SptVersion>> GetSptVersionsAsync(SptVersionsQuery? query = null, CancellationToken ct = default) =>
        GetPagedAsync<SptVersion>("/api/v0/spt/versions", query, ct);

    // ---- Internals ------------------------------------------------------------------------------

    private sealed class PingResponse
    {
        public string? Message { get; set; }
    }

    // Wraps fields/include as query parameters without a dedicated query type.
    private sealed class QueryParametersShim(string? fields, string? include) : IEnumerable<KeyValuePair<string, string?>>
    {
        public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
        {
            if (fields is not null) yield return new("fields", fields);
            if (include is not null) yield return new("include", include);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private async Task<PagedResult<T>> GetPagedAsync<T>(string path, IEnumerable<KeyValuePair<string, string?>>? query, CancellationToken ct)
    {
        var json = await SendAsync(path, query, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<PagedResult<T>>(json, SpModJson.Options)
               ?? new PagedResult<T>();
    }

    private async Task<T?> GetSingleAsync<T>(string path, IEnumerable<KeyValuePair<string, string?>>? query, CancellationToken ct)
    {
        var json = await SendAsync(path, query, ct).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<ApiResponse<T>>(json, SpModJson.Options);
        return envelope is null ? default : envelope.Data;
    }

    private async Task<T> GetSingleRequiredAsync<T>(string path, IEnumerable<KeyValuePair<string, string?>>? query, CancellationToken ct)
    {
        var data = await GetSingleAsync<T>(path, query, ct).ConfigureAwait(false);
        return data ?? throw new SpModApiException(HttpStatusCode.OK, null, $"Response for {path} had no data.");
    }

    private async Task<string> SendAsync(string path, IEnumerable<KeyValuePair<string, string?>>? query, CancellationToken ct)
    {
        var url = BuildUrl(path, query);
        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode) return body;

        string? code = null;
        string message = $"sp-mod.com request to {url} failed with status {(int)response.StatusCode}.";
        try
        {
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, SpModJson.Options);
            if (error is not null)
            {
                code = error.Code;
                if (!string.IsNullOrWhiteSpace(error.Message)) message = error.Message!;
            }
        }
        catch (JsonException)
        {
            // Not a well-formed error envelope; use the generic message.
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            throw new SpModApiRateLimitedException(code, message, retryAfter);
        }

        throw new SpModApiException(response.StatusCode, code, message);
    }

    private static string BuildUrl(string path, IEnumerable<KeyValuePair<string, string?>>? query)
    {
        if (query is null) return path;

        var sb = new StringBuilder(path);
        var first = true;
        foreach (var (key, value) in query)
        {
            if (value is null) continue;
            sb.Append(first ? '?' : '&');
            first = false;
            sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
