using System.Text.RegularExpressions;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// D13: nothing the user reads is written where it sits. A key that exists is checked by
// LocalizationKeyTests; this is the other half - a sentence that was never keyed at all, so no key
// is missing and nothing looks wrong until the app is read in another language.
//
// The pseudo-locale finds these too, but only for a screen somebody opened, only while a pseudo
// build is at hand, and only if the reader notices one word in twenty that did not get mangled.
// That is how the crash dialog survived all of S4: it is shown on a path nobody takes on purpose.
//
// Two scans, because the two halves of the app hide prose differently. Both were run against the
// tree before this was written, and each found a real miss - eleven page titles and a stray
// Text="x" in XAML, the crash dialog in C#.
//
public class LocalizationLiteralTests
{
    //
    // Literals that read like prose but are not: a value the app matches on, a format another
    // system parses, a fragment whose prose half is keyed elsewhere.
    //
    // Deliberately a list of exact strings rather than a pattern. A pattern here would grow to
    // cover whatever the next literal looks like, and the point of this guard is that adding to it
    // is a decision somebody writes a reason for.
    //
    private static readonly (string File, string Literal, string Why)[] NotProse =
    [
        ("AppTheme.cs",
            "rgba({solid.Color.R}, {solid.Color.G}, {solid.Color.B}, ",
            "CSS-shaped colour for the log, not for a reader"),
        ("AppTheme.cs",
            "{solid.Color.A / 255d * solid.Opacity:0.##})",
            "the tail of the same log value"),
        ("ModListFileDialog.cs",
            "{Strings.ModListFile_TypeName} ({ModListFile.FilePattern})|{ModListFile.FilePattern}",
            "Win32 filter syntax; both names in it are keyed"),
        ("ModListFileDialog.cs",
            "|{Strings.ModListFile_AllFiles} ({ModListFile.AllFilesPattern})|{ModListFile.AllFilesPattern}",
            "Win32 filter syntax; both names in it are keyed"),
        ("WebViews.cs",
            "<!doctype html><title></title>",
            "an empty web page, for a player address naming no video"),
        ("WebViews.cs",
            "Content-Type: text/html; charset=utf-8",
            "an HTTP header"),
        ("WebViews.cs",
            "autoplay; encrypted-media; picture-in-picture; fullscreen",
            "an iframe's permission list, inside the video player page"),
    ];

    //
    // The attributes a reader's eye lands on. Not every attribute: x:Name, Tag, a converter
    // parameter and a Setter's Property all hold English that no user ever sees, and flagging them
    // would make the allow-list longer than the guard.
    //
    // Title is here because of what it caught: every page carried Title="Browse" and the eleven of
    // them had been read past a dozen times, since a page title looks like structure rather than
    // like a string.
    //
    private static readonly Regex XamlAttribute = new(
        @"\b(Text|Content|ToolTip|PlaceholderText|Title|Message|Header|OnContent|OffContent|Description)\s*=\s*""([^""]*)""",
        RegexOptions.Compiled);

    //
    // A string literal, verbatim ones excluded. Those hold paths, JSON and regexes in this app and
    // never prose, and their escaping would have to be undone before the value could be judged.
    //
    private static readonly Regex CSharpLiteral = new(
        @"(?<![@\w""])\$?""((?:[^""\\\n]|\\.)*)""",
        RegexOptions.Compiled);

    // Log text is written for whoever reads the log file, which is us - it stays English on purpose.
    private static readonly Regex LogCall = new(@"\bAppLog\.\w+\s*\(", RegexOptions.Compiled);

    [Fact]
    public void No_xaml_attribute_holds_english()
    {
        var found = new List<string>();

        foreach (var file in AppSource.Files(".xaml"))
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in XamlAttribute.Matches(lines[i]))
                {
                    var value = m.Groups[2].Value;

                    // A binding or a markup extension: the value is a reference, not the text.
                    if (value.StartsWith('{')) continue;

                    // Glyph codes, numbers, a single symbol - nothing to translate.
                    if (!value.Any(char.IsLetter)) continue;

                    if (Allowed(file, value)) continue;

                    found.Add($"{Path.GetFileName(file)}:{i + 1}  {m.Groups[1].Value}=\"{value}\"");
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "English sitting in XAML instead of behind a key - use {loc:Str Key}:"
            + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    //
    // The C# half, which needs a sense of what a sentence looks like: a space and two letters. That
    // lets through "en-GB", "%APPDATA%", "SPT.Server.exe" and every other identifier-shaped literal
    // the app is full of, and it lets through a one-word label too - which is the known hole in
    // this scan, and the reason the pseudo-locale still exists.
    //
    [Fact]
    public void No_csharp_literal_holds_a_sentence()
    {
        var found = new List<string>();

        foreach (var file in AppSource.Files(".cs"))
        {
            if (Path.GetFileName(file) == "Strings.Designer.cs") continue;

            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('*')) continue;
                if (InLogCall(lines, i)) continue;

                foreach (Match m in CSharpLiteral.Matches(lines[i]))
                {
                    var value = m.Groups[1].Value;

                    if (!value.Contains(' ')) continue;
                    if (value.Count(char.IsLetter) < 2) continue;
                    if (Allowed(file, value)) continue;

                    found.Add($"{Path.GetFileName(file)}:{i + 1}  \"{value}\"");
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "English sitting in C# instead of behind a key - add it to Strings.resx:"
            + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    //
    // A log call's text is often several concatenated literals over several lines, and only the
    // first line carries the AppLog. part. Walking back to the start of the statement is what makes
    // the exclusion hold for the rest of them - it has to match AppLog as a CALL rather than as a
    // word, or "AppLog.CurrentFile" sitting in an argument excuses a line it should not.
    //
    private static bool InLogCall(string[] lines, int index)
    {
        var start = index;

        while (start > 0)
        {
            var previous = lines[start - 1].TrimEnd();

            if (previous.Length == 0) break;
            if (previous.EndsWith(';') || previous.EndsWith('{') || previous.EndsWith('}')) break;

            start--;
        }

        for (var i = start; i <= index; i++)
        {
            if (LogCall.IsMatch(lines[i])) return true;
        }

        return false;
    }

    private static bool Allowed(string file, string literal) =>
        NotProse.Any(e => Path.GetFileName(file) == e.File && literal == e.Literal);

    //
    // The allow-list is the part of this that rots: an entry outliving the literal it excuses is an
    // exception nobody would grant today, sitting open for the next literal that happens to match.
    //
    [Fact]
    public void Every_allowed_literal_is_still_there()
    {
        var sources = AppSource.Files(".cs", ".xaml")
            .GroupBy(p => Path.GetFileName(p)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => string.Concat(g.Select(File.ReadAllText)), StringComparer.Ordinal);

        var stale = NotProse
            .Where(e => !sources.TryGetValue(e.File, out var text) || !text.Contains(e.Literal, StringComparison.Ordinal))
            .Select(e => $"{e.File}: \"{e.Literal}\"")
            .ToList();

        Assert.True(
            stale.Count == 0,
            "Allowed as not-prose but no longer in the source - drop the entry:"
            + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }
}
