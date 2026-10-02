using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// How one planned change is named wherever a list of them is shown in a sentence - the Mod lists
// preview, the Play page check and a machine's card on the Server map - so the three read alike.
//
// A change that moves a version carries both ends of it: "SAIN (3.0.0 → 3.1.2)". A name alone says
// something differs; the versions say what, which is the question a player about to join is asking.
//
public static class ModListActionWording
{
    public static string Named(ModListAction action) =>
        MovesVersion(action)
            ? LocalizationService.Text(Strings.ModLists_ChangeVersionPairFormat,
                action.Name,
                action.InstalledVersion ?? Strings.Common_Unknown,
                action.TargetVersion ?? Strings.ModLists_ActionNewestPublished)
            : action.Name;

    public static bool MovesVersion(ModListAction action) =>
        !action.IsRepair
        && (action.Kind == ModListActionKind.Update
            || (action.Kind == ModListActionKind.Enable && action.NeedsUpdateAfterEnable));
}
