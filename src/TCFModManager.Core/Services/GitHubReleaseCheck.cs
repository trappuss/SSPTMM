using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork: whether a newer SSPTMM is published on GitHub. It only tells - nothing here downloads or
// installs anything; the user is sent to the release page.
//
// One request to GitHub's public REST API, GET /repos/{owner}/{repo}/releases/latest, which answers
// with the newest release that is neither a draft nor a pre-release (404 while there is none).
// Asked with the html media type, so the release notes come back as body_html - GitHub's own
// rendering of the Markdown - which the About page shows with the same HTML renderer as a mod's
// changelog. No sign-in: unauthenticated callers get 60 requests an hour per address, and this
// asks once per start or press of Check now.
//
public sealed class GitHubReleaseCheck : IDisposable
{
    public const string Repository = "trappuss/SSPTMM";

    // The release zip's name, as SSPTMM-release-to-github.bat makes it: SSPTMM-<version>-win-x64.zip.
    private const string AssetPrefix = "SSPTMM-";
    private const string AssetSuffix = "-win-x64.zip";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public GitHubReleaseCheck(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    //
    // The newest release next to the running version, or null when the repository has no release
    // yet or the newest one's tag isn't a version. Failures throw GitHubReleaseCheckException
    // (rate limited, or GitHub answered with an error) or what HttpClient throws (unreachable,
    // timed out) - the caller tells those apart from "nothing newer".
    //
    public async Task<AppUpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        // GitHub refuses API calls without a User-Agent.
        request.Headers.UserAgent.ParseAdd($"{SelfMod.ShortName}/{currentVersion}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.html+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            AppLog.Info("AppUpdate", $"{Repository} has no published release yet; running {currentVersion}");
            return null;
        }

        if (IsRateLimited(response))
            throw new GitHubReleaseCheckException(response.StatusCode, rateLimited: true);

        if (!response.IsSuccessStatusCode)
            throw new GitHubReleaseCheckException(response.StatusCode, rateLimited: false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return Read(json, currentVersion);
    }

    // Split out so the parsing is tested without a network.
    public static AppUpdateInfo? Read(string json, string currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = String(root, "tag_name");
        if (!SemanticVersion.TryParse(tag, out var latest))
        {
            AppLog.Info("AppUpdate", $"newest GitHub release's tag '{tag}' is not a version; running {currentVersion}");
            return null;
        }

        var latestText = latest.Value.ToString();
        var changeKind = SemanticVersion.Classify(currentVersion, latestText);
        AppLog.Info("AppUpdate", $"running {currentVersion}, newest on GitHub {latestText} ({changeKind?.ToString() ?? "unknown"})");

        return new AppUpdateInfo
        {
            CurrentVersion = currentVersion,
            LatestVersion = latestText,
            ChangeKind = changeKind,
            // Fork (1.3.0): the release zip's own download link, for Install update on About - which
            // asks first, every time. Null when the release has no SSPTMM zip, so CanInstall is false.
            DownloadUrl = ZipAsset(root)?.Url,
            ModPageUrl = String(root, "html_url") is { Length: > 0 } page
                ? page
                : $"https://github.com/{Repository}/releases/latest",
            Changelog = String(root, "body_html"),
            DownloadSizeBytes = ZipAsset(root)?.Bytes,
            PublishedAt = root.TryGetProperty("published_at", out var published)
                && published.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(published.GetString(), out var at)
                    ? at
                    : null,
        };
    }

    // The release's SSPTMM-<version>-win-x64.zip: its download link and size.
    private static (string? Url, long? Bytes)? ZipAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = String(asset, "name");
            if (name is null
                || !name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase)) continue;

            // Only GitHub's own download address for it - nothing else is fetched from here.
            // Compared as a parsed address, so "..", escapes or another host can't pass for it.
            var url = String(asset, "browser_download_url");
            if (url is not null
                && !(Uri.TryCreate(url, UriKind.Absolute, out var uri)
                     && uri.Scheme == Uri.UriSchemeHttps
                     && uri.IsDefaultPort
                     && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                     && string.IsNullOrEmpty(uri.UserInfo)
                     && uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase)))
            {
                url = null;
            }

            return (url, asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null);
        }

        return null;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // GitHub's primary limit answers 403 or 429 with x-ratelimit-remaining: 0; its secondary limit
    // answers either with a retry-after header.
    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)) return false;
        if (response.Headers.RetryAfter is not null) return true;

        return response.Headers.TryGetValues("x-ratelimit-remaining", out var values)
            && values.FirstOrDefault() == "0";
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}

public sealed class GitHubReleaseCheckException(HttpStatusCode status, bool rateLimited)
    : Exception($"GitHub answered {(int)status} {status}{(rateLimited ? " (rate limited)" : "")}")
{
    public HttpStatusCode Status { get; } = status;

    public bool RateLimited { get; } = rateLimited;
}
