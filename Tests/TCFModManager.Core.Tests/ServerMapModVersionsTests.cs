using System.Net;
using System.Reflection;
using TCFModManager.Core.ServerMap;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

public class ServerMapModVersionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smv-" + Guid.NewGuid().ToString("N"));

    public ServerMapModVersionsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Any real .NET assembly stands in for the mod's: what matters is that its version is read back.
    private static string SomeAssembly => typeof(ServerMapModVersions).Assembly.Location;

    private static string ExpectedVersion
    {
        get
        {
            var informational = typeof(ServerMapModVersions).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
    }

    private string Place(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(SomeAssembly, path);
        return path;
    }

    [Theory]
    [InlineData("0.1.0", "0.2.0", true)]
    [InlineData("0.2.0", "0.2.1", true)]
    [InlineData("0.2.0", "1.0.0", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.3.0", "0.2.0", false)]
    [InlineData(null, "0.2.0", false)]
    [InlineData("0.2.0", null, false)]
    [InlineData("junk", "0.2.0", false)]
    public void IsBehind_only_claims_a_newer_release_it_can_read(string? installed, string? latest, bool behind) =>
        Assert.Equal(behind, ServerMapModVersions.IsBehind(installed, latest));

    [Fact]
    public void VersionOf_reads_the_informational_version_without_the_commit()
    {
        Assert.Equal(ExpectedVersion, ServerMapModVersions.VersionOf(SomeAssembly));
    }

    [Fact]
    public void VersionOf_a_file_that_is_not_an_assembly_is_null()
    {
        var path = Path.Combine(_root, "not.dll");
        File.WriteAllText(path, "not a dll");

        Assert.Null(ServerMapModVersions.VersionOf(path));
        Assert.Null(ServerMapModVersions.VersionOf(Path.Combine(_root, "missing.dll")));
    }

    [Fact]
    public void Nothing_installed_is_null()
    {
        Assert.Null(ServerMapModVersions.FindInstalled(_root, appDirectory: _root));
    }

    // SPT 4.1: the app in <root>\TCFModManager, the server in <root>\SPT_Runtime.
    [Fact]
    public void The_payload_and_the_41_stub_are_found_from_the_apps_own_folder()
    {
        var payload = Place("TCFModManager", "ServerMap", "payload", ServerMapAddon.PayloadFileName);
        var stub = Place("SPT_Runtime", "user", "mods", ServerMapAddon.StubFolderName, ServerMapAddon.StubFileName);
        var app = Path.Combine(_root, "TCFModManager");

        var found = ServerMapModVersions.FindInstalled(sptInstallPath: null, appDirectory: app)!;

        Assert.Equal(payload, found.PayloadPath);
        Assert.Equal(ExpectedVersion, found.PayloadVersion);
        Assert.Equal(stub, found.StubPath);
        Assert.Equal(ExpectedVersion, found.StubVersion);
        Assert.Equal(ServerMapSptLine.Spt41, found.StubLine);
        Assert.False(found.StubBehindPayload);
    }

    // SPT 4.0.13: the server in <root>\SPT, found from the configured install folder.
    [Fact]
    public void The_40_stub_is_found_from_the_configured_install()
    {
        Place("TCFModManager", "ServerMap", "payload", ServerMapAddon.PayloadFileName);
        var stub = Place("SPT", "user", "mods", ServerMapAddon.StubFolderName, ServerMapAddon.StubFileName);

        var found = ServerMapModVersions.FindInstalled(sptInstallPath: _root, appDirectory: Path.Combine(_root, "elsewhere"))!;

        Assert.Equal(stub, found.StubPath);
        Assert.Equal(ServerMapSptLine.Spt40, found.StubLine);
    }

    [Fact]
    public void A_payload_without_a_stub_still_reports_the_payload()
    {
        Place("TCFModManager", "ServerMap", "payload", ServerMapAddon.PayloadFileName);

        var found = ServerMapModVersions.FindInstalled(_root, appDirectory: _root)!;

        Assert.Null(found.StubPath);
        Assert.Null(found.StubVersion);
        Assert.Null(found.StubLine);
        Assert.False(found.StubBehindPayload);
    }

    [Theory]
    [InlineData(ServerMapSptLine.Spt40, "126", "https://sp-mod.com/addon/126/tcf-server-mapper")]
    [InlineData(ServerMapSptLine.Spt41, "142", "https://sp-mod.com/addon/142/tcf-server-mapper-41")]
    public void Each_line_has_its_own_addon(ServerMapSptLine line, string id, string url)
    {
        Assert.Equal(id, ServerMapAddon.AddonId(line));
        Assert.Equal(url, ServerMapAddon.PageUrl(line));
    }

    [Theory]
    [InlineData(ServerMapSptLine.Spt40)]
    [InlineData(ServerMapSptLine.Spt41)]
    public void The_stub_on_disk_decides_the_line(ServerMapSptLine line)
    {
        var installed = new InstalledServerMapMod { PayloadPath = "p", StubLine = line };

        Assert.Equal(line, ServerMapModVersions.LineFor(installed, sptInstallPath: null));
    }

    // Nothing on disk says which line - no stub, no readable server exe - so the current one.
    [Fact]
    public void With_nothing_to_go_on_the_line_is_41()
    {
        Assert.Equal(ServerMapSptLine.Spt41, ServerMapModVersions.LineFor(null, sptInstallPath: null));
        Assert.Equal(ServerMapSptLine.Spt41, ServerMapModVersions.LineFor(null, _root));
    }

    [Fact]
    public void A_stub_older_than_its_payload_is_flagged()
    {
        var mod = new InstalledServerMapMod { PayloadPath = "p", PayloadVersion = "0.2.0", StubPath = "s", StubVersion = "0.1.0" };

        Assert.True(mod.StubBehindPayload);
    }

    private const string VersionsFixture = """
        {"success":true,"data":[
          {"id":3,"version":"0.1.0","description":"<p>old</p>","link":"https://sp-mod.com/a/1","published_at":"2026-09-30T10:00:00.000000Z"},
          {"id":2,"version":"0.2.0","description":"<p>The map.</p>","link":"https://sp-mod.com/a/2","published_at":"2026-09-29T10:00:00.000000Z"},
          {"id":1,"version":"not-a-version","published_at":"2026-09-01T10:00:00.000000Z"}
        ],"links":{},"meta":{}}
        """;

    [Fact]
    public async Task The_newest_release_is_the_highest_version_not_the_latest_published()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, VersionsFixture);
        using var client = new SpModApiClient(new HttpClient(handler));

        var latest = await new ServerMapModUpdateService(client).LatestAsync(ServerMapSptLine.Spt41);

        Assert.Equal("0.2.0", latest!.LatestVersion);
        Assert.Equal("<p>The map.</p>", latest.Changelog);
        Assert.Contains("/api/v0/addon/142/versions", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task The_40_line_asks_its_own_addon()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, VersionsFixture);
        using var client = new SpModApiClient(new HttpClient(handler));

        await new ServerMapModUpdateService(client).LatestAsync(ServerMapSptLine.Spt40);

        Assert.Contains("/api/v0/addon/126/versions", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task A_listing_with_no_readable_version_is_null()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"success":true,"data":[],"links":{},"meta":{}}""");
        using var client = new SpModApiClient(new HttpClient(handler));

        Assert.Null(await new ServerMapModUpdateService(client).LatestAsync(ServerMapSptLine.Spt41));
    }
}
