using TCFModManager.App.Localization;

namespace TCFModManager.App.ViewModels;

//
// Fork (SSPTMM): what the Subscribed items page's one-row toolbar and its empty states read.
//
// The page used to carry 21 controls above the first mod. The filters now sit behind one Filters
// button, which says how many of them are narrowing the list; and a page with nothing installed
// says what the page is for instead of showing the whole toolbar over "No mods found".
//
// Kept in its own file so the original's InstalledViewModel.cs merges with as little in it as
// possible: ApplyFilter calls UpdateToolbarState, and that is all.
//
public partial class InstalledViewModel
{
    // How many of the Filters popup's filters are narrowing the list. Counted against each one's
    // neutral setting ("Any update status", "All categories" ...), not against the saved default:
    // the number says how much of your mod list is hidden, whatever the page opens with.
    // Sort and "Show in sections" are not counted - they change the order and layout, not which
    // mods are shown. Nor is the search box, which is on show beside the button.
    public int ActiveFilterCount =>
        (SelectedUpdateFilter.Value != UpdateFilterOptions[0].Value ? 1 : 0)
        + (SelectedEnabledFilter.Value != EnabledFilterOptions[0].Value ? 1 : 0)
        + (SelectedCategory.Title is not null ? 1 : 0)
        + (SelectedGroupFilter.AllGroups ? 0 : 1)
        + AttributeOptions.Count(o => o.IsSelected);

    public bool HasActiveFilters => ActiveFilterCount > 0;

    public string FiltersLabel => ActiveFilterCount == 0
        ? Strings.Installed_Filters
        : Text(Strings.Installed_FiltersCountFormat, ActiveFilterCount);

    // The scan has run, found an SPT folder, and found nothing in it: the page shows what it is for
    // and where mods come from, in place of the toolbar and an empty list. With no SPT folder set,
    // the status line's own message (set it in Options) stays the one thing said.
    public bool HasNoMods =>
        _hasScanned
        && !IsBusy
        && _all.Count == 0
        && !string.IsNullOrWhiteSpace(AppServices.SptEnvironment.InstallPath);

    // Mods are installed, but the search and filters hide every one of them. Not in the Groups view,
    // whose group headers stay on screen with "0 mods" beside each and say it there.
    public bool NothingMatches => _all.Count > 0 && _filtered.Count == 0 && !ShowGroups;

    private void UpdateToolbarState()
    {
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(FiltersLabel));
        OnPropertyChanged(nameof(HasNoMods));
        OnPropertyChanged(nameof(NothingMatches));
    }

    // A scan ends by clearing IsBusy after its last ApplyFilter, so the empty state is decided here
    // too - otherwise it would wait for the next filter change to appear.
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(HasNoMods));
}
