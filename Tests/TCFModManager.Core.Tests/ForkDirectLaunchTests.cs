using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork: starting the game without SPT's launcher (SptDirectLaunch, SptLauncherApi). The server is a
// fake that answers the launcher routes the way SPT 4.1.6's does: zlib-wrapped JSON, { "Response" },
// lower-case profile fields (SP-Tushonka server-csharp 4.1.6, LauncherV2Callbacks).
//
public class ForkDirectLaunchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ssptmm-direct-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly string _server;

    public ForkDirectLaunchTests()
    {
        _game = Path.Combine(_root, "Game");
        _server = Path.Combine(_game, "SPT_Runtime");
        Directory.CreateDirectory(_server);
        File.WriteAllText(Path.Combine(_game, "EscapeFromTarkov.exe"), "game");
        File.WriteAllText(Path.Combine(_server, "SPT.Server.exe"), "server");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Game(string relative) => Path.Combine(_game, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string full, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    // ---- the fake server -----------------------------------------------------------------------

    private sealed class FakeServer : HttpMessageHandler
    {
        public string Version = "4.1.6";
        public bool HasProfile = true;
        public bool WipeAllowed = true;
        public bool Invalid;
        public object? Mods;
        public object? FikaProfile;
        public List<string> Cookies = [];
        public List<SptBundleEntry> Bundles = [];
        public Dictionary<string, byte[]> BundleFiles = [];
        public List<(string Method, string Path, string? Body)> Calls = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            string? body = request.Content is null ? null : SptLauncherApi.Decompress(await request.Content.ReadAsByteArrayAsync(ct));
            lock (Calls) Calls.Add((request.Method.Method, Uri.UnescapeDataString(path), body));
            if (request.Headers.TryGetValues("Cookie", out var cookies)) lock (Cookies) Cookies.AddRange(cookies);

            if (path.StartsWith(SptLauncherApi.BundleFilePath))
            {
                var name = Uri.UnescapeDataString(path[SptLauncherApi.BundleFilePath.Length..]);
                return BundleFiles.TryGetValue(name, out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            object? answer = path switch
            {
                SptLauncherApi.PingPath => new { Response = "Pong!" },
                SptLauncherApi.VersionPath => new { Response = Version },
                SptLauncherApi.LoginPath => new { Response = HasProfile },
                SptLauncherApi.WipePath => new { Response = WipeAllowed, Profiles = Array.Empty<object>() },
                SptLauncherApi.BundlesPath => Bundles,
                SptLauncherApi.ModsPath => Mods,
                SptLauncherApi.FikaProfilePath => FikaProfile,
                SptLauncherApi.ProfilesPath => new
                {
                    Response = new[] { new { username = "trap", nickname = "Trap", side = "Usec", currlvl = 64, profileId = "6abee519842869498c34024b", edition = "SPT Developer", wipe = false, invalidOrUnloadableProfile = Invalid } },
                },
                _ => null,
            };

            return answer is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SptLauncherApi.Compress(JsonSerializer.Serialize(answer))) };
        }
    }

    // ---- Fork (1.4.7): another PC's server's mods ------------------------------------------------

    [Fact]
    public async Task The_servers_loaded_mods_are_read_as_it_sends_them()
    {
        // The shape a 4.1.6 server answers with: name to metadata, versions as text.
        var server = new FakeServer
        {
            Mods = new
            {
                Response = new Dictionary<string, object>
                {
                    ["Casino"] = new { ModGuid = "com.casino", Name = "Casino", Author = "x", Version = "1.2.0", SptVersion = "~4.1.0", HasPrepatcher = false, License = "MIT" },
                    ["Fika"] = new { ModGuid = "com.fika.server", Name = "Fika", Author = "y", Version = "2.0.1", Url = "https://example.org" },
                },
            },
        };
        using var api = new SptLauncherApi("127.0.0.1:6969", server);

        var mods = await api.ModsAsync();

        Assert.Equal(["com.casino", "com.fika.server"], mods.Select(m => m.ModGuid));
        Assert.Equal(["1.2.0", "2.0.1"], mods.Select(m => m.Version));

        // A server that does not know the question has none to say.
        using var old = new SptLauncherApi("127.0.0.1:6969", new FakeServer());
        Assert.Empty(await old.ModsAsync());
    }

    [Fact]
    public async Task A_fika_server_hands_over_the_profile_asked_for_by_its_id()
    {
        var server = new FakeServer { FikaProfile = new { profile = new { info = new { id = "6abee519842869498c34024b" } }, modData = new { } } };
        using var api = new SptLauncherApi("203.0.113.7:6969", server);

        var json = await api.FikaProfileAsync("6abee519842869498c34024b");

        Assert.Equal("{\"info\":{\"id\":\"6abee519842869498c34024b\"}}", json);
        Assert.Equal(["PHPSESSID=6abee519842869498c34024b"], server.Cookies);

        // No Fika there; an id that is not one is not sent at all.
        using var plain = new SptLauncherApi("203.0.113.7:6969", new FakeServer());
        Assert.Null(await plain.FikaProfileAsync("6abee519842869498c34024b"));
        Assert.Null(await api.FikaProfileAsync("../../etc"));
        Assert.Single(server.Cookies);
    }

    [Fact]
    public void Copies_of_a_remote_profile_are_kept_when_it_changed_and_only_the_newest()
    {
        var copies = new RemoteProfileCopies(Path.Combine(_root, "copies"), keep: 2);
        var at = new DateTimeOffset(2026, 11, 11, 20, 0, 0, TimeSpan.Zero);
        const string id = "6abee519842869498c34024b";

        var first = copies.Save("203.0.113.7:6969", id, "{\"level\":1}", at);
        Assert.NotNull(first);
        Assert.StartsWith(copies.FolderFor("203.0.113.7:6969"), first);
        Assert.DoesNotContain(':', Path.GetFileName(Path.GetDirectoryName(first)!));

        Assert.Null(copies.Save("203.0.113.7:6969", id, "{\"level\":1}", at.AddMinutes(1))); // the same
        Assert.NotNull(copies.Save("203.0.113.7:6969", id, "{\"level\":2}", at.AddMinutes(2)));
        Assert.NotNull(copies.Save("203.0.113.7:6969", id, "{\"level\":3}", at.AddMinutes(3)));

        var kept = Directory.GetFiles(copies.FolderFor("203.0.113.7:6969")).Order().ToList();
        Assert.Equal(2, kept.Count);
        Assert.Equal("{\"level\":3}", File.ReadAllText(kept[^1]));
        Assert.False(File.Exists(first));
    }

    [Fact]
    public void A_collection_from_a_servers_mods_holds_them_at_its_versions_and_turns_nothing_off()
    {
        SptServerMod[] host =
        [
            new() { ModGuid = "com.casino", Name = "Casino Server", Version = "1.2.0" },
            new() { ModGuid = "com.private", Name = "Friend's own mod", Version = "0.1.0" },
            new() { ModGuid = "", Name = "", Version = "1.0.0" },
        ];
        HostMods.CatalogLookup catalog = guid => guid == "com.casino" ? (77, "Casino") : null;
        ModListCapture.VersionLookup versions = id => id == 77
            ? [new ModVersionSummary { Id = 501, Version = "1.1.0" }, new ModVersionSummary { Id = 502, Version = "1.2.0" }]
            : null;
        var now = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

        var list = HostMods.BuildList("Mods on Friend", "203.0.113.7:6969", host, catalog, versions, "4.1.6", null, now);

        Assert.Equal(ModListPolicy.Additive, list.Policy);
        Assert.Equal(ModListOrigin.Local, list.Origin);
        Assert.Equal("203.0.113.7:6969", list.Source);
        Assert.Equal(2, list.Entries.Count);

        var casino = list.Entries.Single(e => e.Guid == "com.casino");
        Assert.Equal("Casino", casino.Name); // as sp-mod.com names it
        Assert.Equal(77, casino.ModId);
        Assert.Equal(502, casino.VersionId);
        Assert.Equal("1.2.0", casino.Version);

        var own = list.Entries.Single(e => e.Guid == "com.private");
        Assert.False(own.IsResolved); // by hand

        // Asked again: the same collection, replaced.
        Assert.Same(list, HostMods.Existing([list], "203.0.113.7:6969"));
        var again = HostMods.BuildList("Mods on Friend", "203.0.113.7:6969", host, catalog, versions, "4.1.6", list, now.AddDays(1));
        Assert.Equal(list.Id, again.Id);
        Assert.Equal(list.CreatedAt, again.CreatedAt);
    }

    [Fact]
    public void What_was_taken_out_of_a_servers_collection_is_not_put_back_by_the_next_look()
    {
        SptServerMod[] host =
        [
            new() { ModGuid = "com.casino", Name = "Casino Server", Version = "1.2.0" },
            new() { ModGuid = "com.serveronly", Name = "Server Only", Version = "1.0.0" },
        ];
        HostMods.CatalogLookup catalog = guid => guid == "com.casino" ? (77, "Casino") : guid == "com.serveronly" ? (78, "Server Only") : null;
        var now = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

        var list = HostMods.BuildList("Mods on Friend", "203.0.113.7:6969", host, catalog, null, null, null, now);
        var seen = host.Select(HostMods.Key).ToList();
        Assert.Equal(["com.casino", "com.serveronly"], seen);

        // The player takes the server-only one out. The host then adds a third.
        list.Entries.RemoveAll(e => e.Guid == "com.serveronly");
        SptServerMod[] later = [.. host, new() { ModGuid = "com.new", Name = "New", Version = "2.0.0" }];

        var leftOut = HostMods.LeftOut(list, seen);
        Assert.Equal(["com.serveronly"], leftOut);

        var again = HostMods.BuildList("Mods on Friend", "203.0.113.7:6969", later, catalog, null, null, list, now, leftOut);
        Assert.Equal(["com.casino", "com.new"], again.Entries.Select(e => e.Guid).Order());

        // Nothing kept to go by: nothing is left out.
        Assert.Empty(HostMods.LeftOut(null, seen));
        Assert.Empty(HostMods.LeftOut(list, null));
    }

    [Fact]
    public async Task A_server_answering_with_nulls_gives_mods_with_empty_text()
    {
        var server = new FakeServer { Mods = new { Response = new Dictionary<string, object?> { ["A"] = new { ModGuid = (string?)null, Name = "A", Version = (string?)null }, ["B"] = null } } };
        using var api = new SptLauncherApi("127.0.0.1:6969", server);

        var mod = Assert.Single(await api.ModsAsync());
        Assert.Equal(string.Empty, mod.ModGuid);
        Assert.Equal(string.Empty, mod.Version);
        Assert.Equal("A", HostMods.Key(mod));
    }

    private static readonly SptMiniProfile Trap = new() { Username = "trap", ProfileId = "6abee519842869498c34024b", Edition = "SPT Developer" };

    // A real versioned assembly as spt-core.dll, and the server saying that same version.
    private (FakeServer Server, SptDirectLaunch Launch, List<ProcessStartInfo> Started, List<(string Delta, string Source, string Target)> Patched) Setup()
    {
        var core = typeof(object).Assembly.Location;
        Directory.CreateDirectory(Game("BepInEx/plugins/spt"));
        File.Copy(core, Game("BepInEx/plugins/spt/spt-core.dll"));
        var info = FileVersionInfo.GetVersionInfo(core);

        var server = new FakeServer { Version = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}" };
        var api = new SptLauncherApi("127.0.0.1:6969", server);
        var started = new List<ProcessStartInfo>();
        var patched = new List<(string, string, string)>();
        Directory.CreateDirectory(Path.Combine(_server, "SPT_Data", "Launcher", "Patches"));

        var launch = new SptDirectLaunch(_game, _server, api)
        {
            StartProcess = started.Add,
            ApplyPatch = (delta, source, target, _) =>
            {
                patched.Add((delta, source, target));
                File.WriteAllText(target, "patched from " + File.ReadAllText(source));
                return Task.CompletedTask;
            },
        };

        return (server, launch, started, patched);
    }

    // ---- versions and addresses ----------------------------------------------------------------

    [Theory]
    [InlineData("4.1.6", DirectLaunchSupport.Supported)]
    [InlineData("4.1.3", DirectLaunchSupport.Supported)]
    [InlineData("4.1.7", DirectLaunchSupport.NewerUntested)]
    [InlineData("4.1.2", DirectLaunchSupport.Unsupported)]   // no bundle step yet
    [InlineData("4.0.13", DirectLaunchSupport.Unsupported)]  // its launcher checks game ownership
    [InlineData("4.2.0", DirectLaunchSupport.Unsupported)]
    [InlineData("5.0.0", DirectLaunchSupport.Unsupported)]
    [InlineData(null, DirectLaunchSupport.Unsupported)]
    public void Only_SPT_4_1_from_4_1_3_is_started_directly(string? version, DirectLaunchSupport expected) =>
        Assert.Equal(expected, SptDirectLaunch.Support(version));

    [Theory]
    [InlineData("""{ "ip": "127.0.0.1", "port": 6969 }""", "127.0.0.1:6969")]
    [InlineData("""{ "ip": "0.0.0.0", "port": 7000 }""", "127.0.0.1:7000")]
    [InlineData("""{ "ip": "192.168.1.20", "port": 6969 }""", "192.168.1.20:6969")]
    [InlineData("""{ "port": 6970 }""", "127.0.0.1:6970")]
    [InlineData("{ // a comment\n \"ip\": \"::\", \"port\": 6969, }", "127.0.0.1:6969")]
    public void The_server_address_is_read_from_its_http_json(string json, string expected)
    {
        Write(Path.Combine(_server, "SPT_Data", "configs", "http.json"), json);
        Assert.Equal(expected, SptDirectLaunch.ServerAddress(Path.Combine(_server, "SPT.Server.exe")));
    }

    [Fact]
    public void With_no_http_json_it_is_SPTs_default() =>
        Assert.Equal("127.0.0.1:6969", SptDirectLaunch.ServerAddress(Path.Combine(_server, "SPT.Server.exe")));

    [Fact]
    public void The_game_arguments_are_the_launchers() =>
        Assert.Equal(
            "-force-gfx-jobs native -token=6abee519842869498c34024b -config={'BackendUrl':'https://127.0.0.1:6969','Version':'live','MatchingVersion':'live'}",
            SptDirectLaunch.GameArguments("6abee519842869498c34024b", "127.0.0.1:6969"));

    // ---- the server's routes -------------------------------------------------------------------

    [Fact]
    public void Bodies_are_zlib_both_ways_and_plain_text_reads_as_it_is()
    {
        var packed = SptLauncherApi.Compress("""{"username":"trap"}""");
        Assert.Equal(0x78, packed[0]);
        Assert.Equal("""{"username":"trap"}""", SptLauncherApi.Decompress(packed));
        Assert.Equal("not found", SptLauncherApi.Decompress(Encoding.UTF8.GetBytes("not found")));
    }

    [Fact]
    public async Task Profiles_ping_and_login_read_the_servers_answers()
    {
        var server = new FakeServer();
        using var api = new SptLauncherApi("127.0.0.1:6969", server);

        Assert.True(await api.PingAsync());
        var profile = Assert.Single(await api.ProfilesAsync());
        Assert.Equal(("trap", "Trap", 64, "Usec", "6abee519842869498c34024b"), (profile.Username, profile.Nickname, profile.Level, profile.Side, profile.ProfileId));
        Assert.True(await api.LoginAsync("trap"));

        var login = server.Calls.Single(c => c.Path == SptLauncherApi.LoginPath);
        Assert.Equal("PUT", login.Method);
        Assert.Equal("""{"username":"trap"}""", login.Body);
    }

    [Fact]
    public void Only_this_machines_addresses_count_as_local()
    {
        Assert.True(SptLauncherApi.IsThisMachine(IPAddress.Loopback));
        Assert.True(SptLauncherApi.IsThisMachine(IPAddress.IPv6Loopback));
        Assert.False(SptLauncherApi.IsThisMachine(IPAddress.Parse("203.0.113.7")));
    }

    // ---- the steps -----------------------------------------------------------------------------

    [Fact]
    public async Task A_start_cleans_up_as_the_launcher_does_and_keeps_what_it_is_told_to()
    {
        var (_, launch, started, _) = Setup();
        Write(Game("BattlEye/BEClient_x64.dll"), "be");
        Write(Game("ConsistencyInfo"), "ci");
        Write(Game("UnityCrashHandler64.exe"), "uch");
        Write(Game("EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll"), "hw");
        Write(Game("Logs/log_2026.10.03_14-38-17_0.16.9.5.40743/errors.log"), "an error");
        var keep = Path.Combine(_root, "Kept");

        var result = await launch.RunAsync(Trap, new DirectLaunchOptions
        {
            ExcludeFromCleanup = ["ConsistencyInfo", "hwecho.dll"],
            KeepGameLogsIn = keep,
        }, null, default);

        Assert.True(result.Started);
        Assert.False(Directory.Exists(Game("BattlEye")));
        Assert.False(File.Exists(Game("UnityCrashHandler64.exe")));
        Assert.False(Directory.Exists(Game("Logs")));
        Assert.True(File.Exists(Game("ConsistencyInfo")));                                   // excluded
        Assert.False(File.Exists(Game("EscapeFromTarkov_Data/Plugins/x86_64/hwecho.dll")));  // never excludable
        Assert.Equal("an error", File.ReadAllText(Path.Combine(keep, "log_2026.10.03_14-38-17_0.16.9.5.40743", "errors.log")));
        Assert.Single(started);
    }

    [Fact]
    public async Task Patches_put_the_backups_back_then_patch_from_them()
    {
        var (_, launch, _, patched) = Setup();
        var patchDir = Path.Combine(_server, "SPT_Data", "Launcher", "Patches", "SPT-core", "EscapeFromTarkov_Data", "Managed");
        Write(Path.Combine(patchDir, "Assembly-CSharp.dll.delta"), "delta");
        Write(Game("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"), "patched last time");
        Write(Game("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak"), "original");
        Write(Game("SomeMod/Other.dll"), "modded");
        Write(Game("SomeMod/Other.dll.spt-bak"), "other original");

        var result = await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default);

        Assert.True(result.Started);
        var (_, source, _) = Assert.Single(patched);
        Assert.EndsWith("Assembly-CSharp.dll.spt-bak", source);
        Assert.Equal("patched from original", File.ReadAllText(Game("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")));
        Assert.Equal("other original", File.ReadAllText(Game("SomeMod/Other.dll")));                 // every backup goes back
        Assert.False(File.Exists(Game("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-new")));
    }

    [Fact]
    public async Task A_file_with_no_backup_yet_gets_one_before_it_is_patched()
    {
        var (_, launch, _, _) = Setup();
        Write(Path.Combine(_server, "SPT_Data", "Launcher", "Patches", "SPT-core", "EscapeFromTarkov_Data", "Managed", "Assembly-CSharp.dll.delta"), "delta");
        Write(Game("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll"), "fresh from the game");

        await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default);

        Assert.Equal("fresh from the game", File.ReadAllText(Game("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll.spt-bak")));
        Assert.Equal("patched from fresh from the game", File.ReadAllText(Game("EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll")));
    }

    [Fact]
    public async Task A_patch_that_fails_leaves_the_file_as_it_was_and_starts_nothing()
    {
        var (_, launch, started, _) = Setup();
        Write(Path.Combine(_server, "SPT_Data", "Launcher", "Patches", "SPT-core", "a.dll.delta"), "delta");
        Write(Game("a.dll"), "patched before");
        Write(Game("a.dll.spt-bak"), "original");
        var failing = new SptDirectLaunch(_game, _server, new SptLauncherApi("127.0.0.1:6969", new FakeServer { Version = VersionOfCore() }))
        {
            StartProcess = started.Add,
            ApplyPatch = (_, _, target, _) =>
            {
                File.WriteAllText(target, "half");
                throw new InvalidDataException("bad delta");
            },
        };

        var result = await failing.RunAsync(Trap, new DirectLaunchOptions(), null, default);

        Assert.Equal(DirectLaunchProblem.PatchFailed, result.Problem);
        Assert.Equal("original", File.ReadAllText(Game("a.dll")));   // the backup put back, not half a patch
        Assert.False(File.Exists(Game("a.dll.spt-new")));
        Assert.Empty(started);
    }

    private static string VersionOfCore()
    {
        var info = FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location);
        return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
    }

    [Fact]
    public async Task No_patch_folder_stops_the_start()
    {
        var (_, launch, started, _) = Setup();
        Directory.Delete(Path.Combine(_server, "SPT_Data", "Launcher", "Patches"));

        var result = await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default);

        Assert.Equal(DirectLaunchProblem.PatchFailed, result.Problem);
        Assert.Empty(started);
    }

    [Fact]
    public async Task A_version_mismatch_stops_before_anything_is_touched()
    {
        var (server, launch, started, _) = Setup();
        server.Version = "4.1.6 - BLEEDINGEDGE";
        Write(Game("BattlEye/x.dll"), "be");

        var result = await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default);

        Assert.Equal(DirectLaunchProblem.VersionMismatch, result.Problem);
        Assert.Equal("4.1.6", result.ServerVersion);
        Assert.True(Directory.Exists(Game("BattlEye")));
        Assert.Empty(started);
    }

    [Fact]
    public async Task A_missing_spt_core_dll_is_said()
    {
        var (_, launch, _, _) = Setup();
        File.Delete(Game("BepInEx/plugins/spt/spt-core.dll"));

        Assert.Equal(DirectLaunchProblem.CoreDllMissing, (await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default)).Problem);
    }

    [Fact]
    public async Task A_profile_the_server_does_not_have_is_said()
    {
        var (server, launch, started, _) = Setup();
        server.HasProfile = false;

        Assert.Equal(DirectLaunchProblem.NoSuchProfile, (await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default)).Problem);
        Assert.Empty(started);
    }

    [Fact]
    public async Task A_wipe_is_asked_for_with_the_profiles_edition_and_a_refusal_stops()
    {
        var (server, launch, started, _) = Setup();
        Assert.True((await launch.RunAsync(Trap, new DirectLaunchOptions { Wipe = true }, null, default)).Started);
        Assert.Equal("""{"username":"trap","edition":"SPT Developer"}""", server.Calls.Single(c => c.Path == SptLauncherApi.WipePath).Body);

        server.WipeAllowed = false;
        started.Clear();
        Assert.Equal(DirectLaunchProblem.WipeRefused, (await launch.RunAsync(Trap, new DirectLaunchOptions { Wipe = true }, null, default)).Problem);
        Assert.Empty(started);
    }

    [Fact]
    public async Task A_profile_the_server_marked_invalid_is_said_before_any_wipe()
    {
        var (server, launch, started, _) = Setup();
        server.Invalid = true;

        Assert.Equal(DirectLaunchProblem.ProfileInvalid, (await launch.RunAsync(Trap, new DirectLaunchOptions { Wipe = true }, null, default)).Problem);
        Assert.DoesNotContain(server.Calls, c => c.Path == SptLauncherApi.WipePath);
        Assert.Empty(started);
    }

    [Fact]
    public async Task Clear_cache_removes_sptappdata()
    {
        var (_, launch, _, _) = Setup();
        Write(Path.Combine(_server, "user", "sptappdata", "cache.bin"), "x");

        await launch.RunAsync(Trap, new DirectLaunchOptions { ClearCache = true }, null, default);

        Assert.False(Directory.Exists(Path.Combine(_server, "user", "sptappdata")));
    }

    [Fact]
    public async Task Bundles_the_mods_already_have_are_not_downloaded_and_the_rest_go_to_the_cache()
    {
        var (server, launch, _, _) = Setup();
        var have = Encoding.UTF8.GetBytes("bundle one");
        Write(Path.Combine(_server, "user", "mods", "Weapons", "bundles", "assets", "one.bundle"), "bundle one");
        var mtime = File.GetLastWriteTimeUtc(Path.Combine(_server, "user", "mods", "Weapons", "bundles", "assets", "one.bundle")).Ticks;
        var two = Encoding.UTF8.GetBytes("bundle two!");
        server.Bundles =
        [
            new SptBundleEntry { FileName = "assets/one.bundle", ModPath = "user/mods/Weapons", Size = have.Length, Crc = Crc32.Of(have), ModifiedUtcTicks = mtime },
            new SptBundleEntry { FileName = "assets/two.bundle", ModPath = "user/mods/Weapons", Size = two.Length, Crc = Crc32.Of(two) },
        ];
        server.BundleFiles["assets/two.bundle"] = two;
        var stages = new List<DirectLaunchProgress>();

        var result = await launch.RunAsync(Trap, new DirectLaunchOptions(), new SyncProgress<DirectLaunchProgress>(stages.Add), default);

        Assert.True(result.Started);
        Assert.DoesNotContain(server.Calls, c => c.Path.EndsWith("one.bundle"));
        var cached = Path.Combine(_server, "user", "cache", "bundles", Crc32.Of(two).ToString("X8"), "assets", "two.bundle");
        Assert.Equal("bundle two!", File.ReadAllText(cached));
        Assert.Contains(stages, s => s.Stage == DirectLaunchStage.Bundles);
        Assert.Equal(DirectLaunchStage.Starting, stages[^1].Stage);
    }

    [Fact]
    public async Task A_bundle_that_will_not_download_stops_the_start()
    {
        var (server, launch, started, _) = Setup();
        server.Bundles = [new SptBundleEntry { FileName = "gone.bundle", ModPath = "user/mods/X", Size = 5, Crc = 1 }];

        var result = await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default);

        Assert.Equal(DirectLaunchProblem.BundlesFailed, result.Problem);
        Assert.Equal("1|gone.bundle", result.Detail);
        Assert.Empty(started);
    }

    [Fact]
    public async Task The_game_is_started_from_its_folder_with_the_profiles_token()
    {
        var (_, launch, started, _) = Setup();

        await launch.RunAsync(Trap, new DirectLaunchOptions(), null, default);

        var start = Assert.Single(started);
        Assert.Equal(Game("EscapeFromTarkov.exe"), start.FileName);
        Assert.Equal(_game, start.WorkingDirectory);
        Assert.False(start.UseShellExecute);
        Assert.Contains("-token=6abee519842869498c34024b", start.Arguments);
    }

    [Fact]
    public void Crc32_is_the_standard_one() =>
        Assert.Equal(0xCBF43926u, Crc32.Of(Encoding.ASCII.GetBytes("123456789")));

    // ---- profiles and the launcher's settings, from disk ---------------------------------------

    [Fact]
    public void Profiles_are_read_from_their_files_before_the_server_is_up()
    {
        Write(Path.Combine(_server, "user", "profiles", "6abee519842869498c34024b.json"), """
            { "info": { "id": "6abee519842869498c34024b", "username": "trap", "edition": "SPT Developer", "wipe": false, "aid": 1 },
              "characters": { "pmc": { "Info": { "Nickname": "Trap", "Level": 64, "Side": "Usec" } } } }
            """);
        Write(Path.Combine(_server, "user", "profiles", "notes.json"), """{ "something": "else" }""");

        var profile = Assert.Single(SptLocalProfiles.Read(_game));

        Assert.Equal(("trap", "Trap", 64, "Usec", "6abee519842869498c34024b", "SPT Developer"),
            (profile.Username, profile.Nickname, profile.Level, profile.Side, profile.ProfileId, profile.Edition));
    }

    [Fact]
    public void The_launchers_own_settings_are_read()
    {
        Write(Path.Combine(_server, "user", "Launcher", "LauncherSettings.json"), """
            { "ClearCacheOnLaunch": true, "ExcludeFromCleanup": ["BattlEye"],
              "PreferredProfile": { "ServerId": "1721162719", "ProfileId": "6abee519842869498c34024b" } }
            """);

        var settings = SptLauncherSettings.Read(_server);

        Assert.True(settings.ClearCacheOnLaunch);
        Assert.Equal(["BattlEye"], settings.ExcludeFromCleanup);
        Assert.Equal("6abee519842869498c34024b", settings.PreferredProfileId);
    }

    // Fork (1.4.3): the servers added in SPT's launcher, for Play on another PC's server.
    [Fact]
    public void The_launchers_added_servers_are_read_without_this_pcs_own()
    {
        Write(Path.Combine(_server, "user", "Launcher", "LauncherSettings.json"), """
            { "Servers": [
                { "Name": "Fika night", "IpAddress": "https://203.0.113.7:6969/", "ServerId": "1760000000" },
                { "Name": "", "IpAddress": "10.0.0.5", "ServerId": "1760000001" },
                { "Name": "Local Server", "IpAddress": "127.0.0.1:6969", "ServerId": "1721162719" },
                { "Name": "Broken", "IpAddress": "not an address", "ServerId": "1760000002" } ],
              "PreferredProfile": { "ServerId": "1760000000", "ProfileId": "abc" } }
            """);

        var settings = SptLauncherSettings.Read(_server);

        Assert.Equal(
            [new SptLauncherServer("Fika night", "203.0.113.7:6969", "1760000000"), new SptLauncherServer("10.0.0.5:6969", "10.0.0.5:6969", "1760000001")],
            settings.Servers);
        Assert.Equal("1760000000", settings.PreferredServerId);
    }

    [Theory]
    [InlineData("203.0.113.7:6969", "203.0.113.7:6969")]
    [InlineData(" https://203.0.113.7:7000/ ", "203.0.113.7:7000")]
    [InlineData("http://fika.example.org", "fika.example.org:6969")]
    [InlineData("10.0.0.5", "10.0.0.5:6969")]
    [InlineData("[2001:db8::7]:6970", "[2001:db8::7]:6970")]
    [InlineData("2001:db8::7", "[2001:db8::7]:6969")]
    [InlineData("", null)]
    [InlineData("host:0", null)]
    [InlineData("host:70000", null)]
    [InlineData("host:port", null)]
    [InlineData("a b:6969", null)]
    [InlineData("203.0.113.7:6969/path", null)]
    public void A_server_address_is_read_as_host_and_port(string text, string? expected)
    {
        var ok = SptLauncherApi.TryNormalizeAddress(text, out var address);
        Assert.Equal(expected is not null, ok);
        if (expected is not null) Assert.Equal(expected, address);
    }

    [Theory]
    [InlineData("127.0.0.1:6969", true)]
    [InlineData("localhost:6969", true)]
    [InlineData("[::1]:6969", true)]
    [InlineData("203.0.113.7:6969", false)]
    [InlineData("fika.example.org:6969", false)]
    public void This_pcs_own_address_is_told_from_another_pcs(string address, bool local) =>
        Assert.Equal(local, SptLauncherApi.IsLocalAddress(address));

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            lock (this) report(value);
        }
    }

    [Theory]
    [InlineData("game/a/b.dll", true)]
    [InlineData("game/../outside.dll", false)]
    [InlineData("elsewhere/x.dll", false)]
    [InlineData("game/bad\0name", false)]
    public void PathsToWrite_MustLieInTheirFolder_AndUnresolvableOnesDont(string relative, bool inside)
    {
        var root = Path.Combine(Path.GetTempPath(), "ssptmm-inside");
        var path = Path.Combine(root, relative);
        Assert.Equal(inside, SptDirectLaunch.IsSafelyInside(path, Path.Combine(root, "game")));
    }
}
