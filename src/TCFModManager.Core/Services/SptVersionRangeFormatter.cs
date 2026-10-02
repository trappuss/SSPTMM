namespace TCFModManager.Core.Services;

// 
// Turns the SemVer range strings sp-mod.com puts on mod versions (spt_version_constraint) into
// something readable by someone who doesn't know what "^" and "~" mean - e.g. "^4.0.13 &lt;4.1.0"
// becomes "4.0.13 - 4.0.x". Returns null for anything it can't parse, so callers can fall back to
// showing the raw constraint.
// 
public static class SptVersionRangeFormatter
{
    // 
    // Formats <paramref name="constraint"/> as a plain version range. Null when the constraint is
    // missing or has no clause this understands.
    // 
    public static string? Format(string? constraint) =>
        SptVersionRange.TryParse(constraint, out var bounds) ? Format(bounds) : null;

    // Formats an already-parsed window, for callers that combined several constraints.
    public static string Format(SptVersionBounds bounds)
    {
        var (min, minExclusive, maxExclusive, exact) = bounds;

        // A bare exact version means that patch and later ones on its line (SptVersionBounds.Contains).
        if (exact is not null && min == exact && maxExclusive == SptVersionRange.NextPatch(exact))
            return Format(new SptVersionBounds(exact, false, SptVersionRange.NextMinor(exact), null));

        if (min is null) return $"up to {DescribeUpperBound(maxExclusive!)}";

        // A bare "*" parses to "0.0.0 and up", which reads better as "any version".
        if (maxExclusive is null && !minExclusive && min == new Version(0, 0, 0, 0)) return "any version";

        if (maxExclusive is null)
            return minExclusive ? $"newer than {FormatVersion(min)}" : $"{FormatVersion(min)}+";

        var upper = DescribeUpperBound(maxExclusive);

        // "4.0.0 - 4.0.x" is just "4.0.x", and "4.0.0 - 4.x" is just "4.x".
        if (!minExclusive && min.Build == 0)
        {
            if (maxExclusive == SptVersionRange.NextMinor(min)) return upper;
            if (min.Minor == 0 && maxExclusive == SptVersionRange.NextMajor(min)) return upper;
        }

        return minExclusive ? $"after {FormatVersion(min)}, up to {upper}" : $"{FormatVersion(min)} - {upper}";
    }

    // Renders an exclusive upper bound as the highest release it actually allows -
    // "&lt;4.1.0" becomes "4.0.x", "&lt;4.0.14" becomes "4.0.13", "&lt;5.0.0" becomes "4.x".
    private static string DescribeUpperBound(Version maxExclusive)
    {
        if (maxExclusive.Revision == 0)
        {
            if (maxExclusive.Build > 0) return $"{maxExclusive.Major}.{maxExclusive.Minor}.{maxExclusive.Build - 1}";
            if (maxExclusive.Minor > 0) return $"{maxExclusive.Major}.{maxExclusive.Minor - 1}.x";
            if (maxExclusive.Major > 0) return $"{maxExclusive.Major - 1}.x";
        }

        return $"below {FormatVersion(maxExclusive)}";
    }

    private static string FormatVersion(Version v) =>
        v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
}
