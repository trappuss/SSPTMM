using System.Net;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork: the check for a newer SSPTMM on GitHub. The JSON is the shape GitHub's
// /repos/trappuss/SSPTMM/releases/latest returned for v1.0.0 on 2026-10-03 (html media type, so
// body_html rather than body), cut down to the fields read plus a few that are not.
//
public class ForkGitHubReleaseCheckTests
{
    private static string Release(string tag, string zipName = "SSPTMM-1.0.0-win-x64.zip", long zipSize = 67467802) => $$"""
        {
          "url": "https://api.github.com/repos/trappuss/SSPTMM/releases/402513551",
          "html_url": "https://github.com/trappuss/SSPTMM/releases/tag/{{tag}}",
          "id": 402513551,
          "tag_name": "{{tag}}",
          "name": "SSPTMM {{tag}}",
          "draft": false,
          "prerelease": false,
          "immutable": false,
          "created_at": "2026-10-03T12:40:02Z",
          "published_at": "2026-10-03T12:56:11Z",
          "assets": [
            {
              "name": "{{zipName}}",
              "content_type": "application/zip",
              "size": {{zipSize}},
              "download_count": 3,
              "browser_download_url": "https://github.com/trappuss/SSPTMM/releases/download/{{tag}}/{{zipName}}"
            }
          ],
          "body_html": "<h1>SSPTMM</h1>\n<p>Notes.</p>"
        }
        """;

    private sealed class Answer(HttpStatusCode status, string body, Action<HttpResponseMessage>? headers = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked = request;
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            headers?.Invoke(response);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void A_newer_minor_release_is_an_update_with_its_page_notes_size_and_date()
    {
        var info = GitHubReleaseCheck.Read(Release("v1.1.0", "SSPTMM-1.1.0-win-x64.zip", 70_000_000), "1.0.0")!;

        Assert.Equal("1.1.0", info.LatestVersion);
        Assert.Equal(VersionChangeKind.Minor, info.ChangeKind);
        Assert.True(info.IsUpdate);
        Assert.True(info.CanInstall); // 1.3.0: installable from About, after a confirmation
        Assert.Equal("https://github.com/trappuss/SSPTMM/releases/download/v1.1.0/SSPTMM-1.1.0-win-x64.zip", info.DownloadUrl);
        Assert.Equal("https://github.com/trappuss/SSPTMM/releases/tag/v1.1.0", info.ModPageUrl);
        Assert.Equal(70_000_000, info.DownloadSizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 12, 56, 11, TimeSpan.Zero), info.PublishedAt);
        Assert.Contains("<p>Notes.</p>", info.Changelog);
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0", VersionChangeKind.None)]
    [InlineData("v1.0.0", "1.1.0", VersionChangeKind.None)]   // running a newer build than released
    [InlineData("v1.0.1", "1.0.0", VersionChangeKind.Patch)]
    [InlineData("V2.0.0", "1.4.2", VersionChangeKind.Major)]
    [InlineData("1.2.0", "1.1.0", VersionChangeKind.Minor)]   // a tag without the v
    public void The_tag_is_compared_with_the_running_version(string tag, string running, VersionChangeKind expected) =>
        Assert.Equal(expected, GitHubReleaseCheck.Read(Release(tag), running)!.ChangeKind);

    [Fact]
    public void A_tag_that_is_not_a_version_is_no_answer_rather_than_a_guess() =>
        Assert.Null(GitHubReleaseCheck.Read(Release("latest-build"), "1.0.0"));

    [Fact]
    public void Only_the_release_zip_counts_for_the_size()
    {
        Assert.Null(GitHubReleaseCheck.Read(Release("v1.1.0", "source.tar.gz"), "1.0.0")!.DownloadSizeBytes);
        Assert.Equal(5, GitHubReleaseCheck.Read(Release("v1.1.0", "ssptmm-1.1.0-WIN-X64.ZIP", 5), "1.0.0")!.DownloadSizeBytes);
    }

    [Fact]
    public void A_release_with_no_page_link_falls_back_to_the_latest_release_page()
    {
        var info = GitHubReleaseCheck.Read("""{ "tag_name": "v1.1.0" }""", "1.0.0")!;

        Assert.Equal("https://github.com/trappuss/SSPTMM/releases/latest", info.ModPageUrl);
        Assert.Null(info.Changelog);
        Assert.Null(info.DownloadSizeBytes);
        Assert.Null(info.PublishedAt);
    }

    [Fact]
    public async Task It_asks_GitHub_for_the_latest_release_with_a_user_agent_and_the_html_notes()
    {
        var handler = new Answer(HttpStatusCode.OK, Release("v1.1.0"));
        using var check = new GitHubReleaseCheck(new HttpClient(handler));

        var info = await check.CheckAsync("1.0.0");

        Assert.True(info!.IsUpdate);
        Assert.Equal("https://api.github.com/repos/trappuss/SSPTMM/releases/latest", handler.Asked!.RequestUri!.ToString());
        Assert.Contains("SSPTMM/1.0.0", handler.Asked.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github.html+json", handler.Asked.Headers.Accept.ToString());
    }

    [Fact]
    public async Task No_release_yet_is_no_answer_not_an_error()
    {
        using var check = new GitHubReleaseCheck(new HttpClient(new Answer(HttpStatusCode.NotFound, """{"message":"Not Found"}""")));

        Assert.Null(await check.CheckAsync("1.0.0"));
    }

    [Fact]
    public async Task A_spent_rate_limit_is_said_as_such()
    {
        using var check = new GitHubReleaseCheck(new HttpClient(new Answer(HttpStatusCode.Forbidden, "{}",
            r => r.Headers.Add("x-ratelimit-remaining", "0"))));

        var error = await Assert.ThrowsAsync<GitHubReleaseCheckException>(() => check.CheckAsync("1.0.0"));
        Assert.True(error.RateLimited);
    }

    [Fact]
    public async Task Another_refusal_is_an_error_but_not_a_rate_limit()
    {
        using var check = new GitHubReleaseCheck(new HttpClient(new Answer(HttpStatusCode.Forbidden, "{}",
            r => r.Headers.Add("x-ratelimit-remaining", "41"))));

        var error = await Assert.ThrowsAsync<GitHubReleaseCheckException>(() => check.CheckAsync("1.0.0"));
        Assert.False(error.RateLimited);
        Assert.Equal(HttpStatusCode.Forbidden, error.Status);
    }

    [Fact]
    public void A_release_without_its_zip_is_not_installable()
    {
        var info = GitHubReleaseCheck.Read(Release("v1.1.0", "source.tar.gz"), "1.0.0")!;

        Assert.True(info.IsUpdate);
        Assert.False(info.CanInstall);
        Assert.Null(info.DownloadUrl);
    }

    [Fact]
    public void A_download_link_off_the_repositorys_releases_is_never_used()
    {
        var json = Release("v1.1.0", "SSPTMM-1.1.0-win-x64.zip")
            .Replace("https://github.com/trappuss/SSPTMM/releases/download/", "https://example.com/elsewhere/");

        var info = GitHubReleaseCheck.Read(json, "1.0.0")!;

        Assert.False(info.CanInstall);
        Assert.Equal(67467802, info.DownloadSizeBytes);
    }

    [Theory]
    [InlineData("https://github.com/trappuss/SSPTMM/releases/download/../../../../other/repo/releases/download/")]
    [InlineData("https://github.com/trappuss/SSPTMM/releases/download/%2e%2e/%2e%2e/%2e%2e/%2e%2e/other/repo/releases/download/")]
    [InlineData("https://github.com.example.com/trappuss/SSPTMM/releases/download/")]
    [InlineData("http://github.com/trappuss/SSPTMM/releases/download/")]
    [InlineData("https://user@github.com/trappuss/SSPTMM/releases/download/")]
    [InlineData("https://github.com:8443/trappuss/SSPTMM/releases/download/")]
    public void A_link_that_only_looks_like_the_repositorys_is_never_used(string prefix)
    {
        var json = Release("v1.1.0", "SSPTMM-1.1.0-win-x64.zip")
            .Replace("https://github.com/trappuss/SSPTMM/releases/download/", prefix);

        Assert.False(GitHubReleaseCheck.Read(json, "1.0.0")!.CanInstall);
    }

    [Fact]
    public void The_running_version_or_older_is_not_installable()
    {
        Assert.False(GitHubReleaseCheck.Read(Release("v1.0.0", "SSPTMM-1.0.0-win-x64.zip"), "1.0.0")!.CanInstall);
    }
}
