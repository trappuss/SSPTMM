using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

// Decides a dependency's status, and how severe it is relative to others.
public static class DependencyStatusResolver
{
    // 
    // Resolves one node's status. <paramref name="installedVersion"/> is null when the dependency
    // isn't on disk. <paramref name="requiredVersion"/> is the node's latest compatible version,
    // which the API leaves null when nothing published fits the installed SPT.
    // 
    //
    // <paramref name="exactVersion"/>: the installed version is the published one (the app installed
    // it, or it was confirmed by hand). Otherwise it was read from a DLL, whose version authors do
    // not always keep in step - then only a later major line counts as too new, not a later minor.
    //
    public static ModStatus Resolve(DependencyNode node, string? installedVersion, string? requiredVersion, bool installedButDisabled = false, bool exactVersion = true)
    {
        // A conflict is about the graph as a whole, so it outranks whatever is on disk.
        if (node.Conflict) return ModStatus.Conflict;

        // A disabled dependency is on disk but isn't loaded, so nothing depending on it works.
        if (installedButDisabled) return ModStatus.Disabled;

        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            // With no compatible version published there's nothing to install either, which is more
            // useful to say than a plain "missing".
            return string.IsNullOrWhiteSpace(requiredVersion)
                ? ModStatus.NoCompatibleVersion
                : ModStatus.NotInstalled;
        }

        if (ModVersionComparer.IsUpdateAvailable(installedVersion, requiredVersion) == true)
            return ModStatus.UpdateAvailable;

        //
        // Newer than the newest version that fits: requiredVersion is the highest one that satisfies
        // the constraint (and this SPT), so anything above it does not - CommonLib 3.0.6 installed
        // for a mod made for 2.x. Not "installed and fine".
        //
        var newer = exactVersion
            ? ModVersionComparer.IsUpdateAvailable(requiredVersion, installedVersion)
            : ModVersionComparer.IsLaterMajor(installedVersion, requiredVersion);
        return newer == true ? ModStatus.TooNew : ModStatus.Installed;
    }

    // Sort key for "worst" - lower is more severe. Drives the per-mod header icon.
    public static int Severity(ModStatus status) => status switch
    {
        ModStatus.Conflict => 0,
        ModStatus.NotInstalled => 1,
        ModStatus.Disabled => 2,
        ModStatus.TooNew => 3,
        ModStatus.NoCompatibleVersion => 4,
        ModStatus.UpdateAvailable => 5,
        _ => 6,
    };

    // The most severe status in a set, or Installed when empty.
    public static ModStatus Worst(IEnumerable<ModStatus> statuses)
    {
        var worst = ModStatus.Installed;
        var best = Severity(worst);

        foreach (var status in statuses)
        {
            var severity = Severity(status);
            if (severity >= best) continue;

            best = severity;
            worst = status;
        }

        return worst;
    }
}
