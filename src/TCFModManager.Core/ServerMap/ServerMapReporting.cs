using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.Core.ServerMap;

public enum ReportConsentState
{
    Unanswered,
    Allowed,
    Declined,
}

//
// What this install tells the map about itself, and whether it is allowed to.
//
// Nothing here sends anything. The caller builds a report, checks consent, and hands both to
// ServerMapClient - so a code path that reports without asking has to be written on purpose.
//
public static class ServerMapReporting
{
    public const int MaxDisplayNameLength = 64;

    public static string EnsureClientId(ServerMapSettings settings)
    {
        if (Guid.TryParseExact(settings.ClientId, "D", out var existing)) return existing.ToString("D");

        settings.ClientId = Guid.NewGuid().ToString("D");
        return settings.ClientId;
    }

    public static string DisplayNameOf(ServerMapSettings settings, string? machineName = null)
    {
        var name = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? machineName ?? Environment.MachineName
            : settings.DisplayName;

        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (name.Length == 0) name = machineName ?? Environment.MachineName;

        return name.Length <= MaxDisplayNameLength ? name : name[..MaxDisplayNameLength];
    }

    public static string ServerKey(ServerMapEndpoint endpoint) =>
        endpoint.HasPin
            ? endpoint.PinnedThumbprint!.Trim().ToUpperInvariant()
            : $"{endpoint.Host.Trim().ToLowerInvariant()}:{endpoint.Port}";

    public static ReportConsentState ConsentFor(ServerMapSettings settings, ServerMapEndpoint endpoint)
    {
        var key = ServerKey(endpoint);
        var answer = settings.ReportConsent.FirstOrDefault(c =>
            string.Equals(c.Server, key, StringComparison.OrdinalIgnoreCase));

        return answer is null
            ? ReportConsentState.Unanswered
            : answer.Allowed ? ReportConsentState.Allowed : ReportConsentState.Declined;
    }

    public static void SetConsent(ServerMapSettings settings, ServerMapEndpoint endpoint, bool allowed,
        DateTimeOffset? now = null)
    {
        var key = ServerKey(endpoint);

        settings.ReportConsent.RemoveAll(c => string.Equals(c.Server, key, StringComparison.OrdinalIgnoreCase));
        settings.ReportConsent.Add(new ServerMapConsent
        {
            Server = key,
            Allowed = allowed,
            AnsweredAt = now ?? DateTimeOffset.UtcNow,
        });
    }

    public static void ForgetConsent(ServerMapSettings settings, ServerMapEndpoint endpoint)
    {
        var key = ServerKey(endpoint);
        settings.ReportConsent.RemoveAll(c => string.Equals(c.Server, key, StringComparison.OrdinalIgnoreCase));
    }

    //
    // True when this install runs the Server Map server mod: the mod writes its key into the
    // install's Data\ServerMap\ the first time it starts, and nothing else does.
    //
    public static bool HostsServerMap(string? sptInstallPath, string? appDirectory = null) =>
        ServerMapKeyFile.TryFind(sptInstallPath, out _, appDirectory);

    public static MachineReport Build(
        ServerMapSettings settings,
        InstallRoles roles,
        bool hosts,
        bool gameRunning,
        string? sptVersion,
        string? appVersion,
        IReadOnlyList<ReportedMod> inventory,
        bool includeInventory,
        string? machineName = null) =>
        new()
        {
            ClientId = EnsureClientId(settings),
            DisplayName = DisplayNameOf(settings, machineName),
            Hosts = hosts,
            Plays = roles.HasFlag(InstallRoles.Player),
            Headless = roles.HasFlag(InstallRoles.Headless),
            GameRunning = gameRunning,
            SptVersion = sptVersion,
            AppVersion = appVersion,
            InventoryHash = ServerMapMachines.HashOf(inventory),
            Mods = includeInventory ? inventory.ToList() : null,
        };
}
