using System.Net;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using Xunit;

namespace TCFModManager.Core.Tests;

public class UpdateAnnouncerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private static UpdateCandidate Sain(string version = "4.5.1") => new(791, false, "SAIN", version);

    private static UpdateCandidate UiFixes(string version = "6.0.2") => new(1342, false, "UI Fixes", version);

    private static UpdateNotificationState Baselined(params UpdateCandidate[] announced) => new()
    {
        BaselineTaken = true,
        Announced = [.. announced.Select(c => new AnnouncedUpdate
        {
            ModId = c.ModId, IsAddon = c.IsAddon, Version = c.Version, AnnouncedAt = Now.AddDays(-1),
        })],
    };

    private static DownloadedModRecord Pending(int modId, string version) => new()
    {
        ModId = modId, Name = "x", Version = version, DownloadedAt = Now, ArchivePath = "x.zip",
    };

    [Fact]
    public void TheFirstCheckAfterSwitchingOnAnnouncesNothingButRecordsEverything()
    {
        var result = UpdateAnnouncer.Pick([Sain(), UiFixes()], new UpdateNotificationState(), [], Now);

        Assert.Empty(result.New);
        Assert.True(result.State.BaselineTaken);
        Assert.Equal(2, result.State.Announced.Count);
    }

    [Fact]
    public void AVersionIsAnnouncedOnce()
    {
        var first = UpdateAnnouncer.Pick([Sain()], Baselined(), [], Now);
        var second = UpdateAnnouncer.Pick([Sain()], first.State, [], Now.AddHours(1));

        Assert.Equal([Sain()], first.New);
        Assert.Empty(second.New);
    }

    [Fact]
    public void ANewerReleaseOfAnAnnouncedModIsNewsAgain()
    {
        var result = UpdateAnnouncer.Pick([Sain("4.5.2")], Baselined(Sain("4.5.1")), [], Now);

        Assert.Equal("4.5.2", Assert.Single(result.New).Version);
        Assert.Equal("4.5.2", Assert.Single(result.State.Announced).Version);
    }

    [Fact]
    public void AnUpdateThatWasInstalledIsForgottenSoItCanComeBack()
    {
        var installed = UpdateAnnouncer.Pick([], Baselined(Sain()), [], Now);
        var again = UpdateAnnouncer.Pick([Sain()], installed.State, [], Now);

        Assert.Empty(installed.State.Announced);
        Assert.Single(again.New);
    }

    [Fact]
    public void AModAndAnAddonWithTheSameIdAreSeparate()
    {
        var result = UpdateAnnouncer.Pick(
            [new UpdateCandidate(116, false, "Mod", "1.1.0"), new UpdateCandidate(116, true, "Addon", "1.1.0")],
            Baselined(new UpdateCandidate(116, false, "Mod", "1.1.0")),
            [],
            Now);

        Assert.True(Assert.Single(result.New).IsAddon);
    }

    [Fact]
    public void AnUpdateAlreadyDownloadedInMonitorModeIsNotAnnounced()
    {
        var result = UpdateAnnouncer.Pick([Sain("4.5.1")], Baselined(), [Pending(791, "4.5.1")], Now);

        Assert.Empty(result.New);
        Assert.Empty(result.State.Announced);
    }

    [Fact]
    public void AnOlderPendingDownloadDoesNotHideANewerUpdate()
    {
        var result = UpdateAnnouncer.Pick([Sain("4.5.2")], Baselined(), [Pending(791, "4.5.1")], Now);

        Assert.Single(result.New);
    }

    [Fact]
    public void ACandidateListedTwiceIsAnnouncedOnce()
    {
        var result = UpdateAnnouncer.Pick([Sain(), Sain()], Baselined(), [], Now);

        Assert.Single(result.New);
    }
}

public class UpdateNotificationStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "TCFModManagerUpdateNotificationTests_" + Guid.NewGuid());

    private readonly UpdateNotificationStore _store;

    public UpdateNotificationStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new UpdateNotificationStore(Path.Combine(_directory, "update_notifications.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NoFileMeansNoBaselineYet()
    {
        Assert.False(_store.Load().BaselineTaken);
    }

    [Fact]
    public void ResetBaselineKeepsWhatWasAnnounced()
    {
        _store.Save(new UpdateNotificationState
        {
            BaselineTaken = true,
            Announced = [new AnnouncedUpdate { ModId = 791, Version = "4.5.1", AnnouncedAt = DateTimeOffset.UtcNow }],
        });

        _store.ResetBaseline();
        var state = _store.Load();

        Assert.False(state.BaselineTaken);
        Assert.Equal("4.5.1", Assert.Single(state.Announced).Version);
    }

    [Fact]
    public void ACorruptFileReadsAsNotYetBaselined()
    {
        File.WriteAllText(Path.Combine(_directory, "update_notifications.json"), "{ nope");

        var state = _store.Load();

        Assert.False(state.BaselineTaken);
        Assert.Empty(state.Announced);
    }
}

public class UpdateCheckServiceTests
{
    // Trimmed from the live answer the spike got for Chris's 4.0.13 install, 2026-09-27.
    private const string UpdatesResponse = """
        {"success":true,"data":{"spt_version":"4.0.13",
        "updates":[{"current_version":{"id":14842,"mod_id":2909,"guid":"com.hj.tasksearch","name":"Task Search","slug":"task-search","version":"1.0.1"},
                    "recommended_version":{"id":15303,"version":"1.9.1","link":"https://sp-mod.com/mod/download/2909/task-search/1.9.1","content_length":31608,"fika_compatibility":"compatible","spt_versions":["4.0.13"]},
                    "update_reason":"newer_version_available"}],
        "blocked_updates":[],
        "up_to_date":[{"id":3085,"mod_id":1090,"guid":"com.rairai.colorconverterapi.eft","name":"Color Converter API","version":"1.1.1","spt_versions":["4.0.13"]}],
        "incompatible_with_spt":[{"id":14783,"mod_id":2935,"guid":"com.rana-hamza.advancesearch","name":"Advance Stash Search","version":"1.0.0"}]}}
        """;

    [Fact]
    public async Task OnlyTheUpdatesListCounts()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, UpdatesResponse);
        using var client = new SpModApiClient(new HttpClient(handler));

        var check = await new UpdateCheckService(client)
            .FindUpdatedModsAsync([(2909, "1.0.1"), (1090, "1.1.1"), (2935, "1.0.0")], "4.0.13");

        Assert.Equal([2909], check.Updated);
        Assert.Empty(check.NotRecognised);
        Assert.Contains("mods=2909%3A1.0.1%2C1090%3A1.1.1%2C2935%3A1.0.0", handler.LastRequestUri!.Query);
        Assert.Contains("spt_version=4.0.13", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task AModTheAnswerLeavesOutIsReadAgainDirectly()
    {
        // 3001 was sent as a file version with no matching release, which the endpoint drops.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, UpdatesResponse);
        using var client = new SpModApiClient(new HttpClient(handler));

        var check = await new UpdateCheckService(client)
            .FindUpdatedModsAsync([(2909, "1.0.1"), (1090, "1.1.1"), (3001, "1.0.1.0")], "4.0.13");

        Assert.Equal([3001], check.NotRecognised);
        Assert.Equal([2909, 3001], check.ToRefetch);
    }

    [Fact]
    public async Task FetchingModsAsksForJustThoseIdsWithTheirVersions()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"success":true,"data":[{"id":2909,"name":"Task Search"}]}""");
        using var client = new SpModApiClient(new HttpClient(handler));

        var mods = await new UpdateCheckService(client).FetchModsAsync([2909, 2909]);

        var query = Uri.UnescapeDataString(handler.LastRequestUri!.Query);
        Assert.Equal(2909, Assert.Single(mods).Id);
        Assert.Contains("filter[id]=2909&", query);
        Assert.Contains("include=category,versions", query);
    }

    [Fact]
    public void AnInstallFitsInOneQuery()
    {
        var installed = Enumerable.Range(1, 80).Select(i => (2000 + i, "1.2.3")).ToList();

        Assert.Single(UpdateCheckService.ModsQueryChunks(installed));
    }

    [Fact]
    public void ALongListIsSplitWithoutLosingAnything()
    {
        var installed = Enumerable.Range(1, 400).Select(i => (10000 + i, "10.20.30-beta")).ToList();

        var chunks = UpdateCheckService.ModsQueryChunks(installed).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= UpdateCheckService.MaxModsQueryLength));
        Assert.Equal(400, chunks.Sum(c => c.Split(',').Length));
    }

    [Fact]
    public void AModWithNoVersionIsLeftOut()
    {
        Assert.Equal("1:1.0", Assert.Single(UpdateCheckService.ModsQueryChunks([(1, "1.0"), (2, " ")])));
    }
}

public class UpdateNotificationSettingsTests
{
    [Fact]
    public void OffAndHourlyUntilChosen()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;

        Assert.False(settings.UpdateNotifications.Enabled);
        Assert.Equal(TimeSpan.FromHours(1), settings.UpdateNotifications.Interval.ToTimeSpan());
    }

    [Fact]
    public void TheIntervalIsWrittenAsAName()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings
        {
            UpdateNotifications = new UpdateNotificationSettings { Enabled = true, Interval = UpdateCheckInterval.SixHours },
        });

        Assert.Contains("\"Interval\":\"SixHours\"", json);
    }

    [Fact]
    public void ANullBlockReadsAsTheDefaults()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"UpdateNotifications":null}""")!;

        Assert.False(settings.UpdateNotifications.Enabled);
    }

    [Fact]
    public void AHandEditedNumberIsNeverATightLoop()
    {
        Assert.Equal(TimeSpan.FromHours(1), ((UpdateCheckInterval)99).ToTimeSpan());
        Assert.Equal(TimeSpan.FromMinutes(30), UpdateCheckInterval.ThirtyMinutes.ToTimeSpan());
    }
}

public class TraySettingTests
{
    [Fact]
    public void TheTrayIsOnlyKeptWhileNotificationsAreOn()
    {
        Assert.False(new UpdateNotificationSettings { KeepRunningInTray = true }.KeepsRunningInTray);
        Assert.False(new UpdateNotificationSettings { Enabled = true }.KeepsRunningInTray);
        Assert.True(new UpdateNotificationSettings { Enabled = true, KeepRunningInTray = true }.KeepsRunningInTray);
    }

    [Fact]
    public void TheDerivedAnswerIsNotWrittenToSettings()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new UpdateNotificationSettings { Enabled = true, KeepRunningInTray = true });

        Assert.DoesNotContain("KeepsRunningInTray", json);
        Assert.Contains("\"KeepRunningInTray\":true", json);
    }
}
