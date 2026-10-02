using TCFModManager.App.Localization;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// One entry in the Options page's "Check every" dropdown. Holds a key rather than a label, so a
// language change relabels it in place - the same arrangement as InstallModeItem.
public sealed class UpdateIntervalItem(string key, UpdateCheckInterval value) : LocalizedViewModel
{
    public UpdateCheckInterval Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}
