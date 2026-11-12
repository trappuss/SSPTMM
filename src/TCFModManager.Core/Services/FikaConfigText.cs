using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

//
// Fork (1.4.7): single values changed in Fika's fika.jsonc as text, so the comments and the rest
// of the file stay as they are. The names are Fika's own (FikaConfigClient, read from its source):
// the file is that model written out, so each is there once, as "name": true or false.
//
public static class FikaConfigText
{
    /// <summary>The on/off settings under "client" a host changes most.</summary>
    public static readonly IReadOnlyList<string> ClientSwitches =
        ["friendlyFire", "sharedQuestProgression", "useInertia", "allowFreeCam", "anyoneCanStartRaid"];

    private static Regex Switch(string name) => new("(\"" + Regex.Escape(name) + "\"\\s*:\\s*)(true|false)\\b");

    public static bool? ReadSwitch(string text, string name) =>
        Switch(name).Match(text) is { Success: true } match ? match.Groups[2].Value == "true" : null;

    /// <summary>The text with the setting changed; null when it is not in it.</summary>
    public static string? SetSwitch(string text, string name, bool value)
    {
        var regex = Switch(name);
        return regex.IsMatch(text) ? regex.Replace(text, "${1}" + (value ? "true" : "false"), 1) : null;
    }

    //
    // The address Fika listens on: "ip" under server > SPT > http - found by walking those names in
    // order, since other parts of the file have an "ip" of their own.
    //
    public static string? SetListenAddress(string text, string ip)
    {
        var at = 0;
        foreach (var name in new[] { "server", "SPT", "http" })
        {
            var found = Regex.Match(text[at..], "\"" + name + "\"\\s*:\\s*\\{");
            if (!found.Success) return null;
            at += found.Index + found.Length;
        }

        var value = Regex.Match(text[at..], "(\"ip\"\\s*:\\s*\")([^\"]*)(\")");
        if (!value.Success) return null;

        // Still inside "http": no closing brace before it.
        if (text.AsSpan(at, value.Index).Contains('}')) return null;

        var start = at + value.Groups[2].Index;
        return string.Concat(text.AsSpan(0, start), ip, text.AsSpan(start + value.Groups[2].Length));
    }
}
