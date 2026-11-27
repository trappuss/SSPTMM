using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM 1.3.0), from TCF Mod Manager's NewestFittingVersion (448af87, MIT): the newest
// version of an addon whose mod-version constraint the parent's version satisfies - for a collection
// entry that names no version, or whose named version is gone. Null when versions were published but
// none fits, so the caller can say why rather than install one that won't load.
//
// Fork (1.4.9), as TCF 43251ea and 774c4c4: "newest" is the highest version number, everywhere. An
// author who edits an older version after releasing a newer one gets a later publish date on the
// older one (WTT - Content Backport 2.0.6, dated a minute after 2.0.7), and by date that older
// version was the one offered - with a file its host no longer had.
//
public static class NewestFittingVersion
{
    // A version with no readable constraint isn't ruled out, and with the parent's version unknown
    // (not installed yet, or fetched at its newest), simply the newest.
    public static AddonVersion? ForParent(IEnumerable<AddonVersion> versions, string? parentVersion) =>
        versions
            .NewestFirst()
            .FirstOrDefault(v => string.IsNullOrWhiteSpace(parentVersion)
                || ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentVersion) != false);

    public static IEnumerable<ModVersionSummary> NewestFirst(this IEnumerable<ModVersionSummary> versions) =>
        Newest(versions, v => v.Version, v => v.PublishedAt ?? v.CreatedAt);

    public static IEnumerable<ModVersion> NewestFirst(this IEnumerable<ModVersion> versions) =>
        Newest(versions, v => v.Version, v => v.PublishedAt ?? v.CreatedAt);

    public static IEnumerable<AddonVersionSummary> NewestFirst(this IEnumerable<AddonVersionSummary> versions) =>
        Newest(versions, v => v.Version, v => v.PublishedAt);

    public static IEnumerable<AddonVersion> NewestFirst(this IEnumerable<AddonVersion> versions) =>
        Newest(versions, v => v.Version, v => v.PublishedAt);

    // Highest version number first. The publish date only orders versions with the same number, and
    // the ones whose number cannot be read, which go last.
    private static IEnumerable<T> Newest<T>(
        IEnumerable<T> versions, Func<T, string?> version, Func<T, DateTimeOffset?> published) =>
        versions
            .OrderByDescending(v => Readable(version(v)))
            .ThenByDescending(v => version(v), ByVersion)
            .ThenByDescending(v => published(v) ?? DateTimeOffset.MinValue);

    private static readonly Comparer<string?> ByVersion =
        Comparer<string?>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0);

    private static bool Readable(string? version) => ModVersionComparer.Compare(version, version) is not null;
}
