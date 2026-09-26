using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>Where one installed mod stands on an SPT release the user is thinking of moving to.</summary>
public enum SptUpgradeStanding
{
    // The version installed now is published for that release.
    Ready,

    // A newer version is published for it; the installed one is not.
    UpdateNeeded,

    // Nothing published for it yet.
    NotYet,

    // Not matched to an sp-mod.com listing (installed by hand, or from a file), or its versions say
    // nothing readable about SPT - it has to be checked by hand.
    Unknown,
}

/// <summary>One installed mod in an upgrade report.</summary>
/// <param name="Version">The version for the target release: the installed one when Ready, the one
/// to update to when UpdateNeeded, else null.</param>
public sealed record SptUpgradeRow(string Name, int? ModId, string? Installed, SptUpgradeStanding Standing, string? Version);

/// <summary>What the report is built from, for each installed mod.</summary>
public sealed record SptUpgradeInput(string Name, int? ModId, string? Installed);

//
// "If I moved to SPT x.y, which of my mods would come with me?" - answered from the catalog each mod
// is already matched to, before anything is changed: the ones whose installed version is published
// for that release, the ones with a newer version that is, the ones with nothing for it yet, and
// the ones this app cannot know about.
//
public static class SptUpgradeReport
{
    public static List<SptUpgradeRow> Build(IEnumerable<SptUpgradeInput> installed, IReadOnlyList<Mod> catalog, string targetSpt)
    {
        var byId = catalog.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        var rows = new List<SptUpgradeRow>();

        foreach (var mod in installed)
        {
            if (mod.ModId is not { } id || !byId.TryGetValue(id, out var listing) || listing.Versions is not { Count: > 0 } versions)
            {
                rows.Add(new SptUpgradeRow(mod.Name, mod.ModId, mod.Installed, SptUpgradeStanding.Unknown, null));
                continue;
            }

            // Each version's answer for the target; null when its constraint cannot be read.
            var fits = versions.Select(v => (v.Version, Fits: SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, targetSpt))).ToList();

            var best = ModVersionComparer.BestSameRelease(mod.Installed, fits.Select(v => v.Version));
            var mine = fits.FirstOrDefault(v => best is not null && v.Version == best);
            if (mine.Fits == true)
            {
                rows.Add(new SptUpgradeRow(mod.Name, id, mod.Installed, SptUpgradeStanding.Ready, mine.Version));
                continue;
            }

            // The installed version is not among the versions known here (the catalog keeps only the
            // latest few), or what it needs cannot be read: nothing can be said about it.
            if (mine.Version is null || mine.Fits is null)
            {
                rows.Add(new SptUpgradeRow(mod.Name, id, mod.Installed, SptUpgradeStanding.Unknown, null));
                continue;
            }

            // Only a newer version is an update: an older one published for the target is not.
            var newest = fits
                .Where(v => v.Fits == true && v.Version is not null
                    && ModVersionComparer.IsUpdateAvailable(mod.Installed, v.Version) == true)
                .OrderByDescending(v => v.Version, Comparer<string?>.Create((a, b) => ModVersionComparer.Compare(a, b) ?? 0))
                .Select(v => v.Version)
                .FirstOrDefault();

            rows.Add(newest is not null
                ? new SptUpgradeRow(mod.Name, id, mod.Installed, SptUpgradeStanding.UpdateNeeded, newest)
                : new SptUpgradeRow(mod.Name, id, mod.Installed, SptUpgradeStanding.NotYet, null));
        }

        // What stands in the way first.
        return [.. rows.OrderBy(r => Array.IndexOf(Order, r.Standing)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static readonly SptUpgradeStanding[] Order =
        [SptUpgradeStanding.NotYet, SptUpgradeStanding.UpdateNeeded, SptUpgradeStanding.Unknown, SptUpgradeStanding.Ready];
}
