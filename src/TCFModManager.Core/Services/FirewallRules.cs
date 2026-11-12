using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TCFModManager.Core.Services;

/// <summary>One Windows Firewall rule, as far as it matters here. Protocol: 6 TCP, 17 UDP, 256 any.
/// Profiles: 1 domain, 2 private, 4 public, added up.</summary>
public sealed record FirewallRule(string? Application, int Protocol, string? LocalPorts, bool Enabled, bool Allows, bool Inbound, int Profiles);

/// <summary>The rules, the kinds of network the PC is on now, and whether the firewall is on for them.</summary>
public sealed record FirewallState(IReadOnlyList<FirewallRule> Rules, int CurrentProfiles, bool On);

//
// Fork (1.4.7): whether Windows Firewall lets something from another PC reach a program here - for
// a Fika host, whose friends otherwise see nothing and no reason. Read only: nothing is changed, and
// what to do about it is said, not done.
//
// As Windows decides it: a rule that blocks wins over one that allows, and with neither, what comes
// in is refused. Only Windows' own firewall is known; another firewall, or the router, is not.
//
// Which rules count. A rule naming the program counts, either way. A rule naming no program is
// seldom for every program: Windows lists the rules of Store apps and of single user accounts that
// way too, with what narrows them not shown here (seen on a real PC: "Microsoft Store" allowing
// everything, and a sandbox account's rule blocking everything). So such a rule counts only when it
// allows and names the port itself - someone opening a port by hand - and never when it blocks.
// HUNCH: that this reading is right more often than Windows' full one would be worth.
//
public static class FirewallRules
{
    public const int Tcp = 6;
    public const int Udp = 17;
    private const int AnyProtocol = 256;

    /// <summary>True when it gets through, false when it does not; null when it cannot be told.</summary>
    public static bool? Reaches(FirewallState? state, string program, int protocol, int port)
    {
        if (state is null) return null;
        if (!state.On) return true;

        var matching = state.Rules.Where(rule => rule.Enabled && rule.Inbound
            && (rule.Profiles & state.CurrentProfiles) != 0
            && (rule.Protocol == protocol || rule.Protocol == AnyProtocol)
            && HasPort(rule.LocalPorts, port)
            && (string.IsNullOrWhiteSpace(rule.Application)
                ? rule.Allows && NamesPorts(rule.LocalPorts)
                : IsProgram(rule.Application, program))).ToList();

        if (matching.Any(rule => !rule.Allows)) return false;
        return matching.Count > 0;
    }

    private static bool NamesPorts(string? ports) => !string.IsNullOrWhiteSpace(ports) && ports.Trim() != "*";

    private static bool IsProgram(string application, string program)
    {

        try
        {
            return string.Equals(
                Path.GetFullPath(Environment.ExpandEnvironmentVariables(application.Trim())),
                Path.GetFullPath(program),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    // "*", nothing, "6969", "6969,7000-7010". A name ("RPC") is some other port.
    private static bool HasPort(string? ports, int port)
    {
        if (string.IsNullOrWhiteSpace(ports) || ports.Trim() == "*") return true;

        foreach (var part in ports.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(ends[0], out var from)) continue;

            var to = from;
            if (ends.Length == 2 && !int.TryParse(ends[1], out to)) continue;
            if (port >= from && port <= to) return true;
        }

        return false;
    }

    /// <summary>The PC's firewall as it stands; null when it cannot be read (or this is not Windows).</summary>
    public static FirewallState? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;

        object? policy = null;
        try
        {
            if (Type.GetTypeFromProgID("HNetCfg.FwPolicy2") is not { } type) return null;
            policy = Activator.CreateInstance(type);
            if (policy is null) return null;

            var current = Convert.ToInt32(Get(policy, "CurrentProfileTypes"));
            var on = false;
            foreach (var profile in new[] { 1, 2, 4 })
                if ((current & profile) != 0) on |= Convert.ToBoolean(Get(policy, "FirewallEnabled", profile));

            var rules = new List<FirewallRule>();
            if (Get(policy, "Rules") is IEnumerable all)
            {
                foreach (var rule in all)
                {
                    if (rule is null) continue;
                    rules.Add(new FirewallRule(
                        Get(rule, "ApplicationName") as string,
                        Convert.ToInt32(Get(rule, "Protocol")),
                        Get(rule, "LocalPorts") as string,
                        Convert.ToBoolean(Get(rule, "Enabled")),
                        Convert.ToInt32(Get(rule, "Action")) == 1,
                        Convert.ToInt32(Get(rule, "Direction")) == 1,
                        Convert.ToInt32(Get(rule, "Profiles"))));
                }
            }

            return new FirewallState(rules, current, on);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidCastException
            or FormatException or UnauthorizedAccessException or MissingMethodException or NotSupportedException)
        {
            AppLog.Debug("Fika", $"couldn't read Windows Firewall: {ex.Message}");
            return null;
        }
        finally
        {
            if (policy is not null && OperatingSystem.IsWindows() && Marshal.IsComObject(policy)) Marshal.ReleaseComObject(policy);
        }
    }

    private static object? Get(object target, string name, params object[] index) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, index);
}
