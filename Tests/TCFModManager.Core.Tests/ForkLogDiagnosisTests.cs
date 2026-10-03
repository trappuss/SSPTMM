using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

// Fork: Diagnose logs. The lines are from a real SPT 4.1.6 install's logs (2026-10-03), shortened
// where marked "...", with the user's path replaced.
public sealed class ForkLogDiagnosisTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "diagnose-" + Guid.NewGuid().ToString("N"));

    public ForkLogDiagnosisTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly LogFileSet NoFiles = new(null, null, null, null, false);

    private static DiagnosedMod Server(string name, string? guid = null, string? folder = null) =>
        new(name, name, folder ?? Path.Combine("mods", name), InstalledModTarget.Server, guid);

    private static DiagnosedMod Client(string name) =>
        new(name, name, Path.Combine("plugins", name), InstalledModTarget.Client, null);

    private static readonly DiagnosedMod Casino = Server("Casino", "com.casino");
    private static readonly DiagnosedMod Svm = Server("[SVM] Server Value Modifier", "com.svm");
    private static readonly DiagnosedMod Interchange = Client("ManimalInterchange");
    private static readonly DiagnosedMod Icebreaker = Client("ManimalIcebreaker");
    private static readonly DiagnosedMod LootingBots = Client("LootingBots");
    private static readonly DiagnosedMod Orbit = Client("ORBIT");
    private static readonly DiagnosedMod Checkmarks = Client("AllQuestsCheckmarks");

    private static LogModLocator Mods(params DiagnosedMod[] extra) => LogModLocator.From(
    [
        (Casino, [], ["Roulette.Server", "Casino.Server"]),
        (Svm, [], ["ServerValueModifier"]),
        (Interchange, ["Manimal-InterchangeRework"], []),
        (Icebreaker, ["Manimal-Icebreaker"], []),
        (LootingBots, ["LootingBots"], []),
        (Orbit, ["com.chazut.orbit"], []),
        (Checkmarks, ["ZGFueDkx-AllQuestsCheckmarks"], []),
        .. extra.Select(m => (m, Enumerable.Empty<string>(), Enumerable.Empty<string>())),
    ]);

    private static List<LogEntry> ServerLog(params string[] lines) => LogEntries.Parse(LogKind.Server, "spt.log", lines);

    private static IReadOnlyList<LogFinding> Find(IReadOnlyList<LogEntry> server, LogModLocator? mods = null,
        IReadOnlyList<LogEntry>? bepInEx = null, IReadOnlyList<LogEntry>? game = null) =>
        LogDiagnoser.Diagnose(NoFiles, server, bepInEx ?? [], game ?? [], mods ?? Mods()).Findings;

    [Fact]
    public void Server_lines_split_into_entries_with_what_is_under_them()
    {
        var entries = ServerLog(
            "[2026-09-27 21:11:25.683][Error][SPTarkov.Server.Core.Controllers.ClientLogController] [SPT.Singleplayer] 2 plugins failed to load due to errors:",
            "Could not load [Manimal-InterchangeRework 1.0.5] because it has missing dependencies: com.arys.unitytoolkit (v2.0.2 or newer)",
            "Skipping [Manimal-Icebreaker 1.1.3] because it has a dependency that was not loaded. See previous errors for details.",
            "",
            "[2026-09-27 21:11:27.746][Information][SPTarkov.Server.Middleware.SptLoggerMiddleware] [Client Request] /client/checkVersion");

        Assert.Equal(2, entries.Count);
        Assert.Equal("Error", entries[0].Level);
        Assert.Equal("SPTarkov.Server.Core.Controllers.ClientLogController", entries[0].Source);
        Assert.Equal(2, entries[0].More.Count);
        Assert.Equal(new DateTime(2026, 9, 27, 21, 11, 25, 683), entries[0].At);
        Assert.Equal(5, entries[1].Line);
    }

    [Fact]
    public void Only_the_servers_last_run_is_read()
    {
        var entries = ServerLog(
            "[2026-10-03 01:45:05.594][Information][SPTarkov.Server.Modding.ModLoader] Applying enum prepatch definitions: X",
            "[2026-10-03 01:45:08.827][Information][SPTarkov.Server.Modding.ModValidator] ModLoader: loading: 63 server mods...",
            "[2026-10-03 01:50:00.000][Error][ServerValueModifier.SVM] [SVM] Initialization cancelled: Preset or/and Loader is not found or null",
            "[2026-10-03 02:16:20.000][Information][SPTarkov.Server.Modding.ModLoader] Applying enum prepatch definitions: X",
            "[2026-10-03 02:16:21.000][Information][SPTarkov.Server.Modding.ModValidator] ModLoader: loading: 63 server mods...");

        var run = LogDiagnoser.LastRun(entries);

        Assert.Equal(2, run.Count);
        Assert.StartsWith("Applying enum prepatch", run[0].Message);
        Assert.Empty(Find(run));
    }

    [Fact]
    public void Plugins_the_game_could_not_load_are_traced_to_their_mods()
    {
        var found = Find(ServerLog(
            "[2026-09-27 21:11:25.683][Error][SPTarkov.Server.Core.Controllers.ClientLogController] [SPT.Singleplayer] 2 plugins failed to load due to errors:",
            "Could not load [Manimal-InterchangeRework 1.0.5] because it has missing dependencies: com.arys.unitytoolkit (v2.0.2 or newer)",
            "Skipping [Manimal-Icebreaker 1.1.3] because it has a dependency that was not loaded. See previous errors for details.",
            "[2026-09-27 22:21:52.504][Error][SPTarkov.Server.Core.Controllers.ClientLogController] [SPT.Singleplayer] 1 plugin failed to load due to errors:",
            "Could not load [LootingBots 1.8.0] because it is incompatible with: com.chazut.orbit"));

        var missing = Assert.Single(found, f => f.Kind == LogFindingKind.PluginMissingDependency);
        Assert.Same(Interchange, missing.Mod);
        Assert.Equal(["Manimal-InterchangeRework 1.0.5", "com.arys.unitytoolkit (v2.0.2 or newer)"], missing.Args);

        Assert.Same(Icebreaker, Assert.Single(found, f => f.Kind == LogFindingKind.PluginDependencyNotLoaded).Mod);

        var clash = Assert.Single(found, f => f.Kind == LogFindingKind.PluginIncompatible);
        Assert.Same(LootingBots, clash.Mod);
        Assert.Same(Orbit, Assert.Single(clash.Others));
    }

    [Fact]
    public void A_failed_request_is_traced_to_the_mod_in_its_stack()
    {
        var found = Find(ServerLog(
            "[2026-09-29 04:17:15.327][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware] Error handling request: /roulette/state",
            "[2026-09-29 04:17:15.327][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware] Object reference not set to an instance of an object.",
            "[2026-09-29 04:17:15.332][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware]    at SPTarkov.Server.Core.Routers.EventOutputHolder.ResetOutput(MongoId sessionId)",
            "   at SPTarkov.Server.Core.Routers.EventOutputHolder.GetOutput(MongoId sessionId)",
            @"   at Roulette.Server.RouletteCallbacks.Output(MongoId sessionId) in H:\SPTMods\SPT-Casino\src\Roulette.Server\RouletteCallbacks.cs:line 93",
            "[2026-09-29 04:17:16.000][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware] Error handling request: /roulette/state",
            "[2026-09-29 04:17:16.000][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware] Object reference not set to an instance of an object.",
            "[2026-09-29 04:17:16.000][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware]    at SPTarkov.Server.Core.Routers.EventOutputHolder.ResetOutput(MongoId sessionId)",
            "   at Roulette.Server.RouletteCallbacks.State(StateRequest info, MongoId sessionId)"));

        var failed = Assert.Single(found);
        Assert.Equal(LogFindingKind.RequestFailed, failed.Kind);
        Assert.Same(Casino, failed.Mod);
        Assert.Equal(["/roulette/state", "Object reference not set to an instance of an object."], failed.Args);
        Assert.Equal(2, failed.Count);
        Assert.Equal(
            "Error handling request: /roulette/state\nObject reference not set to an instance of an object.\n"
            + @"at Roulette.Server.RouletteCallbacks.Output(MongoId sessionId) in H:\SPTMods\SPT-Casino\src\Roulette.Server\RouletteCallbacks.cs:line 93",
            failed.Quote);
    }

    [Fact]
    public void Raid_results_the_server_could_not_read_are_said_with_the_type()
    {
        var found = Find(ServerLog(
            "[2026-10-02 10:00:00.000][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware] Error handling request: /client/match/local/end",
            "[2026-10-02 10:00:00.000][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware] The JSON value could not be converted to SPTarkov.Server.Core.Models.Enums.SkillTypes. Path: $[52].Id | LineNumber: 0 | BytePositionInLine: 4513.",
            "[2026-10-02 10:00:00.000][Critical][SPTarkov.Server.Middleware.SptLoggerMiddleware]    at System.Text.Json.ThrowHelper.ThrowJsonException(String message)"));

        var lost = Assert.Single(found);
        Assert.Equal(LogFindingKind.RaidResultsLost, lost.Kind);
        Assert.Equal(LogSeverity.Critical, lost.Severity);
        Assert.Equal(["SkillTypes"], lost.Args);
    }

    [Fact]
    public void Profiles_clothing_and_the_port_are_said_without_a_mod()
    {
        var found = Find(ServerLog(
            "[2026-09-27 15:51:01.942][Critical][SPTarkov.Server.Core.Services.Profile.ProfileMigrationService] SPTarkov.Server.Core.Exceptions.Items.InvalidModdedClothingException: Clothing item: CustomisationStorage { Id = 6ab93267b208402e592581f6, Source = unlockedInGame, Type = suite } found in profile that does not exist in SPT. You WILL experience errors...",
            "[2026-09-27 15:51:01.943][Critical][SPTarkov.Server.Core.Services.Profile.ProfileMigrationService] Failed to load profile with ID '6ab93267b208402e592581aa'. The profile will be marked as invalid.",
            "[2026-09-27 15:52:00.000][Critical][SPTarkov.Server.Core] Failed to start the web server on 127.0.0.1:6969. Socket error: AddressAlreadyInUse (10048)."));

        Assert.Equal(
            [LogFindingKind.ProfileInvalid, LogFindingKind.PortInUse, LogFindingKind.ProfileClothingMissing],
            found.Select(f => f.Kind));
        Assert.All(found, f => Assert.Null(f.Mod));
        Assert.Equal(["127.0.0.1:6969"], found[1].Args);
    }

    [Fact]
    public void A_mods_own_error_and_a_relayed_plugin_error_name_the_mod()
    {
        var found = Find(ServerLog(
            "[2026-10-03 03:00:00.000][Error][ServerValueModifier.SVM] [SVM] Initialization cancelled: Preset or/and Loader is not found or null",
            "[2026-09-27 22:21:52.506][Error][SPTarkov.Server.Core.Controllers.ClientLogController] [ZGFueDkx-AllQuestsCheckmarks] Something broke"));

        Assert.Equal(2, found.Count);
        Assert.Same(Svm, found[0].Mod);
        Assert.Equal(["Initialization cancelled: Preset or/and Loader is not found or null"], found[0].Args);
        Assert.Same(Checkmarks, found[1].Mod);
    }

    [Fact]
    public void A_server_mod_missing_from_the_loaded_list_is_said_unless_it_came_after_the_run()
    {
        var later = Server("NewMod", "com.new") with { InstalledAt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 3))) };
        var found = Find(ServerLog(
            "[2026-10-03 01:45:08.827][Information][SPTarkov.Server.Modding.ModValidator] ModLoader: loading: 2 server mods...",
            "[2026-10-03 01:45:09.257][Information][SPTarkov.Server.Modding.ModValidator] Mod: Casino version: 1.3.3 (GUID: com.casino | targets SPT: ~4.1.0) by: someone loaded"),
            Mods(later));

        Assert.Same(Svm, Assert.Single(found, f => f.Kind == LogFindingKind.ServerModNotLoaded).Mod);
    }

    [Fact]
    public void Two_mods_shipping_the_same_bundle_are_both_named()
    {
        foreach (var name in new[] { "SPTBetterRearSights", "WTT-ContentBackport" })
        {
            var bundle = Path.Combine(_dir, name, "bundles", "assets", "content", "items", "mods", "barrels");
            Directory.CreateDirectory(bundle);
            File.WriteAllText(Path.Combine(bundle, "barrel_vpo215_600mm_366tkm.bundle"), "x");
        }

        var a = Server("SPTBetterRearSights", folder: Path.Combine(_dir, "SPTBetterRearSights"));
        var b = Server("WTT-ContentBackport", folder: Path.Combine(_dir, "WTT-ContentBackport"));

        var found = Find(ServerLog(
            "[2026-10-03 05:21:06.980][Error][SPTarkov.Server.Core.Loaders.BundleLoader] Unable to add bundle: assets/content/items/mods/barrels/barrel_vpo215_600mm_366tkm.bundle"),
            Mods(a, b));

        var clash = Assert.Single(found);
        Assert.Equal(LogFindingKind.BundleClash, clash.Kind);
        Assert.Equal([a, b], new[] { clash.Mod! }.Concat(clash.Others));
    }

    [Fact]
    public void BepInEx_and_game_logs_are_read_too()
    {
        var bepInEx = LogEntries.Parse(LogKind.BepInEx, "LogOutput.log",
        [
            "[Message:   BepInEx] BepInEx 5.4.23.5 - EscapeFromTarkov (9/25/2026 5:56:49 PM)",
            "[Error  :ZGFueDkx-AllQuestsCheckmarks] Failed to set custom checkmark in ItemSpecificationPanel!",
            "[Error  :ZGFueDkx-AllQuestsCheckmarks] Failed to set custom checkmark in ItemSpecificationPanel!",
            "[Error  :   BepInEx] Could not load [LootingBots 1.8.0] because it is incompatible with: com.chazut.orbit",
        ]);
        var game = LogEntries.Parse(LogKind.GameErrors, "errors.log",
        [
            "2026-10-03 01:22:18.403 -04:00|0.16.9.5.40743|Error|assetBundle|Bundle \"mods/ak/Pic_Adapter_for_AK.bundle\" has dependency \"cab-1dc8d26be8722a766953ce9d8a444e8c\" that was not found in manifest ",
        ]);

        Assert.Equal("BepInEx", bepInEx[0].Source);
        Assert.Equal("Message", bepInEx[0].Level);

        var found = Find([], bepInEx: bepInEx, game: game);

        Assert.Same(LootingBots, Assert.Single(found, f => f.Kind == LogFindingKind.PluginIncompatible).Mod);
        var logged = Assert.Single(found, f => f.Kind == LogFindingKind.PluginLoggedErrors);
        Assert.Same(Checkmarks, logged.Mod);
        Assert.Equal(2, logged.Count);
        Assert.Equal(["mods/ak/Pic_Adapter_for_AK.bundle"], Assert.Single(found, f => f.Kind == LogFindingKind.BundleMissingDependency).Args);
    }

    [Fact]
    public void The_locator_takes_the_longest_namespace_and_names_nobody_for_a_shared_one()
    {
        var a = Server("A");
        var b = Server("B");
        var mods = LogModLocator.From(
        [
            (a, [], ["Shared.Lib", "Mod.A"]),
            (b, [], ["Shared.Lib", "Mod.A.Extra"]),
        ]);

        Assert.Same(a, mods.ByCode("Mod.A.Thing.Run"));
        Assert.Same(b, mods.ByCode("Mod.A.Extra.Thing.Run"));
        Assert.Null(mods.ByCode("Shared.Lib.Helper.Do"));
        Assert.Null(mods.ByCode("SPTarkov.Server.Core.Thing.Do"));
    }

    [Fact]
    public void Redacting_takes_out_the_user_ids_and_addresses_but_not_versions()
    {
        var text = string.Join("\n",
            @"Applying enum prepatch definitions: C:\Users\notso\SPT\user\patchers\x.json",
            "C:/Users/notso/AppData and notso said",
            "Failed to load profile with ID '6ab93267b208402e592581aa'. 6ab93267b208402e592581aa again",
            "client 192.168.1.20:25565 joined; server https://127.0.0.1:6969; plugin 1.8.0.0");

        var redacted = LogRedactor.Redact(text, @"C:\Users\notso", "notso");

        Assert.DoesNotContain("notso", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\SPT\user\patchers", redacted);
        Assert.Contains("%USERPROFILE%/AppData and <user> said", redacted);
        Assert.DoesNotContain("6ab93267b208402e592581aa", redacted);
        var id = System.Text.RegularExpressions.Regex.Matches(redacted, "id-[0-9a-f]{6}");
        Assert.Equal(2, id.Count);
        Assert.Equal(id[0].Value, id[1].Value);
        Assert.Contains("<ip>:25565", redacted);
        Assert.Contains("127.0.0.1:6969", redacted);
        Assert.Contains("1.8.0.0", redacted);
    }

    [Fact]
    public void The_files_are_found_where_SPT_4_1_puts_them()
    {
        var install = Path.Combine(_dir, "game");
        var logs = Path.Combine(install, "SPT_Runtime", "user", "logs");
        Directory.CreateDirectory(Path.Combine(logs, "spt"));
        Directory.CreateDirectory(Path.Combine(install, "SPT_Runtime", "SPT_Data"));
        File.WriteAllText(Path.Combine(install, "SPT_Runtime", "SPT.Server.exe"), "");
        File.WriteAllText(Path.Combine(logs, "spt", "spt20261002.log"), "old");
        File.SetLastWriteTimeUtc(Path.Combine(logs, "spt", "spt20261002.log"), new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(Path.Combine(logs, "spt", "spt20261003.log"), "new");
        File.WriteAllText(Path.Combine(logs, "Launcher.log"), "[SPTarkov.Core.Helpers.GameHelper] Recursive Removal: G:\\Games\\SPT\\Logs\n");
        Directory.CreateDirectory(Path.Combine(install, "BepInEx"));
        File.WriteAllText(Path.Combine(install, "BepInEx", "LogOutput.log"), "");
        var session = Path.Combine(install, "Logs", "log_2026.10.03_5-22-11_0.16.9.5.40743");
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "2026.10.03_5-22-11_0.16.9.5.40743 errors.log"), "");

        var files = LogFiles.Find(install);

        Assert.EndsWith("spt20261003.log", files.Server);
        Assert.NotNull(files.BepInEx);
        Assert.EndsWith(" errors.log", files.GameErrors);
        Assert.True(files.LauncherClearsGameLogs);
    }
}
