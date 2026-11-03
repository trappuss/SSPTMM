using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.7): mods installed straight from a GitHub repository's releases, for the ones not on
// sp-mod.com or for people who would rather not go through it. A repository is remembered here
// under a local id (LocalArchive.IdFor, so negative and never an sp-mod.com id) with the releases
// last read from it; ModFor turns those into a Mod whose versions are the releases, so the card,
// Update, Update all and Skip this update treat it as they treat a listing.
//
// One request per repository to GitHub's public REST API, GET /repos/{owner}/{repo}/releases, with
// the ETag of the last answer: an unchanged list comes back 304, which GitHub doesn't count against
// the 60 requests an hour an address gets without signing in.
//
public sealed partial class GitHubMods : IDisposable
{
    private const int SchemaVersion = 1;

    // HUNCH: how long a repository's releases are trusted before they are asked for again.
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(30);

    // The archive types a release's files are taken from - Install from file's.
    private static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz"];

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private Dictionary<int, Entry>? _entries;

    public GitHubMods(HttpClient? httpClient = null, string? filePath = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "github_mods.json");
    }

    /// <summary>A repository mods are installed from.</summary>
    public sealed class Entry
    {
        public int Id { get; set; }

        // "owner/name", as GitHub spells it.
        public string Repository { get; set; } = string.Empty;

        // The chosen file's name with its numbers taken out (ShapeOf), so a later release's file
        // of the same kind is found again however its version is written.
        public string? AssetShape { get; set; }

        public string? ETag { get; set; }
        public DateTimeOffset? CheckedAt { get; set; }
        public List<Release> Releases { get; set; } = [];
    }

    public sealed class Release
    {
        public long Id { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Page { get; set; }
        public bool Prerelease { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public List<Asset> Assets { get; set; } = [];

        // The tag as a version: "v1.2.3" is 1.2.3.
        public string Version => VersionOf(Tag);
    }

    public sealed class Asset
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public long? Size { get; set; }
    }

    private sealed class Stored
    {
        public int SchemaVersion { get; set; }
        public List<Entry> Repositories { get; set; } = [];
    }

    /// <summary>The id a repository's mod is recorded under.</summary>
    public static int IdFor(string repository) => LocalArchive.IdFor("github:" + repository);

    public static string PageOf(string repository) => "https://github.com/" + repository;

    public static string ReleasesPageOf(string repository) => PageOf(repository) + "/releases";

    // The owner's picture, as GitHub serves it for any account.
    public static string OwnerPictureOf(string repository) =>
        "https://github.com/" + repository.Split('/')[0] + ".png?size=128";

    //
    // "owner/name", from what a user would paste: the two names, the repository's address with or
    // without https:// and .git, or any page under it (a release, a file). Null for anything else.
    //
    public static string? RepositoryFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = RepositoryPattern().Match(text.Trim());
        if (!match.Success) return null;

        var owner = match.Groups["owner"].Value;
        var name = match.Groups["name"].Value;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        return name.Length == 0 || name is "." or ".." ? null : owner + "/" + name;
    }

    [GeneratedRegex(@"^(?:(?:https?://)?(?:www\.)?github\.com/)?(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<name>[A-Za-z0-9._-]{1,100})(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex RepositoryPattern();

    /// <summary>A tag as a version: a leading "v" goes ("v1.2.3" is 1.2.3).</summary>
    public static string VersionOf(string tag)
    {
        var trimmed = tag.Trim();
        return trimmed.Length > 1 && trimmed[0] is 'v' or 'V' && char.IsDigit(trimmed[1]) ? trimmed[1..] : trimmed;
    }

    /// <summary>A file name with its numbers taken out: "MyMod-1.2.3.zip" and "MyMod-1.3.zip" are
    /// both "mymod-#.zip". The archive type is kept as it is - the 7 of ".7z" is not a version.</summary>
    public static string ShapeOf(string assetName)
    {
        var name = assetName.ToLowerInvariant();
        var type = ArchiveExtensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.Ordinal)) ?? string.Empty;
        return NumbersPattern().Replace(name[..^type.Length], "#") + type;
    }

    [GeneratedRegex(@"\d+(?:\.\d+)*")]
    private static partial Regex NumbersPattern();

    /// <summary>The release's archives - what a mod is installed from. Source code GitHub adds to
    /// every release isn't among a release's files, so it is never offered.</summary>
    public static List<Asset> ArchivesOf(Release release) =>
        [.. release.Assets.Where(a => ArchiveExtensions.Any(e => a.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))];

    //
    // The file of this release to install: the one shaped like the file chosen before, else the only
    // archive. Null when there is none, or several and nothing to choose between them by.
    //
    public static Asset? AssetFor(Release release, string? shape)
    {
        var archives = ArchivesOf(release);
        if (shape is not null && archives.FirstOrDefault(a => ShapeOf(a.Name) == shape) is { } same) return same;

        return archives.Count == 1 ? archives[0] : null;
    }

    //
    // The releases in GitHub's answer, newest first: drafts left out, and pre-releases when there is
    // a full release - a repository that has only ever made pre-releases still has something to
    // install. A file whose address isn't GitHub's own download address for this repository is left
    // out, so nothing else is ever fetched from here.
    //
    public static List<Release> ReadReleases(string json, string repository)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

        var releases = new List<Release>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (Bool(element, "draft")) continue;
            if (String(element, "tag_name") is not { Length: > 0 } tag) continue;

            var release = new Release
            {
                Id = element.TryGetProperty("id", out var id) && id.TryGetInt64(out var number) ? number : 0,
                Tag = tag,
                Name = String(element, "name"),
                Page = String(element, "html_url"),
                Prerelease = Bool(element, "prerelease"),
                PublishedAt = String(element, "published_at") is { } at && DateTimeOffset.TryParse(at, out var when) ? when : null,
            };

            if (element.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (String(asset, "name") is not { Length: > 0 } name) continue;
                    if (String(asset, "browser_download_url") is not { } url || !IsDownloadOf(url, repository)) continue;

                    release.Assets.Add(new Asset
                    {
                        Name = name,
                        Url = url,
                        Size = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null,
                    });
                }
            }

            releases.Add(release);
        }

        if (releases.Any(r => !r.Prerelease)) releases.RemoveAll(r => r.Prerelease);
        return [.. releases.OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue)];
    }

    // Compared as a parsed address, so "..", escapes or another host can't pass for it.
    private static bool IsDownloadOf(string url, string repository) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase);

    //
    // The repository as a listing: its releases that have a file to install are its versions, newest
    // first. Null for an id that isn't a repository's.
    //
    public Mod? ModFor(int id)
    {
        if (Find(id) is not { } entry) return null;

        var versions = entry.Releases
            .Select(r => (Release: r, Asset: AssetFor(r, entry.AssetShape)))
            .Where(r => r.Asset is not null)
            .Select(r => new ModVersionSummary
            {
                Id = (int)(r.Release.Id & 0x7FFFFFFF),
                Version = r.Release.Version,
                PublishedAt = r.Release.PublishedAt,
                ContentLength = r.Asset!.Size,
            })
            .ToList();

        var owner = entry.Repository.Split('/')[0];
        return new Mod
        {
            Id = id,
            Name = entry.Repository.Split('/')[1],
            DetailUrl = ReleasesPageOf(entry.Repository),
            Owner = new Owner { Name = owner },
            Thumbnail = OwnerPictureOf(entry.Repository),
            Versions = versions,
            UpdatedAt = versions.FirstOrDefault()?.PublishedAt,
        };
    }

    /// <summary>The download of one version, for the queue. Null when no release of it has a file.</summary>
    public ModVersion? VersionFor(int id, string version)
    {
        if (Find(id) is not { } entry) return null;

        foreach (var release in entry.Releases.Where(r => r.Version == version))
        {
            if (AssetFor(release, entry.AssetShape) is not { } asset) continue;

            return new ModVersion
            {
                Id = (int)(release.Id & 0x7FFFFFFF),
                Version = release.Version,
                Link = asset.Url,
                ContentLength = asset.Size,
                PublishedAt = release.PublishedAt,
            };
        }

        return null;
    }

    public string? RepositoryOf(int id) => Find(id)?.Repository;

    public Entry? Find(int id)
    {
        lock (_gate) return Entries.GetValueOrDefault(id);
    }

    /// <summary>Remembers a repository and the file chosen from it; installing from it again
    /// replaces what was remembered.</summary>
    public Entry Remember(string repository, List<Release> releases, string? etag, Asset chosen)
    {
        var entry = new Entry
        {
            Id = IdFor(repository),
            Repository = repository,
            AssetShape = ShapeOf(chosen.Name),
            ETag = etag,
            CheckedAt = DateTimeOffset.UtcNow,
            Releases = releases,
        };

        lock (_gate)
        {
            Entries[entry.Id] = entry;
            Save();
        }

        AppLog.Info("GitHub", $"{repository}: remembered, installing {chosen.Name}");
        return entry;
    }

    /// <summary>A repository's releases, newest first, and the ETag of the answer.</summary>
    public async Task<(List<Release> Releases, string? ETag)> ReleasesAsync(string repository, CancellationToken ct = default)
    {
        var (releases, etag, _) = await FetchAsync(repository, null, ct).ConfigureAwait(false);
        return (releases ?? [], etag);
    }

    //
    // Reads again the releases of the repositories among <paramref name="ids"/> not read for
    // RecheckAfter. True when any of them changed. A repository that can't be read keeps what it
    // had; a rate limit stops the round, as every other request would meet it too.
    //
    public async Task<bool> RefreshAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var due = ids.Distinct()
            .Select(Find)
            .OfType<Entry>()
            .Where(e => e.CheckedAt is not { } at || DateTimeOffset.UtcNow - at > RecheckAfter)
            .ToList();

        var changed = false;
        foreach (var entry in due)
        {
            try
            {
                var (releases, etag, modified) = await FetchAsync(entry.Repository, entry.ETag, ct).ConfigureAwait(false);
                lock (_gate)
                {
                    entry.CheckedAt = DateTimeOffset.UtcNow;
                    if (modified)
                    {
                        entry.Releases = releases ?? [];
                        entry.ETag = etag;
                        changed = true;
                    }
                }
            }
            catch (GitHubReleaseCheckException ex) when (ex.RateLimited)
            {
                AppLog.Warn("GitHub", $"rate limited reading {entry.Repository}; trying again later");
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or GitHubReleaseCheckException or JsonException
                                           or TaskCanceledException && !ct.IsCancellationRequested)
            {
                AppLog.Warn("GitHub", $"couldn't read {entry.Repository}'s releases: {ex.Message}");
            }
        }

        if (due.Count > 0)
        {
            lock (_gate) Save();
        }

        return changed;
    }

    // Null releases on 304 (not modified). Throws GitHubReleaseCheckException for an error answer -
    // 404 when the repository doesn't exist or is private.
    private async Task<(List<Release>? Releases, string? ETag, bool Modified)> FetchAsync(
        string repository, string? etag, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=30");
        // GitHub refuses API calls without a User-Agent.
        request.Headers.UserAgent.ParseAdd($"{SelfMod.ShortName}/{AppVersion.Current}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified) return (null, etag, false);

        if (GitHubReleaseCheck.IsRateLimited(response))
            throw new GitHubReleaseCheckException(response.StatusCode, rateLimited: true);

        if (!response.IsSuccessStatusCode)
            throw new GitHubReleaseCheckException(response.StatusCode, rateLimited: false);

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (ReadReleases(json, repository), response.Headers.ETag?.ToString(), true);
    }

    private Dictionary<int, Entry> Entries => _entries ??= Load();

    private Dictionary<int, Entry> Load()
    {
        if (!File.Exists(_filePath)) return [];

        try
        {
            var data = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_filePath));
            if (data is null || data.SchemaVersion != SchemaVersion) return [];
            return data.Repositories.Where(e => e.Repository.Length > 0).ToDictionary(e => e.Id);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            SafeFile.PreserveDamaged(_filePath);
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var data = new Stored { SchemaVersion = SchemaVersion, Repositories = [.. Entries.Values] };
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(data));
        }
        catch (IOException ex)
        {
            AppLog.Warn("GitHub", $"couldn't save {_filePath}: {ex.Message}");
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
