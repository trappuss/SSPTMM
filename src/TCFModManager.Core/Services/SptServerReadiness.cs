using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

//
// When a server that was just started can take a launcher: once something on this PC is listening
// on the port the server is set to use. SPT.Server takes a while to load its database and mods
// before it opens its port, and a launcher started before that has nothing to connect to.
//
// The port comes from the server's own http.json beside its exe (SPT_Data/configs in 4.x,
// SPT_Data/Server/configs before), and SPT's default 6969 is always watched too - Fika can move the
// binding at runtime without touching http.json (see ServerMapEndpoint).
//
public static class SptServerReadiness
{
    public const int DefaultPort = 6969;

    private static readonly string[] HttpConfigPaths =
    [
        Path.Combine("SPT_Data", "configs", "http.json"),
        Path.Combine("SPT_Data", "Server", "configs", "http.json"),
    ];

    /// <summary>The ports worth watching for a server whose exe is at <paramref name="serverExePath"/>.</summary>
    public static IReadOnlySet<int> PortsFor(string? serverExePath)
    {
        var ports = new HashSet<int> { DefaultPort };

        if (Path.GetDirectoryName(serverExePath) is not { Length: > 0 } folder) return ports;

        foreach (var relative in HttpConfigPaths)
        {
            var path = Path.Combine(folder, relative);
            if (!File.Exists(path)) continue;

            try
            {
                if (ReadPort(File.ReadAllText(path)) is { } port) ports.Add(port);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Debug("Launch", $"couldn't read {path}: {ex.Message}");
            }
        }

        return ports;
    }

    /// <summary>The "port" in an http.json, which may carry comments; null when there is none.</summary>
    public static int? ReadPort(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            return doc.RootElement.TryGetProperty("port", out var port) && port.TryGetInt32(out var value)
                && value is > 0 and <= 65535
                ? value
                : null;
        }
        catch (JsonException)
        {
            // Hand-edited into something that is not quite JSON: a plain "port": 1234 still reads.
            var match = Regex.Match(json, "\"port\"\\s*:\\s*(\\d{1,5})");
            return match.Success && int.TryParse(match.Groups[1].Value, out var value) && value is > 0 and <= 65535
                ? value
                : null;
        }
    }

    /// <summary>True once anything on this PC listens on one of these ports.</summary>
    public static bool IsListening(IReadOnlySet<int> ports)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => ports.Contains(e.Port));
        }
        catch (NetworkInformationException ex)
        {
            AppLog.Debug("Launch", $"couldn't list listening ports: {ex.Message}");
            return false;
        }
    }

    /// <summary>Waits until one of the ports is listened on, or the time runs out.</summary>
    public static async Task<bool> WaitUntilListeningAsync(
        IReadOnlySet<int> ports, TimeSpan timeout, Func<bool> stillWanted, CancellationToken ct = default)
    {
        var until = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < until && stillWanted())
        {
            if (IsListening(ports)) return true;
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }

        return false;
    }
}
