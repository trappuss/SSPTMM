using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TCFModManager.App.Localization;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// One collapsible section in the Mod Groups window: either a real, user-created ModGroup, or the
// fixed "Ungrouped" bucket (GroupId null) holding every installed mod nothing was assigned to.
public partial class ModGroupSectionViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Null for the Ungrouped bucket, which can't be renamed, deleted, reordered, or collapsed.
    public Guid? GroupId { get; init; }

    public bool IsRealGroup => GroupId is not null;

    // Whether the header's move up/down buttons show: a real group, while groups are in their own
    // manual order (an alphabetical order would just undo a move). Set by whoever builds the list.
    public bool CanReorder { get; set; }

    // The category a section holds when the Groups view is sorted by category ("" for no category);
    // null for a group's section and for Ungrouped. Such a section is not a group: nothing is
    // assigned to it and nothing can be dropped on it.
    public string? CategoryKey { get; init; }

    public bool IsCategory => CategoryKey is not null;

    // Fork: "enabled" or "disabled" when Subscribed items is grouped by install state; null otherwise.
    // Like a category, nothing is assigned to it or dropped on it.
    public string? StateKey { get; init; }

    // A category's section folds too, for as long as the app is open (it is not stored anywhere).
    public bool CanCollapse => IsRealGroup || IsCategory || StateKey is not null;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronGlyph))]
    private bool _isCollapsed;

    // Fluent icon name for the header's collapse toggle - matches a SymbolRegular member in the
    // WPF-UI build in use, same convention as ModStatusDisplay.Glyph.
    public string ChevronGlyph => IsCollapsed ? "ChevronRight24" : "ChevronDown24";

    // True while the header's name TextBox is shown in place of the TextBlock.
    [ObservableProperty]
    private bool _isEditing;

    public ObservableCollection<InstalledModCardViewModel> Items { get; } = [];

    public string CountLabel => Strings.Installed_GroupCount(Items.Count);

    public int DisabledCount => Items.Count(i => i.IsDisabled);

    // Shown after CountLabel in the header, so a group's state reads without expanding it.
    // Empty when nothing in the group is disabled, so an untouched group stays uncluttered.
    public string StateLabel => DisabledCount switch
    {
        _ when StateKey is not null => string.Empty, // Fork: the section's name already says it
        0 => string.Empty,
        var n when n == Items.Count => Strings.Installed_GroupAllDisabled,
        var n => Text(Strings.Installed_GroupSomeDisabledFormat, n),
    };

    // Whether the header's enable-all/disable-all/invert buttons have anything to act on.
    public bool HasItems => Items.Count > 0;

    public ModGroupSectionViewModel()
    {
        // These have no backing field for Items.CollectionChanged to invalidate on their own.
        Items.CollectionChanged += (_, _) => RefreshCounts();
    }

    // Re-reads the header's counts - also after its mods changed state without any coming or going.
    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(DisabledCount));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(HasItems));
    }

    public static ModGroupSectionViewModel FromGroup(ModGroup group) => new()
    {
        GroupId = group.Id,
        Name = group.Name,
        IsCollapsed = group.IsCollapsed,
    };

    public static ModGroupSectionViewModel ForCategory(string? category, bool collapsed) => new()
    {
        CategoryKey = category ?? string.Empty,
        Name = category ?? Strings.Installed_NoCategory,
        IsCollapsed = collapsed,
    };

    // Fork: Subscribed items grouped by install state.
    public static ModGroupSectionViewModel ForState(bool enabled, bool collapsed) => new()
    {
        StateKey = enabled ? "enabled" : "disabled",
        Name = enabled ? Strings.Installed_SectionEnabled : Strings.Installed_SectionDisabled,
        IsCollapsed = collapsed,
    };

    public static ModGroupSectionViewModel Ungrouped() => new()
    {
        GroupId = null,
        Name = Strings.Filter_Ungrouped,
    };
}
