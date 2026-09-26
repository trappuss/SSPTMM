using System.Text;
using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The merge in isolation: three texts in, one text out. Everything here is about what the OUTPUT text
// looks like, because the whole reason this splices rather than re-serialises is that a config's
// comments and layout are the mod author's documentation.
//
public class JsonConfigMergeTests
{
    private static JsonConfigMergeResult Merge(string baseline, string user, string newDefaults) =>
        JsonConfigMerge.Merge(Bytes(baseline), Bytes(user), Bytes(newDefaults));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(JsonConfigMergeResult result) => Encoding.UTF8.GetString(result.Content!);

    [Fact]
    public void AChangedValueIsCarriedAndAChangedDefaultArrives()
    {
        var result = Merge(
            """{ "mine": 1, "theirs": 1 }""",
            """{ "mine": 5, "theirs": 1 }""",
            """{ "mine": 2, "theirs": 9 }""");

        Assert.Equal(["mine"], result.Carried);
        Assert.Equal("""{ "mine": 5, "theirs": 9 }""", Text(result));
    }

    [Fact]
    public void CommentsAndLayoutAreTheNewVersionsUntouched()
    {
        const string baseline = "{\n  // what it does\n  \"count\": 1\n}";
        const string user = "{\n  // what it does\n  \"count\": 4\n}";
        const string updated = "{\n\n  /* rewritten docs */\n  \"count\": 2,\n  \"added\": false\n}";

        var result = Merge(baseline, user, updated);

        Assert.Equal("{\n\n  /* rewritten docs */\n  \"count\": 4,\n  \"added\": false\n}", Text(result));
    }

    [Fact]
    public void ANestedValueIsAddressedByItsPath()
    {
        var result = Merge(
            """{ "a": { "b": { "c": 1 } }, "d": 1 }""",
            """{ "a": { "b": { "c": 3 } }, "d": 1 }""",
            """{ "a": { "b": { "c": 1 } }, "d": 1 }""");

        Assert.Equal(["a.b.c"], result.Carried);
        Assert.Contains("\"c\": 3", Text(result));
    }

    //
    // An array is a leaf: there is no correct element-wise merge, so a changed array is carried whole.
    //
    [Fact]
    public void AChangedArrayIsCarriedWhole()
    {
        var result = Merge(
            """{ "items": [1, 2] }""",
            """{ "items": [1, 2, 3] }""",
            """{ "items": [1, 2] }""");

        Assert.Equal(["items"], result.Carried);
        Assert.Equal("""{ "items": [1, 2, 3] }""", Text(result));
    }

    // Nothing inside an array is addressed on its own, so an object in one can't be half-merged.
    [Fact]
    public void AnObjectInsideAnArrayIsNotAddressedSeparately()
    {
        var result = Merge(
            """{ "rules": [ { "id": 1 } ] }""",
            """{ "rules": [ { "id": 2 } ] }""",
            """{ "rules": [ { "id": 1 } ] }""");

        Assert.Equal(["rules"], result.Carried);
    }

    [Fact]
    public void AKeyTheNewVersionDroppedIsReportedNotReinserted()
    {
        var result = Merge(
            """{ "gone": 1 }""",
            """{ "gone": 2 }""",
            """{ "here": 1 }""");

        Assert.Equal(["gone"], result.Dropped);
        Assert.Equal("""{ "here": 1 }""", Text(result));
    }

    //
    // A key the user added themselves is reported and left out: the object it belonged to may have
    // moved or gone, and inventing a place for it in someone else's file is a guess.
    //
    [Fact]
    public void AKeyTheUserAddedIsReportedNotApplied()
    {
        var result = Merge(
            """{ "a": 1 }""",
            """{ "a": 1, "mine": true }""",
            """{ "a": 1 }""");

        Assert.Equal(["mine"], result.UserAdded);
        Assert.Empty(result.Carried);
        Assert.Equal("""{ "a": 1 }""", Text(result));
    }

    [Fact]
    public void AnUnchangedFileCarriesNothing()
    {
        var result = Merge("""{ "a": 1 }""", """{ "a": 1 }""", """{ "a": 2 }""");

        Assert.Empty(result.Carried);
        Assert.Equal("""{ "a": 2 }""", Text(result));
    }

    [Fact]
    public void TrailingCommasAndCommentsAreAccepted()
    {
        var result = Merge(
            "{ \"a\": 1, /* note */ }",
            "{ \"a\": 2, /* note */ }",
            "{ \"a\": 1, /* note */ }");

        Assert.Equal(["a"], result.Carried);
        Assert.Equal("{ \"a\": 2, /* note */ }", Text(result));
    }

    // BepInEx writes no byte order mark, but plenty of server mods ship one and adding or dropping one
    // would be an unrequested change to the file.
    [Fact]
    public void AByteOrderMarkOnTheNewFileSurvives()
    {
        var newDefaults = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Bytes("""{ "a": 1 }""")).ToArray();

        var result = JsonConfigMerge.Merge(Bytes("""{ "a": 1 }"""), Bytes("""{ "a": 7 }"""), newDefaults);

        Assert.Equal(newDefaults[..3], result.Content![..3]);
        Assert.Equal("""{ "a": 7 }""", Encoding.UTF8.GetString(result.Content[3..]));
    }

    [Fact]
    public void JsonTheReaderCannotParseStops()
    {
        // Unquoted keys: JSON5, which SPT mods do use.
        var result = Merge("{ a: 1 }", "{ a: 2 }", "{ a: 1 }");

        Assert.Equal(JsonConfigMergeStop.NotJson, result.Stop);
        Assert.Null(result.Content);
    }

    // Nothing addressable means nothing to merge into, rather than a merge that quietly carries nothing.
    [Fact]
    public void ADocumentWhoseRootIsAnArrayStops()
    {
        var result = Merge("[1, 2]", "[1, 3]", "[1, 2]");

        Assert.Equal(JsonConfigMergeStop.NotJson, result.Stop);
    }

    [Fact]
    public void PastTheValueBoundItStops()
    {
        var many = "{" + string.Join(",", Enumerable.Range(0, JsonConfigMerge.MaxValues + 1).Select(i => $"\"k{i}\": {i}")) + "}";

        Assert.Equal(JsonConfigMergeStop.TooLarge, Merge(many, many, many).Stop);
    }

    [Fact]
    public void PastTheSizeBoundItStops()
    {
        var big = "{ \"a\": \"" + new string('x', JsonConfigMerge.MaxBytes) + "\" }";

        Assert.Equal(JsonConfigMergeStop.TooLarge, Merge(big, big, big).Stop);
    }

    // Duplicate keys: the last one wins, which is how the file's own reader reads it.
    [Fact]
    public void ADuplicateKeyIsMergedAtItsLastOccurrence()
    {
        var result = Merge(
            """{ "a": 1, "a": 2 }""",
            """{ "a": 1, "a": 5 }""",
            """{ "a": 1, "a": 2 }""");

        Assert.Equal("""{ "a": 1, "a": 5 }""", Text(result));
    }

    [Theory]
    [InlineData("5", "7", "[5, 10]")]
    [InlineData("5", "7", "\"five\"")]
    [InlineData("true", "false", "1")]
    public void AValueWhoseKindTheNewVersionChanged_IsNotCarried(string shipped, string mine, string updated)
    {
        var result = Merge($"{{ \"x\": {shipped} }}", $"{{ \"x\": {mine} }}", $"{{ \"x\": {updated} }}");

        Assert.Equal($"{{ \"x\": {updated} }}", Text(result));
        Assert.Equal(["x"], result.Dropped);
        Assert.Empty(result.Carried);
    }

    [Theory]
    [InlineData("null", "\"mine\"", "null")]
    [InlineData("\"a\"", "null", "\"b\"")]
    [InlineData("true", "false", "true")]
    public void AnUnsetValue_OrTheOtherBoolean_IsStillCarried(string shipped, string mine, string updated)
    {
        var result = Merge($"{{ \"x\": {shipped} }}", $"{{ \"x\": {mine} }}", $"{{ \"x\": {updated} }}");

        Assert.Equal($"{{ \"x\": {mine} }}", Text(result));
        Assert.Equal(["x"], result.Carried);
    }
}
