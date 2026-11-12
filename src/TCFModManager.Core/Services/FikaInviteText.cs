using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

/// <summary>What an invite says: the addresses to try, in its order, and the versions it names.</summary>
public sealed record FikaInviteInfo(IReadOnlyList<string> Addresses, string? Spt, string? Fika);

//
// Fork (1.4.7): the invite a Fika host copies from Play (PlayViewModel.FikaInvite), read back on a
// friend's PC - or any message with a server's address in it. Told by what cannot be translated:
// the addresses themselves, and "SPT" and "Fika" before a version.
//
public static partial class FikaInviteText
{
    // An IPv4 address, with or without a port; a name only with a port or "http(s)://" before it,
    // so a web address in passing is not taken for a server.
    [GeneratedRegex(@"(?<![\w.:/])(?<scheme>https?://)?(?<host>\d{1,3}(?:\.\d{1,3}){3}|[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9-]+)+)(?::(?<port>\d{2,5}))?(?![\w.])")]
    private static partial Regex Address();

    [GeneratedRegex(@"\bSPT\s+v?(?<version>\d+(?:\.\d+){1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex Spt();

    [GeneratedRegex(@"\bFika\s+v?(?<version>\d+(?:\.\d+){1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex Fika();

    public static FikaInviteInfo Parse(string? text)
    {
        text ??= string.Empty;
        var addresses = new List<string>();

        foreach (Match match in Address().Matches(text))
        {
            // Four numbers are an address; fewer are a version ("4.1.6"), and a name needs more to go on.
            var host = match.Groups["host"].Value;
            var isNumbers = host.All(c => char.IsDigit(c) || c == '.');
            if (isNumbers ? host.Count(c => c == '.') != 3 : !match.Groups["port"].Success && !match.Groups["scheme"].Success) continue;

            if (SptLauncherApi.TryNormalizeAddress(match.Value, out var address)
                && !SptLauncherApi.IsLocalAddress(address)
                && !addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                addresses.Add(address);
            }
        }

        return new FikaInviteInfo(addresses, Version(Spt().Match(text)), Version(Fika().Match(text)));
    }

    private static string? Version(Match match) => match.Success ? match.Groups["version"].Value : null;
}
