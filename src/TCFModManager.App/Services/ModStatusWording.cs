using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// What each mod status says to the user, on its own.
//
// Core decides what a mod's status IS and hands back a ModStatus; the sentence describing it lives
// here, beside the rest of this app's prose, for the same reason ModInstallProblems and
// ApiProblems do - Core has no UI, and a sentence buried in a project with no UI is one nobody
// finds when it needs rewording.
//
// The icon stays in Core with the status: an icon name is data, and four callers read it.
//
public static class ModStatusWording
{
    public static string Tooltip(ModStatus status) => status switch
    {
        ModStatus.Installed => Strings.ModStatus_Installed,
        ModStatus.UpdateAvailable => Strings.ModStatus_UpdateAvailable,
        ModStatus.NotInstalled => Strings.ModStatus_NotInstalled,
        ModStatus.NoCompatibleVersion => Strings.ModStatus_NoCompatibleVersion,
        ModStatus.Unknown => Strings.ModStatus_Unknown,
        ModStatus.Disabled => Strings.ModStatus_Disabled,
        ModStatus.TooNew => Strings.ModStatus_TooNew,
        _ => Strings.ModStatus_Conflict,
    };
}
