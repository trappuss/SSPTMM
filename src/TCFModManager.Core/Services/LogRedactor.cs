using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM): takes out of log text what someone asking for help shouldn't have to share. SPT's
// logs hold full paths - so the Windows user name - and profile and account ids (seen in a real
// install's logs, 2026-10-03); a Fika host's logs hold players' addresses.
//
//   - the user's own folder becomes %USERPROFILE%, and the user name anywhere else becomes <user>;
//   - a 24-character id (SPT's MongoId: profiles, items, traders) becomes "id-" and six characters
//     of its SHA-256, the same each time, so lines about one id still read as one;
//   - an IPv4 address with a port becomes <ip>:<port>, except this machine's own (127.0.0.1, 0.0.0.0).
//     Without a port a dotted number is left alone - "1.8.0.0" is far more often a version.
//
public static partial class LogRedactor
{
    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9a-f]{24}(?![0-9A-Fa-f])")]
    private static partial Regex MongoId();

    [GeneratedRegex(@"(?<![\d.])(?<ip>\d{1,3}(?:\.\d{1,3}){3}):(?<port>\d{2,5})\b")]
    private static partial Regex Address();

    public static string Redact(string text, string? userProfile, string? userName)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (userProfile is { Length: > 3 })
        {
            foreach (var spelling in new[] { userProfile, userProfile.Replace('\\', '/') }.Distinct())
                text = text.Replace(spelling, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        if (userName is { Length: >= 3 })
            text = Regex.Replace(text, $@"(?<![\w]){Regex.Escape(userName)}(?![\w])", "<user>", RegexOptions.IgnoreCase);

        text = MongoId().Replace(text, m => "id-" + Short(m.Value));

        text = Address().Replace(text, m =>
            m.Groups["ip"].Value is "127.0.0.1" or "0.0.0.0" ? m.Value : "<ip>:" + m.Groups["port"].Value);

        return text;
    }

    private static string Short(string id) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(id.ToLowerInvariant())))[..6];
}
