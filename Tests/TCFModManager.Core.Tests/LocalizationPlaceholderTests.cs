using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// A translation keeps every {0}, {1}... its English value has, and adds none.
//
// Nothing else checks this. A translation that drops a placeholder loses the value it stood for -
// in a Help step, the name of the button the step is about - and one that invents {2} where English
// passes two values throws from string.Format the first time it is shown, in front of whoever
// picked that language.
//
// Plural forms are left out: a language's "_one" form legitimately spells the number out rather
// than showing {0}, and its forms are a different set from English's anyway.
//
public class LocalizationPlaceholderTests
{
    private static readonly Regex Placeholder = new(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    private static readonly Regex PluralForm = new(@"_(zero|one|two|few|many|other)$", RegexOptions.Compiled);

    private static Dictionary<string, string> Values(string resx) =>
        XDocument.Load(resx).Root!.Elements("data")
            .Where(d => d.Element("value") is not null)
            .ToDictionary(d => (string)d.Attribute("name")!, d => d.Element("value")!.Value, StringComparer.Ordinal);

    private static string Shape(string value) =>
        string.Join(",", Placeholder.Matches(value).Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order());

    public static TheoryData<string> Translations()
    {
        var data = new TheoryData<string>();
        var folder = Path.GetDirectoryName(AppSource.Resx)!;

        foreach (var file in Directory.EnumerateFiles(folder, "Strings.*.resx").Order())
        {
            // Generated from English by padding and accenting, so it can't disagree by itself.
            if (file.EndsWith(".qps-ploc.resx", StringComparison.OrdinalIgnoreCase)) continue;
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_translation_keeps_the_english_placeholders(string file)
    {
        var english = Values(AppSource.Resx);
        var translated = Values(Path.Combine(Path.GetDirectoryName(AppSource.Resx)!, file));

        var wrong = translated
            .Where(t => !PluralForm.IsMatch(t.Key) && english.ContainsKey(t.Key))
            .Where(t => Shape(t.Value) != Shape(english[t.Key]))
            .Select(t => $"{t.Key}: English has {{{Shape(english[t.Key])}}}, {file} has {{{Shape(t.Value)}}}")
            .Order()
            .ToList();

        Assert.True(
            wrong.Count == 0,
            "Placeholders that don't match English:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }

    //
    // A Help step with a Monitor mode form is handed the same labels whichever form is showing, so
    // the two have to agree on their placeholders - checked in English, and the test above then
    // holds every translation of both to that.
    //
    [Fact]
    public void Every_monitor_mode_form_keeps_its_base_placeholders()
    {
        const string suffix = "_Monitor";
        var english = Values(AppSource.Resx);

        var wrong = english
            .Where(e => e.Key.EndsWith(suffix, StringComparison.Ordinal))
            .Select(e => (Key: e.Key, Base: e.Key[..^suffix.Length], Value: e.Value))
            .Where(e => !english.ContainsKey(e.Base) || Shape(english[e.Base]) != Shape(e.Value))
            .Select(e => english.ContainsKey(e.Base)
                ? $"{e.Key}: {{{Shape(e.Value)}}}, but {e.Base} has {{{Shape(english[e.Base])}}}"
                : $"{e.Key}: no {e.Base} for it to be the Monitor mode form of")
            .Order()
            .ToList();

        Assert.True(
            wrong.Count == 0,
            "Monitor mode forms that don't match their base key:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
    }
}
