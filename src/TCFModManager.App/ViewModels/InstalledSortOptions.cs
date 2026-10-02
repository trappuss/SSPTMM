using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

// Installed page's "Sort by" dropdown - orders both the flat grid and each group's mod list in
// group view.
public enum ModSortOption
{
    NameAscending,
    NameDescending,
    AuthorAscending,
    AuthorDescending,

    // By the user-defined group a mod is in; ungrouped mods sort last in both directions.
    GroupAscending,
    GroupDescending,
}

// One entry in the "Sort by" dropdown. Overrides ToString() so the label shows instead of the enum name.
//
// Holds a KEY, not a label: the text is read on every get, so choosing a language relabels the
// dropdown in place. Rebuilding the list instead would replace the item instances and drop the
// SelectedItem binding, which resets the dropdown to its first entry every time - see D9.
//
public sealed class ModSortItem(string key, ModSortOption value) : LocalizedViewModel
{
    public ModSortOption Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// Group view's "Sort groups" dropdown - orders the section list itself. Manual is each group's own
// up/down-reorderable ModGroup.SortOrder; the header's move-up/down buttons only make sense - and
// only show (see InstalledViewModel.CanReorderGroups) - while this is selected, since an
// alphabetical sort would just override anything they did.
public enum GroupSortOption
{
    Manual,
    NameAscending,
    NameDescending,

    // Not an order of your groups: one section per sp-mod.com category in their place.
    Category,
}

public sealed class GroupSortItem(string key, GroupSortOption value) : LocalizedViewModel
{
    public GroupSortOption Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// How the Cards and List views arrange their mods: one run of them, or in sections - your own groups
// (the Groups view's sections) or sp-mod.com's categories. The Groups view is always in sections and
// keeps its own choice (its "Sort groups" dropdown).
public enum InstalledGrouping
{
    None,
    Groups,
    Category,
}

public sealed class GroupingItem(string key, InstalledGrouping value) : LocalizedViewModel
{
    public InstalledGrouping Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}
