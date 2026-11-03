using System.Net;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork (1.4.7): mods installed from a GitHub repository's releases. The JSON is the shape GitHub's
// /repos/{owner}/{repo}/releases answers with, cut down to the fields read plus a few that are not.
//
public class ForkGitHubModsTests : IDisposable
{
    private const string Repo = "someone/CoolMod";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ghmods-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static string Asset(string tag, string name, long size = 1000, string repo = Repo) => $$"""
        { "name": "{{name}}", "size": {{size}}, "download_count": 4,
          "browser_download_url": "https://github.com/{{repo}}/releases/download/{{tag}}/{{name}}" }
        """;

    private static string ReleaseJson(long id, string tag, string published, bool prerelease = false, bool draft = false, params string[] assets) => $$"""
        { "id": {{id}}, "tag_name": "{{tag}}", "name": "CoolMod {{tag}}",
          "html_url": "https://github.com/{{Repo}}/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "{{published}}",
          "assets": [{{string.Join(",", assets)}}] }
        """;

    private static string List(params string[] releases) => "[" + string.Join(",", releases) + "]";

    private sealed class Answers : HttpMessageHandler
    {
        public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Next { get; } = new();
        public List<HttpRequestMessage> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked.Add(request);
            return Task.FromResult(Next.Dequeue()(request));
        }
    }

    private static HttpResponseMessage Ok(string body, string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    [Theory]
    [InlineData("someone/CoolMod", "someone/CoolMod")]
    [InlineData("https://github.com/someone/CoolMod", "someone/CoolMod")]
    [InlineData("github.com/someone/CoolMod.git", "someone/CoolMod")]
    [InlineData("https://www.github.com/someone/Cool.Mod_2/releases/tag/v1.0", "someone/Cool.Mod_2")]
    [InlineData("  http://github.com/someone/CoolMod/  ", "someone/CoolMod")]
    public void A_repository_is_found_in_what_is_pasted(string text, string expected) =>
        Assert.Equal(expected, GitHubMods.RepositoryFrom(text));

    [Theory]
    [InlineData("")]
    [InlineData("CoolMod")]
    [InlineData("https://gitlab.com/someone/CoolMod")]
    [InlineData("https://github.com/someone")]
    [InlineData("https://evil.example/github.com/someone/CoolMod")]
    [InlineData("someone/..")]
    public void Anything_else_is_not_a_repository(string text) =>
        Assert.Null(GitHubMods.RepositoryFrom(text));

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V2.0", "2.0")]
    [InlineData("1.0.0-beta", "1.0.0-beta")]
    [InlineData("version-3", "version-3")]
    public void A_tag_is_a_version_without_its_v(string tag, string version) =>
        Assert.Equal(version, GitHubMods.VersionOf(tag));

    [Fact]
    public void A_file_of_the_same_kind_has_the_same_shape_whatever_its_version()
    {
        Assert.Equal(GitHubMods.ShapeOf("CoolMod-1.2.3.zip"), GitHubMods.ShapeOf("CoolMod-1.3.zip"));
        Assert.Equal("coolmod-#.7z", GitHubMods.ShapeOf("CoolMod-1.5.0.7z"));
        Assert.Equal(GitHubMods.ShapeOf("CoolMod-v1.2.3-client.7z"), GitHubMods.ShapeOf("coolmod-v2.0.0-client.7z"));
        Assert.NotEqual(GitHubMods.ShapeOf("CoolMod-1.2.3-client.zip"), GitHubMods.ShapeOf("CoolMod-1.2.3-server.zip"));
    }

    [Fact]
    public void Releases_are_read_newest_first_without_drafts_or_prereleases_or_other_hosts()
    {
        var json = List(
            ReleaseJson(1, "v1.0.0", "2026-10-01T00:00:00Z", assets: Asset("v1.0.0", "CoolMod-1.0.0.zip")),
            ReleaseJson(3, "v1.2.0-beta", "2026-10-20T00:00:00Z", prerelease: true, assets: Asset("v1.2.0-beta", "CoolMod-1.2.0-beta.zip")),
            ReleaseJson(4, "v2.0.0", "2026-10-25T00:00:00Z", draft: true, assets: Asset("v2.0.0", "CoolMod-2.0.0.zip")),
            ReleaseJson(2, "v1.1.0", "2026-10-10T00:00:00Z", assets:
            [
                Asset("v1.1.0", "CoolMod-1.1.0.zip", 2048),
                Asset("v1.1.0", "Elsewhere.zip", repo: "other/Repo"),
            ]));

        var releases = GitHubMods.ReadReleases(json, Repo);

        Assert.Equal(["1.1.0", "1.0.0"], releases.Select(r => r.Version));
        var newest = Assert.Single(releases[0].Assets);
        Assert.Equal("CoolMod-1.1.0.zip", newest.Name);
        Assert.Equal(2048, newest.Size);
    }

    [Fact]
    public void A_repository_of_only_prereleases_still_has_them()
    {
        var json = List(ReleaseJson(1, "v0.1.0", "2026-10-01T00:00:00Z", prerelease: true, assets: Asset("v0.1.0", "CoolMod.zip")));

        Assert.Single(GitHubMods.ReadReleases(json, Repo));
    }

    [Fact]
    public void The_file_installed_is_the_only_archive_or_the_one_shaped_like_the_one_chosen()
    {
        var release = GitHubMods.ReadReleases(List(ReleaseJson(1, "v1.1.0", "2026-10-10T00:00:00Z", assets:
        [
            Asset("v1.1.0", "CoolMod-1.1.0-client.zip"),
            Asset("v1.1.0", "CoolMod-1.1.0-server.zip"),
            Asset("v1.1.0", "CoolMod.pdb"),
        ])), Repo)[0];

        Assert.Equal(2, GitHubMods.ArchivesOf(release).Count);
        Assert.Null(GitHubMods.AssetFor(release, shape: null));
        Assert.Equal("CoolMod-1.1.0-server.zip", GitHubMods.AssetFor(release, GitHubMods.ShapeOf("CoolMod-1.0.0-server.zip"))!.Name);
    }

    [Fact]
    public async Task A_remembered_repository_is_a_listing_whose_versions_are_its_releases()
    {
        var answers = new Answers();
        answers.Next.Enqueue(_ => Ok(List(
            ReleaseJson(2, "v1.1.0", "2026-10-10T00:00:00Z", assets: Asset("v1.1.0", "CoolMod-1.1.0.zip", 3000)),
            ReleaseJson(1, "v1.0.0", "2026-10-01T00:00:00Z", assets: Asset("v1.0.0", "CoolMod-1.0.0.zip"))), "\"a\""));

        using var mods = new GitHubMods(new HttpClient(answers), Path.Combine(_dir, "github_mods.json"));
        var (releases, etag) = await mods.ReleasesAsync(Repo);
        var entry = mods.Remember(Repo, releases, etag, releases[1].Assets[0]);

        Assert.True(entry.Id < 0);
        Assert.Equal(GitHubMods.IdFor(Repo), entry.Id);
        Assert.Contains("User-Agent", answers.Asked[0].Headers.Select(h => h.Key));

        var mod = mods.ModFor(entry.Id)!;
        Assert.Equal("CoolMod", mod.Name);
        Assert.Equal("someone", mod.Owner!.Name);
        Assert.Equal("https://github.com/someone/CoolMod/releases", mod.DetailUrl);
        Assert.Equal(["1.1.0", "1.0.0"], mod.Versions!.Select(v => v.Version));

        var update = mods.VersionFor(entry.Id, "1.1.0")!;
        Assert.Equal("https://github.com/someone/CoolMod/releases/download/v1.1.0/CoolMod-1.1.0.zip", update.Link);
        Assert.Equal(3000, update.ContentLength);

        // Kept on disk for the next start.
        using var again = new GitHubMods(new HttpClient(new Answers()), Path.Combine(_dir, "github_mods.json"));
        Assert.Equal(Repo, again.RepositoryOf(entry.Id));
    }

    [Fact]
    public async Task A_recheck_sends_the_etag_and_an_unchanged_answer_changes_nothing()
    {
        var answers = new Answers();
        answers.Next.Enqueue(_ => Ok(List(ReleaseJson(1, "v1.0.0", "2026-10-01T00:00:00Z", assets: Asset("v1.0.0", "CoolMod-1.0.0.zip"))), "\"a\""));
        answers.Next.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        using var mods = new GitHubMods(new HttpClient(answers), Path.Combine(_dir, "github_mods.json"));
        var (releases, etag) = await mods.ReleasesAsync(Repo);
        var entry = mods.Remember(Repo, releases, etag, releases[0].Assets[0]);
        entry.CheckedAt = DateTimeOffset.UtcNow - GitHubMods.RecheckAfter - TimeSpan.FromMinutes(1);

        Assert.False(await mods.RefreshAsync([entry.Id]));
        Assert.Equal("\"a\"", answers.Asked[1].Headers.GetValues("If-None-Match").Single());

        // Checked just now: not asked again.
        Assert.False(await mods.RefreshAsync([entry.Id]));
        Assert.Equal(2, answers.Asked.Count);
    }

    [Fact]
    public async Task A_newer_release_found_on_a_recheck_is_a_new_version()
    {
        var answers = new Answers();
        answers.Next.Enqueue(_ => Ok(List(ReleaseJson(1, "v1.0.0", "2026-10-01T00:00:00Z", assets: Asset("v1.0.0", "CoolMod-1.0.0.zip"))), "\"a\""));
        answers.Next.Enqueue(_ => Ok(List(
            ReleaseJson(2, "v1.1.0", "2026-10-10T00:00:00Z", assets: Asset("v1.1.0", "CoolMod-1.1.0.zip")),
            ReleaseJson(1, "v1.0.0", "2026-10-01T00:00:00Z", assets: Asset("v1.0.0", "CoolMod-1.0.0.zip"))), "\"b\""));

        using var mods = new GitHubMods(new HttpClient(answers), Path.Combine(_dir, "github_mods.json"));
        var (releases, etag) = await mods.ReleasesAsync(Repo);
        var entry = mods.Remember(Repo, releases, etag, releases[0].Assets[0]);
        entry.CheckedAt = null;

        Assert.True(await mods.RefreshAsync([entry.Id]));
        Assert.Equal("1.1.0", mods.ModFor(entry.Id)!.Versions![0].Version);
    }

    [Fact]
    public async Task A_missing_repository_is_a_404()
    {
        var answers = new Answers();
        answers.Next.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });

        using var mods = new GitHubMods(new HttpClient(answers), Path.Combine(_dir, "github_mods.json"));
        var ex = await Assert.ThrowsAsync<GitHubReleaseCheckException>(() => mods.ReleasesAsync(Repo));
        Assert.Equal(HttpStatusCode.NotFound, ex.Status);
    }
}
