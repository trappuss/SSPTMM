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

    // Fallback only. The live Mod.DetailUrl from the API is preferred wherever one is available,
    // so a slug change on sp-mod.com doesn't leave the app pointing at a dead link.
    public const string ModPageUrl = "https://sp-mod.com/mod/2945/tcf-mod-manager";

    //
    // True in the steam-workshop-ui fork. The listing above is the original app's: its download is
    // the original build, so installing it over this one would replace the fork with the original
    // and every change the fork makes would be gone. The update check still runs - a newer version
    // of the original is worth knowing about, to merge - but the in-app installer does not.
    //
    public const bool IsFork = true;
}
