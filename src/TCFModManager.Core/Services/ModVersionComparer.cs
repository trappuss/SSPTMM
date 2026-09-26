namespace TCFModManager.Core.Services;

// 
// Compares two loosely-formatted version strings (an installed mod's version vs. the latest one
// published on sp-mod.com) to determine whether an update is available. Not a full SemVer
// implementation: variable segment count, tolerant of a leading "v"/"V", build metadata ("+...")
// ignored. A pre-release sorts below its release, as SemVer has it - 1.2.0-beta then 1.2.0 is an
// update - and two pre-releases of one version compare by their dot-separated parts (numbers as
// numbers: beta.2 before beta.10).
// 
public static class ModVersionComparer
{
    // True if <paramref name="latest"/> is a newer version than <paramref name="installed"/>.
    // Null when either is missing or unparsable.
    public static bool? IsUpdateAvailable(string? installed, string? latest) =>
        Compare(latest, installed) is { } order ? order > 0 : null;

    /// <summary>Negative when <paramref name="a"/> is older than <paramref name="b"/>, zero when they
    /// are the same version, positive when newer; null when either cannot be read.</summary>
    public static int? Compare(string? a, string? b)
    {
        var left = Parse(a);
        var right = Parse(b);
        if (left is null || right is null) return null;

        var core = left.Value.Core.CompareTo(right.Value.Core);
        if (core != 0) return Math.Sign(core);

        return ComparePreRelease(left.Value.Pre, right.Value.Pre);
    }

    /// <summary>True when <paramref name="a"/> is a later breaking line than <paramref name="b"/>:
    /// a higher major version, or for 0.x a higher minor one (npm's reading of ^0.x). Null when
    /// either cannot be read.</summary>
    public static bool? IsLaterMajor(string? a, string? b)
    {
        var left = Parse(a);
        var right = Parse(b);
        if (left is null || right is null) return null;

        var (l, r) = (left.Value.Core, right.Value.Core);
        if (l.Major != r.Major) return l.Major > r.Major;
        return l.Major == 0 && l.Minor > r.Minor;
    }

    // No pre-release outranks any pre-release; otherwise part by part, numbers below words.
    private static int ComparePreRelease(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0) return a.Length == 0 ? (b.Length == 0 ? 0 : 1) : -1;

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumber = int.TryParse(a[i], out var an);
            var bNumber = int.TryParse(b[i], out var bn);

            var order = (aNumber, bNumber) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase),
            };
            if (order != 0) return Math.Sign(order);
        }

        return a.Length.CompareTo(b.Length);
    }

    private static (Version Core, string[] Pre)? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var halves = text.Split('-', 2);
        var parts = halves[0].Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var major)) return null;

        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;

        var pre = halves.Length > 1 && halves[1].Length > 0
            ? halves[1].Split('.', StringSplitOptions.RemoveEmptyEntries)
            : [];

        return (new Version(major, Part(1), Part(2), Part(3)), pre);
    }
}
