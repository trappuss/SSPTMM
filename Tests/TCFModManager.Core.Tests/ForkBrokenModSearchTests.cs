using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// Fork (1.4.7): Find the broken mod - the halving search (ModBisect), the search for what is wrong
// with the server around it (BrokenModSearch), reading what a start logged (ServerTrial) and the
// session that puts the mods back (BrokenModSessionStore).
//
public class ForkBrokenModSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "modsearch-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // Runs a search to its end against a rule for what fails; returns it and how many tries it took.
    private static (ModBisect Search, int Tries) Run(int count, Func<IReadOnlyList<int>, bool> fails, Func<int, IEnumerable<int>>? needs = null)
    {
        var search = new ModBisect(count, needs);
        var tries = 0;

        while (search.Next is { } on)
        {
            Assert.True(++tries <= 64, "the search does not end");
            search.Report(fails(on));
        }

        return (search, tries);
    }

    [Fact]
    public void One_bad_mod_is_found_wherever_it_is()
    {
        for (var count = 1; count <= 40; count++)
        {
            for (var bad = 0; bad < count; bad++)
            {
                var (search, tries) = Run(count, on => on.Contains(bad));

                Assert.Equal(bad, search.Culprit);
                Assert.Null(search.Partner);
                Assert.True(tries <= Math.Ceiling(Math.Log2(count)) + 1, $"{tries} tries for {count} mods");
            }
        }
    }

    [Fact]
    public void Two_mods_that_only_fail_together_are_both_found()
    {
        for (var count = 2; count <= 24; count++)
        {
            for (var one = 0; one < count; one++)
            {
                for (var other = one + 1; other < count; other++)
                {
                    var (search, _) = Run(count, on => on.Contains(one) && on.Contains(other));

                    Assert.Equal(other, search.Culprit);
                    Assert.Equal(one, search.Partner);
                }
            }
        }
    }

    [Fact]
    public void A_mod_is_never_tried_without_what_it_needs()
    {
        // 0 <- 1 <- 3, 2 <- 4, 5 by itself; 3 is the bad one.
        int[][] needs = [[], [0], [], [1], [2], []];

        var (search, _) = Run(6, on =>
        {
            foreach (var mod in on)
                foreach (var needed in needs[mod]) Assert.Contains(needed, on);
            return on.Contains(3);
        }, i => needs[i]);

        Assert.Equal(3, search.Culprit);
        Assert.Null(search.Partner);
    }

    [Fact]
    public void A_fault_that_comes_and_goes_still_ends_the_search()
    {
        var random = new Random(7);

        var (search, _) = Run(30, _ => random.Next(2) == 0);

        Assert.NotNull(search.Culprit);
        Assert.True(search.IsDone);
    }

    // ---- the search around it ------------------------------------------------------------------

    private static LogEntry Error(string source, string message) =>
        new(LogKind.Server, "spt.log", 1, null, "Error", source, message, []);

    private static Task<BrokenModResult> Search(int mods, Func<IReadOnlyList<int>, ServerTrialOutcome> server) =>
        BrokenModSearch.RunAsync(mods, _ => [], (on, _) => Task.FromResult(server(on)));

    [Fact]
    public async Task A_server_that_starts_clean_has_nothing_wrong()
    {
        var result = await Search(8, _ => new ServerTrialOutcome(true, false, []));

        Assert.Equal(BrokenModVerdict.NothingWrong, result.Verdict);
        Assert.Equal(1, result.Tries);
    }

    [Fact]
    public async Task A_server_that_stays_down_with_every_mod_off_is_not_the_mods()
    {
        var result = await Search(8, _ => new ServerTrialOutcome(false, false, [Error("Program", "port 6969 in use")]));

        Assert.Equal(BrokenModVerdict.NotTheMods, result.Verdict);
        Assert.Equal(BrokenModSymptom.ServerDoesNotStart, result.Symptom);
        Assert.Equal(2, result.Tries);
    }

    [Fact]
    public async Task The_mod_that_keeps_the_server_down_is_found()
    {
        var result = await Search(20, on => on.Contains(13)
            ? new ServerTrialOutcome(false, false, [Error("ModLoader", "BadMod could not be loaded")])
            : new ServerTrialOutcome(true, false, []));

        Assert.Equal(BrokenModVerdict.Found, result.Verdict);
        Assert.Equal(BrokenModSymptom.ServerDoesNotStart, result.Symptom);
        Assert.Equal(13, result.Culprit);
        Assert.Equal("BadMod could not be loaded", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task An_error_logged_with_every_mod_off_is_not_looked_for()
    {
        // The profile's error is there whatever is on, and turning mods off adds one of its own;
        // mod 5 is the one logging the error that started it.
        var result = await Search(12, on =>
        {
            var errors = new List<LogEntry> { Error("Profiles", "profile 12 has a problem") };
            if (on.Count < 12) errors.Add(Error("Profiles", $"item {on.Count} is from a mod that is off"));
            if (on.Contains(5)) errors.Add(Error("BadMod", "null reference in 3 places"));
            return new ServerTrialOutcome(true, false, errors);
        });

        Assert.Equal(BrokenModVerdict.Found, result.Verdict);
        Assert.Equal(BrokenModSymptom.ServerLogsErrors, result.Symptom);
        Assert.Equal(5, result.Culprit);
        Assert.Equal("BadMod", Assert.Single(result.Errors).Source);
    }

    [Fact]
    public async Task Errors_that_stay_with_every_mod_off_are_not_the_mods()
    {
        var result = await Search(4, _ => new ServerTrialOutcome(true, false, [Error("Profiles", "profile 1 has a problem")]));

        Assert.Equal(BrokenModVerdict.NotTheMods, result.Verdict);
    }

    [Fact]
    public async Task Progress_counts_every_try()
    {
        var seen = new List<BrokenModProgress>();
        var progress = new Collect(seen);

        var result = await BrokenModSearch.RunAsync(16, _ => [],
            (on, _) => Task.FromResult(new ServerTrialOutcome(!on.Contains(9), false, [])), progress);

        Assert.Equal(result.Tries, seen.Count);
        Assert.Equal([.. Enumerable.Range(1, seen.Count)], seen.Select(p => p.Try));
        Assert.All(seen, p => Assert.True(p.About >= p.Try));
        Assert.Equal(16, seen[0].ModsOn);
        Assert.Equal(0, seen[1].ModsOn);
    }

    private sealed class Collect(List<BrokenModProgress> into) : IProgress<BrokenModProgress>
    {
        public void Report(BrokenModProgress value) => into.Add(value);
    }

    // ---- the log -------------------------------------------------------------------------------

    [Fact]
    public void Only_what_was_logged_since_is_read()
    {
        var install = Path.Combine(_dir, "SPT");
        var logs = Path.Combine(install, "user", "logs", "spt");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(install, "SPT.Server.exe"), "");
        var log = Path.Combine(logs, "spt20261111.log");
        File.WriteAllText(log, "[2026-11-11 10:00:00.000][Error][Old] from an earlier start\n");

        var before = ServerTrial.LogLengths(install);

        File.AppendAllText(log,
            "[2026-11-11 11:00:00.000][Information][Loader] loading mods\n" +
            "[2026-11-11 11:00:01.000][Error][BadMod] \u001b[31mcould not load 3 items\u001b[0m\n" +
            "   at BadMod.Load()\n");

        var error = Assert.Single(ServerTrial.ErrorsSince(install, before));
        Assert.Equal("BadMod", error.Source);
        Assert.Equal("could not load 3 items", error.Message);
        Assert.Equal("BadMod|could not load # items", ServerTrial.Signature(error));
    }

    // ---- crash signs ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(unchecked((int)0xC0000005), true)]
    [InlineData(unchecked((int)0xC0000409), true)]
    [InlineData(unchecked((int)0xE0434352), true)]
    public void A_fault_code_is_a_crash_and_an_ordinary_end_is_not(int exitCode, bool crash) =>
        Assert.Equal(crash, CrashSigns.IsCrashExitCode(exitCode));

    [Fact]
    public void The_server_giving_up_is_its_critical_line()
    {
        LogEntry[] entries =
        [
            Error("BadMod", "could not load"),
            new(LogKind.Server, "spt.log", 2, null, "Critical", "SPTarkov.Server.Core", "The server has unexpectedly stopped", []),
        ];

        Assert.Equal("SPTarkov.Server.Core", CrashSigns.ServerCrash(entries)!.Source);
        Assert.Null(CrashSigns.ServerCrash(entries.Take(1)));

        // A request that failed is Critical too, and the server goes on.
        Assert.Null(CrashSigns.ServerCrash(
            [new(LogKind.Server, "spt.log", 3, null, "Critical", "SPTarkov.Server.Middleware.SptLoggerMiddleware", "Error handling request: /x", [])]));
    }

    [Fact]
    public void What_a_search_logged_is_taken_back_out()
    {
        var install = Path.Combine(_dir, "SPT");
        var logs = Path.Combine(install, "user", "logs", "spt");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(install, "SPT.Server.exe"), "");
        var log = Path.Combine(logs, "spt20261111.log");
        File.WriteAllText(log, "[2026-11-11 10:00:00.000][Information][Server] the run before\n");

        var before = ServerTrial.LogLengths(install);
        File.AppendAllText(log, "[2026-11-11 11:00:00.000][Error][BadMod] from a search's start\n");
        File.WriteAllText(Path.Combine(logs, "spt20261112.log"), "[2026-11-12 00:00:01.000][Error][BadMod] past midnight\n");

        ServerTrial.TrimLogs(install, before);

        Assert.Equal("[2026-11-11 10:00:00.000][Information][Server] the run before\n", File.ReadAllText(log));
        Assert.False(File.Exists(Path.Combine(logs, "spt20261112.log")));
    }

    // ---- the session ---------------------------------------------------------------------------

    [Fact]
    public void A_session_turns_mods_off_and_puts_every_one_back()
    {
        var install = Path.Combine(_dir, "SPT");
        var mods = Path.Combine(install, "user", "mods");
        string[] names = ["Alpha", "Beta", "Gamma"];
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.Combine(mods, name));
            File.WriteAllText(Path.Combine(mods, name, "mod.dll"), name);
        }

        var session = new BrokenModSession
        {
            InstallPath = install,
            StartedAt = DateTimeOffset.Now,
            Mods = [.. names.Select(n => Path.Combine(mods, n))],
        };

        var store = new BrokenModSessionStore(Path.Combine(_dir, "broken_mod_search.json"));
        store.Save(session);

        var outcome = BrokenModSessionStore.Set(session, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { session.Mods[1] });

        Assert.Empty(outcome.Failed);
        Assert.False(Directory.Exists(Path.Combine(mods, "Alpha")));
        Assert.True(Directory.Exists(Path.Combine(mods, "Beta")));
        Assert.True(File.Exists(Path.Combine(install, "user", "mods.disabled", "Gamma", "mod.dll")));
        Assert.Equal(2, BrokenModSessionStore.TurnedOff(session));

        // As the next start of the app finds it.
        var found = new BrokenModSessionStore(Path.Combine(_dir, "broken_mod_search.json")).Load()!;
        Assert.Equal(session.Mods, found.Mods);

        Assert.Empty(BrokenModSessionStore.PutBack(found).Failed);
        Assert.All(names, n => Assert.True(File.Exists(Path.Combine(mods, n, "mod.dll"))));
        Assert.False(Directory.Exists(Path.Combine(install, "user", "mods.disabled")));
        Assert.Equal(0, BrokenModSessionStore.TurnedOff(found));

        store.Clear();
        Assert.Null(store.Load());
    }
}
