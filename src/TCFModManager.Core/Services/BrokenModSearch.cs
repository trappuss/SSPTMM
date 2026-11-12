using System.Text.Json;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>What is wrong with the server, as the first try with every mod on showed it.</summary>
public enum BrokenModSymptom
{
    ServerDoesNotStart,
    ServerLogsErrors,
}

public enum BrokenModVerdict
{
    /// <summary>The server starts and logs no error with every mod on.</summary>
    NothingWrong,

    /// <summary>The same thing happens with every server mod off.</summary>
    NotTheMods,

    Found,
}

/// <summary>How a search ended. Culprit and Partner number the mods as they were given.
/// RecentTried: how many recently changed mods the search was kept to, when the fault was among them.</summary>
public sealed record BrokenModResult(
    BrokenModVerdict Verdict,
    BrokenModSymptom? Symptom,
    int? Culprit,
    int? Partner,
    IReadOnlyList<LogEntry> Errors,
    int Tries,
    int RecentTried = 0);

/// <summary>One try about to be made: which it is, about how many there will be, how many mods are on.</summary>
/// <remarks>Suspects: the mods the fault can still be, once it is known to be one of them.</remarks>
public sealed record BrokenModProgress(int Try, int About, int ModsOn, int Mods, IReadOnlyList<int>? Suspects = null);

//
// Fork (1.4.7): Find the broken mod, for the SPT server. The mods are the install's enabled server
// mods, each after what it needs (ModBisect); a try is the server started with some of them on.
//
// First with all of them on, to see what is wrong: the server does not come up, or it does and logs
// errors. Then with none, to see that it is the mods: errors logged even then are not theirs, and
// are not looked for afterwards. Then ModBisect, where a try fails when it shows what the first one
// showed - the server down, or one of those errors.
//
// Recent first: given the mods that changed since the server last came up, one more try before the
// halving - every mod on but those (and the ones that need them). When that works the fault is among
// them, and only they are halved, the rest staying on. When it does not, nothing is known that was
// not before, and every mod is halved as usual: a wrong guess costs one start.
//
public static class BrokenModSearch
{
    public static async Task<BrokenModResult> RunAsync(
        int mods,
        Func<int, IEnumerable<int>> needs,
        Func<IReadOnlyList<int>, bool, CancellationToken, Task<ServerTrialOutcome>> tryWith,
        IProgress<BrokenModProgress>? progress = null,
        CancellationToken ct = default,
        IReadOnlyCollection<int>? recent = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(mods, 1);

        var tries = 0;

        // errors: whether what a server that came up logs is wanted - when it is not, the try
        // need not wait for it.
        async Task<ServerTrialOutcome> Try(IReadOnlyList<int> on, int left, bool errors, IReadOnlyList<int>? suspects = null)
        {
            ct.ThrowIfCancellationRequested();
            tries++;
            progress?.Report(new BrokenModProgress(tries, tries + left, on.Count, mods, suspects));
            return await tryWith(on, errors, ct).ConfigureAwait(false);
        }

        var all = Enumerable.Range(0, mods).ToList();
        var search = new ModBisect(mods, needs);

        var first = await Try(all, 1 + search.TriesLeft, errors: true).ConfigureAwait(false);
        if (first.Started && first.Errors.Count == 0)
            return new BrokenModResult(BrokenModVerdict.NothingWrong, null, null, null, [], tries);

        var symptom = first.Started ? BrokenModSymptom.ServerLogsErrors : BrokenModSymptom.ServerDoesNotStart;

        var errorsMatter = symptom == BrokenModSymptom.ServerLogsErrors;

        var none = await Try([], search.TriesLeft, errorsMatter).ConfigureAwait(false);
        var theirs = first.Errors.Select(ServerTrial.Signature)
            .Except(none.Errors.Select(ServerTrial.Signature), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var stillWrong = symptom == BrokenModSymptom.ServerDoesNotStart ? !none.Started : !none.Started || theirs.Count == 0;
        if (stillWrong)
            return new BrokenModResult(BrokenModVerdict.NotTheMods, symptom, null, null, none.Errors, tries);

        IReadOnlyList<LogEntry> Shown(ServerTrialOutcome outcome) =>
            symptom == BrokenModSymptom.ServerDoesNotStart
                ? outcome.Errors
                : [.. outcome.Errors.Where(e => theirs.Contains(ServerTrial.Signature(e)))];

        bool Fails(ServerTrialOutcome outcome) => !outcome.Started || (symptom == BrokenModSymptom.ServerLogsErrors && Shown(outcome).Count > 0);

        // The errors of the last try that failed with the fewest mods on - the closest to the fault.
        var errors = Shown(first);
        var fewest = mods;

        // Recent first. among: the mods halved, by their numbers here; rest: the ones on throughout.
        List<int>? among = null;
        List<int> rest = [];

        if (recent is { Count: > 0 } && WithDependents(mods, needs, recent) is { Count: > 0 } changed && changed.Count * 2 <= mods)
        {
            var others = all.Except(changed).ToList();
            var narrow = new ModBisect(changed.Count, i => needs(changed[i]).Select(n => changed.IndexOf(n)).Where(n => n >= 0));

            // No suspects said for this try: until it has passed, the fault is not known to be among them.
            var without = await Try(others, 1 + narrow.TriesLeft, errorsMatter).ConfigureAwait(false);
            if (!Fails(without))
            {
                among = changed;
                rest = others;
                search = narrow;
            }
        }

        int Mod(int number) => among is null ? number : among[number];

        while (search.Next is { } next)
        {
            IReadOnlyList<int> on = among is null ? next : [.. rest.Concat(next.Select(Mod)).Order()];
            IReadOnlyList<int> suspects = [.. search.Suspects.Select(Mod)];

            var outcome = await Try(on, search.TriesLeft - 1, errorsMatter, suspects).ConfigureAwait(false);
            var failed = Fails(outcome);

            if (failed && on.Count <= fewest)
            {
                fewest = on.Count;
                errors = Shown(outcome);
            }

            search.Report(failed);
        }

        return new BrokenModResult(
            BrokenModVerdict.Found, symptom,
            Mod(search.Culprit!.Value), search.Partner is { } partner ? Mod(partner) : null,
            errors, tries, among?.Count ?? 0);
    }

    // These mods and every one that needs one of them, in order. A mod's needs come before it, so
    // one pass finds them all.
    private static List<int> WithDependents(int mods, Func<int, IEnumerable<int>> needs, IReadOnlyCollection<int> picked)
    {
        var chosen = new bool[mods];
        foreach (var mod in picked)
            if (mod >= 0 && mod < mods) chosen[mod] = true;

        for (var i = 0; i < mods; i++)
            if (!chosen[i] && needs(i).Any(n => n >= 0 && n < mods && chosen[n])) chosen[i] = true;

        return [.. Enumerable.Range(0, mods).Where(i => chosen[i])];
    }

    //
    // Which of these mod folders changed after a moment: the folder or something directly in it
    // made or written since. Made as well as written, since a file taken out of an archive can keep
    // the archive's date as when it was written. Turning a mod off and on moves its folder, which
    // changes neither.
    //
    // HUNCH: that looking directly in the folder is enough - a mod updated only deeper down is
    // missed, and is then found by the ordinary search, one start later.
    //
    public static List<int> ChangedSince(IReadOnlyList<string> folders, DateTimeOffset since)
    {
        var moment = since.UtcDateTime;
        var changed = new List<int>();

        for (var i = 0; i < folders.Count; i++)
        {
            try
            {
                FileSystemInfo entry = Directory.Exists(folders[i]) ? new DirectoryInfo(folders[i]) : new FileInfo(folders[i]);
                if (!entry.Exists) continue;

                var inside = entry is DirectoryInfo folder ? folder.EnumerateFileSystemInfos() : [];
                if (inside.Prepend(entry).Any(e => e.CreationTimeUtc > moment || e.LastWriteTimeUtc > moment)) changed.Add(i);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not known to have changed: it is searched with the others.
            }
        }

        return changed;
    }

    //
    // About how long the search still has, from how long its starts took so far. Null until two
    // are done: the first, with every mod on, is the slowest, and the second, with none, the
    // quickest.
    //
    // HUNCH: that a start still to come takes the average of those done. The ones left have about
    // half the mods on, between the two it begins with - not measured on a real install.
    //
    public static TimeSpan? TimeLeft(IReadOnlyList<TimeSpan> finished, int triesLeft, TimeSpan intoCurrent)
    {
        if (finished.Count < 2 || triesLeft <= 0) return null;

        var average = TimeSpan.FromTicks((long)finished.Average(t => t.Ticks));
        var left = average * triesLeft - intoCurrent;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    //
    // The install's enabled server mods, each after what it needs among them - the order ModBisect
    // wants. A mod that is in the install twice (a copy in user/mods and one in user/mods.disabled)
    // is left as it is and not searched: which copy is meant is Subscribed items' question.
    //
    public static (IReadOnlyList<InstalledMod> Mods, Func<int, IEnumerable<int>> Needs) Suspects(IReadOnlyList<InstalledMod> scanned)
    {
        var twice = ModDisableService.DuplicatePairs(scanned).Select(p => p.Enabled).ToHashSet();
        var graph = ModDependencyGraph.Build(scanned);

        var enabled = scanned.Where(m => m.Target == InstalledModTarget.Server && !m.IsDisabled && !twice.Contains(m)).ToList();
        var ordered = graph.DependencyOrder(enabled, m => [m]);

        var number = new Dictionary<InstalledMod, int>();
        for (var i = 0; i < ordered.Count; i++) number[ordered[i]] = i;

        return (ordered, i => graph.DependenciesOf(ordered[i])
            .Where(link => !link.IsSoft && number.ContainsKey(link.Dependency))
            .Select(link => number[link.Dependency]));
    }
}

//
// Fork (1.4.7): Data\server_last_up.json - when each install's server was last seen up, so Find the
// broken mod can try the mods changed since then first. Written when a server is first seen
// listening, not for as long as it does.
//
public sealed class ServerUpLog
{
    private readonly string _filePath;

    public ServerUpLog() : this(Path.Combine(AppPaths.DataDirectory, "server_last_up.json"))
    {
    }

    // A file of its own - for the tests.
    public ServerUpLog(string filePath) => _filePath = filePath;

    public DateTimeOffset? Last(string installPath) =>
        Read().TryGetValue(installPath, out var at) ? at : null;

    public void Note(string installPath, DateTimeOffset at)
    {
        try
        {
            var all = Read();
            all[installPath] = at;
            SafeFile.WriteText(_filePath, JsonSerializer.Serialize(all));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Debug("Search", $"server_last_up.json couldn't be written: {ex.Message}");
        }
    }

    private Dictionary<string, DateTimeOffset> Read()
    {
        try
        {
            if (File.Exists(_filePath)
                && JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_filePath)) is { } read)
                return new Dictionary<string, DateTimeOffset>(read, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Debug("Search", $"server_last_up.json couldn't be read: {ex.Message}");
        }

        return new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>A search under way, as kept on disk until every mod is back on.</summary>
public sealed class BrokenModSession
{
    public string InstallPath { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>The searched mods' folders, as their paths read when enabled - all on when it began.</summary>
    public List<string> Mods { get; set; } = [];

    /// <summary>How long each of the server's log files was when it began (ServerTrial.LogLengths),
    /// so the search's lines can be taken out again also by the start that finds it cut short.</summary>
    public Dictionary<string, long>? Logs { get; set; }
}

//
// Data\broken_mod_search.json: written before the search turns its first mod off and deleted once
// they are all back on, so a search cut short - the app closed or crashed, the PC turned off - is
// found at the next start and the mods are put back then.
//
public sealed class BrokenModSessionStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath;

    public BrokenModSessionStore() : this(Path.Combine(AppPaths.DataDirectory, "broken_mod_search.json"))
    {
    }

    // A file of its own - for the tests.
    public BrokenModSessionStore(string filePath) => _filePath = filePath;

    public void Save(BrokenModSession session) =>
        SafeFile.WriteText(_filePath, JsonSerializer.Serialize(session, Options));

    /// <summary>The search that was not finished, or null.</summary>
    public BrokenModSession? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var session = JsonSerializer.Deserialize<BrokenModSession>(File.ReadAllText(_filePath));
            return session is { InstallPath.Length: > 0, Mods: not null } ? session : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Warn("Search", $"broken_mod_search.json couldn't be read: {ex.Message}");
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Search", $"broken_mod_search.json couldn't be deleted: {ex.Message}");
        }
    }

    //
    // Turns the session's mods on or off so that exactly <paramref name="on"/> of them are on -
    // ModDisableService's own move, between user/mods and user/mods.disabled. A mod found in
    // neither place is skipped, and one in both is left as it is.
    //
    public static ModDisableOutcome Set(BrokenModSession session, IReadOnlySet<string> on)
    {
        var turnOff = new List<InstalledMod>();
        var turnOn = new List<InstalledMod>();

        foreach (var enabledPath in session.Mods)
        {
            if (DisabledModPaths.Counterpart(enabledPath) is not { } disabledPath) continue;

            var isOn = Exists(enabledPath);
            var isOff = Exists(disabledPath);
            if (isOn == isOff) continue;

            var wanted = on.Contains(enabledPath);
            if (isOn && !wanted) turnOff.Add(Entry(enabledPath, disabled: false));
            else if (isOff && wanted) turnOn.Add(Entry(disabledPath, disabled: true));
        }

        var off = ModDisableService.Apply(turnOff, disable: true, session.InstallPath);
        var back = ModDisableService.Apply(turnOn, disable: false, session.InstallPath);
        return new ModDisableOutcome([.. off.Moved, .. back.Moved], [.. off.Failed, .. back.Failed]);
    }

    /// <summary>Every mod of the session back on.</summary>
    public static ModDisableOutcome PutBack(BrokenModSession session) =>
        Set(session, session.Mods.ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>The session's mods that are off right now.</summary>
    public static int TurnedOff(BrokenModSession session) =>
        session.Mods.Count(path => !Exists(path) && DisabledModPaths.Counterpart(path) is { } off && Exists(off));

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static InstalledMod Entry(string path, bool disabled) => new()
    {
        Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
        Target = InstalledModTarget.Server,
        FolderPath = path,
        IsDisabled = disabled,
    };
}
