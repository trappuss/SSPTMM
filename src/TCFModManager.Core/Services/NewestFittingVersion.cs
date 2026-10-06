using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Fork (SSPTMM 1.3.0), from TCF Mod Manager's NewestFittingVersion (448af87, MIT): the newest
// version of an addon whose mod-version constraint the parent's version satisfies - for a collection
// entry that names no version, or whose named version is gone. Null when versions were published but
// none fits, so the caller can say why rather than install one that won't load.
//
public static class NewestFittingVersion
{
    // A version with no readable constraint isn't ruled out, and with the parent's version unknown
    // (not installed yet, or fetched at its newest), simply the newest.
    public static AddonVersion? ForParent(IEnumerable<AddonVersion> versions, string? parentVersion) =>
        versions
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Version, Comparer<string?>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
            .FirstOrDefault(v => string.IsNullOrWhiteSpace(parentVersion)
                || ModVersionMatcher.IsSatisfiedBy(v.ModVersionConstraint, parentVersion) != false);
}
