using System.Text.RegularExpressions;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>What a finding is. Each one's <see cref="LogFinding.Args"/> are listed beside it.</summary>
public enum LogFindingKind
{
    /// <summary>A profile the server refused to load. Args: profile id.</summary>
    ProfileInvalid,

    /// <summary>A profile wears clothing no installed mod adds. Args: clothing id.</summary>
    ProfileClothingMissing,

    /// <summary>Fork (1.3.1): a profile holds an item no installed mod adds. Args: item id.</summary>
    ProfileItemMissing,

    /// <summary>Fork (1.3.1): a profile knows a trader no installed mod adds. Args: trader id.</summary>
    ProfileTraderMissing,

    /// <summary>Raid results the server couldn't read, so they weren't saved. Args: the type it couldn't read into.</summary>
    RaidResultsLost,

    /// <summary>The server couldn't start: its port is taken. Args: address.</summary>
    PortInUse,

    /// <summary>A plugin BepInEx didn't load for a missing dependency. Args: plugin "[Name Version]", what's missing.</summary>
    PluginMissingDependency,

    /// <summary>A plugin BepInEx didn't load because another is installed. Args: plugin, the other's GUID.</summary>
    PluginIncompatible,

    /// <summary>A plugin skipped because one it needs wasn't loaded. Args: plugin.</summary>
    PluginDependencyNotLoaded,

    /// <summary>A plugin skipped for a newer copy of itself. Args: plugin.</summary>
    PluginDuplicate,

    /// <summary>A plugin that failed while loading. Args: plugin, reason.</summary>
    PluginLoadError,

    /// <summary>An enabled server mod missing from the server's list of loaded mods. Args: none.</summary>
    ServerModNotLoaded,

    /// <summary>SPT's mod loader complained. Args: its message.</summary>
    ModLoaderProblem,

    /// <summary>A mod's server code failed answering the game. Args: route, exception message.</summary>
    RequestFailed,

    /// <summary>A task the server runs on a timer kept failing. Args: task, exception message.</summary>
    ScheduledTaskFailed,

    /// <summary>A mod logged an error on the server. Args: message.</summary>
    ModReportedError,

    /// <summary>Two or more mods ship the same bundle; one couldn't be added. Args: bundle.</summary>
    BundleClash,

    /// <summary>A bundle the game couldn't use: it needs others that aren't there. Args: bundle.</summary>
    BundleMissingDependency,

    /// <summary>A plugin logged errors in the game. Args: first message.</summary>
    PluginLoggedErrors,
}

public enum LogSeverity
{
    Critical,
    Warning,
    Info,
}

/// <summary>One thing found in the logs: what, about which mod (when it could be told), how often, and
/// the first entry that showed it.</summary>
/// <param name="Quote">The one line it was read from, when that is a line under the entry (a plugin in
/// SPT's "N plugins failed to load" list) rather than the entry itself.</param>
public sealed record LogFinding(
    LogFindingKind Kind,
    LogSeverity Severity,
    DiagnosedMod? Mod,
    IReadOnlyList<string> Args,
    int Count,
    LogEntry Sample,
    IReadOnlyList<DiagnosedMod> Others,
    string? Quote = null);

/// <summary>What Diagnose logs read and found.</summary>
/// <param name="ServerRunStartedAt">When the server's last run in its newest log began (only that run is read).</param>
/// <param name="Unexplained">Errors no check knew, in log order (the first 50) - for the copied report.</param>
public sealed record LogDiagnosis(
    LogFileSet Files, DateTime? ServerRunStartedAt, IReadOnlyList<LogFinding> Findings, int OtherErrors, IReadOnlyList<LogEntry> Unexplained);

//
// Fork (SSPTMM): Diagnose logs. Reads the server's last run, the last game session's BepInEx log and
// the game's own errors log, and says what is wrong in them and with which mod.
//
// Every check below is a line seen in a real SPT 4.1.6 install's logs (2026-10-03), except where
// marked "SPT source": those are SPT's own locale strings (server-csharp 4.1.6, en.json) not seen in
// that install. A finding names a mod only when the line names it in a way LogModLocator can trace;
// anything else is counted, not guessed at.
//
public static partial class LogDiagnoser
{
    public static LogDiagnosis Diagnose(LogFileSet files, LogModLocator mods)
    {
        var server = files.Server is { } s ? LastRun(LogEntries.Parse(LogKind.Server, s, LogEntries.ReadLines(s))) : [];
        var bepInEx = files.BepInEx is { } b ? LogEntries.Parse(LogKind.BepInEx, b, LogEntries.ReadLines(b)) : [];
        var game = files.GameErrors is { } g ? LogEntries.Parse(LogKind.GameErrors, g, LogEntries.ReadLines(g)) : [];

        return Diagnose(files, server, bepInEx, game, mods);
    }

    public static LogDiagnosis Diagnose(
        LogFileSet files, IReadOnlyList<LogEntry> server, IReadOnlyList<LogEntry> bepInEx, IReadOnlyList<LogEntry> game, LogModLocator mods)
    {
        var found = new Findings();
        var explained = new HashSet<LogEntry>();

        CheckServer(server, mods, found, explained);
        CheckBepInEx(bepInEx, mods, found, explained);
        CheckGame(game, mods, found);

        var other = server.Where(e => e.IsError && !explained.Contains(e) && !IsStackPart(e))
            .Concat(bepInEx.Where(e => e.IsError && !explained.Contains(e)))
            .ToList();

        return new LogDiagnosis(files, server.FirstOrDefault()?.At, found.List(), other.Count, other.Take(50).ToList());
    }

    //
    // Only the last run of the server in the file: a day's log holds every start that day, and what a
    // run before a fix said is no longer true. A run starts where SPT's mod loader reports how many
    // mods it is loading; the prepatch lines just before it belong to the same start.
    //
    public static List<LogEntry> LastRun(List<LogEntry> entries)
    {
        var start = entries.FindLastIndex(e => e.Message.StartsWith("ModLoader: loading:", StringComparison.Ordinal));
        if (start < 0) return entries;

        while (start > 0 && entries[start - 1].Source.EndsWith(".ModLoader", StringComparison.Ordinal)) start--;
        return entries.GetRange(start, entries.Count - start);
    }

    // ---- The server log ----

    [GeneratedRegex(@"^Mod: .+ version: \S+ \(GUID: (?<guid>[^ |)]+)")]
    private static partial Regex LoadedMod();

    [GeneratedRegex(@"^Failed to load profile with ID '(?<id>[0-9a-fA-F]{24})'")]
    private static partial Regex ProfileInvalid();

    [GeneratedRegex(@"InvalidModdedClothingException: Clothing item: \w+ \{ Id = (?<id>[0-9a-fA-F]{24})")]
    private static partial Regex ClothingMissing();

    // SPT 4.1: "...InvalidModdedItemException: Item: 6ac08b5ba2a7936f34652936 found in profile that does
    // not exist in items db." and "...InvalidModdedTraderException: Trader: 699f89c757994beece5cf7e1
    // found in profile but does not exist in SPT." (measured on a 4.1.6 server log).
    [GeneratedRegex(@"InvalidModdedItemException: Item: (?<id>[0-9a-fA-F]{24}) found in profile")]
    private static partial Regex ItemMissing();

    [GeneratedRegex(@"InvalidModdedTraderException: Trader: (?<id>[0-9a-fA-F]{24}) found in profile")]
    private static partial Regex TraderMissing();

    [GeneratedRegex(@"^Failed to start the web server on (?<address>\S+?)\. Socket error: AddressAlreadyInUse")]
    private static partial Regex PortInUse();

    [GeneratedRegex(@"^Error handling request: (?<route>\S+)")]
    private static partial Regex RequestFailed();

    [GeneratedRegex(@"^Scheduled event: '(?<task>[^']+)' failed to run successfully")]
    private static partial Regex ScheduledFailed();

    [GeneratedRegex(@"The JSON value could not be converted to (?<type>[\w.]*\w)")]
    private static partial Regex JsonConversion();

    [GeneratedRegex(@"^Unable to add bundle: (?<bundle>\S+)")]
    private static partial Regex BundleNotAdded();

    [GeneratedRegex(@"^(?:\[[^\]]+\] )?\d+ plugins? failed to load due to errors:")]
    private static partial Regex PluginsFailed();

    // "[LateToTheParty] You must use ..." - a plugin's message relayed by SPT's ClientLogController.
    [GeneratedRegex(@"^\[(?<who>[^\]]+)\] (?<message>.+)$")]
    private static partial Regex Relayed();

    // SPT source: the mod loader's complaints. Matched loosely - the message itself is shown.
    private static readonly string[] ModLoaderSigns =
    [
        "is not compatible with the current version of SPT",
        "is a pre-4.0.0 server mod",
        "No Assemblies found in path",
        "is a client mod and should be placed in",
        "You incorrectly installed a mod",
        "to be installed",
        "more than one version of",
        "is incompatible with",
        "Exception occured while loading a mod",
        "Failed to locate patcher directory",
    ];

    private static void CheckServer(IReadOnlyList<LogEntry> entries, LogModLocator mods, Findings found, HashSet<LogEntry> explained)
    {
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            Match m;

            if ((m = LoadedMod().Match(entry.Message)).Success)
            {
                loaded.Add(m.Groups["guid"].Value);
                continue;
            }

            if (!entry.IsError && !entry.IsWarning) continue;

            if ((m = ProfileInvalid().Match(entry.Message)).Success)
            {
                found.Add(LogFindingKind.ProfileInvalid, LogSeverity.Critical, null, [m.Groups["id"].Value], entry);
                explained.Add(entry);
            }
            else if ((m = ClothingMissing().Match(entry.Message)).Success)
            {
                found.Add(LogFindingKind.ProfileClothingMissing, LogSeverity.Warning, null, [m.Groups["id"].Value], entry);
                explained.Add(entry);
            }
            else if ((m = ItemMissing().Match(entry.Message)).Success)
            {
                found.Add(LogFindingKind.ProfileItemMissing, LogSeverity.Warning, null, [m.Groups["id"].Value], entry);
                explained.Add(entry);
            }
            else if ((m = TraderMissing().Match(entry.Message)).Success)
            {
                found.Add(LogFindingKind.ProfileTraderMissing, LogSeverity.Warning, null, [m.Groups["id"].Value], entry);
                explained.Add(entry);
            }
            else if ((m = PortInUse().Match(entry.Message)).Success)
            {
                found.Add(LogFindingKind.PortInUse, LogSeverity.Critical, null, [m.Groups["address"].Value], entry);
                explained.Add(entry);
            }
            else if ((m = RequestFailed().Match(entry.Message)).Success)
            {
                var detail = Following(entries, i, explained);
                explained.Add(entry);
                var route = m.Groups["route"].Value;
                var error = FirstLine(detail);
                var origin = Origin(detail, mods);
                var quote = Excerpt(entry, detail, origin, mods);

                if (origin is null && JsonConversion().Match(string.Join("\n", detail)) is { Success: true } json
                    && route.StartsWith("/client/match/local/end", StringComparison.Ordinal))
                {
                    found.Add(LogFindingKind.RaidResultsLost, LogSeverity.Critical, null, [ShortType(json.Groups["type"].Value)], entry, quote: quote);
                }
                else
                {
                    found.Add(LogFindingKind.RequestFailed, origin is null ? LogSeverity.Info : LogSeverity.Warning, origin, [route, error], entry, quote: quote);
                }
            }
            else if ((m = ScheduledFailed().Match(entry.Message)).Success)
            {
                var detail = Following(entries, i, explained);
                explained.Add(entry);
                var origin = Origin(detail, mods);
                found.Add(LogFindingKind.ScheduledTaskFailed, LogSeverity.Info, origin,
                    [ShortType(m.Groups["task"].Value), FirstLine(detail)], entry, quote: Excerpt(entry, detail, origin, mods));
            }
            else if ((m = BundleNotAdded().Match(entry.Message)).Success)
            {
                var bundle = m.Groups["bundle"].Value;
                var holders = mods.ByBundle(bundle);
                found.Add(LogFindingKind.BundleClash, LogSeverity.Warning, holders.FirstOrDefault(), [bundle], entry, holders.Skip(1).ToList());
                explained.Add(entry);
            }
            else if (entry.Source.EndsWith("ClientLogController", StringComparison.Ordinal))
            {
                if (PluginsFailed().IsMatch(entry.Message))
                {
                    foreach (var line in entry.More) CheckChainloader(line, entry, mods, found);
                    explained.Add(entry);
                }
                else if ((m = Relayed().Match(entry.Message)).Success && mods.ByName(m.Groups["who"].Value) is { } who && entry.IsError)
                {
                    found.Add(LogFindingKind.ModReportedError, LogSeverity.Warning, who, [m.Groups["message"].Value], entry);
                    explained.Add(entry);
                }
            }
            else if (entry.Source.Contains(".Modding.", StringComparison.Ordinal)
                     && ModLoaderSigns.Any(sign => entry.Message.Contains(sign, StringComparison.Ordinal)))
            {
                found.Add(LogFindingKind.ModLoaderProblem, entry.IsError ? LogSeverity.Critical : LogSeverity.Warning,
                    Mentioned(entry.Message, mods), [entry.Message], entry);
                explained.Add(entry);
            }
            else if (entry.IsError && !IsStackPart(entry) && mods.ByCode(entry.Source) is { } owner)
            {
                found.Add(LogFindingKind.ModReportedError, LogSeverity.Warning, owner, [Unbracket(entry.Message)], entry);
                explained.Add(entry);
            }
        }

        // An enabled 4.x server mod the server didn't list as loaded - only said when the run listed any,
        // so a log in another format never makes every mod look unloaded.
        // A mod put in after that run started can't have been loaded by it, and isn't said.
        if (loaded.Count > 0 && entries.Count > 0)
        {
            var started = entries[0].At;
            foreach (var mod in mods.ServerMods.Where(m => m.Guid is { } guid && !loaded.Contains(guid)))
            {
                if (started is { } at && mod.InstalledAt is { } made && made.LocalDateTime > at) continue;
                found.Add(LogFindingKind.ServerModNotLoaded, LogSeverity.Warning, mod, [], entries[0]);
            }
        }
    }

    // ---- BepInEx ----

    [GeneratedRegex(@"^Could not load \[(?<plugin>.+? \S+)\] because it has missing dependencies: (?<what>.+)$")]
    private static partial Regex MissingDependencies();

    [GeneratedRegex(@"^Could not load \[(?<plugin>.+? \S+)\] because it is incompatible with: (?<what>.+)$")]
    private static partial Regex Incompatible();

    [GeneratedRegex(@"^Skipping \[(?<plugin>.+? \S+)\] because it has a dependency that was not loaded")]
    private static partial Regex DependencyNotLoaded();

    [GeneratedRegex(@"^Skipping \[(?<plugin>.+? \S+)\] because a newer version exists")]
    private static partial Regex NewerExists();

    [GeneratedRegex(@"^Error loading \[(?<plugin>.+? \S+)\] ?: ?(?<why>.+)$")]
    private static partial Regex LoadError();

    // "Manimal-InterchangeRework 1.0.5" -> "Manimal-InterchangeRework".
    private static string PluginName(string plugin) => plugin[..plugin.LastIndexOf(' ')];

    private static bool CheckChainloader(string line, LogEntry entry, LogModLocator mods, Findings found)
    {
        Match m;
        if ((m = MissingDependencies().Match(line)).Success)
            found.Add(LogFindingKind.PluginMissingDependency, LogSeverity.Critical, mods.ByName(PluginName(m.Groups["plugin"].Value)),
                [m.Groups["plugin"].Value, m.Groups["what"].Value], entry, quote: Quote(line, entry));
        else if ((m = Incompatible().Match(line)).Success)
            found.Add(LogFindingKind.PluginIncompatible, LogSeverity.Critical, mods.ByName(PluginName(m.Groups["plugin"].Value)),
                [m.Groups["plugin"].Value, m.Groups["what"].Value], entry,
                mods.ByName(m.Groups["what"].Value) is { } other ? [other] : [], Quote(line, entry));
        else if ((m = DependencyNotLoaded().Match(line)).Success)
            found.Add(LogFindingKind.PluginDependencyNotLoaded, LogSeverity.Warning, mods.ByName(PluginName(m.Groups["plugin"].Value)),
                [m.Groups["plugin"].Value], entry, quote: Quote(line, entry));
        else if ((m = NewerExists().Match(line)).Success)
            found.Add(LogFindingKind.PluginDuplicate, LogSeverity.Warning, mods.ByName(PluginName(m.Groups["plugin"].Value)),
                [m.Groups["plugin"].Value], entry, quote: Quote(line, entry));
        else if ((m = LoadError().Match(line)).Success)
            found.Add(LogFindingKind.PluginLoadError, LogSeverity.Critical, mods.ByName(PluginName(m.Groups["plugin"].Value)),
                [m.Groups["plugin"].Value, m.Groups["why"].Value], entry, quote: Quote(line, entry));
        else
            return false;

        return true;
    }

    private static void CheckBepInEx(IReadOnlyList<LogEntry> entries, LogModLocator mods, Findings found, HashSet<LogEntry> explained)
    {
        foreach (var entry in entries)
        {
            if (entry.Source == "BepInEx")
            {
                if (CheckChainloader(entry.Message, entry, mods, found)) explained.Add(entry);
                continue;
            }

            if (!entry.IsError) continue;

            // A plugin's own logger is named after the plugin; Unity's errors carry a stack to trace.
            var owner = mods.ByName(entry.Source) ?? Origin(entry.More, mods);
            if (owner is null) continue;

            found.Add(LogFindingKind.PluginLoggedErrors, LogSeverity.Info, owner, [entry.Message], entry);
            explained.Add(entry);
        }
    }

    // ---- The game's errors log ----

    [GeneratedRegex("^Bundle \"(?<bundle>[^\"]+)\" has dependency \"[^\"]+\" that was not found in manifest")]
    private static partial Regex BundleDependency();

    private static void CheckGame(IReadOnlyList<LogEntry> entries, LogModLocator mods, Findings found)
    {
        foreach (var entry in entries)
        {
            if (BundleDependency().Match(entry.Message) is not { Success: true } m) continue;

            var bundle = m.Groups["bundle"].Value;
            var holders = mods.ByBundle(bundle);
            found.Add(LogFindingKind.BundleMissingDependency, LogSeverity.Warning, holders.FirstOrDefault(), [bundle], entry);
        }
    }

    // ---- Shared ----

    private static string? Quote(string line, LogEntry entry) => line == entry.Message ? null : line;

    // A failure's line, its error, and the stack frame in the mod's code when there is one.
    private static string Excerpt(LogEntry entry, List<string> detail, DiagnosedMod? origin, LogModLocator mods)
    {
        var lines = new List<string> { entry.Message };
        if (detail.FirstOrDefault(l => !IsFrame(l)) is { } error) lines.Add(error.Trim());

        var frame = origin is null ? null : detail.FirstOrDefault(l => IsFrame(l) && mods.ByCode(FrameName(l)) == origin);
        if (frame is not null) lines.Add(frame.Trim());

        return string.Join("\n", lines);
    }

    private static string FrameName(string line)
    {
        var trimmed = line.Trim();
        var frame = trimmed.Length > 3 ? trimmed[3..] : string.Empty;
        var paren = frame.IndexOf('(');
        return paren > 0 ? frame[..paren] : frame;
    }

    // The entries straight after a request or task failure that carry its exception: the same logger,
    // within a second. Their text, the first line being the exception's message.
    private static List<string> Following(IReadOnlyList<LogEntry> entries, int index, HashSet<LogEntry> explained)
    {
        var lead = entries[index];
        var lines = new List<string>(lead.More);
        for (var j = index + 1; j < entries.Count && j <= index + 4; j++)
        {
            var next = entries[j];
            if (next.Source != lead.Source || next.Level != lead.Level) break;
            if (next.At is { } at && lead.At is { } start && (at - start).Duration() > TimeSpan.FromSeconds(1)) break;
            if (RequestFailed().IsMatch(next.Message) || ScheduledFailed().IsMatch(next.Message)) break;

            lines.Add(next.Message);
            lines.AddRange(next.More);
            explained.Add(next);
        }

        return lines;
    }

    private static string FirstLine(List<string> detail) =>
        detail.FirstOrDefault(l => !IsFrame(l))?.Trim() ?? string.Empty;

    // The first frame of a stack trace that is in a mod's code.
    private static DiagnosedMod? Origin(IEnumerable<string> lines, LogModLocator mods)
    {
        foreach (var line in lines)
        {
            if (!IsFrame(line)) continue;
            if (mods.ByCode(FrameName(line)) is { } mod) return mod;
        }

        return null;
    }

    private static bool IsFrame(string line) => line.TrimStart().StartsWith("at ", StringComparison.Ordinal);

    // A stack line SPT logged as an entry of its own ("[Critical][...]    at ...").
    private static bool IsStackPart(LogEntry entry) => IsFrame(entry.Message);

    // The installed mod a message names by folder, declared name or GUID - the longest such name, so
    // "SAIN" does not win over "SAIN-ServerMod". Names of three letters or fewer are not looked for.
    private static DiagnosedMod? Mentioned(string message, LogModLocator mods) =>
        mods.All
            .SelectMany(m => new[] { m.Name, m.DisplayName, m.Guid }.Where(n => n is { Length: > 3 }).Select(n => (Mod: m, Name: n!)))
            .Where(x => message.Contains(x.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Name.Length)
            .Select(x => x.Mod)
            .FirstOrDefault();

    // "SPTarkov.Server.Core.Models.Enums.SkillTypes" -> "SkillTypes".
    private static string ShortType(string type) => type[(type.LastIndexOf('.') + 1)..];

    // "[SVM] Initialization cancelled" -> "Initialization cancelled": the mod is named beside it already.
    private static string Unbracket(string message) =>
        Relayed().Match(message) is { Success: true } m ? m.Groups["message"].Value : message;

    // Findings of the same kind about the same mod and the same subject count together.
    private sealed class Findings
    {
        private readonly Dictionary<string, LogFinding> _byKey = [];
        private readonly List<string> _order = [];

        public void Add(LogFindingKind kind, LogSeverity severity, DiagnosedMod? mod, IReadOnlyList<string> args, LogEntry sample,
            IReadOnlyList<DiagnosedMod>? others = null, string? quote = null)
        {
            // Errors a mod reports differ by wording each time (ids, counts); one card per mod says it.
            var subject = kind is LogFindingKind.ModReportedError or LogFindingKind.PluginLoggedErrors or LogFindingKind.RequestFailed
                    or LogFindingKind.ScheduledTaskFailed
                ? (args.Count > 0 && kind is LogFindingKind.RequestFailed or LogFindingKind.ScheduledTaskFailed ? args[0] : string.Empty)
                : string.Join("|", args);
            var key = $"{kind}|{mod?.FolderPath}|{subject}";

            if (_byKey.TryGetValue(key, out var seen))
            {
                _byKey[key] = seen with { Count = seen.Count + 1 };
                return;
            }

            _byKey[key] = new LogFinding(kind, severity, mod, args, 1, sample, others ?? [], quote);
            _order.Add(key);
        }

        public List<LogFinding> List() =>
            [.. _order.Select(k => _byKey[k]).OrderBy(f => f.Severity)];
    }
}
