using System.Text.RegularExpressions;

namespace TCFModManager.Core.Services;

// The version window a constraint allows. MaxExclusive is the first version it rejects.
public readonly record struct SptVersionBounds(Version? Min, bool MinExclusive, Version? MaxExclusive, Version? Exact)
{
    // True when <paramref name="version"/> falls inside this window.
    //
    // The lower bound is honoured exactly as written (Chris, 2026-10-02). It used to be soft - "~4.1.3"
    // or ">=4.1.3" counted as fine on 4.1.1, on the belief that SPT doesn't break mods between
    // patches - but a server mod built against a newer patch references that SPTarkov.Server.Core
    // assembly version, and .NET refuses to load it on an older one: PityLoot "~4.1.6" on SPT 4.1.5
    // logged "Could not load file or assembly 'SPTarkov.Server.Core, Version=4.1.6.0'" and wasn't
    // loaded. A newer patch than the floor is fine; an older one is not.
    //
    // A bare exact constraint ("4.1.2", no operator) is the release the author tested with: it
    // matches that patch and every later one on its own major.minor line, never an earlier one.
    public bool Contains(Version version)
    {
        if (Exact is { } exact)
        {
            return version.Major == exact.Major && version.Minor == exact.Minor && version >= exact;
        }

        return Allows(version);
    }

    //
    // The same window read literally, with none of the SPT release-line relaxation Contains applies.
    // For a constraint written against another MOD's version - an addon's mod_version_constraint -
    // where a bare "1.7.0" means that release and ">=1.5.3" means 1.5.3 or newer, full stop. Mod
    // authors do break things between patch releases; SPT is the special case, not the rule.
    //
    public bool Allows(Version version)
    {
        if (Min is { } min && (MinExclusive ? version <= min : version < min)) return false;
        if (MaxExclusive is { } max && version >= max) return false;
        return true;
    }
}

// 
// Parses the SemVer range strings sp-mod.com puts on mod versions (spt_version_constraint) into
// the window of versions they allow, and answers questions about that window. Handles the
// documented operators plus wildcard forms ("4.0.*", "4.x"), which the Forge does publish.
// 
public static class SptVersionRange
{
    private static readonly Regex ClausePattern = new(
        @"^\s*(\^|~|>=|<=|>|<|=)?\s*([0-9]+(?:\.(?:[0-9]+|\*|[xX])){0,3}|\*)\s*$",
        RegexOptions.Compiled);

    // Parses every clause and intersects them. False when the constraint is missing or has
    // a clause this doesn't understand.
    public static bool TryParse(string? constraint, out SptVersionBounds bounds)
    {
        bounds = default;
        if (string.IsNullOrWhiteSpace(constraint)) return false;

        Version? min = null;
        Version? maxExclusive = null;
        Version? exact = null;
        var minExclusive = false;

        var clauses = constraint.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (clauses.Length == 0) return false;

        foreach (var clause in clauses)
        {
            // A bare wildcard means any version at all.
            if (clause is "*" or "x" or "X")
            {
                RaiseMin(ref min, ref minExclusive, new Version(0, 0, 0, 0), exclusive: false);
                continue;
            }

            var match = ClausePattern.Match(clause);
            if (!match.Success) return false;

            var op = match.Groups[1].Success ? match.Groups[1].Value : "=";
            if (!TryParseOperand(match.Groups[2].Value, out var value, out var wildcardCeiling, out var components))
                return false;

            // A wildcard operand is a range in its own right ("4.0.*" is the whole 4.0 line), so it
            // overrides the operator's usual meaning when no explicit operator was given.
            if (wildcardCeiling is not null && !match.Groups[1].Success)
            {
                RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                LowerMax(ref maxExclusive, wildcardCeiling);
                continue;
            }

            switch (op)
            {
                case "^":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                    LowerMax(ref maxExclusive, wildcardCeiling ?? NextMajor(value));
                    break;
                case "~":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);

                    // "~4.1.3" and "~4.1" both stop at the next minor, but "~4" - a major with no
                    // minor written after it - means the whole major line and stops at the next
                    // major. Same rule npm's semver uses, and the difference is not academic: TCF
                    // Mod Manager's own sp-mod listing is published as "~4", so reading it as the
                    // 4.0 line only made this app look incompatible with SPT 4.1 in its own Browse.
                    LowerMax(ref maxExclusive, wildcardCeiling ?? (components <= 1 ? NextMajor(value) : NextMinor(value)));
                    break;
                case ">=":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                    break;
                case ">":
                    RaiseMin(ref min, ref minExclusive, value, exclusive: true);
                    break;
                case "<":
                    LowerMax(ref maxExclusive, value);
                    break;
                case "<=":
                    LowerMax(ref maxExclusive, wildcardCeiling ?? CeilingFor(value, components));
                    break;
                default:
                    // A bare major with nothing after it ("4") covers all of SPT 4, exactly as
                    // "4.x" does - it is not the 4.0 line. Handled as a range rather than an exact
                    // match, because SptVersionBounds.Contains reads an exact as "this major.minor
                    // line", which is the right answer for "4.0.13" and the wrong one for "4".
                    if (components <= 1)
                    {
                        RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                        LowerMax(ref maxExclusive, NextMajor(value));
                        break;
                    }

                    exact = value;
                    RaiseMin(ref min, ref minExclusive, value, exclusive: false);
                    LowerMax(ref maxExclusive, NextPatch(value));
                    break;
            }
        }

        if (min is null && maxExclusive is null) return false;

        bounds = new SptVersionBounds(min, minExclusive, maxExclusive, exact);
        return true;
    }

    // 
    // True when the constraint allows any version in the given major.minor release line - e.g.
    // "^4.0.13" spans the 4.0 line and every line above it, while "~4.1.1" spans only 4.1.
    // A constraint this can't parse returns false; callers decide what an unreadable mod means.
    // 
    public static bool IntersectsReleaseLine(string? constraint, int major, int minor) =>
        TryParse(constraint, out var bounds) && Intersects(bounds, major, minor);

    // 
    // The combined window of every constraint that touches the given release line, clamped to that
    // line. Null when none of them do. Used to describe what a mod supports on one SPT line.
    // 
    public static SptVersionBounds? UnionForLine(IEnumerable<string?> constraints, int major, int minor)
    {
        var lineStart = new Version(major, minor, 0, 0);
        var lineEnd = new Version(major, minor + 1, 0, 0);

        Version? min = null;
        Version? maxExclusive = null;
        var found = false;

        foreach (var constraint in constraints)
        {
            if (!TryParse(constraint, out var bounds) || !Intersects(bounds, major, minor)) continue;

            found = true;

            var clampedMin = bounds.Min is { } m && m > lineStart ? m : lineStart;
            var clampedMax = bounds.MaxExclusive is { } x && x < lineEnd ? x : lineEnd;

            if (min is null || clampedMin < min) min = clampedMin;
            if (maxExclusive is null || clampedMax > maxExclusive) maxExclusive = clampedMax;
        }

        return found ? new SptVersionBounds(min, false, maxExclusive, null) : null;
    }

    private static bool Intersects(SptVersionBounds bounds, int major, int minor)
    {
        var lineStart = new Version(major, minor, 0, 0);
        var lineEnd = new Version(major, minor + 1, 0, 0);

        if (bounds.MaxExclusive is { } max && max <= lineStart) return false;
        if (bounds.Min is { } min && min >= lineEnd) return false;

        return true;
    }

    private static void RaiseMin(ref Version? min, ref bool minExclusive, Version value, bool exclusive)
    {
        if (min is not null && value <= min) return;

        min = value;
        minExclusive = exclusive;
    }

    private static void LowerMax(ref Version? maxExclusive, Version value)
    {
        if (maxExclusive is null || value < maxExclusive) maxExclusive = value;
    }

    //
    // The first version above what an operand of this precision names: "4" covers all of 4.x.x,
    // "4.1" all of 4.1.x, and "4.1.3" only that release. `components` is how many numbers the
    // author actually wrote, which the parsed Version can't tell you - it zero-fills.
    //
    private static Version CeilingFor(Version value, int components) => components switch
    {
        <= 1 => NextMajor(value),
        2 => NextMinor(value),
        _ => NextPatch(value),
    };

    internal static Version NextMajor(Version v) => new(v.Major + 1, 0, 0, 0);

    internal static Version NextMinor(Version v) => new(v.Major, v.Minor + 1, 0, 0);

    internal static Version NextPatch(Version v) => new(v.Major, v.Minor, v.Build + 1, 0);

    // 
    // Parses a clause's version operand. <paramref name="wildcardCeiling"/> is the first version
    // above the wildcard's range ("4.0.*" yields 4.0.0 with a ceiling of 4.1.0) and is null for a
    // fully specified version. <paramref name="components"/> is how many numbers were actually
    // written - the Version can't say, since it zero-fills "4" into 4.0.0.0 - and is what tells
    // "~4" (all of SPT 4) apart from "~4.0" (the 4.0 line).
    // 
    private static bool TryParseOperand(string raw, out Version value, out Version? wildcardCeiling, out int components)
    {
        value = new Version(0, 0, 0, 0);
        wildcardCeiling = null;
        components = 0;

        // Drop any pre-release/build suffix (e.g. "3.11.4-dev" -> "3.11.4").
        var core = raw.Split('-', 2)[0];
        var parts = core.Split('.');

        var numbers = new int[4];
        var wildcardAt = -1;

        for (var i = 0; i < parts.Length && i < 4; i++)
        {
            if (parts[i] is "*" or "x" or "X")
            {
                wildcardAt = i;
                break;
            }

            if (!int.TryParse(parts[i], out numbers[i])) return false;
            components = i + 1;
        }

        value = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);

        if (wildcardAt < 0) return true;

        // A bare "*" is handled by the caller before it gets here.
        if (wildcardAt == 0) return false;

        wildcardCeiling = wildcardAt switch
        {
            1 => NextMajor(value),
            2 => NextMinor(value),
            _ => NextPatch(value),
        };

        return true;
    }
}
