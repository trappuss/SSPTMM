using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// D7: every key the app asks for exists, and every key the resx holds is asked for.
//
// These read the App's SOURCE rather than its assembly. The App is a net9.0-windows WPF project and
// this test project is not, so referencing it would drag the whole UI stack in for the sake of
// scanning text - and the XAML half could not be checked that way regardless, since a {loc:Str}
// key is a string the compiler never sees.
//
public static class AppSource
{
    //
    // Walks up from the test binary to the folder holding the solution. Nothing else identifies the
    // repo root reliably: the binary sits several levels down under bin/, and its depth changes
    // with configuration and target framework.
    //
    public static string Root { get; } = FindRoot();

    public static string ProjectDir => Path.Combine(Root, "src", "TCFModManager.App");

    public static string Resx => Path.Combine(ProjectDir, "Localization", "Strings.resx");

    public static string PseudoResx =>
        Path.Combine(ProjectDir, "Localization", "Strings.qps-ploc.resx");

    public static IEnumerable<string> Files(params string[] extensions)
    {
        foreach (var path in Directory.EnumerateFiles(ProjectDir, "*", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            if (extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                yield return path;
        }
    }

    public static IReadOnlyList<string> Keys(string resx) =>
        XDocument.Load(resx).Root!.Elements("data")
            .Select(d => (string)d.Attribute("name")!)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (dir.GetFiles("TCFModManager.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Couldn't find TCFModManager.sln above " + AppContext.BaseDirectory);
    }
}

public class LocalizationKeyTests
{
    private static readonly string[] Categories =
        ["_zero", "_one", "_two", "_few", "_many", "_other"];

    //
    // Read by name through ResourceManager rather than as Strings.X, so an unused-key check would
    // otherwise report them as dead. Meta_ShippedLanguages is also the one key the pseudo-locale
    // must not touch - it decides which languages the dropdown offers.
    //
    private static readonly string[] ReadByName = ["Meta_LanguageName", "Meta_ShippedLanguages"];

    //
    // A plural family is referred to by its BASE - Strings.Installed_CountFound(n) - while the resx
    // holds one key per form. Both sides have to be collapsed to the base before they can be
    // compared, or every family reads as one missing key and several unused ones.
    //
    private static string Base(string key)
    {
        var suffix = Categories.FirstOrDefault(c => key.EndsWith(c, StringComparison.Ordinal));
        return suffix is null ? key : key[..^suffix.Length];
    }

    private static HashSet<string> Referable() =>
        AppSource.Keys(AppSource.Resx).Select(Base).ToHashSet(StringComparer.Ordinal);

    //
    // Both halves of how a key is named: C# reads Strings.X (a property, or a family's method), and
    // XAML reads {loc:Str X}.
    //
    // The XAML pattern deliberately does not care which attribute it sits in. An earlier per-file
    // pass checked Text, Content, ToolTip and PlaceholderText and missed six strings sitting in
    // WPF-UI InfoBar Title and Message - so this matches the markup extension wherever it appears.
    //
    private static HashSet<string> Used()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in AppSource.Files(".cs", ".xaml"))
        {
            if (Path.GetFileName(file) == "Strings.Designer.cs") continue;

            var text = File.ReadAllText(file);

            // Capitalised, because every key is Page_Thing and the prose around this code says
            // "Strings.resx" often enough to matter.
            foreach (Match m in Regex.Matches(text, @"\bStrings\.([A-Z]\w*)"))
                used.Add(m.Groups[1].Value);

            foreach (Match m in Regex.Matches(text, @"loc:Str\s+(\w+)"))
                used.Add(m.Groups[1].Value);

            // A binding to LocalizationService's indexer - {Binding [Key], Source=...} or
            // Path="[Key]" - which a style's Setter uses so the text follows a language switch.
            // Keys only: every key has an underscore, which keeps other indexers out.
            foreach (Match m in Regex.Matches(text, @"(?:Path=""|Binding\s+)\[([A-Z]\w*_\w*)\]"))
                used.Add(m.Groups[1].Value);
        }

        // Strings.ResourceManager is the accessor's own member, not a key.
        used.Remove("ResourceManager");
        return used;
    }

    [Fact]
    public void Every_key_the_app_asks_for_exists()
    {
        var missing = Used().Except(Referable()).Order().ToList();

        Assert.True(
            missing.Count == 0,
            "Asked for but not in Strings.resx:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Every_key_in_the_resx_is_asked_for()
    {
        var unused = Referable().Except(Used()).Except(ReadByName).Order().ToList();

        Assert.True(
            unused.Count == 0,
            "In Strings.resx but nothing asks for it:" + Environment.NewLine + string.Join(Environment.NewLine, unused));
    }

    //
    // A key name has to be unique whether or not it ends in a category suffix: PreLaunch_Behind was
    // once both a headline and a family base, which the generated accessor turned into two members
    // of the same name. The compiler caught that one; this says so in the resx instead.
    //
    [Fact]
    public void No_family_base_collides_with_a_plain_key()
    {
        var keys = AppSource.Keys(AppSource.Resx);
        var families = keys.Where(k => Base(k) != k).Select(Base).ToHashSet(StringComparer.Ordinal);
        var plain = keys.Where(k => Base(k) == k).ToHashSet(StringComparer.Ordinal);

        var clash = families.Intersect(plain).Order().ToList();

        Assert.True(
            clash.Count == 0,
            "A plural family and a plain key share a name:" + Environment.NewLine + string.Join(Environment.NewLine, clash));
    }

    //
    // Every family needs the form its own language falls back to. Without _other a count that lands
    // outside the forms present renders the base key, which is a visible wrong rather than a silent
    // one - but visible in front of a user, not a test.
    //
    [Fact]
    public void Every_plural_family_has_an_other_form()
    {
        var keys = AppSource.Keys(AppSource.Resx).ToHashSet(StringComparer.Ordinal);

        var missing = keys
            .Where(k => Base(k) != k)
            .Select(Base)
            .Distinct()
            .Where(b => !keys.Contains(b + "_other"))
            .Order()
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Plural family with no _other form:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    //
    // Every tag the dropdown offers needs a plural rule written for it. A shipped language without
    // one reads English's categories and quietly picks the wrong forms - the failure R3 accepted
    // when it said a rule is written per language shipped.
    //
    [Fact]
    public void Every_shipped_language_has_a_plural_rule()
    {
        var withoutRule = Shipped()
            .Where(tag => !HasOwnRule(tag))
            .Order()
            .ToList();

        Assert.True(
            withoutRule.Count == 0,
            "Shipped with no plural rule in PluralRules.For:" + Environment.NewLine + string.Join(Environment.NewLine, withoutRule));
    }

    //
    // The other half of the same pairing, and the one thing about a translation's state worth
    // failing over. How complete a language is only ever gets reported - a release is never held up
    // by a translation, and a language that has fallen behind renders the English value. A tag with
    // no file at all is different: it is our mistake rather than a translator's, and it puts an
    // entry in the Options dropdown that changes nothing when chosen.
    //
    [Fact]
    public void Every_shipped_language_has_a_resource_file()
    {
        var missing = Shipped()
            .Where(tag => !File.Exists(ResxFor(tag)))
            .Order()
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Named in Meta_ShippedLanguages with no Strings.<tag>.resx beside it (the pseudo-locale "
            + "is generated, so build TCFModManager.App if that is the one listed):"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    // English is the neutral file rather than a language of its own - it is what the others are
    // translations OF, and the end of the fallback chain.
    private static string ResxFor(string tag) =>
        tag == "en"
            ? AppSource.Resx
            : Path.Combine(AppSource.ProjectDir, "Localization", $"Strings.{tag}.resx");

    private static string[] Shipped() =>
        XDocument.Load(AppSource.Resx).Root!.Elements("data")
            .First(d => (string)d.Attribute("name")! == "Meta_ShippedLanguages")
            .Element("value")!.Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    //
    // There is no way to ask PluralRules whether a language is named in it, and adding one would be
    // API written for a test. Reading the source is the honest alternative: the rule is a case label.
    //
    private static bool HasOwnRule(string tag)
    {
        var language = CultureInfo.GetCultureInfo(tag).TwoLetterISOLanguageName;
        var source = File.ReadAllText(
            Path.Combine(AppSource.Root, "src", "TCFModManager.Core", "Services", "PluralRules.cs"));

        return Regex.IsMatch(source, $@"""{Regex.Escape(language)}""");
    }
}
