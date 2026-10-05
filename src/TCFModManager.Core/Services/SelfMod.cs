namespace TCFModManager.Core.Services;

//
// Who this app is, and the app it was built from.
//
// SSPTMM (Steamified SPT Mod Manager) is its own tool. It began as a fork of TCF Mod Manager by
// TheCrimsonFckr (MIT), whose copyright notice stays in LICENSE and who is credited on the About
// page; since 1.2.0 it no longer follows the original's releases. The constants for the original's
// sp-mod.com listing remain because Browse and collections leave that listing out - a mod manager
// is not one of the mods it manages - and because the original's own updater code, which never runs
// here, reads them.
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
    public const string AppName = "Steamified SPT Mod Manager";

    public const string ShortName = "SSPTMM";

    public const string RepositoryUrl = "https://github.com/trappuss/SSPTMM";

    public const string IssuesUrl = RepositoryUrl + "/issues";

    // SSPTMM's own guide, opened from Help.
    public const string WikiUrl = RepositoryUrl + "/wiki";

    // The original, credited on the About page.
    public const string OriginalAuthor = "TheCrimsonFckr";

    // Fallback only. The live Mod.DetailUrl from the API is preferred wherever one is available,
    // so a slug change on sp-mod.com doesn't leave the app pointing at a dead link.
    public const string ModPageUrl = "https://sp-mod.com/mod/2945/tcf-mod-manager";

    //
    // True for SSPTMM. The listing above is the original app's: its download is the original build,
    // so installing it over this one would replace SSPTMM with the original. So the original's update
    // check does not run at all (AppUpdateViewModel) and its installer is never offered. SSPTMM's own
    // releases are found on GitHub (GitHubReleaseCheck).
    //
    public const bool IsFork = true;

    // Whether the original's sp-mod.com listing is checked for a newer version. A property rather
    // than a const, so code behind it is not compiled as unreachable.
    public static bool ChecksOriginalUpdates => !IsFork;
}
