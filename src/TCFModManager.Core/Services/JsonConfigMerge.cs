using System.Text.Json;

namespace TCFModManager.Core.Services;

//
// A three-way merge of one config file: the copy the old version shipped (baseline), the copy the
// user has now, and the copy the new version ships.
//
// The output is the NEW file's text with the user's changed values spliced into it, never a
// re-serialised object graph. SPT server configs are full of comments explaining each setting and a
// parse-and-write round trip destroys them - the same constraint that made the Configs page give JSON
// a raw text editor rather than a generated form. Only the byte spans of changed values are replaced,
// so comments, key order, spacing and line endings are all the new version's, untouched.
//
// An array is a leaf. There is no correct element-wise merge of two arrays, so if the user changed
// one, their whole array wins.
//
public static class JsonConfigMerge
{
    // Past either of these a file is data rather than settings, whatever folder it sits in. Measured
    // against the live install: the largest real config there is ~27 KB and ~820 values, and the file
    // these exist to skip is a 121 KB price cache with 3,135.
    public const int MaxBytes = 64 * 1024;

    public const int MaxValues = 2000;

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 64,
    };

    public static JsonConfigMergeResult Merge(byte[] baseline, byte[] user, byte[] newDefaults)
    {
        if (baseline.Length > MaxBytes || user.Length > MaxBytes || newDefaults.Length > MaxBytes)
            return JsonConfigMergeResult.Stopped(JsonConfigMergeStop.TooLarge);

        var bom = StartsWithBom(newDefaults);

        if (ReadValues(Body(baseline)) is not { } baseValues
            || ReadValues(Body(user)) is not { } userValues
            || ReadValues(Body(newDefaults)) is not { } newValues)
        {
            return JsonConfigMergeResult.Stopped(JsonConfigMergeStop.NotJson);
        }

        if (baseValues.Count > MaxValues || userValues.Count > MaxValues || newValues.Count > MaxValues)
            return JsonConfigMergeResult.Stopped(JsonConfigMergeStop.TooLarge);

        // A document whose root is an array, or which holds nothing addressable, has no paths to
        // carry anything into. Left to the per-mod policy rather than reported as a merge that
        // carried nothing.
        if (newValues.Count == 0) return JsonConfigMergeResult.Stopped(JsonConfigMergeStop.NotJson);

        var carried = new List<string>();
        var dropped = new List<string>();
        var added = new List<string>();
        var edits = new List<JsonValueSpan>();

        foreach (var (path, value) in userValues)
        {
            var knownBefore = baseValues.TryGetValue(path, out var shipped);

            //
            // The user never touched this one, so the new version's value stands - which is the whole
            // point of having a baseline: a default the mod changed reaches them.
            //
            // Compared as bytes rather than with ==: Text is a byte[], and == on one of those asks
            // whether it is the same array, which it never is.
            //
            if (SameText(shipped, value)) continue;

            if (!newValues.TryGetValue(path, out var replacing))
            {
                // Changed by the user and gone from the new version, or never shipped by either. The
                // second is a key the user added themselves: there is no honest place to put it back,
                // since the object it belonged to may have moved or gone, so it is reported instead.
                (knownBefore ? dropped : added).Add(path);
                continue;
            }

            //
            // The new version changed what kind of value this is - a number that is now a list, a
            // switch that is now a word. The user's old value is not one the mod reads any more, so
            // carrying it would break the file; it is reported as not carried instead. A null on
            // either side is not a change of kind: an unset optional value, or one being unset.
            //
            if (value.Kind != replacing.Kind && value.Kind != JsonValueKind.Null && replacing.Kind != JsonValueKind.Null)
            {
                dropped.Add(path);
                continue;
            }

            edits.Add(replacing with { Text = value.Text });
            carried.Add(path);
        }

        var merged = Splice(newDefaults, edits, bom ? Bom.Length : 0);

        return new JsonConfigMergeResult(merged, carried, dropped, added, null);
    }

    // left is what TryGetValue handed back, so it is null exactly when the baseline never had the
    // path - which is not the same text as anything.
    private static bool SameText(JsonValueSpan? left, JsonValueSpan right) =>
        left is not null && left.Text.AsSpan().SequenceEqual(right.Text);

    //
    // Every scalar and array value in the document, by dotted path, with the byte span of the value
    // in the text. Null when the bytes are not JSON this can read - a .json5 file, or a genuinely
    // malformed one.
    //
    // Arrays are skipped whole rather than walked, so an array is one value with one span.
    //
    private static Dictionary<string, JsonValueSpan>? ReadValues(ReadOnlySpan<byte> utf8)
    {
        var values = new Dictionary<string, JsonValueSpan>(StringComparer.Ordinal);

        //
        // One slot per open object, each holding the property name that object is currently on. The
        // slots joined together are the path of whatever value comes next, which is why the innermost
        // one is overwritten by each PropertyName rather than pushed.
        //
        var segments = new List<string>();

        try
        {
            var reader = new Utf8JsonReader(utf8, ReaderOptions);

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        segments.Add(string.Empty);
                        break;

                    case JsonTokenType.EndObject:
                        if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                        break;

                    case JsonTokenType.PropertyName:
                        if (segments.Count > 0) segments[^1] = reader.GetString() ?? string.Empty;
                        break;

                    // Skipped whole, so an array is one value with one span and nothing inside it is
                    // addressed on its own.
                    case JsonTokenType.StartArray:
                    {
                        var start = reader.TokenStartIndex;
                        reader.Skip();
                        Record(values, segments, utf8, start, reader.BytesConsumed, JsonValueKind.Array);
                        break;
                    }

                    case JsonTokenType.String:
                        Record(values, segments, utf8, reader.TokenStartIndex, reader.BytesConsumed, JsonValueKind.String);
                        break;

                    case JsonTokenType.Number:
                        Record(values, segments, utf8, reader.TokenStartIndex, reader.BytesConsumed, JsonValueKind.Number);
                        break;

                    // true and false are one kind: a switch.
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        Record(values, segments, utf8, reader.TokenStartIndex, reader.BytesConsumed, JsonValueKind.True);
                        break;

                    case JsonTokenType.Null:
                        Record(values, segments, utf8, reader.TokenStartIndex, reader.BytesConsumed, JsonValueKind.Null);
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return values;
    }

    // A value only counts when it has a path - a document whose root is a bare value or an array has
    // nothing a merge can address, and is left to the per-mod policy.
    private static void Record(
        Dictionary<string, JsonValueSpan> values,
        List<string> path,
        ReadOnlySpan<byte> utf8,
        long start,
        long end,
        JsonValueKind kind)
    {
        if (path.Count == 0) return;

        var dotted = string.Join('.', path);
        if (dotted.Length == 0) return;

        var length = (int)(end - start);
        var text = utf8.Slice((int)start, length).ToArray();

        // A duplicate key means the last one wins, which is how the file's own reader treats it.
        values[dotted] = new JsonValueSpan(dotted, (int)start, length, text, kind);
    }

    // Back-to-front, so an earlier edit never moves a later span.
    private static byte[] Splice(byte[] original, List<JsonValueSpan> edits, int offset)
    {
        if (edits.Count == 0) return original;

        var result = new List<byte>(original.Length + 64);
        result.AddRange(original);

        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            result.RemoveRange(edit.Start + offset, edit.Length);
            result.InsertRange(edit.Start + offset, edit.Text);
        }

        return [.. result];
    }

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static bool StartsWithBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == Bom[0] && bytes[1] == Bom[1] && bytes[2] == Bom[2];

    // Utf8JsonReader treats a byte order mark as invalid JSON, so it is read past and the offsets it
    // reports are shifted back on when splicing.
    private static ReadOnlySpan<byte> Body(byte[] bytes) =>
        StartsWithBom(bytes) ? bytes.AsSpan(3) : bytes;
}

// One value in a document: where it is, and the exact text of it.
// Kind is what sort of value it is (true stands for both booleans).
internal sealed record JsonValueSpan(string Path, int Start, int Length, byte[] Text, JsonValueKind Kind);

//
// The merged file, plus what the merge did and did not carry. Content is null when it stopped.
//
public sealed record JsonConfigMergeResult(
    byte[]? Content,
    List<string> Carried,
    List<string> Dropped,
    List<string> UserAdded,
    JsonConfigMergeStop? Stop)
{
    public static JsonConfigMergeResult Stopped(JsonConfigMergeStop stop) => new(null, [], [], [], stop);
}

public enum JsonConfigMergeStop
{
    // Not JSON this can read - JSON5, or malformed.
    NotJson,

    // Data rather than settings: past MaxBytes or MaxValues.
    TooLarge,
}
