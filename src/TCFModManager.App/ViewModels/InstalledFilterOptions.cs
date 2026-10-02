using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

public enum UpdateFilter
{
    // No restriction - every installed mod shows regardless of update status.
    All,

    // Only mods with a confirmed newer version on sp-mod.com show (UpdateAvailable == true).
    NeedsUpdate,

    // Only mods confirmed up to date show (UpdateAvailable == false).
    UpToDate,

    // Only mods with no sp-mod.com match at all show (MatchedModName == null).
    NotFound,

    // Only mods installed in the last RecentDays days, newest first - this order wins over the
    // Sort by dropdown while it is picked. Mods with no known install date are left out.
    RecentlyInstalled,
}

// One entry in Installed's Update status dropdown. Overrides ToString() so the label shows instead
// of the enum name.
//
// Holds a KEY, not a label: the text is read on every get, so choosing a language relabels the
// dropdown in place. Rebuilding the list instead would replace the item instances and drop the
// SelectedItem binding, which resets the dropdown to its first entry every time - see D9.
//
public sealed class UpdateFilterItem(string key, UpdateFilter value) : LocalizedViewModel
{
    public UpdateFilter Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

public enum EnabledFilter
{
    // No restriction - enabled and disabled mods both show.
    All,

    // Only mods SPT actually loads.
    EnabledOnly,

    // Only mods moved out into a ".disabled" container.
    DisabledOnly,
}

// One entry in Installed's Enabled/Disabled dropdown.
public sealed class EnabledFilterItem(string key, EnabledFilter value) : LocalizedViewModel
{
    public EnabledFilter Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

//
// One entry in Installed's Group dropdown, rebuilt from the group store whenever groups change.
// "All" places no restriction; "Ungrouped" (AllGroups false, GroupId null) matches mods assigned to
// nothing; anything else matches one group by id.
//
// The two fixed entries hold a key; every other entry holds a group NAME the user typed, which is
// data and is never translated - the trap §10 describes, and the reason this one type has both.
//
public sealed class GroupFilterItem(string label, Guid? groupId, bool allGroups) : LocalizedViewModel
{
    private readonly string? _key;

    private GroupFilterItem(string key, bool allGroups) : this(string.Empty, null, allGroups) => _key = key;

    public Guid? GroupId { get; } = groupId;

    public bool AllGroups { get; } = allGroups;

    public string Label => _key is null ? label : LocalizationService.Get(_key);

    public static GroupFilterItem All { get; } = new(nameof(Strings.Filter_AllGroups), true);

    public static GroupFilterItem Ungrouped { get; } = new(nameof(Strings.Filter_Ungrouped), false);

    // Two entries describe the same filter when they match on both fields - used to keep the
    // current selection across a rebuild of the list.
    public bool SameAs(GroupFilterItem? other) =>
        other is not null && other.AllGroups == AllGroups && other.GroupId == GroupId;

    public override string ToString() => Label;
}
