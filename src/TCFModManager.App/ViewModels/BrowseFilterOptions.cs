using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public enum ModSortOrder
{
    Newest,
    LastUpdated,
    MostDownloaded,
    MostFavourited,
    MostEndorsed,
}

// One entry in Browse's Sort by dropdown. Overrides ToString() so the label shows instead of the
// enum name.
//
// Holds a KEY, not a label: the text is read on every get, so choosing a language relabels the
// dropdown in place. Rebuilding the list instead would replace the item instances and drop the
// SelectedItem binding, which resets the dropdown to its first entry every time - see D9.
//
public sealed class SortOptionItem(string key, ModSortOrder value) : LocalizedViewModel
{
    public ModSortOrder Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

public enum FeaturedFilter
{
    // No restriction - featured and non-featured mods both show.
    Include,

    // Featured mods are hidden.
    Exclude,

    // Only featured mods show.
    Only,

    // Steam's "Created by Followed": only mods by an author followed from the Workshop pages.
    CreatedByFollowed,
}

// One entry in Browse's Featured dropdown.
public sealed class FeaturedFilterItem(string key, FeaturedFilter value) : LocalizedViewModel
{
    public FeaturedFilter Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

// One of Steam's filter chips on Browse's count line: what it says, whether it carries a [+]
// (true) or [-] (false) tag box or none (null), and how to take its filter off.
public sealed record BrowseFilterChip(string Text, bool? Include, Action Remove)
{
    public bool IsInclude => Include == true;

    public bool IsExclude => Include == false;
}
