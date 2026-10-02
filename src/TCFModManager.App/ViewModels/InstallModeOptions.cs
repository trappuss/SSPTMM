using TCFModManager.App.Localization;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

//
// One entry in the Options page's Monitor mode dropdown. Holds a key rather than a label, so a
// language change relabels it in place - the same arrangement as ThemeOptionItem.
//
public sealed class InstallModeItem(string key, InstallMode value) : LocalizedViewModel
{
    public InstallMode Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// One entry in the Options page's Keep removed mods dropdown (R11).
public sealed class RemovedModsRetentionItem(string key, RemovedModsRetention value) : LocalizedViewModel
{
    public RemovedModsRetention Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// One entry in the Monitor mode card's "when a downloaded mod shows up installed" dropdown (R2).
public sealed class DownloadConfirmationItem(string key, DownloadConfirmation value) : LocalizedViewModel
{
    public DownloadConfirmation Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// One removal in the Installed page's Undo menu: its holding folder, and "<mod> - removed <when>".
public sealed record HeldRemovalItem(string Folder, string Label);
