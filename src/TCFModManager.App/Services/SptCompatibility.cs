using System.Windows;
using TCFModManager.App.Localization;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The one question every install path asks before it fetches a version that doesn't support the
// installed SPT (Chris, 2026-10-02). It used to fetch the newest version silently when none fitted -
// PityLoot "~4.1.6" onto SPT 4.1.5, which the server then refused to load.
//
// Asked, never refused: the constraint is the author's word and can be wrong. The answer defaults
// to No. An unknown SPT version or an unreadable constraint is not "incompatible" and isn't asked.
//
public static class SptCompatibility
{
    public static bool IsIncompatible(string? sptVersionConstraint) =>
        SptVersionMatcher.IsSatisfiedBy(sptVersionConstraint, AppServices.SptEnvironment.InstalledVersion) == false;

    // "PityLoot 1.0.0 - needs SPT 4.1.6 or newer on 4.1", one per mod in the question.
    public static string Line(string name, string? version, string? constraint) =>
        LocalizationService.Text(
            Strings.Install_IncompatibleLineFormat,
            name,
            version,
            SptVersionRangeFormatter.Format(constraint) ?? constraint);

    //
    // Asks whether to install versions that don't support the installed SPT anyway. Nothing to ask
    // returns true. For a batch, No means "everything else, without these".
    //
    public static bool ConfirmAnyway(IReadOnlyList<string> lines, bool batch)
    {
        if (lines.Count == 0) return true;

        var body = LocalizationService.Text(
            batch ? Strings.Install_IncompatibleBatchFormat : Strings.Install_IncompatibleOneFormat,
            AppServices.SptEnvironment.InstalledVersion,
            string.Join("\n", lines));

        return MessageBox.Show(
            body,
            Strings.Install_IncompatibleTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
