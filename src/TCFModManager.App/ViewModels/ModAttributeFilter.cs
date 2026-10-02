using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

//
// The tick-box filters that describe a mod itself rather than its relationship to this install.
// Shared by Browse and Installed so the same five options mean the same thing on both pages.
//
public enum ModAttributeFilter
{
    // Only mods flagged Fika compatible.
    FikaCompatible,

    // Mods flagged as containing ads are hidden.
    HideAds,

    // Mods flagged as containing AI-generated content are hidden.
    HideAiContent,

    // Only mods that pull in other mods.
    HasDependencies,

    // Only mods that have addons published for them.
    HasAddons,

    // Mods already installed are hidden. Browse only - on Installed it would empty the page.
    HideInstalled,

    // Only mods with a Monitor mode download on disk that nobody has confirmed yet, whether it
    // looks fully installed or only partly. Installed only (R8).
    DownloadedNotConfirmed,

    // Only mods in a conflict (OPEN-11). Installed only.
    HasConflicts,
}

//
// One tickable line in an attribute filter dropdown.
//
// Three of these hide things and two of them require things, which reads oddly as a list until you
// notice every one of them narrows what you see - that is the whole contract of the dropdown, and
// why "Hide ads" sits happily beside "Has addons".
//
public partial class ModAttributeOption(ModAttributeFilter value, string key, string? toolTipKey = null)
    : LocalizedViewModel
{
    public ModAttributeFilter Value { get; } = value;

    // Both read on every get, so a language change relabels the tick boxes where they stand.
    public string Label => LocalizationService.Get(key);

    // Null for the options that need no explaining.
    public string? ToolTip => toolTipKey is null ? null : LocalizationService.Get(toolTipKey);

    [ObservableProperty]
    private bool _isSelected;

    //
    // The five options both pages carry, in the order both pages show them. Declared here rather
    // than twice, so adding a sixth is one edit and the two dropdowns cannot drift apart.
    //
    // A NEW collection every call, never a shared instance: each page ticks its own.
    //
    // The dependency tooltip is the one thing the two pages disagree about, and the disagreement is
    // real - Installed answers from the dependency graph built off the scan, which covers
    // everything installed, while Browse answers from whatever has been looked up so far. So the
    // caller states that one rather than this list pretending they are the same.
    //
    public static ObservableCollection<ModAttributeOption> Standard(string dependenciesToolTipKey) =>
    [
        new(ModAttributeFilter.FikaCompatible, nameof(Strings.Filter_FikaOnly)),
        new(ModAttributeFilter.HideAds, nameof(Strings.Filter_HideAds)),
        new(ModAttributeFilter.HideAiContent, nameof(Strings.Filter_HideAiContent)),
        new(ModAttributeFilter.HasDependencies, nameof(Strings.Filter_HasDependencies), dependenciesToolTipKey),
        new(ModAttributeFilter.HasAddons, nameof(Strings.Filter_HasAddons), nameof(Strings.Filter_HasAddonsToolTip)),
    ];
}

//
// One entry in a Category dropdown. Categories come from the cached catalog rather than a fixed
// list, so the app never shows a category The Forge has stopped using or misses one it has added.
//
// The "All categories" entry holds a key; every other entry holds a category title from the Forge,
// which arrives in whatever language its author wrote and is never translated here.
//
public sealed class CategoryFilterItem(string label, string? title) : LocalizedViewModel
{
    private readonly string? _key;

    private CategoryFilterItem(string key) : this(string.Empty, null) => _key = key;

    public string? Title { get; } = title;

    public string Label => _key is null ? label : LocalizationService.Get(_key);

    // No restriction.
    public static CategoryFilterItem All { get; } = new(nameof(Strings.Filter_AllCategories));

    public bool SameAs(CategoryFilterItem? other) =>
        other is not null && string.Equals(other.Title, Title, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Label;
}
