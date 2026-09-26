namespace TCFModManager.Core.Services;

// 
// Compares two loosely-formatted version strings (an installed mod's version vs. the latest one
// published on sp-mod.com) to determine whether an update is available. Not a full SemVer
// implementation: variable segment count, tolerant of a leading "v"/"V", build metadata ("+...")
// ignored. A pre-release sorts below its release, as SemVer has it - 1.2.0-beta then 1.2.0 is an
// update - and two pre-releases of one version compare by their dot-separated parts (numbers as
// numbers: beta.2 before beta.10).
//
// Except the labels mod authors use for a fix AFTER a release - 1.2.0-hotfix, 1.2.0-fix2,
// 1.2.0-patch1: SemVer would put those below 1.2.0, but every author using them means the opposite,
// so they sort above it (and above each other by the same part-by-part rule).
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

    /// <summary>True when an installed version and a published one are the same release. Also when
    /// the installed one was read from a DLL - no pre-release label (a DLL cannot carry one) - and
    /// the release numbers match: 1.2.0.0 on disk is 1.2.0-beta published.</summary>
    public static bool IsSameRelease(string? installed, string? published)
    {
        var left = Parse(installed);
        var right = Parse(published);
        if (left is null || right is null) return false;

        if (left.Value.Core != right.Value.Core) return false;
        return left.Value.Pre.Length == 0 || ComparePreRelease(left.Value.Pre, right.Value.Pre) == 0;
    }

    /// <summary>True when both are the same major.minor.patch, whatever else either carries (a
    /// fourth, build number part; a label): "2.0.0.42986" and "2.0.0".</summary>
    public static bool SameNumbers(string? a, string? b)
    {
        var left = Parse(a);
        var right = Parse(b);
        if (left is null || right is null) return false;

        var (l, r) = (left.Value.Core, right.Value.Core);
        return l.Major == r.Major && l.Minor == r.Minor && l.Build == r.Build;
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

    // A fix after the release, by the words authors use for one - see the class comment.
    private static readonly string[] PostReleaseWords = ["hotfix", "fix", "patch", "hf", "post"];

    private static bool IsPostRelease(string[] pre) =>
        pre.Length > 0 && PostReleaseWords.Any(w =>
            pre[0].StartsWith(w, StringComparison.OrdinalIgnoreCase)
            && pre[0].Length >= w.Length
            && pre[0][w.Length..].All(c => char.IsDigit(c) || c == '-' || c == '_'));

    // Rank against the plain release: below it (-1), the release itself (0), a fix after it (1).
    private static int Rank(string[] pre) => pre.Length == 0 ? 0 : IsPostRelease(pre) ? 1 : -1;

    // No pre-release outranks any pre-release (a post-release fix outranks both); otherwise part by
    // part, numbers below words.
    private static int ComparePreRelease(string[] a, string[] b)
    {
        var rank = Rank(a).CompareTo(Rank(b));
        if (rank != 0) return rank;
        if (a.Length == 0) return 0;

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumber = int.TryParse(a[i], out var an);
            var bNumber = int.TryParse(b[i], out var bn);

            var order = (aNumber, bNumber) switch
            {
                (true, true) => an.CompareTo(bn),
                (true, false) => -1,
                (false, true) => 1,
                _ => CompareWords(a[i], b[i]),
            };
            if (order != 0) return Math.Sign(order);
        }

        return a.Length.CompareTo(b.Length);
    }

    // A word with a number glued on - "hotfix9", "beta10", "rc2" - compares by the word, then the
    // number as a number: hotfix9 before hotfix10. Anything else as text.
    private static int CompareWords(string a, string b)
    {
        static (string Word, int? Number) Split(string s)
        {
            var end = s.Length;
            while (end > 0 && char.IsDigit(s[end - 1])) end--;
            return end < s.Length && end > 0 && int.TryParse(s[end..], out var n) ? (s[..end], n) : (s, null);
        }

        var (aWord, aNumber) = Split(a);
        var (bWord, bNumber) = Split(b);

        var words = string.Compare(aWord, bWord, StringComparison.OrdinalIgnoreCase);
        if (words != 0 || (aNumber is null && bNumber is null)) return words != 0 ? words : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);

        return (aNumber ?? 0).CompareTo(bNumber ?? 0);
    }

    /// <summary>Of <paramref name="published"/>, the one <paramref name="installed"/> is: the exact
    /// same version first; for a version read from a DLL (no label), the plain release before any
    /// labelled one (a -beta, a -hotfix) with the same numbers. Null when none is.</summary>
    public static string? BestSameRelease(string? installed, IEnumerable<string?> published)
    {
        var candidates = published.Where(p => p is not null && IsSameRelease(installed, p)).ToList();

        return candidates.FirstOrDefault(p => Compare(p, installed) == 0)
            ?? candidates.FirstOrDefault(p => Parse(p) is { Pre.Length: 0 })
            ?? candidates.FirstOrDefault();
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
