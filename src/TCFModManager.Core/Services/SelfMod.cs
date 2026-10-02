namespace TCFModManager.Core.Services;

//
// This app's own listing on sp-mod.com. It is a real, published mod page like any other - the
// self-updater downloads from it through the same public API and the same download link a person
// clicking "Download" on that page would get, and Browse hides it purely so the manager doesn't
// list itself among the mods it manages.
//
// Kept as one set of constants so the updater and the Browse filter can never drift apart.
//
public static class SelfMod
{
    // The sp-mod.com mod id. Everything else here is derivable from the API given this.
    public const string ModId = "2945";

    // The mod's GUID on sp-mod.com. Not used for the update check (which goes by id) - it's here so
    // anything matching installed mods against the catalog can recognise this listing as "us".
    public const string Guid = "com.tcf.tcfmodmanager";

    public const string Name = "TCF Mod Manager";

    // ---- this app ---------------------------------------------------------------------------------
    //
    // Steamified SPT Mod Manager (SSPTMM) is its own tool, built on TCF Mod Manager's code. Everything
    // above is the ORIGINAL app's sp-mod.com listing - still known so Browse can leave it out of the
    // mods it lists, and so the merge of a newer original release has something to compare with -
    // and nothing here downloads or installs it.
    //
    public const string AppName = "Steamified SPT Mod Manager";

    public const string ShortName = "SSPTMM";

    public const string RepositoryUrl = "https://github.com/trappuss/SSPTMM";

    public const string IssuesUrl = RepositoryUrl + "/issues";

    // The original, credited on the About page. OriginalVersion is the release last merged in -
    // bump it with each merge.
    public const string OriginalAuthor = "TheCrimsonFckr";

    public const string OriginalVersion = "1.19.0-beta";

    // Fallback only. The live Mod.DetailUrl from the API is preferred wherever one is available,
    // so a slug change on sp-mod.com doesn't leave the app pointing at a dead link.
    public const string ModPageUrl = "https://sp-mod.com/mod/2945/tcf-mod-manager";

    //
    // True for SSPTMM. The listing above is the original app's: its download is the original build,
    // so installing it over this one would replace SSPTMM with the original. So the original's update
    // check does not run at all (AppUpdateViewModel) and its installer is never offered; newer
    // releases of the original are merged in by hand instead.
    //
    public const bool IsFork = true;

    // Whether the original's sp-mod.com listing is checked for a newer version. A property rather
    // than a const, so code behind it is not compiled as unreachable.
    public static bool ChecksOriginalUpdates => !IsFork;
}
