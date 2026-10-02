using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.ServerMap;

//
// One installed mod as a machine reports it to the map.
//
// The server stores these without reading them and hands them back out as they arrived, so this
// class is the only definition of the shape anywhere. Fields a later build adds are ignored by an
// earlier one, and fields it drops read as their defaults - never a reason to refuse a whole map.
//
public sealed class ReportedMod
{
    public string Name { get; set; } = "";

    public int? ModId { get; set; }

    // Addon ids and mod ids are separate sequences on sp-mod.com; without this, addon 116 would
    // match mod 116 on the reading side.
    public bool IsAddon { get; set; }

    public string? Version { get; set; }

    public string? Guid { get; set; }

    public bool Disabled { get; set; }

    public bool Incomplete { get; set; }

    // Folder names only, never paths. They are a list-match key for mods the catalog never matched.
    public List<string> Folders { get; set; } = [];
}

//
// The body of POST /tcfservermap/report. Mods is null on an ordinary heartbeat and attached when the
// inventory changed or the server replied resend.
//
public sealed class MachineReport
{
    public int Protocol { get; set; } = ServerMapClient.SupportedProtocol;

    public string ClientId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool Hosts { get; set; }

    public bool Plays { get; set; }

    public bool Headless { get; set; }

    public bool GameRunning { get; set; }

    public string? SptVersion { get; set; }

    public string? AppVersion { get; set; }

    public string InventoryHash { get; set; } = "";

    public List<ReportedMod>? Mods { get; set; }
}

// One row of GET /tcfservermap/clients.
public sealed class MapMachine
{
    public bool IsYou { get; set; }

    public string DisplayName { get; set; } = "";

    public bool Hosts { get; set; }

    public bool Plays { get; set; }

    public bool Headless { get; set; }

    public bool GameRunning { get; set; }

    public string? SptVersion { get; set; }

    public string? AppVersion { get; set; }

    public DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    // Measured on the server, so presence never depends on this machine's clock agreeing with it.
    public long SecondsSinceSeen { get; set; }

    public DateTimeOffset? InventoryReportedAt { get; set; }

    // Null when the machine has not sent an inventory yet, or sent one this build cannot read.
    [JsonIgnore]
    public List<ReportedMod>? Mods { get; set; }

    [JsonIgnore]
    public bool InventoryUnreadable { get; set; }
}

public enum MapPresence
{
    // Reported within the last few intervals, and the game was running.
    InGame,

    // Reported within the last few intervals - the app is running, maybe only in the tray.
    AppOpen,

    // Not heard from lately. LastSeen says when.
    Away,
}

public sealed record ServerMapReportResult
{
    public required ServerMapEndpoint Endpoint { get; init; }

    public ServerMapProblem Problem { get; init; }

    // The server has no inventory for this machine, or a different one: send it with the next report.
    public bool Resend { get; init; }

    public int IntervalSeconds { get; init; } = ServerMapMachines.DefaultIntervalSeconds;

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Succeeded => Problem == ServerMapProblem.None;
}

public sealed record ServerMapClientsResult
{
    public required ServerMapEndpoint Endpoint { get; init; }

    public IReadOnlyList<MapMachine> Machines { get; init; } = [];

    public int IntervalSeconds { get; init; } = ServerMapMachines.DefaultIntervalSeconds;

    public ServerMapProblem Problem { get; init; }

    public int? StatusCode { get; init; }

    public Exception? Error { get; init; }

    public bool Succeeded => Problem == ServerMapProblem.None;
}

//
// Where a machine stands against the list the server publishes.
//
// Behind and Manual come from the same planner the Mod lists page and the Play page use, run against
// what the machine reported, so the map can never disagree with what that machine would be told if
// it applied the list itself. Extras are installed mods the list does not name at all.
//
public sealed record MachineStanding
{
    public required IReadOnlyList<ModListAction> Behind { get; init; }

    public required IReadOnlyList<ModListAction> Manual { get; init; }

    public required IReadOnlyList<ReportedMod> Extras { get; init; }

    // How many entries apply to this machine - the denominator for "N of M".
    public required int Expected { get; init; }

    public bool UpToDate => Behind.Count == 0 && Manual.Count == 0;
}

public static class ServerMapMachines
{
    public const int DefaultIntervalSeconds = 60;

    // How many missed heartbeats before a machine reads as away. Two and a half rather than one, so
    // a single slow or dropped report does not make somebody flicker off the map.
    public const double StaleAfterIntervals = 2.5;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<ReportedMod> InventoryOf(IEnumerable<ModListCandidate> installed) =>
        installed
            .Select(c => new ReportedMod
            {
                Name = c.Name.Trim(),
                ModId = c.ModId,
                IsAddon = c.IsAddon,
                Version = string.IsNullOrWhiteSpace(c.Version) ? null : c.Version.Trim(),
                Guid = string.IsNullOrWhiteSpace(c.Guid) ? null : c.Guid.Trim(),
                Disabled = c.IsDisabled,
                Incomplete = c.IsIncomplete,
                Folders = c.Folders
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => LastSegment(f.Trim().TrimEnd('\\', '/')))
                    .Where(f => f.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .OrderBy(m => m.IsAddon)
            .ThenBy(m => m.ModId ?? int.MaxValue)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Guid, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string LastSegment(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    //
    // A fingerprint of the inventory, so a heartbeat can say "same as last time" without resending
    // it. Taken over InventoryOf's output, which is already sorted and trimmed, so two scans of an
    // unchanged install always agree however the scanner happened to order them.
    //
    public static string HashOf(IReadOnlyList<ReportedMod> inventory)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(inventory, Json);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static List<ModListCandidate> CandidatesOf(IEnumerable<ReportedMod> inventory) =>
        inventory
            .Select(m => new ModListCandidate
            {
                Name = m.Name,
                ModId = m.ModId,
                IsAddon = m.IsAddon,
                Version = m.Version,
                Guid = m.Guid,
                IsDisabled = m.Disabled,
                IsIncomplete = m.Incomplete,
                Folders = m.Folders,
            })
            .ToList();

    //
    // What the published list asks of a machine with these roles. A host takes the entries scoped to
    // the server, a player the client ones, a headless the headless ones, and a machine doing several
    // takes the union. A machine that claims none is read as a player, as InstallRole does.
    //
    public static ModListEntryScope ScopeFor(bool hosts, bool plays, bool headless)
    {
        ModListEntryScope scope = 0;

        if (plays) scope |= ModListEntryScope.Client;
        if (headless) scope |= ModListEntryScope.Headless;
        if (hosts) scope |= ModListEntryScope.Server;

        return scope == 0 ? ModListEntryScope.Client : scope;
    }

    public static ModListEntryScope ScopeFor(MapMachine machine) =>
        ScopeFor(machine.Hosts, machine.Plays, machine.Headless);

    public static MapPresence PresenceOf(MapMachine machine, int intervalSeconds)
    {
        var interval = intervalSeconds > 0 ? intervalSeconds : DefaultIntervalSeconds;

        if (machine.SecondsSinceSeen > interval * StaleAfterIntervals) return MapPresence.Away;

        return machine.GameRunning ? MapPresence.InGame : MapPresence.AppOpen;
    }

    //
    // Null when the machine has not reported an inventory, which is "not known" rather than
    // "has nothing" - a card must not claim somebody is missing every mod on the list.
    //
    public static MachineStanding? StandingOf(ModList published, MapMachine machine)
    {
        if (machine.Mods is null) return null;

        published = AsServed(published);

        var candidates = CandidatesOf(machine.Mods);
        var scope = ScopeFor(machine);

        var plan = ModListPlanner.Build(published, candidates, machine: scope);

        var behind = plan.Actions
            .Where(a => a.Kind is ModListActionKind.Install or ModListActionKind.Update or ModListActionKind.Enable)
            .ToList();

        var match = new ModListMatch(candidates);
        var claimed = new bool[candidates.Count];

        foreach (var entry in published.Entries)
        {
            var index = match.IndexOf(entry);
            if (index >= 0) claimed[index] = true;
        }

        var extras = machine.Mods.Where((_, index) => !claimed[index]).ToList();

        return new MachineStanding
        {
            Behind = behind,
            Manual = plan.Manual.ToList(),
            Extras = extras,
            Expected = published.EntriesApplyingTo(scope).Count(),
        };
    }

    //
    // The list as the machines on the map receive it. On the host the published list is the
    // operator's own, held Local, and a Local list applies whole - which would have every player
    // missing fika-server. Read as served, scope decides and nothing is swept.
    //
    internal static ModList AsServed(ModList list) =>
        list.Origin == ModListOrigin.Server
            ? list
            : new ModList
            {
                Id = list.Id,
                Name = list.Name,
                Description = list.Description,
                Revision = list.Revision,
                Origin = ModListOrigin.Server,
                Policy = list.Policy,
                SptVersion = list.SptVersion,
                CreatedAt = list.CreatedAt,
                UpdatedAt = list.UpdatedAt,
                Entries = [.. list.Entries],
            };

    //
    // Reads a /clients body. A machine whose inventory this build cannot read keeps its row with the
    // inventory marked unreadable, rather than taking the whole map down with it - another player on
    // a newer build should never blank the page for everybody else.
    //
    internal static (List<MapMachine> Machines, int IntervalSeconds) ParseClients(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        var interval = DefaultIntervalSeconds;
        if (TryProperty(root, "intervalSeconds", out var intervalElement)
            && intervalElement.TryGetInt32(out var parsedInterval) && parsedInterval > 0)
        {
            interval = parsedInterval;
        }

        var machines = new List<MapMachine>();

        if (!TryProperty(root, "clients", out var clients) || clients.ValueKind != JsonValueKind.Array)
            return (machines, interval);

        foreach (var element in clients.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            MapMachine? machine;
            try
            {
                machine = element.Deserialize<MapMachine>(Json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (machine is null) continue;

            if (TryProperty(element, "mods", out var mods) && mods.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    machine.Mods = mods.Deserialize<List<ReportedMod>>(Json);
                    machine.InventoryUnreadable = machine.Mods is null;
                }
                catch (JsonException)
                {
                    machine.InventoryUnreadable = true;
                }
            }

            machines.Add(machine);
        }

        return (machines, interval);
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
