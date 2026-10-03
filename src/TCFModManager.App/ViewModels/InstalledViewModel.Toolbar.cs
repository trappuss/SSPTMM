using CommunityToolkit.Mvvm.Input;
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

//
// Fork (SSPTMM, UI tidy-up 4): picking several mods the way Steam's library does - Ctrl+click adds
// or drops one, Shift+click takes everything between the last one picked and this one, and a tick
// box shows on a card under the pointer for anyone who doesn't know the keys. Nothing has to be
// switched on first: as soon as one is picked, a bar along the bottom of the page offers what can
// be done to them, and goes again when the last is dropped. (Multi select, the mode this replaces,
// is no longer offered; SelectionMode stays false.)
//
public partial class InstalledViewModel
{
    // The mod Shift+click counts from: the last one picked on its own.
    private InstalledModCardViewModel? _selectionAnchor;

    public bool HasAnySelection => SelectedCount > 0;

    public string SelectionBarLabel => Strings.Installed_SelectionBar(SelectedCount);

    // Ctrl+click: one mod in or out, and the next Shift+click counts from it.
    public void ToggleSelected(InstalledModCardViewModel mod)
    {
        mod.IsSelected = !mod.IsSelected;
        _selectionAnchor = mod;
    }

    // The tick box, which ticks through its own binding: the next Shift+click counts from it.
    public void NoteAnchor(InstalledModCardViewModel mod) => _selectionAnchor = mod;

    //
    // Shift+click: exactly the mods from the anchor to this one, in the order they are on screen -
    // as Windows and Steam do it, so whatever else was picked is dropped. With nothing picked yet,
    // it is just this one.
    //
    public void SelectRange(InstalledModCardViewModel target)
    {
        var order = VisibleOrder();
        var from = _selectionAnchor is null ? -1 : order.IndexOf(_selectionAnchor);
        var to = order.IndexOf(target);
        if (to < 0) return;
        if (from < 0) from = to;

        var (low, high) = from <= to ? (from, to) : (to, from);
        for (var i = 0; i < order.Count; i++)
            order[i].IsSelected = i >= low && i <= high;

        foreach (var mod in _all.Except(order)) mod.IsSelected = false;
        _selectionAnchor ??= target;
    }

    // The mods in the order the view on screen shows them: the Groups view's open sections, Cards'
    // or List's sections when they are in sections, or the filtered list otherwise.
    private List<InstalledModCardViewModel> VisibleOrder() =>
        ShowGroups ? [.. Sections.Where(s => !s.IsCollapsed).SelectMany(s => s.Items)]
        : IsViewGrouped ? [.. ViewSections.Where(s => !s.IsCollapsed).SelectMany(s => s.Items)]
        : [.. _filtered];

    private void AnnounceSelection()
    {
        OnPropertyChanged(nameof(HasAnySelection));
        OnPropertyChanged(nameof(SelectionBarLabel));
        if (SelectedCount == 0) _selectionAnchor = null;
    }

    //
    // The blue Update on a card that has one waiting: the same pass as Update all and Update
    // selected, for this one mod - the mod's page first if Options says so, the SPT version check,
    // and the download queue.
    //
    [RelayCommand]
    private void UpdateOne(InstalledModCardViewModel? card)
    {
        if (card is not null) UpdateCards([card]);
    }

    // Whether each card shows that button: exactly the mods Update all would take.
    private void MarkUpdatableCards()
    {
        foreach (var card in _all) card.CanUpdateHere = IsUpdatable(card);
    }
}
