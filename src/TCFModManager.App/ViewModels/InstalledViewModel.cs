using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

/// <summary>
/// Scans AppServices.SptEnvironment.InstallPath for what's actually installed (see InstalledModScanner),
/// then merges and paginates the results the same way BrowseViewModel does for the sp-mod.com catalog.
///
/// A fresh instance is created every time InstalledPage is navigated to, so it re-scans disk on every visit.
/// </summary>
public partial class InstalledViewModel : LocalizedViewModel, IModActionHost
{
    void IModActionHost.ShowActionMessage(string? message) => StatusMessage = message;

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    // Fills the grid exactly at the 3- and 4-column width breakpoints (see UpdateLayoutForWidth).
    // Fork: one of the sizes on offer; the page opens on Infinite until a size is picked - see
    // DefaultPageSize().
    private const int DefaultPageSizeValue = 12;

    // Below this a card can't show its summary line without truncating it to uselessness. Only
    // reachable on a very narrow window, where Columns is already 1.
    private const double MinimumCardWidth = 240;

    /// <summary>Raised after RemoveAsync actually removes a mod from disk. Lets BrowseViewModel refresh its
    /// cards' install/update status dots. Static since InstalledViewModel is a fresh instance per navigation.</summary>
    public static event EventHandler? ModRemoved;

    /// <summary>Raised after mods are set aside (disabled) or brought back, or that is undone, so
    /// Browse's installed ticks follow - a set-aside mod is not installed as far as the game goes.</summary>
    public static event EventHandler? ModsMoved;

    private List<InstalledModCardViewModel> _all = [];

    private readonly System.Windows.Threading.DispatcherTimer _rescanAfterInstall;
    private List<InstalledModCardViewModel> _filtered = [];

    //
    // Whether each view's collection still reflects _filtered, i.e. whether it needs rebuilding
    // before being shown. All three start dirty so the first fill always happens.
    //
    // Only the view on screen is ever rebuilt, so the other two go stale while they're hidden and
    // have to catch up when switched to. Without these flags that catch-up ran on every switch,
    // whether or not anything had changed: each rebuild clears the collection, which destroys every
    // realized row, and the List and Groups views are unpaginated and don't virtualize - so
    // switching to List regenerated a CardExpander per installed mod each time, for a result
    // identical to what was already there. ApplyFilter is the one place _filtered changes, so it is
    // the one place that marks all three dirty again.
    //
    private bool _cardsDirty = true;
    private bool _listDirty = true;
    private bool _sectionsDirty = true;

    // Cards' and List's sections, when they are grouped (ViewSections) - the same flag again.
    private bool _viewSectionsDirty = true;

    // Who needs whom among the mods currently on disk, rebuilt on every scan. Backs the warning
    // shown before a disable takes something else's dependency away.
    private ModDependencyGraph _dependencies = ModDependencyGraph.Build([]);

    // Maps each raw scan entry back to the card it was merged into, so a dependency link (which is
    // between InstalledMods) can be reported and acted on as whole mods.
    private Dictionary<InstalledMod, InstalledModCardViewModel> _cardByEntry = [];

    // The moves the last disable/enable made, and what to call it - the undo payload.
    private List<ModMove> _lastMoves = [];
    private string? _lastMoveLabelKey;
    private object? _lastMoveLabelArg;

    // The current page of the Cards grid.
    public ObservableCollection<InstalledModCardViewModel> Results { get; } = [];

    // Every filtered mod, unpaginated, for the List view - which scrolls rather than pages.
    public ObservableCollection<InstalledModCardViewModel> ListItems { get; } = [];

    public List<UpdateFilterItem> UpdateFilterOptions { get; } =
    [
        new(nameof(Strings.Filter_UpdateAny), UpdateFilter.All),
        new(nameof(Strings.Filter_UpdateNeeded), UpdateFilter.NeedsUpdate),
        new(nameof(Strings.Filter_UpdateUpToDate), UpdateFilter.UpToDate),
        new(nameof(Strings.Filter_UpdateNotFound), UpdateFilter.NotFound),
        new(nameof(Strings.Filter_UpdateRecentlyInstalled), UpdateFilter.RecentlyInstalled),
    ];

    // How far back the Recently installed filter looks.
    private const int RecentDays = 7;

    [ObservableProperty]
    private UpdateFilterItem _selectedUpdateFilter;

    public List<EnabledFilterItem> EnabledFilterOptions { get; } =
    [
        new(nameof(Strings.Filter_EnabledAll), EnabledFilter.All),
        new(nameof(Strings.Filter_EnabledOnly), EnabledFilter.EnabledOnly),
        new(nameof(Strings.Filter_DisabledOnly), EnabledFilter.DisabledOnly),
    ];

    [ObservableProperty]
    private EnabledFilterItem _selectedEnabledFilter;

    // Rebuilt from the group store by RefreshGroups whenever groups change, so the dropdown always
    // lists exactly the groups that exist.
    public ObservableCollection<GroupFilterItem> GroupFilterOptions { get; } =
        [GroupFilterItem.All, GroupFilterItem.Ungrouped];

    [ObservableProperty]
    private GroupFilterItem _selectedGroupFilter;

    public List<ModSortItem> SortOptions { get; } =
    [
        // "By ..." rather than a bare "Name (A-Z)": with the inline "Sort by:" label gone, a
        // dropdown reading "Group (A-Z)" sitting next to the Group *filter* would be read as
        // another filter. The prefix is what makes these read as orderings on their own.
        new(nameof(Strings.Sort_NameAscending), ModSortOption.NameAscending),
        new(nameof(Strings.Sort_NameDescending), ModSortOption.NameDescending),
        new(nameof(Strings.Sort_AuthorAscending), ModSortOption.AuthorAscending),
        new(nameof(Strings.Sort_AuthorDescending), ModSortOption.AuthorDescending),
        new(nameof(Strings.Sort_GroupAscending), ModSortOption.GroupAscending),
        new(nameof(Strings.Sort_GroupDescending), ModSortOption.GroupDescending),
        new(nameof(Strings.Sort_RecentlyInstalled), ModSortOption.RecentlyInstalled), // Fork
    ];

    [ObservableProperty]
    private ModSortItem _selectedSortOption;

    // Set while ClearFilters is resetting several properties at once, so each individual
    // OnXxxChanged below doesn't run its own AutoApplyFilter.
    private bool _suppressAutoApplyFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    //
    // The same five tick boxes Browse carries, in one dropdown rather than three toggle switches.
    // "Has dependencies" is a stronger answer here than on Browse: it comes from the dependency
    // graph built off the scan, which reads BepInEx's own metadata, so it covers everything
    // installed rather than whatever has been looked up so far.
    //
    // Plus one of Installed's own: Monitor mode downloads that are on disk but unconfirmed (R8).
    public ObservableCollection<ModAttributeOption> AttributeOptions { get; } =
    [
        .. ModAttributeOption.Standard(nameof(Strings.Filter_HasDependenciesInstalledToolTip)),
        new(ModAttributeFilter.DownloadedNotConfirmed,
            nameof(Strings.Filter_DownloadedNotConfirmed),
            nameof(Strings.Filter_DownloadedNotConfirmedToolTip)),
        new(ModAttributeFilter.HasConflicts,
            nameof(Strings.Filter_HasConflicts),
            nameof(Strings.Filter_HasConflictsToolTip)),
    ];

    //
    // Downloads the confirm prompt has already put to the user this session and been told "not
    // now". A scan runs after every remove, enable and dialog close, so without this the same
    // question would come back every few seconds.
    //
    private readonly HashSet<(int ModId, bool IsAddon, string Version)> _deferredDownloads = [];

    [ObservableProperty]
    private string _attributeFilterSummary = Strings.Filter_AnyMod;

    /// <summary>The Category dropdown's entries, rebuilt after each scan from the categories actually present in the install.</summary>
    public ObservableCollection<CategoryFilterItem> CategoryOptions { get; } = [CategoryFilterItem.All];

    [ObservableProperty]
    private CategoryFilterItem _selectedCategory = CategoryFilterItem.All;

    private bool IsOn(ModAttributeFilter filter) =>
        AttributeOptions.Any(o => o.Value == filter && o.IsSelected);

    //
    // Which of the three views the results area is showing. All three render the same filtered,
    // sorted set - Cards paginates summary cards, Groups arranges compact rows under the user's own
    // MO2-style separators, List scrolls one expandable row per mod with its full details.
    //
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCards))]
    [NotifyPropertyChangedFor(nameof(ShowPager))]
    [NotifyPropertyChangedFor(nameof(ShowGroups))]
    [NotifyPropertyChangedFor(nameof(ShowGroupBar))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    [NotifyPropertyChangedFor(nameof(ShowExpanders))]
    [NotifyCanExecuteChangedFor(nameof(ExpandAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CollapseAllCommand))]
    [NotifyPropertyChangedFor(nameof(ScrollsItself))]
    [NotifyPropertyChangedFor(nameof(ShowGroupingPicker))]
    [NotifyPropertyChangedFor(nameof(ShowPageSize))]
    [NotifyPropertyChangedFor(nameof(ShowCardsFlat))]
    [NotifyPropertyChangedFor(nameof(ShowCardSections))]
    [NotifyPropertyChangedFor(nameof(ShowListSections))]
    [NotifyPropertyChangedFor(nameof(GroupChipsRedundant))]
    [NotifyPropertyChangedFor(nameof(NothingMatches))] // Fork: InstalledViewModel.Toolbar.cs
    private InstalledViewMode _viewMode = InstalledViewMode.Cards;

    public bool ShowCards => ViewMode == InstalledViewMode.Cards;

    public bool ShowGroups => ViewMode == InstalledViewMode.Groups;

    public bool ShowList => ViewMode == InstalledViewMode.List;

    // Cards and List are the two views made of expandable rows; group view's sections collapse on
    // their own terms and have their own controls, so expand/collapse all hides there.
    public bool ShowExpanders => ViewMode is InstalledViewMode.Cards or InstalledViewMode.List;

    /// <summary>True for the two views that scroll rather than paginate - what hides the pagination
    /// controls and the per-page picker.</summary>
    public bool ScrollsItself => !ShowCards || IsViewGrouped;

    //
    // Cards and List in sections, as the Groups view is: your own groups or sp-mod.com's categories.
    // The Groups view is always in sections and keeps its own choice (Sort groups), so this picker is
    // shown over the other two only. In sections Cards shows every mod, as List does - a page
    // boundary cutting through a section would split it.
    //
    public List<GroupingItem> GroupingOptions { get; } =
    [
        new(nameof(Strings.Installed_GroupingNone), InstalledGrouping.None),
        new(nameof(Strings.Installed_GroupingGroups), InstalledGrouping.Groups),
        new(nameof(Strings.Installed_GroupingCategory), InstalledGrouping.Category),
        new(nameof(Strings.Installed_GroupingInstallState), InstalledGrouping.InstallState), // Fork
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewGrouped))]
    [NotifyPropertyChangedFor(nameof(ScrollsItself))]
    [NotifyPropertyChangedFor(nameof(ShowPager))]
    [NotifyPropertyChangedFor(nameof(ShowPageSize))]
    [NotifyPropertyChangedFor(nameof(ShowCardsFlat))]
    [NotifyPropertyChangedFor(nameof(ShowCardSections))]
    [NotifyPropertyChangedFor(nameof(ShowListSections))]
    [NotifyPropertyChangedFor(nameof(GroupChipsRedundant))]
    private GroupingItem _selectedGrouping;

    public bool IsViewGrouped => SelectedGrouping.Value != InstalledGrouping.None;

    public bool ShowGroupingPicker => !ShowGroups;

    // Whether a mod can be dragged onto a section in the view on screen: the sections are your
    // groups. A category is not something a mod is put into.
    public bool SectionsTakeDrops => ShowGroups ? !GroupsByCategory : ShowsViewSectionsAsGroups;

    private bool ShowsViewSectionsAsGroups => (ShowCards || ShowList) && SelectedGrouping.Value == InstalledGrouping.Groups;

    // Per page means nothing where every mod is shown.
    public bool ShowPageSize => ShowCards && !IsViewGrouped;

    public bool ShowCardsFlat => ShowCards && !IsViewGrouped;

    public bool ShowCardSections => ShowCards && IsViewGrouped;

    public bool ShowListSections => ShowList && IsViewGrouped;

    // Cards' and List's sections, when grouped. Separate from Sections: the Groups view can be
    // sorted by category while these are in your groups, and the other way round.
    public ObservableCollection<ModGroupSectionViewModel> ViewSections { get; } = [];

    //
    // A mod's group chip says which group it is in - which the section around it already says when
    // the sections are your groups. Shown everywhere else, including the Groups view sorted by
    // category.
    //
    public bool GroupChipsRedundant => ShowGroups
        ? !GroupsByCategory
        : SelectedGrouping.Value == InstalledGrouping.Groups;

    public ObservableCollection<ModGroupSectionViewModel> Sections { get; } = [];

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    public List<GroupSortItem> GroupSortOptions { get; } =
    [
        new(nameof(Strings.Sort_GroupsManual), GroupSortOption.Manual),
        new(nameof(Strings.Sort_GroupNameAscending), GroupSortOption.NameAscending),
        new(nameof(Strings.Sort_GroupNameDescending), GroupSortOption.NameDescending),
        new(nameof(Strings.Sort_GroupsByCategory), GroupSortOption.Category),
    ];

    /// <summary>The Groups view shows one section per category rather than your groups.</summary>
    public bool GroupsByCategory => SelectedGroupSortOption.Value == GroupSortOption.Category;

    // The category sections folded this session, by CategoryKey - kept across rebuilds.
    private readonly HashSet<string> _collapsedCategories = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private GroupSortItem _selectedGroupSortOption;

    /// <summary>Whether the group header's move-up/move-down buttons should show - only while
    /// groups are in their manual order; an alphabetical group sort would just override them.</summary>
    public bool CanReorderGroups => SelectedGroupSortOption.Value == GroupSortOption.Manual;

    // ON turns the cards, List's rows and Groups' rows into a tick-list so several can be disabled,
    // enabled, updated or unsubscribed at once; a click toggles its tick instead of opening the
    // details dialog while this is on. The ticks are the same in all three views.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionModeOff))]
    [NotifyPropertyChangedFor(nameof(ShowGroupBar))]
    private bool _selectionMode;

    /// <summary>The group management bar: Groups view, except while selecting - the selection bar
    /// takes its place then.</summary>
    public bool ShowGroupBar => ShowGroups && !SelectionMode;

    /// <summary>Inverse of SelectionMode, for controls that only show while it's off.</summary>
    public bool SelectionModeOff => !SelectionMode;

    public int SelectedCount => _all.Count(m => m.IsSelected);

    public string SelectedCountLabel => Text(Strings.Installed_SelectedFormat, SelectedCount);

    //
    // Everything the current filters match, not just the page on screen - which is why the count is
    // in the label. "Select all" over a paginated list is ambiguous otherwise, and someone who has
    // filtered to "Update available" means all of them, not the first twelve.
    //
    public string SelectAllLabel => Text(Strings.Installed_SelectAllFormat, _filtered.Count);

    //
    // Whether every mod the filters match is already ticked. Tints Select all, the same way
    // SelectionMode tints Multi select and ViewMode tints the view switcher: a Primary button on
    // this page means "this is already the case".
    //
    // It reads over _filtered rather than _all deliberately, because that is what Select all acts
    // on - with a filter narrowing the list, ticking everything it matches is "all of them".
    //
    public bool AllSelected => _filtered.Count > 0 && _filtered.All(m => m.IsSelected);

    /// <summary>Whether the last disable/enable can still be put back.</summary>
    public bool CanUndo => _lastMoves.Count > 0;

    public string UndoLabel => _lastMoveLabelKey is null
        ? Strings.Installed_Undo
        : Text(LocalizationService.Get(_lastMoveLabelKey), _lastMoveLabelArg);

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _columns = 1;

    // Width of one card slot in the Cards WrapPanel - see UpdateLayoutForWidth for why the cards
    // are no longer in a UniformGrid.
    [ObservableProperty]
    private double _cardWidth = MinimumCardWidth;

    //
    // Whether cards and rows show which mod lists a mod belongs to. Persisted rather than reset per
    // session: it is a display preference, not a filter, which is also why ClearFilters leaves it
    // alone.
    //
    [ObservableProperty]
    private bool _showListBadges = true;

    //
    // Infinite, as Browse has it: no pager, and the next InfiniteStep cards are added as the page
    // nears its bottom. Not "every card at once": Cards view builds its cards rather than
    // virtualising them, so a hundred-odd up front is work the app does not need to be doing.
    //
    public const int InfinitePageSize = BrowseViewModel.InfinitePageSize;

    private const int InfiniteStep = 24;

    public List<int> PageSizeOptions { get; } = [InfinitePageSize, 8, DefaultPageSizeValue, 16, 24, 32];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInfinite))]
    [NotifyPropertyChangedFor(nameof(ShowPager))]
    private int _pageSize = DefaultPageSizeValue;

    public bool IsInfinite => PageSize == InfinitePageSize;

    /// <summary>The pager under the cards: Cards view, unless it is the infinite list.</summary>
    public bool ShowPager => ShowCards && !IsInfinite && !IsViewGrouped;

    // Cards per page, or per load of the infinite list.
    private int PageStep => IsInfinite ? InfiniteStep : PageSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    //
    // What this page opens filtered and sorted to - since 1.2.0 remembered by the page itself as it
    // changes (InstalledViewModel.Remember), in the setting Save as default wrote before; see Core's
    // PageDefaults. Null on an install that has never had one, in which case every Default* helper
    // below answers with the app's own default.
    //
    private InstalledPageDefaults? _defaults; // Fork: replaced as the page remembers itself (InstalledViewModel.Remember)

    //
    // Whether the saved category/group defaults have been handed to their dropdowns yet. Both lists
    // are rebuilt from what is installed, so the first rebuild is the earliest moment either
    // default can be resolved to a real entry - and every rebuild after that has to preserve
    // whatever is currently selected instead, or coming back to the page would silently reset it.
    //
    private bool _categoryDefaultApplied;
    private bool _groupDefaultApplied;

    //
    // Fork (1.3.0): only the saved update status. The original also read Sort = "RecentlyInstalled"
    // as this filter - from the release where Recently installed moved from Sort by to here - but
    // SSPTMM has a Recently installed sort again (5ba33de), so that turned "Installed in the last 7
    // days" back on at every start for anyone sorting by it, whatever this dropdown was left at.
    //
    private UpdateFilterItem DefaultUpdateFilter() =>
        SavedFilterDefaults.Parse<UpdateFilter>(_defaults?.UpdateStatus) is { } value
            ? UpdateFilterOptions.FirstOrDefault(o => o.Value == value) ?? UpdateFilterOptions[0]
            : UpdateFilterOptions[0];

    private EnabledFilterItem DefaultEnabledFilter() =>
        SavedFilterDefaults.Parse<EnabledFilter>(_defaults?.Enabled) is { } value
            ? EnabledFilterOptions.FirstOrDefault(o => o.Value == value) ?? EnabledFilterOptions[0]
            : EnabledFilterOptions[0];

    private ModSortItem DefaultSortOption() =>
        SavedFilterDefaults.Parse<ModSortOption>(_defaults?.Sort) is { } value
            ? SortOptions.FirstOrDefault(o => o.Value == value) ?? SortOptions[0]
            : SortOptions[0];

    private GroupSortItem DefaultGroupSortOption() =>
        SavedFilterDefaults.Parse<GroupSortOption>(_defaults?.GroupSort) is { } value
            ? GroupSortOptions.FirstOrDefault(o => o.Value == value) ?? GroupSortOptions[0]
            : GroupSortOptions[0];

    private GroupingItem DefaultGrouping() =>
        SavedFilterDefaults.Parse<InstalledGrouping>(_defaults?.Grouping) is { } value
            ? GroupingOptions.FirstOrDefault(o => o.Value == value) ?? GroupingOptions[0]
            : GroupingOptions[0];

    private InstalledViewMode DefaultViewMode() =>
        SavedFilterDefaults.Parse<InstalledViewMode>(_defaults?.ViewMode) ?? InstalledViewMode.Cards;

    private int DefaultPageSize() =>
        PageSizeMemory.Load(PageSizeMemory.SubscribedItems, _defaults?.PageSize, PageSizeOptions); // Fork: Infinite, or the size last picked

    //
    // Described rather than looked up, because the entry it describes may not exist: the category
    // list is built from what is installed, so a saved category can be one nothing currently
    // matches. CategoryFilterItem.SameAs compares titles, so this finds its entry when there is one
    // and falls back to "All categories" when there isn't.
    //
    private CategoryFilterItem DefaultCategory() =>
        string.IsNullOrWhiteSpace(_defaults?.Category)
            ? CategoryFilterItem.All
            : new CategoryFilterItem(_defaults.Category!, _defaults.Category);

    //
    // Same again for groups. A saved group that has since been deleted reads as "All groups" -
    // the same thing RebuildSections already does with an assignment pointing at a deleted group.
    //
    private GroupFilterItem DefaultGroupFilter()
    {
        var saved = _defaults?.Group;

        if (string.IsNullOrWhiteSpace(saved)) return GroupFilterItem.All;
        if (saved.Equals("ungrouped", StringComparison.OrdinalIgnoreCase)) return GroupFilterItem.Ungrouped;

        // Label is irrelevant here - SameAs matches on the id and the all-groups flag only.
        return Guid.TryParse(saved, out var id)
            ? new GroupFilterItem(string.Empty, id, allGroups: false)
            : GroupFilterItem.All;
    }

    //
    // The most recently built instance - the Installed page's own, once it has been opened. The
    // Workshop item page's Unsubscribe removes through it, so a removal from there takes exactly the
    // path, the confirmations and the rescan a removal from Subscribed items does.
    //
    public static InstalledViewModel? Current { get; private set; }

    // Whether this page has scanned at least once - see the UpdatesFound subscription below.
    private bool _hasScanned;

    //
    // For a removal asked for elsewhere (the item page, the right-click menu, a collection) before
    // Subscribed items has been opened: one kept apart from the page. It is never Current, and it
    // leaves the page's own business alone - the update notification's filter, the watcher's
    // rescans, Monitor mode's confirm window - so none of those happen twice, or to the wrong one.
    //
    public static InstalledViewModel ForActions => Current ?? (_forActions ??= new InstalledViewModel(forPage: false));

    private static InstalledViewModel? _forActions;

    private readonly bool _forPage;

    public InstalledViewModel() : this(forPage: true)
    {
    }

    private InstalledViewModel(bool forPage)
    {
        _forPage = forPage;
        if (forPage) Current = this;

        // Installs and updates finishing in the queue show here without a Rescan: a second after the
        // last one of a batch lands, the page scans again, as Steam's list updates by itself.
        _rescanAfterInstall = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _rescanAfterInstall.Tick += async (_, _) =>
        {
            _rescanAfterInstall.Stop();

            // Only the newest instance - the page's own - scans. An older one (the one the item
            // page's Unsubscribe built before this page was first opened) would otherwise scan the
            // disk again after every install for the rest of the session, for a list nobody sees.
            if (!ReferenceEquals(Current, this)) return;

            // A scan already running may have read the folder before this install landed: try
            // again once it has finished, rather than dropping the install until the next one.
            if (IsBusy)
            {
                _rescanAfterInstall.Start();
                return;
            }

            if (ScanCommand.CanExecute(null)) await ScanCommand.ExecuteAsync(null);
        };
        AppServices.DownloadQueue.ItemInstalled += (_, _) =>
        {
            if (!ReferenceEquals(Current, this)) return;

            _rescanAfterInstall.Stop();
            _rescanAfterInstall.Start();
        };

        var settings = new SettingsService().Load();
        _showListBadges = settings.ShowModListBadges;
        _defaults = settings.InstalledDefaults;

        //
        // Backing fields rather than the properties: this is the page opening at its default, not
        // someone changing a filter, so none of the OnXxxChanged handlers should run a filter pass
        // over a page that has not scanned anything yet.
        //
        _selectedUpdateFilter = DefaultUpdateFilter();
        _selectedEnabledFilter = DefaultEnabledFilter();
        _selectedSortOption = DefaultSortOption();
        _selectedGroupSortOption = DefaultGroupSortOption();
        _viewMode = DefaultViewMode();
        _selectedGrouping = DefaultGrouping();
        _pageSize = DefaultPageSize();

        // Category and group are resolved later instead: both dropdowns are rebuilt from what is
        // actually installed, and neither list exists yet - see RebuildCategoryOptions/RefreshGroups.
        _selectedGroupFilter = GroupFilterItem.All;

        // Before the subscription below, so applying a saved default doesn't count as a change.
        SavedFilterDefaults.ApplyAttributes(AttributeOptions, _defaults?.Attributes);
        UpdateAttributeFilterSummary();

        // Each tick box drives the same re-filter a dropdown selection does.
        foreach (var option in AttributeOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                UpdateAttributeFilterSummary();
                AutoApplyFilter();
            };
        }

        //
        // A click on an update notification opens this page on "Updates available" (§6). Taken
        // here when the click is what built the page, and from the event when it already existed.
        //
        if (!forPage) return;

        if (AppNavigation.TakeShowUpdates()) _selectedUpdateFilter = _updateFilterFromNavigation = UpdatesAvailableFilter();

        AppNavigation.ShowUpdatesRequested += (_, _) =>
        {
            if (!ReferenceEquals(Current, this)) return;
            if (!AppNavigation.TakeShowUpdates()) return;
            SelectedUpdateFilter = UpdatesAvailableFilter();
            _updateFilterFromNavigation = SelectedUpdateFilter; // not remembered as a choice (Remember)
        };

        // Fork: Diagnose logs' "Show in Subscribed items".
        if (AppNavigation.TakeSearch() is { } search) _searchText = search;
        AppNavigation.SearchRequested += (_, _) =>
        {
            if (!ReferenceEquals(Current, this)) return;
            if (AppNavigation.TakeSearch() is { } wanted) SearchText = wanted;
        };

        //
        // The update watcher patched the catalog, so the arrows on screen are out of date. Only once
        // the page has scanned - before that, its first scan reads the patched catalog anyway.
        //
        AppServices.UpdateWatcher.UpdatesFound += async (_, _) =>
        {
            if (!ReferenceEquals(Current, this) || !_hasScanned || ScanCommand.IsRunning) return;
            await ScanCommand.ExecuteAsync(null);
        };
    }

    private UpdateFilterItem UpdatesAvailableFilter() =>
        UpdateFilterOptions.First(o => o.Value == UpdateFilter.NeedsUpdate);

    partial void OnSelectedCategoryChanged(CategoryFilterItem value)
    {
        if (!_rebuildingFilterLists) _categoryFellBack = false; // Fork: picked, so remembered as it is
        AutoApplyFilter();
    }

    // "Any mod", the one option's own label, or a count.
    private void UpdateAttributeFilterSummary()
    {
        var selected = AttributeOptions.Where(o => o.IsSelected).ToList();

        AttributeFilterSummary = selected.Count switch
        {
            0 => Strings.Filter_AnyMod,
            1 => selected[0].Label,
            _ => Text(Strings.Filter_SelectedCountFormat, selected.Count),
        };
    }

    //
    // Rebuilt from what is actually installed rather than from the whole catalog: filtering your
    // own mods by a category none of them are in would only ever produce an empty page.
    //
    private void RebuildCategoryOptions()
    {
        // The saved default on the first build, and whatever is currently chosen on every rebuild
        // after it - a rescan must not quietly reset a filter the user has since changed.
        // Fork: and while the remembered one has fallen back to All, still the remembered one - so it
        // comes back once something in it is installed, or the catalog says what is.
        var previous = _categoryDefaultApplied && !_categoryFellBack ? SelectedCategory : DefaultCategory();
        _categoryDefaultApplied = true;

        // Fork: the whole rebuild, Clear included - a fallback here is not a choice to remember.
        _rebuildingFilterLists = true;
        try
        {
            CategoryOptions.Clear();
            CategoryOptions.Add(CategoryFilterItem.All);

            var categories = _all
                .Select(c => c.CategoryTag)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

            foreach (var category in categories) CategoryOptions.Add(new CategoryFilterItem(category, category));

            // Keep the current choice if that category is still installed, rather than silently
            // resetting the page's filter under someone every time they come back to it.
            var kept = CategoryOptions.FirstOrDefault(c => c.SameAs(previous));
            SelectedCategory = kept ?? CategoryOptions[0];

            // Fork: a remembered category with nothing installed in it (or no catalog to say so, offline)
            // stays remembered until a category is picked (see Remember).
            _categoryFellBack = kept is null && !previous.SameAs(CategoryFilterItem.All);
        }
        finally
        {
            _rebuildingFilterLists = false;
        }

        RememberIfPending(); // Fork
    }

    partial void OnSelectedUpdateFilterChanged(UpdateFilterItem value)
    {
        _updateFilterFromNavigation = null; // Fork: chosen now, so remembered (see Remember)
        AutoApplyFilter();
    }

    partial void OnSelectedEnabledFilterChanged(EnabledFilterItem value) => AutoApplyFilter();

    partial void OnSelectedGroupFilterChanged(GroupFilterItem value) => AutoApplyFilter();

    // Leaving selection mode drops the selection with it, so a stale tick can't be acted on later.
    partial void OnSelectionModeChanged(bool value)
    {
        if (!value) ClearSelection();
    }

    partial void OnSelectedSortOptionChanged(ModSortItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value)
    {
        PageSizeMemory.Save(PageSizeMemory.SubscribedItems, value); // Fork
        AutoApplyFilter();
    }

    //
    // Deliberately not AutoApplyFilter: nothing about what is shown changes, only whether each row
    // draws its chips, so the three views don't need rebuilding.
    //
    partial void OnShowListBadgesChanged(bool value)
    {
        foreach (var card in _all) card.ShowBadges = value;

        var settings = new SettingsService();
        var current = settings.Load();
        current.ShowModListBadges = value;
        settings.Save(current);
    }

    partial void OnSearchTextChanged(string value) => AutoApplyFilter();


    partial void OnViewModeChanged(InstalledViewMode value)
    {
        // Deliberately not AutoApplyFilter: no filter, search or sort has changed, so re-running
        // ApplyFilter would produce the same _filtered it already holds. Refreshing the view being
        // switched to is all that's needed, and that does nothing at all when it is already current.
        // CurrentPage rather than the default 1 so switching away from Cards and back returns you to
        // the page you were on.
        RefreshActiveView(CurrentPage);

        // Cards and List hold their own expansion, so the same mods can be all-open in one and
        // all-closed in the other - the two buttons have to re-read whichever is now on screen.
        NotifyExpandedState();
        RememberFilters(); // Fork
    }

    partial void OnSelectedGroupSortOptionChanged(GroupSortItem value)
    {
        OnPropertyChanged(nameof(CanReorderGroups));
        OnPropertyChanged(nameof(GroupsByCategory));
        OnPropertyChanged(nameof(GroupChipsRedundant));

        // Cards' and List's groups follow this order too.
        _sectionsDirty = _viewSectionsDirty = true;

        // Clear filters sets this among the rest, then refreshes once itself.
        if (_suppressAutoApplyFilter) return;
        RefreshActiveView(CurrentPage);
        RememberFilters(); // Fork
    }

    partial void OnSelectedGroupingChanged(GroupingItem value)
    {
        _viewSectionsDirty = true;

        // Grouped, Cards leaves its pages - and ungrouped goes back to the one it was on.
        _cardsDirty = true;

        // Clear filters sets this among the rest, then refreshes once itself.
        if (_suppressAutoApplyFilter) return;
        RefreshActiveView(CurrentPage);
        RememberFilters(); // Fork
    }

    /// <summary>Re-filters/re-sorts and refreshes whichever of the three views is active whenever a
    /// filter/search/sort control changes. Suppressed while ClearFilters resets several properties
    /// at once.</summary>
    private void AutoApplyFilter()
    {
        if (_suppressAutoApplyFilter) return;
        ApplyFilter();
        RememberFilters(); // Fork

        // CurrentPage, not 1. Narrowing a filter shrinks TotalPages and GoToPage clamps to it, so a
        // page that no longer exists still lands somewhere sensible - and nothing is left that can
        // silently put you back on page 1. Dropping the resetPage flag was not enough on its own:
        // this method runs whenever any filter/sort/search/page-size property changes, including
        // when one is reassigned in code, and its RefreshActiveView() was taking the default of 1.
        RefreshActiveView(CurrentPage);
    }

    /// <summary>Refills whichever results collection the current view reads from, if it has fallen behind
    /// _filtered. Only the active one is rebuilt; switching views rebuilds the one being switched to, and
    /// only if something has actually changed since that view last drew.</summary>
    // No default for `page`: every caller has to say which page it means. A default of 1 sitting
    // here is what let a caller reset the page without looking like it was doing anything.
    private void RefreshActiveView(int page)
    {
        switch (ViewMode)
        {
            case InstalledViewMode.Groups:
                if (_sectionsDirty) RebuildSections();
                break;
            case InstalledViewMode.List when IsViewGrouped:
            case InstalledViewMode.Cards when IsViewGrouped:
                if (_viewSectionsDirty) RebuildViewSections();
                break;
            case InstalledViewMode.List:
                if (_listDirty) RebuildList();
                break;
            default:
                GoToPage(page);
                break;
        }
    }

    private void RebuildList()
    {
        // Synced rather than cleared, for the same reason the card grid is - see Sync.
        ItemsSync.Apply(ListItems, _filtered);
        _listDirty = false;
    }

    //
    // Resets every filter/search control back to the app's own defaults, then re-applies once
    // immediately.
    //
    // Fork (1.2.0): the app's own, not a saved default - the page now remembers itself (Remember), so
    // "this page's default" would just be how it already is. The cleared page is then remembered.
    //
    // The view mode is deliberately left alone: which of Cards/Groups/List you are looking at is
    // not a filter, and a button in the filter row that also switched view would be a surprise.
    //
    [RelayCommand]
    private void ClearFilters()
    {
        _defaults = null; // Fork: every Default* helper now answers with the app's own
        _suppressAutoApplyFilter = true;
        try
        {
            SearchText = string.Empty;
            SelectedUpdateFilter = DefaultUpdateFilter();
            SelectedEnabledFilter = DefaultEnabledFilter();
            SelectedGroupFilter = GroupFilterOptions.FirstOrDefault(o => o.SameAs(DefaultGroupFilter()))
                ?? GroupFilterOptions[0];
            SelectedSortOption = DefaultSortOption();
            SelectedGroupSortOption = DefaultGroupSortOption();
            SelectedGrouping = DefaultGrouping();
            SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(DefaultCategory()))
                ?? CategoryOptions[0];
            SavedFilterDefaults.ApplyAttributes(AttributeOptions, _defaults?.Attributes ?? []);
            UpdateAttributeFilterSummary();
            PageSize = DefaultPageSize();
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }

        ApplyFilter();
        RefreshActiveView(CurrentPage);
        RememberFilters(); // Fork
    }

    //
    // Tags each card with the mod lists that name it. Worked out through ModListMembership, which
    // runs the real planner, so a badge can never disagree with what applying that list would do.
    //
    // Runs per scan rather than being watched: the Installed page rescans when it is navigated to,
    // so editing a list on the Mod lists page and coming back here shows the change.
    //
    private void ApplyListMembership(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        var lists = AppServices.ModLists.Load().Lists;
        if (lists.Count == 0)
        {
            foreach (var card in cards) card.Lists = [];
            return;
        }

        var names = ModListMembership.Names(lists, ModListCandidates.From(cards));

        for (var i = 0; i < cards.Count; i++) cards[i].Lists = names[i];
    }

    private static void ApplyPins(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        var pins = AppServices.ModLists.GetPins();

        foreach (var card in cards)
            card.IsPinned = pins.Count > 0 && ModListPlanner.IsPinned(ModListCandidates.From(card), pins);
    }

    [RelayCommand]
    private void TogglePin(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;

        var pin = !mod.IsPinned;
        AppServices.ModLists.SetPinned(ModListPlanner.PinKeys(ModListCandidates.From(mod)), pin);
        mod.IsPinned = pin;

        // Fork: pinned mods sit at the top, so the order changes with the pin.
        ApplyFilter();
        RefreshActiveView(CurrentPage);

        StatusMessage = Text(
            pin ? Strings.Installed_PinnedFormat : Strings.Installed_UnpinnedFormat,
            mod.DisplayTitle);
        AppLog.Info("Installed", $"{(pin ? "pinned" : "unpinned")} {mod.Name} against mod list disables");
    }

    // A fresh scan builds new cards, which default to showing badges - they have to be told.
    private void ApplyBadgeVisibility()
    {
        foreach (var card in _all) card.ShowBadges = ShowListBadges;
    }

    public void UpdateLayoutForWidth(double availableWidth)
    {
        //
        // Ignore a width of zero rather than treating it as "very narrow".
        //
        // Collapsing an element makes WPF raise SizeChanged with 0 x 0, so switching to Groups or
        // List view - which collapses the card grid - reported no width and dropped Columns to 1.
        // Coming back to Cards then showed every card full width, one per row, looking like a list,
        // and it stayed that way until the window was resized and a real width arrived. The last
        // real width is still the right answer for a grid nobody can currently see.
        //
        // Also covers the first Loaded call, which can run before the list has been arranged.
        //
        if (availableWidth <= 0) return;

        Columns = availableWidth switch
        {
            < 700 => 1,
            < 1050 => 2,
            < 1400 => 3,
            _ => 4,
        };

        //
        // The cards sit in a WrapPanel now rather than a UniformGrid, because a UniformGrid forces
        // every cell to the same size - opening one card would have made every card on the page
        // that tall. A WrapPanel lets each card keep its own height, which is the whole point of
        // making them expandable, and ItemWidth is what keeps the columns lining up.
        //
        // Floored, and a pixel taken off first: ItemWidth * Columns landing even a fraction over
        // the available width wraps a card onto the next row, so a four-column layout would
        // silently become three.
        //
        CardWidth = Math.Max(MinimumCardWidth, Math.Floor((availableWidth - 1) / Columns));
    }

    //
    // Re-reads the install and rebuilds every card. Deliberately leaves you on the page you were
    // on: a scan changes what the list holds, not which part of it you were looking at.
    //
    // This used to take a resetPage flag, true for the Rescan button and the page's Loaded handler
    // and false for everything else. That is what sent you back to page 1 on closing the mod
    // dialog - Loaded fires again every time the page is re-attached, not just the first time, and
    // any scan that went through the flag-true path clobbered the page. Resetting to page 1
    // belongs to a *filter* change, where the list you are paging through is genuinely different,
    // and AutoApplyFilter already does exactly that.
    //
    [RelayCommand]
    private async Task ScanAsync()
    {
        _hasScanned = true;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _all = [];
            ApplyFilter();
            RefreshActiveView(CurrentPage);
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        IsBusy = true;
        try
        {
            // Reuses whatever's already cached; triggers the one-time catalog fetch if Browse
            // hasn't been visited yet this session. Offline with no copy saved yet, the mods in the
            // SPT folder are still listed - scanning them needs no network - just without what the
            // site knows about them.
            var offline = false;
            try
            {
                await AppServices.ModCache.EnsureLoadedAsync();
                await AppServices.Addons.EnsureLoadedAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
            {
                AppLog.Warn("Installed", $"catalog unavailable, listing the SPT folder only: {ex.Message}");
                offline = true;
            }

            // What this app itself installed, and which folders it placed - identifies those mods
            // exactly instead of inferring them from folder names. Read here, on the UI thread, and
            // not in the background work below: saves made from this thread (confirming a download,
            // setting a version by hand) replace the file, and a read open on another thread at that
            // moment could make the save fail.
            var installRecords = AppServices.InstallManifest.Load().ModsFor(installPath);

            // Read once here rather than inside the background work, so a catalog refresh landing
            // mid-scan can't swap the list out from under it.
            var catalog = AppServices.ModCache.AllMods;
            var addons = AppServices.Addons.AllAddons;
            var sptVersion = AppServices.SptEnvironment.InstalledVersion;

            // The whole scan-and-match pass runs off the UI thread. Matching a large install
            // against a full catalog is the slower half of the two, and doing it inline is what
            // made navigating to this page hang.
            var (scanned, cards, dependencies, loadedGuids, downloads, conflicts) = await Task.Run(() =>
            {
                var found = InstalledModScanner.Scan(installPath);

                var built = InstalledModCardViewModel.BuildFrom(found, catalog, sptVersion, installRecords, addons)
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Built off the same scan the cards came from, so every link points at an entry
                // some card owns.
                return (found, built, ModDependencyGraph.Build(found), InstalledModScanner.LoadedPluginGuids(installPath),
                    MatchDownloads(found, installPath, installRecords), ModConflicts.Find(built));
            });

            // What was open in each view, keyed the same way group assignments are, so the sets
            // survive every card object being replaced. Captured before _all is reassigned, and
            // kept apart because the two views expand independently.
            var openCards = OpenKeys(_all, m => m.IsCardExpanded);
            var openRows = OpenKeys(_all, m => m.IsRowExpanded);

            _all = cards;
            RefreshUndo();

            // What sp-mod.com held back last time stands until it answers again.
            foreach (var card in cards)
            {
                card.HeldBackNote = card.IsAddon ? null : AppServices.HeldBack.Note(card.ModId);
                card.NotForSptNote = card.IsAddon ? null : AppServices.HeldBack.NotForSptNote(card.ModId, card.InstalledVersion);
            }
            _ = CheckHeldBackAsync(cards);

            if (openCards.Count > 0 || openRows.Count > 0)
                foreach (var card in cards)
                {
                    var key = ModGroupStore.KeyFor(card.Name);
                    card.IsCardExpanded = openCards.Contains(key);
                    card.IsRowExpanded = openRows.Contains(key);
                }

            ApplyListMembership(cards);
            ApplyPins(cards);
            ApplyReuploads(cards, installRecords); // Fork
            ApplyPendingDownloads(cards, downloads);
            ApplyLeftovers(cards);
            ModConflicts.Apply(cards, conflicts);
            ConflictCountLabel = conflicts.Count == 0 ? null : Strings.Installed_ConflictCount(conflicts.Count, conflicts.Count);
            if (conflicts.Count > 0)
            {
                AppLog.Info("Conflicts", string.Join("; ", conflicts.Select(c =>
                    $"{c.Kind} {c.Identifier}: {string.Join(", ", c.Members.Select(m => m.Entry.Name).Distinct())}")));
            }
            ApplyBadgeVisibility();
            _dependencies = dependencies;

            // Set before the cards are subscribed to below, so filling it in doesn't read as a
            // card changing under the page.
            foreach (var card in _all)
            {
                card.HasDependencies = card.Entries.Any(e => dependencies.DependenciesOf(e).Count > 0);
                card.MissingDependencyNote = MissingDependencyNote(card, dependencies, loadedGuids, catalog);
            }

            RebuildCategoryOptions();

            _cardByEntry = [];
            foreach (var card in _all)
            {
                card.PropertyChanged += OnCardPropertyChanged;
                foreach (var entry in card.Entries) _cardByEntry[entry] = card;
            }

            RefreshGroups();

            // A rescan replaces every card, so any previous selection is gone with them.
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedCountLabel));
            DisableSelectedCommand.NotifyCanExecuteChanged();
            EnableSelectedCommand.NotifyCanExecuteChanged();
            UnsubscribeSelectedCommand.NotifyCanExecuteChanged();
            UpdateSelectedCommand.NotifyCanExecuteChanged();
            AnnounceUpdates();
            OnPropertyChanged(nameof(AllSelected));
            OnPropertyChanged(nameof(SelectAllLabel));

            var unmatched = _all.Where(m => m.ModId is null).Select(m => m.Name).ToList();
            AppLog.Info("Installed",
                $"scanned {scanned.Count} folder(s) -> {_all.Count} card(s); " +
                $"{_all.Count(m => m.IsAppManaged)} app-managed, {_all.Count(m => m.IsDisabled)} disabled, " +
                $"{unmatched.Count} unmatched; on page {CurrentPage} of {TotalPages}");
            if (unmatched.Count > 0) AppLog.Debug("Installed", $"unmatched: {string.Join(", ", unmatched)}");

            var mixed = _all.Where(m => m.IsMixedState).Select(m => m.Name).ToList();
            if (mixed.Count > 0)
                AppLog.Warn("Installed", $"present in both an enabled and a disabled folder: {string.Join(", ", mixed)}");

            ApplyFilter();
            RefreshActiveView(CurrentPage);

            StatusMessage = _all.Count == 0
                ? Text(Strings.Installed_NoModsFoundFormat, installPath)
                : DescribeCounts();
            if (offline) StatusMessage = Sentences(StatusMessage, Strings.Installed_CatalogUnavailable);
        }
        finally
        {
            IsBusy = false;
        }

        if (_forPage) await OfferDownloadConfirmationsAsync();
    }

    //
    // Monitor mode, §7: every pending download checked against this scan. Runs on the scan's
    // background thread, since the size check is one stat per file the archive would place.
    //
    // A download this app has since installed itself - its record is app-managed at the same
    // version - is settled here without asking: there is nothing for the user to confirm.
    //
    private static List<PendingDownload> MatchDownloads(
        IReadOnlyList<InstalledMod> scanned, string installPath, IReadOnlyList<InstalledModRecord> records)
    {
        var pending = AppServices.DownloadLedger.Pending();
        if (pending.Count == 0) return [];

        var folders = DownloadMatcher.FolderNames(scanned);
        var results = new List<PendingDownload>();

        foreach (var download in pending)
        {
            var record = records.FirstOrDefault(r => r.ModId == download.ModId && r.IsAddon == download.IsAddon);
            if (record is { IsAppManaged: true, Incomplete: false }
                && string.Equals(record.Version, download.Version, StringComparison.Ordinal))
            {
                AppServices.DownloadLedger.SetState(
                    download.ModId, download.IsAddon, download.Version, DownloadState.Confirmed);
                continue;
            }

            var match = DownloadMatcher.Check(download, installPath, folders);
            if (match.Kind != DownloadMatchKind.Absent) results.Add(new PendingDownload(download, match));
        }

        AppLog.Debug("Monitor",
            $"{pending.Count} pending download(s): " +
            $"{results.Count(r => r.Match.Kind == DownloadMatchKind.Installed)} look installed, " +
            $"{results.Count(r => r.Match.Kind == DownloadMatchKind.Partial)} partly");

        return results;
    }

    //
    // Ties each matched download to its card: by listing id first, and by folder when the card
    // resolved to a different listing (or none) - the folder is what the download named.
    //
    private static void ApplyPendingDownloads(
        IReadOnlyList<InstalledModCardViewModel> cards, IReadOnlyList<PendingDownload> downloads)
    {
        foreach (var card in cards) card.PendingDownload = null;

        foreach (var download in downloads)
        {
            var d = download.Download;
            var expected = d.ExpectedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var card = cards.FirstOrDefault(c => c.ModId == d.ModId && c.IsAddon == d.IsAddon)
                ?? cards.FirstOrDefault(c => c.Entries.Any(e => !e.IsDisabled && expected.Overlaps(
                    DownloadMatcher.FolderNames([e]))));

            if (card is not null) card.PendingDownload = download;
        }
    }

    //
    // R2's default: one prompt per scan listing every download that now looks installed, each with
    // a tick box. Ticked ones are confirmed, unticked ones dismissed for that version; "Not now"
    // leaves them all pending and doesn't ask again this session. The quiet setting skips the
    // prompt, and the card's own Confirm install button does the same job one mod at a time.
    //
    private async Task OfferDownloadConfirmationsAsync()
    {
        if (new SettingsService().Load().Monitor.DownloadConfirmation != DownloadConfirmation.Ask) return;

        var offered = _all
            .Where(c => c.CanConfirmDownload)
            .Select(c => c.PendingDownload!)
            .Where(p => !_deferredDownloads.Contains(Key(p.Download)))
            .GroupBy(p => Key(p.Download))
            .Select(g => g.First())
            .ToList();

        if (offered.Count == 0) return;

        var answer = DownloadConfirmWindow.Ask(offered.Select(p => p.Download).ToList());

        if (answer is null)
        {
            foreach (var p in offered) _deferredDownloads.Add(Key(p.Download));
            AppLog.Info("Monitor", $"confirm prompt deferred for {offered.Count} download(s)");
            return;
        }

        foreach (var p in offered)
        {
            var d = p.Download;

            if (answer.Contains(d))
            {
                AppServices.InstallManifest.ConfirmDownload(d, AppServices.SptEnvironment.InstallPath);
                AppServices.DownloadLedger.SetState(d.ModId, d.IsAddon, d.Version, DownloadState.Confirmed);
            }
            else
            {
                AppServices.DownloadLedger.SetState(d.ModId, d.IsAddon, d.Version, DownloadState.Dismissed);
            }
        }

        AppLog.Info("Monitor", $"confirmed {answer.Count} of {offered.Count} download(s) from the prompt");

        await ScanAsync();

        if (answer.Count > 0) StatusMessage = Strings.Installed_DownloadsConfirmed(answer.Count);
    }

    private static (int, bool, string) Key(DownloadedModRecord d) => (d.ModId, d.IsAddon, d.Version);

    // The card's own Confirm install - the quiet setting's way in, and always available on a card
    // whose download looks installed.
    [RelayCommand]
    private async Task ConfirmDownloadAsync(InstalledModCardViewModel? mod)
    {
        if (mod?.PendingDownload is not { Match.Kind: DownloadMatchKind.Installed } pending) return;

        var d = pending.Download;
        AppServices.InstallManifest.ConfirmDownload(d, AppServices.SptEnvironment.InstallPath);
        AppServices.DownloadLedger.SetState(d.ModId, d.IsAddon, d.Version, DownloadState.Confirmed);

        AppLog.Info("Monitor", $"confirmed {d.Name} {d.Version} from its card");

        await ScanAsync();

        StatusMessage = Text(Strings.Installed_DownloadConfirmedFormat, d.Name, d.Version);
    }

    // What each installed mod has for a later SPT release - see SptUpgradeReport.
    [RelayCommand]
    private async Task CheckSptUpgradeAsync()
    {
        try
        {
            await AppServices.SptCatalog.EnsureLoadedAsync();
            await AppServices.ModCache.EnsureLoadedAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            StatusMessage = Strings.Upgrade_Offline;
            return;
        }

        var installed = _all
            .Where(c => !c.IsAddon)
            .Select(c => new SptUpgradeInput(c.DisplayTitle, c.ModId is > 0 ? c.ModId : null, c.InstalledVersion))
            .ToList();

        Views.SptUpgradeWindow.Open(installed, AppServices.ModCache.AllMods, AppServices.SptCatalog.Releases,
            AppServices.SptEnvironment.InstalledVersion);
    }

    //
    // Installs an archive the user already has (see LocalArchive), through the download queue like
    // any other install - matched to its sp-mod.com listing by what is inside it when it can be.
    //
    [RelayCommand]
    private async Task InstallFromFileAsync()
    {
        if (AppServices.SptEnvironment.InstallPath is not { Length: > 0 })
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Installed_InstallFromFileTitle,
            Filter = Strings.Installed_InstallFromFileFilter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        // Whatever it is called: the picker's "All files" is there for an archive named oddly.
        await InstallArchivesAsync([dialog.FileName], anyName: true);
    }

    /// <summary>The archive types Install from file takes - the same as its file picker's filter.</summary>
    public static bool IsModArchive(string path) =>
        ArchiveExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz"];

    // One archive at a time: each asks its own question, and a second drop while the first is
    // still being looked into waits its turn rather than asking over it.
    private readonly SemaphoreSlim _fromFile = new(1, 1);

    /// <summary>Installs archives the user already has, one after another, each asked about first
    /// - what Install from file does, for files dropped on the page.</summary>
    public async Task InstallArchivesAsync(IReadOnlyList<string> files, bool anyName = false)
    {
        if (AppServices.SptEnvironment.InstallPath is not { Length: > 0 } installPath)
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var archives = files.Where(f => File.Exists(f) && (anyName || IsModArchive(f))).ToList();
        if (archives.Count == 0)
        {
            StatusMessage = Strings.Installed_DropNotArchive;
            return;
        }

        await _fromFile.WaitAsync();
        try
        {
            foreach (var file in archives) await InstallArchiveAsync(file, installPath);
        }
        finally
        {
            _fromFile.Release();
        }
    }

    private async Task InstallArchiveAsync(string file, string installPath)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        StatusMessage = Text(Strings.Installed_LocalReadingFormat, Path.GetFileName(file));

        // Off the UI thread: a .7z or .rar is extracted to be looked into, which can take a while.
        LocalArchiveContents contents;
        try
        {
            contents = await Task.Run(() => LocalArchive.InspectAsync(file));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Install", $"couldn't read {file}: {ex.Message}");
            StatusMessage = Text(Strings.Installed_LocalUnreadableFormat, Path.GetFileName(file), ex.Message);
            return;
        }

        if (!contents.Recognised)
        {
            StatusMessage = Text(Strings.Installed_LocalNotRecognisedFormat, Path.GetFileName(file));
            return;
        }

        var catalogKnown = true;
        try
        {
            await AppServices.ModCache.EnsureLoadedAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ex is TaskCanceledException)
        {
            catalogKnown = false;
        }

        var match = catalogKnown ? LocalArchive.MatchIn(contents, AppServices.ModCache.AllMods) : null;
        var listings = catalogKnown ? LocalArchive.ListingsIn(contents, AppServices.ModCache.AllMods) : [];
        var version = LocalArchive.VersionOf(contents, match) ?? stem;
        var name = match?.Name ?? stem;

        //
        // Recorded under a local id only when that is what the user chose knowing why: a pack of
        // listed mods loses each one's page and updates that way, and with the site unreachable it
        // could not be looked up at all.
        //
        string question;
        if (match is not null)
            question = Text(Strings.Installed_LocalConfirmMatchedFormat, Path.GetFileName(file), name, version);
        else if (!catalogKnown)
            question = Text(Strings.Installed_LocalConfirmOfflineFormat, Path.GetFileName(file), name, version);
        else if (listings.Count > 1)
            question = Text(Strings.Installed_LocalConfirmPackFormat, Path.GetFileName(file), TextLists.Join([.. listings.Select(l => l.Name ?? l.Id.ToString())]), name);
        else
            question = Text(Strings.Installed_LocalConfirmUnmatchedFormat, Path.GetFileName(file), name, version);

        if (TCFModManager.App.Views.SteamMessageBox.Show(question, Strings.Installed_InstallFromFileTitle, MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes)
        {
            StatusMessage = null;
            return;
        }

        var target = match is null
            ? new InstallTarget(LocalArchive.IdFor(contents, name), false, name,
                contents.Plugins.Select(p => p.Guid).FirstOrDefault(), null, null)
            : InstallTarget.For(match);
        var published = match?.Versions?.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
        var modVersion = new ModVersion { Id = published?.Id ?? 0, Version = version, Link = LocalArchive.LinkFor(file) };

        AppServices.DownloadQueue.Enqueue(
            target,
            version,
            installPath,
            () => Task.FromResult<ModVersion?>(modVersion),
            checkDependencies: match is not null,
            totalBytes: new FileInfo(file).Length);

        StatusMessage = Text(Strings.Installed_LocalQueuedFormat, name);
    }

    /// <summary>Removes a mod from the install: the manifest-precise ModInstallService.UninstallAsync path
    /// for anything IsAppManaged, or RemoveHandInstalled (move the mod's whole folder into holding) otherwise. Both paths
    /// confirm first.</summary>
    [RelayCommand]
    private async Task RemoveAsync(InstalledModCardViewModel? mod)
    {
        if (mod is not null) await RemoveOneAsync(mod);
    }

    /// <summary>What RemoveCommand does, answering with what it said - the item page and the
    /// right-click menu show that. Null when it was cancelled.</summary>
    public async Task<string?> RemoveOneAsync(InstalledModCardViewModel mod)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return StatusMessage = AppMessages.NoSptInstallFolder;

        mod = await OwnCardAsync(mod);

        // A disabled mod's install record still points at the folders it was installed into, which
        // it no longer occupies - so removal would delete nothing and report success. Enabling it
        // first puts those paths back where the record expects them.
        if (mod.IsDisabled) return StatusMessage = Text(Strings.Installed_RemoveDisabledFirstFormat, mod.DisplayTitle);

        // Installed items that use this one, for the question (or, unasked, the check) to name.
        var needed = NeededBy([mod]);

        ConfigAction configAction;
        if (mod.IsAppManaged && mod.ModId is { } modId)
        {
            var manifest = AppServices.InstallManifest.Load();
            var record = manifest.Find(modId, mod.IsAddon);
            if (record is null) return StatusMessage = Text(Strings.Installed_RemoveNoRecordFormat, mod.Name);

            //
            // Counted with the mod's own entry in hand, so a mod that keeps settings somewhere
            // unconventional - or whose presets this app never installed - is counted the same way the
            // removal itself will treat them.
            //
            var options = new ModConfigOptionsStore().Effective();

            var configs = ModConfigFiles.InRecord(record, options)
                .Concat(ModConfigFiles.UserDataOnDisk(installPath, record, options))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            //
            // 1.19: a record made before v1.19.0 doesn't say which install it came from (D17). When
            // the app doesn't sit inside this install either, nothing proves it belongs here - so the
            // install is named before anything happens.
            //
            var body = record.InstallPath is null && !InstallStamp.AppIsInside(installPath)
                ? Sentences(Strings.Installed_RemoveAppManagedBody, Text(Strings.Installed_RemoveUnstampedFormat, installPath))
                : Strings.Installed_RemoveAppManagedBody;

            // Asked even with the question switched off: nothing else says which install it goes from.
            var unstamped = record.InstallPath is null && !InstallStamp.AppIsInside(installPath);

            if (ConfirmRemoval(mod.DisplayTitle, body, configs.Count, needed, mustAsk: unstamped, changesProfile: ChangesProfile(mod))
                is not { } answer)
            {
                return null;
            }

            configAction = answer;
        }
        else
        {
            var paths = LegacyPaths(mod);
            if (paths.Count == 0) return StatusMessage = Text(Strings.Installed_RemoveNoFolderFormat, mod.Name);

            //
            // 1.19: every folder is checked before the user is asked and before anything moves, so a
            // mod with one acceptable folder and one refused one is left whole rather than half removed.
            //
            foreach (var path in paths)
            {
                if (InstallPathGuard.CheckModFolder(installPath, path) is { } refusal)
                    return StatusMessage = ModInstallProblems.RemovalRefused(path, refusal);
            }

            var configs = ModInstallService.FindLegacyConfigs(installPath, paths);
            if (ConfirmRemoval(
                    mod.DisplayTitle,
                    Text(Strings.Installed_RemoveLegacyBodyFormat, string.Join("\n", paths)),
                    configs.Count,
                    needed,
                    changesProfile: ChangesProfile(mod)) is not { } answer)
            {
                return null;
            }

            configAction = answer;
        }

        // Unasked, the check a removal of several makes still comes first when it has something to
        // say: other installed items that use this one, or folders a hand-installed item's removal
        // deletes - the question would have listed both.
        if (!new SettingsService().Load().ConfirmUnsubscribe && !CheckBeforeRemoving([mod])) return null;

        string said;
        IsBusy = true;
        try
        {
            said = (await RemoveConfirmedAsync(mod, installPath, configAction)).Message;
            ModRemoved?.Invoke(this, EventArgs.Empty);
        }
        catch (ModInstallException ex)
        {
            // SPT or its server is running - the message names what to close.
            said = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            said = Text(Strings.Installed_RemoveFailedFormat, mod.Name, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        // Said after the rescan, which puts the page's count in the status line: said before it,
        // "Removed X." was replaced before it could be read.
        await ScanAsync();
        return StatusMessage = said;
    }

    //
    // The card as a fresh scan has it. The item page and the right-click menu hand in the card from
    // Browse's scan, which is not redone when a mod is set aside or brought back - so it can say a
    // disabled mod is enabled (and removing it would delete nothing but its record), and its files
    // are not the ones the dependency check here knows. Scanned again, then matched by folder, or -
    // for one set aside or brought back since, whose folder moved - by what it is.
    //
    private async Task<InstalledModCardViewModel> OwnCardAsync(InstalledModCardViewModel mod)
    {
        if (_all.Contains(mod)) return mod;

        await ScanAsync();

        return _all.FirstOrDefault(c =>
                   c.IsAddon == mod.IsAddon
                   && string.Equals(c.FolderPath, mod.FolderPath, StringComparison.OrdinalIgnoreCase))
               ?? _all.FirstOrDefault(c =>
                   c.IsAddon == mod.IsAddon
                   && c.ModId == mod.ModId
                   && string.Equals(c.Name, mod.Name, StringComparison.OrdinalIgnoreCase))
               ?? mod;
    }

    // A hand-installed mod's folders - what removing it deletes.
    private static List<string> LegacyPaths(InstalledModCardViewModel mod) =>
        new[] { mod.ClientFolderPath, mod.ServerFolderPath }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

    //
    // Removes one mod once the removal has been agreed to: the manifest-precise uninstall for what
    // this app installed, the mod's folders for anything else. Returns whether it went, what to
    // say, and whether that is more than "Removed X." (files left behind, configs kept aside, or
    // nothing to remove); throws what the install service throws (SPT running) and file errors,
    // for the caller to report.
    //
    private static async Task<(bool Removed, string Message, bool Notable)> RemoveConfirmedAsync(
        InstalledModCardViewModel mod, string installPath, ConfigAction configAction)
    {
        // Checked here, after every question: a download may have reached its install while one was open.
        if (InstallingNow() is { } busy) return (false, Text(Strings.Installed_WaitForInstallFormat, busy), true);

        if (mod.IsAppManaged && mod.ModId is { } modId)
        {
            var record = AppServices.InstallManifest.Load().Find(modId, mod.IsAddon);
            if (record is null) return (false, Text(Strings.Installed_RemoveNoRecordFormat, mod.Name), true);

            // Off the UI thread: deleting a large mod's files and copying the SPT profiles first.
            var result = await Task.Run(() => AppServices.ModInstall.UninstallAsync(installPath, record, configAction));
            return (
                true,
                DescribeRemoval(mod.Name, result.FailedFiles.Count, result.ConfigsKept, result.ConfigsFolder, result),
                result.FailedFiles.Count > 0 || result.ConfigsKept > 0 || result.KeptChanged.Count > 0
                    || result.KeptOwned.Count > 0 || result.RefusedFiles is { Count: > 0 } || result.FoldersLeft.Count > 0);
        }

        var paths = LegacyPaths(mod);
        if (paths.Count == 0) return (false, Text(Strings.Installed_RemoveNoFolderFormat, mod.Name), true);

        foreach (var path in paths)
        {
            if (InstallPathGuard.CheckModFolder(installPath, path) is { } refusal)
                return (false, ModInstallProblems.RemovalRefused(path, refusal), true);
        }

        // Checked before the configs move, so SPT running can't leave them moved out of a mod whose
        // folder then stays put.
        ModInstallService.EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        // Fork: the profiles as they were, as an app-installed mod's removal keeps them.
        await Task.Run(() => AppServices.ProfileBackups.BackupIfChanged(installPath, ProfileBackups.BeforeRemove)); // Fork (1.3.0): off the UI thread

        // Again: SPT may have been started while the copy was being taken.
        ModInstallService.EnsureInstallNotInUse(ModInstallAction.Remove, installPath);

        var configs = ModInstallService.FindLegacyConfigs(installPath, paths);
        var kept = configAction == ConfigAction.Keep && configs.Count > 0
            ? ModInstallService.KeepLegacyConfigs(installPath, configs, mod.Name)
            : new KeptConfigs(0, null);

        // 1.19: into the install's holding folder, where Undo can bring it back (D27, D28).
        var held = AppServices.ModInstall.RemoveHandInstalled([.. paths], installPath, mod.Name, kept.Folder);

        // A manually-confirmed version record would otherwise dangle, pointing at a mod that's no
        // longer on disk.
        if (mod.IsManualOverride && mod.ModId is { } overriddenModId)
            AppServices.InstallManifest.ClearManualVersion(overriddenModId, mod.IsAddon);

        return (true, DescribeRemoval(mod.Name, failedFiles: 0, kept.Count, kept.Folder, held: held is not null), kept.Count > 0);
    }

    /// <summary>Removes the installed mods with these sp-mod.com ids - a collection's Unsubscribe
    /// from all, when removing was chosen over setting aside. Asked about by the caller, and again
    /// here only when other installed mods use these or hand-installed folders would be deleted (what
    /// a single removal's confirmation lists). Their config files are kept (copied aside) as a single
    /// removal's Yes keeps them. Set-aside mods are left alone: their records point at folders they
    /// no longer occupy. Returns what to say, or null when the check was cancelled.</summary>
    public async Task<string?> RemoveModsAsync(IReadOnlySet<int> modIds)
    {
        if (_all.Count == 0) await ScanAsync();

        return await RemoveCardsAsync(_all.Where(c => c is { IsAddon: false, ModId: { } id } && modIds.Contains(id)).ToList());
    }

    //
    // Subscribed items' Unsubscribe selected: every ticked item, addons and hand-installed ones
    // included, removed as a collection's Unsubscribe from all removes - configs kept, the same
    // check first when other items use them or hand-installed folders would go. Asked about first
    // unless the unsubscribe question is off.
    //
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task UnsubscribeSelectedAsync()
    {
        var cards = SelectedCards();
        if (cards.Count == 0) return;

        // Set-aside ones are left alone (RemoveCardsAsync says so), so the question counts and names
        // only what goes - by name, since ticked items can be hidden by the filters since.
        var removing = cards.Where(c => !c.IsDisabled).ToList();

        System.Windows.Controls.CheckBox? dontAsk = null;
        var settingsService = new SettingsService();
        if (removing.Count > 0 && settingsService.Load().ConfirmUnsubscribe)
        {
            var body = new System.Windows.Controls.StackPanel { MaxWidth = 640 };
            body.Children.Add(Paragraph(Strings.Installed_UnsubscribeSelectedBody(removing.Count, removing.Count, AppPaths.LegacyConfigsDirectory)));
            // Each by its title - and, where two share one (a mod and a copy of it), the folder or
            // file each is installed as beside it.
            var repeated = removing
                .GroupBy(c => c.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            body.Children.Add(RemoveCheckDialog.List(
                removing.Select(c => (c.DisplayTitle,
                    repeated.Contains(c.DisplayTitle) ? Path.GetFileName(c.FolderPath) : "")).ToList(),
                monospace: false));
            body.Children.Add(Paragraph(Strings.Installed_UnsubscribeSelectedDisableHint, top: 12));
            dontAsk = AddDontAsk(body);

            var answer = SteamDialog.Show(
                Strings.Installed_UnsubscribeSelectedTitle(removing.Count, removing.Count),
                body,
                new SteamDialogChoice(Strings.Item_Unsubscribe, SteamDialogButton.Blue, IsDefault: true),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));
            if (answer != 0) return;
        }

        // Ticked and then cancelled - here or at the check after it - changes nothing.
        if (await RemoveCardsAsync(cards) is not null && dontAsk is not null)
            TurnOffQuestionIfTicked(settingsService, dontAsk);
    }

    // Before removing: other installed items that use these, and the folders removing hand-installed
    // ones deletes - asked about only when there is either. False when that was cancelled.
    private bool CheckBeforeRemoving(IReadOnlyList<InstalledModCardViewModel> targets)
    {
        var needed = NeededBy(targets);
        var handInstalled = targets.Where(c => !c.IsAppManaged).ToList();

        // Each folder beside the item it is, so a removal of several says whose folder goes.
        var folders = handInstalled.SelectMany(c => LegacyPaths(c).Select(path => (path, c.DisplayTitle))).ToList();

        return (needed.Count == 0 && folders.Count == 0)
            || RemoveCheckDialog.Ask(needed, folders, targets.Count, handInstalled.Count);
    }

    // Enabled installed items, outside these, that use one of them - and how.
    private List<(string Name, string Detail)> NeededBy(IReadOnlyList<InstalledModCardViewModel> targets) =>
        AffectedCards(targets.ToList(), disable: true)
            .Where(a => !a.Card.IsDisabled)
            .Select(a => (a.Card.DisplayTitle, a.Detail))
            .ToList();

    // What RemoveModsAsync and Unsubscribe selected share. Returns what to say, or null when the
    // check was cancelled.
    private async Task<string?> RemoveCardsAsync(List<InstalledModCardViewModel> cards)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return StatusMessage = AppMessages.NoSptInstallFolder;

        if (ModInstallService.RunningBlockers(installPath) is { Count: > 0 } blockers)
            return StatusMessage = ModInstallProblems.InstallInUse(blockers, ModInstallAction.Remove);

        var disabled = cards.Where(c => c.IsDisabled).ToList();

        var targets = cards.Except(disabled).ToList();

        // Fork: what sp-mod.com says may change the profile is named first, and can be left installed.
        if (CheckProfileMods(targets) is not { } kept) return null;
        targets = kept;

        if (!CheckBeforeRemoving(targets)) return null;

        //
        // 1.19 (D17): records made before 1.19.0 don't say which install they came from. When the app
        // doesn't sit inside this install either, the install is named before anything goes - asked
        // whatever the unsubscribe question is set to, since nothing else would say it.
        //
        if (!InstallStamp.AppIsInside(installPath))
        {
            var manifest = AppServices.InstallManifest.Load();
            var unstamped = targets
                .Where(c => c is { IsAppManaged: true, ModId: { } id } && manifest.Find(id, c.IsAddon) is { InstallPath: null })
                .Select(c => c.DisplayTitle)
                .ToList();

            if (unstamped.Count > 0 && !Confirm(
                    Text(Strings.Installed_UnsubscribeTitleFormat, TextLists.Join(unstamped)),
                    Text(Strings.Installed_RemoveUnstampedManyFormat, installPath, string.Join("\n", unstamped))))
            {
                return null;
            }
        }

        var removed = 0;
        var problems = new List<string>();

        IsBusy = true;
        try
        {
            foreach (var card in targets)
            {
                try
                {
                    var outcome = await RemoveConfirmedAsync(card, installPath, ConfigAction.Keep);
                    if (outcome.Removed) removed++;
                    if (outcome.Notable) problems.Add(outcome.Message);
                }
                catch (ModInstallException ex)
                {
                    problems.Add(ModInstallProblems.Describe(ex));

                    // SPT started meanwhile stops the rest; a reason of this one's own (1.19: a record
                    // from another install, a refused folder) leaves the others to go ahead.
                    if (ex.Reason == ModInstallFailure.InstallInUse) break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    problems.Add(Text(Strings.Installed_RemoveFailedFormat, card.Name, ex.Message));
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (removed > 0) ModRemoved?.Invoke(this, EventArgs.Empty);
        await ScanAsync();

        var said = new List<string> { Strings.Installed_RemovedMany(removed, removed) };
        if (disabled.Count > 0)
            said.Add(Strings.Installed_RemoveSkippedDisabled(disabled.Count, disabled.Count, TextLists.Join(disabled.Select(c => c.DisplayTitle).ToList())));
        said.AddRange(problems);

        StatusMessage = string.Join(Strings.Common_SentenceSeparator, said);
        return StatusMessage;
    }

    /// <summary>Opens the mod details/update dialog for whichever card was clicked. Routed through
    /// AppServices.ModUpdateOverlay since only MainWindow owns the dialog presenter. Always rescans once
    /// the dialog closes, staying on the current page.</summary>
    [RelayCommand]
    private async Task ShowDetailsAsync(InstalledModCardViewModel? mod)
    {
        if (mod is null) return;
        if (AppServices.ModUpdateOverlay.ShowAsync is not { } show) return;

        // Only when the dialog actually did something. A rescan re-reads the whole install and
        // replaces every card, which visibly rebuilds the page - not something opening a mod to
        // read its changelog should cause.
        if (await show(mod)) await ScanAsync();
    }

    //
    // Opens a mod's folder in Explorer. The path comes from the card rather than being rebuilt
    // here, so it is the folder the scan actually found - including the disabled location, if the
    // mod is currently disabled.
    //
    [RelayCommand]
    private void OpenModFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        // Checked rather than assumed: a folder removed or renamed outside the app since the last
        // scan would otherwise open an Explorer error the user has to dismiss.
        if (!Directory.Exists(path))
        {
            StatusMessage = Text(Strings.Installed_FolderGoneFormat, path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Installed", $"couldn't open {path}: {ex.Message}");
            StatusMessage = Strings.Common_FolderOpenFailed;
        }
    }

    /// <summary>Flips one mod between enabled and disabled.</summary>
    [RelayCommand]
    private Task ToggleDisableAsync(InstalledModCardViewModel? mod) =>
        mod is null ? Task.CompletedTask : ApplyDisableAsync([mod], !mod.IsDisabled);

    [RelayCommand]
    private Task DisableGroupAsync(ModGroupSectionViewModel? section) =>
        section is null ? Task.CompletedTask : ApplyDisableAsync(section.Items.ToList(), disable: true);

    [RelayCommand]
    private Task EnableGroupAsync(ModGroupSectionViewModel? section) =>
        section is null ? Task.CompletedTask : ApplyDisableAsync(section.Items.ToList(), disable: false);

    /// <summary>Disables everything currently enabled in a group and enables everything currently
    /// disabled in it, as one undoable step.</summary>
    [RelayCommand]
    private async Task InvertGroupAsync(ModGroupSectionViewModel? section)
    {
        if (section is null) return;

        var toDisable = section.Items.Where(m => !m.IsDisabled).ToList();
        var toEnable = section.Items.Where(m => m.IsDisabled).ToList();

        // The disable half is the one that can break other mods, so it asks first; the enable half
        // then runs without a second prompt and its moves are merged into the same undo step.
        var label = (nameof(Strings.Installed_UndoInvertFormat), (object?)section.Name);

        var moves = await ApplyDisableAsync(toDisable, disable: true, label: label);
        if (moves is null) return;

        await ApplyDisableAsync(toEnable, disable: false, label: label, confirm: false, carryOver: moves);
    }

    //
    // Settles a mod found in both a container and its ".disabled" sibling. The user picks which
    // copy to keep; the other is moved into a hidden folder in the install rather than deleted, so
    // a wrong answer costs nothing but a drag back.
    //
    [RelayCommand]
    private async Task ResolveDuplicateAsync(InstalledModCardViewModel? mod)
    {
        if (mod is not { HasDuplicateFolders: true }) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        if (ModInstallService.RunningBlockers(installPath) is { Count: > 0 } blockers)
        {
            StatusMessage = ModInstallProblems.InstallInUse(blockers, ModInstallAction.SortOutDuplicate);
            return;
        }

        if (InstallingNow() is { } busy)
        {
            StatusMessage = Text(Strings.Installed_WaitForInstallFormat, busy);
            return;
        }

        var pairs = mod.DuplicateFolders;
        var folders = string.Join("\n", pairs.SelectMany(p => new[] { p.Enabled.FolderPath, p.Disabled.FolderPath }));

        var answer = TCFModManager.App.Views.SteamMessageBox.Show(
            Text(Strings.Installed_DuplicateBodyFormat, mod.DisplayTitle, folders),
            Text(Strings.Installed_DuplicateTitleFormat, mod.DisplayTitle),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (answer is not (MessageBoxResult.Yes or MessageBoxResult.No)) return;

        var keepEnabled = answer == MessageBoxResult.Yes;
        var moves = new List<ModMove>();
        var failed = new List<ModDisableFailure>();

        IsBusy = true;
        try
        {
            var timestamp = DateTimeOffset.UtcNow;
            foreach (var pair in pairs)
            {
                var outcome = ModDisableService.ResolveDuplicate(installPath, pair, keepEnabled, timestamp);
                moves.AddRange(outcome.Moved);
                failed.AddRange(outcome.Failed);
            }
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SetLastMoves(moves, (nameof(Strings.Installed_UndoSortOutFormat), mod.DisplayTitle));

        var message = Text(
            keepEnabled
                ? Strings.Installed_DuplicateKeptEnabledFormat
                : Strings.Installed_DuplicateKeptDisabledFormat,
            mod.DisplayTitle);
        if (failed.Count > 0) message = Sentences(message, DescribeFailures(failed));

        await ScanAsync();
        StatusMessage = message;
    }

    private bool HasSelection() => SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DisableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: true);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task EnableSelectedAsync() => ApplyDisableAsync(SelectedCards(), disable: false);

    //
    // True when at least one selected mod could actually be updated. Deliberately stricter than
    // HasSelection: offering an enabled Update button that then reports "nothing to update" is
    // worse than a greyed-out one, and the reasons a mod is excluded are explained when it runs.
    //
    private bool HasUpdatableSelection() => SelectedCards().Any(IsUpdatable);

    // The keys of whichever mods a view currently has open.
    private bool CanToggleAllExpanded => ShowExpanders && _filtered.Count > 0;

    //
    // Whether the two buttons' work is already done, which is what tints them. Same rule as the
    // view switcher and Multi select: Primary means the page is already in that state.
    //
    // Neither is simply the other's negation - a half-open list is neither, and both sit Secondary.
    //
    public bool AllExpanded => _filtered.Count > 0 && _filtered.All(IsOpen);

    public bool AllCollapsed => _filtered.Count > 0 && !_filtered.Any(IsOpen);

    // Cards and List keep separate expansion state, so "open" means whichever one is on screen -
    // the same choice SetAllExpanded makes when it writes them.
    private bool IsOpen(InstalledModCardViewModel card) =>
        ShowCards ? card.IsCardExpanded : card.IsRowExpanded;

    private void NotifyExpandedState()
    {
        OnPropertyChanged(nameof(AllExpanded));
        OnPropertyChanged(nameof(AllCollapsed));
    }

    [RelayCommand(CanExecute = nameof(CanToggleAllExpanded))]
    private void ExpandAll() => SetAllExpanded(true);

    [RelayCommand(CanExecute = nameof(CanToggleAllExpanded))]
    private void CollapseAll() => SetAllExpanded(false);

    //
    // Opens or closes every mod the filters currently match - not just the page on screen. Paging
    // forward to find half of them shut again would make "expand all" a lie.
    //
    // Only the view being looked at: the two keep their own expansion state, so opening every card
    // shouldn't quietly open every row in List as well.
    //
    private void SetAllExpanded(bool expanded)
    {
        foreach (var card in _filtered)
        {
            if (ShowCards) card.IsCardExpanded = expanded;
            else card.IsRowExpanded = expanded;
        }
    }

    private static HashSet<string> OpenKeys(
        IEnumerable<InstalledModCardViewModel> cards,
        Func<InstalledModCardViewModel, bool> isOpen) =>
        cards.Where(isOpen)
            .Select(m => ModGroupStore.KeyFor(m.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsUpdatable(InstalledModCardViewModel card) =>
        card is { UpdateAvailable: true, IsDisabled: false, IsAddon: false, ModId: not null }
        && card.LatestPublishedVersion is not null
        && !IsHeldBack(card);

    // sp-mod.com holds back the very version this update would install. A different, newer version
    // being held back leaves this one alone.
    private static bool IsHeldBack(InstalledModCardViewModel card) =>
        AppServices.HeldBack.Holds(card.ModId, card.UpdateVersion ?? card.LatestPublishedVersion);

    // After each scan: ask sp-mod.com about the whole install, then mark what it holds back.
    private async Task CheckHeldBackAsync(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        var installed = cards
            .Where(c => c is { IsAddon: false, ModId: > 0 } && !string.IsNullOrWhiteSpace(c.InstalledVersion))
            .Select(c => (c.ModId!.Value, c.InstalledVersion!))
            .DistinctBy(c => c.Item1)
            .ToList();

        await AppServices.HeldBack.RefreshAsync(installed, AppServices.SptEnvironment.InstalledVersion);
        ApplyHeldBack();
    }

    private void ApplyHeldBack()
    {
        foreach (var card in _all)
        {
            card.HeldBackNote = card.IsAddon ? null : AppServices.HeldBack.Note(card.ModId);
            card.NotForSptNote = card.IsAddon ? null : AppServices.HeldBack.NotForSptNote(card.ModId, card.InstalledVersion);
        }

        AnnounceUpdates();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    //
    // Updates every selected mod that has one waiting, in a single pass.
    //
    // The prompts are asked ONCE for the whole batch rather than once per mod - the same reasoning
    // as applying a mod list: per-mod modals over a long selection train people into turning the
    // gate off globally, which is strictly worse for them than one demanding prompt.
    //
    [RelayCommand(CanExecute = nameof(HasUpdatableSelection))]
    private void UpdateSelected() => UpdateCards(SelectedCards());

    /// <summary>How many installed mods have an update this page can apply - the count on Update all.</summary>
    public int UpdatableCount => _all.Count(IsUpdatable);

    public bool HasUpdates => UpdatableCount > 0;

    public string UpdateAllLabel => Strings.Installed_UpdateAll(UpdatableCount);

    private bool CanUpdateAll() => HasUpdates;

    //
    // Every mod with an update, without selecting them first - the same pass as Update selected,
    // over the whole install. The prompts are still asked once for the lot.
    //
    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private void UpdateAll() => UpdateCards(_all);

    private void AnnounceUpdates()
    {
        MarkUpdatableCards(); // Fork: each card's own Update (InstalledViewModel.Toolbar.cs)
        OnPropertyChanged(nameof(UpdatableCount));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdateAllLabel));
        UpdateAllCommand.NotifyCanExecuteChanged();
    }

    private void UpdateCards(IReadOnlyList<InstalledModCardViewModel> selected)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var targets = selected.Where(IsUpdatable).ToList();

        // Held back by sp-mod.com: said, and left for the mod's own update dialog.
        var heldBack = selected.Where(c => c is { UpdateAvailable: true, IsDisabled: false } && IsHeldBack(c)).ToList();
        var heldBackNote = heldBack.Count == 0
            ? null
            : Strings.Installed_UpdatesHeldBack(heldBack.Count, heldBack.Count, TextLists.Join(heldBack.Select(c => c.DisplayTitle).ToList()));

        if (targets.Count == 0)
        {
            StatusMessage = heldBackNote ?? DescribeNothingToUpdate(selected);
            return;
        }

        // Resolved against the cached catalog before anything is asked, so a mod the catalog can no
        // longer identify is reported now rather than failing halfway through the batch.
        var resolved = new List<(InstalledModCardViewModel Card, Mod Mod, string Version)>();
        var unmatched = new List<string>();

        foreach (var card in targets)
        {
            var match = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == card.ModId);
            var version = match is null
                ? null
                : ModCardViewModel.PickDisplayVersion(match, AppServices.SptEnvironment.InstalledVersion)?.Version;

            if (match is null || version is null) unmatched.Add(card.DisplayTitle);
            else resolved.Add((card, match, version));
        }

        if (resolved.Count == 0)
        {
            StatusMessage = Text(Strings.Installed_UpdateNotInCatalogFormat, TextLists.Join(unmatched));
            return;
        }

        // Asked and queued the way every Update all is - see ModUpdates.
        var (said, queued) = ModUpdates.Queue(resolved
            .Select(r => new UpdateTarget(r.Card.DisplayTitle, r.Mod, r.Version, r.Card.IsAppManaged))
            .ToList());
        if (!queued)
        {
            StatusMessage = said;
            return;
        }

        StatusMessage = said + DescribeSkipped(selected, resolved.Count, unmatched);
        if (heldBackNote is not null) StatusMessage = string.Join(Strings.Common_SentenceSeparator, StatusMessage, heldBackNote);

        AppLog.Info("Installed", $"queued {resolved.Count} update(s) from a selection of {selected.Count}");
    }

    //
    // Why a selection that looked updatable produced nothing. Each reason is separate because they
    // need different things done about them - enabling a mod, opening its parent, or a rescan.
    //
    private static string DescribeNothingToUpdate(IReadOnlyList<InstalledModCardViewModel> selected)
    {
        if (selected.Count == 0) return Strings.Installed_NothingSelected;

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });
        var addons = selected.Count(c => c is { UpdateAvailable: true, IsAddon: true });

        var reasons = new List<string>();

        if (disabled > 0) reasons.Add(Strings.Installed_ReasonDisabled(disabled));
        if (addons > 0) reasons.Add(Strings.Installed_ReasonAddon(addons));

        return reasons.Count == 0
            ? Strings.Installed_NoUpdatesAvailable
            : Text(
                Strings.Installed_NoUpdatesReasonsFormat,
                string.Join(Strings.Common_ListSeparator, reasons));
    }

    private static string DescribeSkipped(
        IReadOnlyList<InstalledModCardViewModel> selected,
        int queued,
        IReadOnlyList<string> unmatched)
    {
        var parts = new List<string>();

        var disabled = selected.Count(c => c is { UpdateAvailable: true, IsDisabled: true });
        var addons = selected.Count(c => c is { UpdateAvailable: true, IsAddon: true });
        var noUpdate = selected.Count(c => c.UpdateAvailable != true);

        if (noUpdate > 0) parts.Add(Strings.Installed_SkippedUpToDate(noUpdate));
        if (disabled > 0) parts.Add(Strings.Installed_SkippedDisabled(disabled));
        if (addons > 0) parts.Add(Strings.Installed_SkippedAddon(addons));
        if (unmatched.Count > 0) parts.Add(Strings.Installed_SkippedNotFound(unmatched.Count));

        return parts.Count == 0
            ? string.Empty
            : Text(Strings.Installed_SkippedFormat, string.Join(Strings.Common_ListSeparator, parts));
    }

    //
    // No CanExecute, deliberately. Gating this on "something is still unticked" turned the button
    // off at exactly the moment AllSelected turns it Primary, and a disabled button takes the
    // theme's disabled fill rather than the accent - so the tint would never once have been seen.
    // Pressing it again when everything is already ticked is a no-op, which is a smaller cost than
    // a state the UI cannot show.
    //
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var mod in _filtered) mod.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var mod in _all) mod.IsSelected = false;
    }

    /// <summary>Puts the last disable/enable back. Fails softly per mod if anything moved on disk since.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_lastMoves.Count == 0) return;

        IsBusy = true;
        string message;
        try
        {
            var outcome = ModDisableService.Revert(_lastMoves, AppServices.SptEnvironment.InstallPath);
            var put = Strings.Installed_Undone(outcome.Moved.Count);
            message = outcome.Failed.Count == 0 ? put : Sentences(put, DescribeFailures(outcome.Failed));
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        SetLastMoves([], null);
        await ScanAsync();
        StatusMessage = message;
        ModsMoved?.Invoke(this, EventArgs.Empty);
    }

    private List<InstalledModCardViewModel> SelectedCards() => _all.Where(m => m.IsSelected).ToList();

    /// <summary>Sets aside (disables) the installed mods with these sp-mod.com ids, through the same
    /// path Disable takes here - it asks about anything else they take with them - scanning first
    /// if this page has not yet. Returns what it said. A collection's Unsubscribe from all.</summary>
    public async Task<string?> DisableModsAsync(IReadOnlySet<int> modIds)
    {
        if (_all.Count == 0) await ScanAsync();

        var cards = _all.Where(c => c is { IsAddon: false, ModId: { } id } && modIds.Contains(id)).ToList();
        await ApplyDisableAsync(cards, disable: true);
        return StatusMessage;
    }

    //
    // The one path every disable/enable goes through: works out what else the change reaches, asks
    // about it, moves the folders, then rescans. Returns the moves it made so a caller running two
    // passes (InvertGroupAsync) can merge them into one undo step, or null when nothing happened.
    //
    private async Task<List<ModMove>?> ApplyDisableAsync(
        IReadOnlyList<InstalledModCardViewModel> cards,
        bool disable,
        (string Key, object? Arg)? label = null,
        bool confirm = true,
        List<ModMove>? carryOver = null)
    {
        var targets = cards.Where(c => c.IsDisabled != disable).ToList();
        if (targets.Count == 0)
        {
            if (carryOver is null)
                StatusMessage = disable ? Strings.Installed_NothingToDisable : Strings.Installed_NothingToEnable;

            return carryOver ?? [];
        }

        // Moving a mod's folders while the download queue is placing files in the same install
        // could split a mod between its enabled and disabled places.
        if (InstallingNow() is { } busy)
        {
            StatusMessage = Text(Strings.Installed_WaitForInstallFormat, busy);
            return null;
        }

        // Checked before anything is asked or moved, so a locked install is reported up front
        // rather than after the user has answered a dialog. ModDisableService guards again itself.
        if (ModInstallService.RunningBlockers(AppServices.SptEnvironment.InstallPath) is { Count: > 0 } blockers)
        {
            StatusMessage = ModInstallProblems.InstallInUse(
                blockers,
                disable ? ModInstallAction.Disable : ModInstallAction.Enable);
            return null;
        }

        var affected = AffectedCards(targets, disable);

        if (confirm && affected.Count > 0)
        {
            var rows = affected
                .Select(a => new ModDisableImpactRow(a.Card.DisplayTitle, a.Detail, a.IsSoft))
                .ToList();

            switch (ModDisableConfirmationWindow.Confirm(disable, targets.Select(t => t.DisplayTitle).ToList(), rows))
            {
                case ModDisableChoice.Cancel:
                    return null;
                case ModDisableChoice.ProceedWithCascade:
                    targets = targets.Concat(affected.Select(a => a.Card)).Distinct().ToList();
                    break;
            }
        }

        var entries = targets.SelectMany(c => c.Entries).Where(e => e.IsDisabled != disable).ToList();

        IsBusy = true;
        ModDisableOutcome outcome;
        try
        {
            // Not while SPT runs: the move is refused then, and its server may be writing them.
            if (AppServices.SptEnvironment.InstallPath is { Length: > 0 } profilesOf
                && ModInstallService.RunningBlockers(profilesOf).Count == 0)
                await Task.Run(() => AppServices.ProfileBackups.BackupIfChanged(profilesOf, ProfileBackups.BeforeDisable)); // Fork (1.3.0): off the UI thread

            outcome = ModDisableService.Apply(entries, disable, AppServices.SptEnvironment.InstallPath);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }

        var moves = (carryOver ?? []).Concat(outcome.Moved).ToList();
        SetLastMoves(moves, label ?? DescribeTargets(targets, disable));

        var message = disable
            ? Strings.Installed_DisabledCount(targets.Count)
            : Strings.Installed_EnabledCount(targets.Count);
        if (outcome.Failed.Count > 0) message = Sentences(message, DescribeFailures(outcome.Failed));

        await ScanAsync();
        StatusMessage = message;
        ModsMoved?.Invoke(this, EventArgs.Empty);

        return moves;
    }

    //
    // What else this change reaches: the mods that would lose a dependency when disabling, or the
    // disabled dependencies still needed when enabling. Reported as whole cards rather than
    // individual scan entries, so a client+server mod is never half-moved.
    //
    private List<(InstalledModCardViewModel Card, string Detail, bool IsSoft)> AffectedCards(
        IReadOnlyList<InstalledModCardViewModel> targets, bool disable)
    {
        var roots = targets.SelectMany(c => c.Entries).ToList();
        var links = disable ? _dependencies.DisableImpact(roots) : _dependencies.EnableRequirements(roots);

        var results = new List<(InstalledModCardViewModel, string, bool)>();
        var seen = new HashSet<InstalledModCardViewModel>(targets);

        foreach (var link in links)
        {
            var reached = disable ? link.Dependent : link.Dependency;
            if (!_cardByEntry.TryGetValue(reached, out var card) || !seen.Add(card)) continue;

            var otherName = _cardByEntry.TryGetValue(disable ? link.Dependency : link.Dependent, out var other)
                ? other.DisplayTitle
                : (disable ? link.Dependency : link.Dependent).Name;

            var detail = Text(
                disable
                    ? link.IsSoft
                        ? Strings.Installed_ImpactOptionallyUsesFormat
                        : Strings.Installed_ImpactNeedsFormat
                    : link.IsSoft
                        ? Strings.Installed_ImpactOptionallyUsedByFormat
                        : Strings.Installed_ImpactNeededByFormat,
                otherName);

            results.Add((card, detail, link.IsSoft));
        }

        return results;
    }

    private void SetLastMoves(List<ModMove> moves, (string Key, object? Arg)? label)
    {
        _lastMoves = moves;
        _lastMoveLabelKey = label?.Key;
        _lastMoveLabelArg = label?.Arg;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private static (string Key, object? Arg) DescribeTargets(
        IReadOnlyList<InstalledModCardViewModel> targets, bool disable)
    {
        if (targets.Count == 1)
        {
            return (disable
                ? nameof(Strings.Installed_UndoDisableNamedFormat)
                : nameof(Strings.Installed_UndoEnableNamedFormat), targets[0].DisplayTitle);
        }

        return (disable
            ? nameof(Strings.Installed_UndoDisableCountFormat)
            : nameof(Strings.Installed_UndoEnableCountFormat), targets.Count);
    }

    private static string DescribeFailures(IReadOnlyList<ModDisableFailure> failures) =>
        failures.Count == 1
            ? Text(Strings.Installed_MoveFailedNamedFormat, failures[0].ModName, failures[0].Reason)
            : Text(
                Strings.Installed_MoveFailedCountFormat,
                failures.Count,
                string.Join(
                    Strings.Common_ClauseSeparator,
                    failures.Select(f => Text(Strings.Installed_MoveFailedItemFormat, f.ModName, f.Reason))));

    // Two complete sentences on one status line, rather than one sentence with the other dropped
    // into it - where the break goes is the translator's to decide.
    //
    // "Needs X, which is not installed." / "... which is disabled." for what a card's enabled folders
    // declare they cannot run without - null when nothing is missing. A GUID BepInEx loads from
    // anywhere under plugins (SPT's own folder, a mod's subfolder) counts as provided.
    //
    private static string? MissingDependencyNote(
        InstalledModCardViewModel card, ModDependencyGraph graph, IReadOnlySet<string> loadedGuids, IReadOnlyList<Mod> catalog)
    {
        var missing = MissingDependencies(card, graph, loadedGuids);
        if (missing.Count == 0) return null;

        string NameOf(ModMissingDependency m) =>
            m.DisabledProvider?.Name
            ?? catalog.FirstOrDefault(c => string.Equals(c.Guid, m.Identifier, StringComparison.OrdinalIgnoreCase))?.Name
            ?? m.Identifier;

        var absent = missing.Where(m => m.DisabledProvider is null).Select(NameOf).ToList();
        var disabled = missing.Where(m => m.DisabledProvider is not null).Select(NameOf).ToList();

        var parts = new List<string>();
        if (absent.Count > 0) parts.Add(Text(Strings.Installed_NeedsMissingFormat, TextLists.Join(absent)));
        if (disabled.Count > 0) parts.Add(Text(Strings.Installed_NeedsDisabledFormat, TextLists.Join(disabled)));
        return string.Join(" ", parts);
    }

    //
    // What an enabled card's enabled folders declare they cannot run without and the install does
    // not provide (missing, or only in a disabled folder). Shared with the Play page's check before
    // launch (1.3.0) - see ModConflicts.ScanAsync.
    //
    internal static List<ModMissingDependency> MissingDependencies(
        InstalledModCardViewModel card, ModDependencyGraph graph, IReadOnlySet<string> loadedGuids)
    {
        if (card.IsDisabled) return [];

        // The plugins BepInEx loads meet a plugin's needs only: a server mod needing a GUID its
        // plugin half also carries is still missing it.
        return card.Entries
            .Where(e => !e.IsDisabled)
            .SelectMany(e => graph.MissingOf(e).Select(m => (e.Target, Missing: m)))
            .Where(x => !(x.Target == InstalledModTarget.Client && loadedGuids.Contains(x.Missing.Identifier))
                && !IsSptItself(x.Missing.Identifier))
            .Select(x => x.Missing)
            .GroupBy(m => m.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    //
    // SPT's own plugins (com.SPT.core and the rest) - part of every install. Read from its folder
    // when it is there (LoadedPluginGuids); by name as well, so an install laid out differently, or
    // a folder that could not be read, does not make every mod on it look broken.
    //
    private static readonly string[] SptOwnPrefixes = ["com.spt.", "com.sp-tarkov.", "com.aki."];

    private static bool IsSptItself(string identifier) =>
        SptOwnPrefixes.Any(p => identifier.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static string Sentences(string first, string second) =>
        string.Join(Strings.Common_SentenceSeparator, first, second);

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Expansion is driven from the cards themselves as well as from the two buttons - clicking
        // one open has to re-tint them just as pressing Expand all does.
        if (e.PropertyName is nameof(InstalledModCardViewModel.IsCardExpanded)
            or nameof(InstalledModCardViewModel.IsRowExpanded))
        {
            NotifyExpandedState();
            return;
        }

        // Update checks answer after the scan, card by card; Update all counts them as they land.
        if (e.PropertyName is nameof(InstalledModCardViewModel.UpdateAvailable)
            or nameof(InstalledModCardViewModel.IsDisabled)
            or nameof(InstalledModCardViewModel.LatestPublishedVersion))
        {
            AnnounceUpdates();
            UpdateSelectedCommand.NotifyCanExecuteChanged();
            return;
        }

        if (e.PropertyName != nameof(InstalledModCardViewModel.IsSelected)) return;

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountLabel));
        OnPropertyChanged(nameof(AllSelected));
        AnnounceSelection(); // Fork: the selection bar (InstalledViewModel.Toolbar.cs)
        DisableSelectedCommand.NotifyCanExecuteChanged();
        EnableSelectedCommand.NotifyCanExecuteChanged();
        UnsubscribeSelectedCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    //
    // The mod the download queue is placing files for right now, if any. Removing, disabling or
    // sorting out mods changes the same folders, and doing both at once could leave either half done
    // or lose a record - so those wait for it.
    //
    private static string? InstallingNow() =>
        AppServices.DownloadQueue.Items.FirstOrDefault(i => i.Status == DownloadQueueItemStatus.Installing)?.ModName;

    private static bool Confirm(string title, string message) =>
        TCFModManager.App.Views.SteamMessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    //
    // Confirms a removal and, when the mod has config files of its own, asks what should happen to
    // them - Steam's modal. Returns null when the user backed out. Not asked at all once "Don't ask
    // again" was ticked (Options > Unsubscribing turns it back on): then the configs are kept, the
    // answer that deletes nothing.
    //
    private static ConfigAction? ConfirmRemoval(
        string modName, string message, int configCount, IReadOnlyList<(string Name, string Detail)> needed,
        bool mustAsk = false, bool changesProfile = false)
    {
        var settingsService = new SettingsService();

        // Fork: a mod sp-mod.com says may change the profile is asked about whatever the setting.
        if (!mustAsk && !changesProfile && !settingsService.Load().ConfirmUnsubscribe) return ConfigAction.Keep;

        var body = new System.Windows.Controls.StackPanel { MaxWidth = 640 };
        if (changesProfile) body.Children.Add(ProfileWarning(Text(Strings.Installed_RemoveProfileWarningFormat, modName)));
        body.Children.Add(Paragraph(message));

        // What may stop working - what a removal of several checks for, named here too.
        if (needed.Count > 0)
        {
            body.Children.Add(Paragraph(Strings.RemoveCheck_NeededIntro(1, 1)));
            var list = RemoveCheckDialog.List(needed, monospace: false);
            list.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(list);
        }

        if (configCount > 0)
            body.Children.Add(Paragraph(Strings.Installed_UnsubscribeConfigs(configCount, modName, configCount, AppPaths.LegacyConfigsDirectory)));

        // Most people reaching for Remove are troubleshooting, where disabling does the job without
        // deleting anything - worth saying at the point they're about to delete. Before it (1.19),
        // how long the removed files are kept, which is what decides whether it can be undone.
        body.Children.Add(Paragraph(HeldSentence()));
        body.Children.Add(Paragraph(Strings.Installed_RemoveDisableHint));

        var dontAsk = AddDontAsk(body);

        var title = Text(Strings.Installed_UnsubscribeTitleFormat, modName);

        ConfigAction? action;
        if (configCount == 0)
        {
            action = SteamDialog.Show(
                title,
                body,
                new SteamDialogChoice(Strings.Item_Unsubscribe, SteamDialogButton.Blue, IsDefault: true),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey)) == 0
                ? ConfigAction.Keep
                : null;
        }
        else
        {
            action = SteamDialog.Show(
                title,
                body,
                new SteamDialogChoice(Strings.Installed_UnsubscribeKeepConfigs, SteamDialogButton.Blue, IsDefault: true),
                new SteamDialogChoice(Strings.Installed_UnsubscribeDeleteConfigs, SteamDialogButton.Grey),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey)) switch
            {
                0 => ConfigAction.Keep,
                1 => ConfigAction.Delete,
                _ => null,
            };
        }

        // Ticked and then cancelled changes nothing: the box goes with the answer.
        if (action is not null) TurnOffQuestionIfTicked(settingsService, dontAsk);

        return action;
    }

    // The unsubscribe question's "Don't ask again", and the line under it saying how to undo it.
    private static System.Windows.Controls.CheckBox AddDontAsk(System.Windows.Controls.Panel body)
    {
        var dontAsk = new System.Windows.Controls.CheckBox
        {
            Content = Strings.Installed_UnsubscribeDontAsk,
            Margin = new Thickness(0, 4, 0, 0),

            // WPF UI's check box keeps 11px of padding before its box (CheckBoxPadding 11,5,11,6);
            // without it the box lines up with the text above, as Steam's dialogs have it.
            Padding = new Thickness(0, 5, 11, 6),

            // The dialog's own text colour: it is Steam-dark whatever the app's theme.
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAC, 0xB2, 0xB8)),
        };
        body.Children.Add(dontAsk);
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Strings.Installed_UnsubscribeDontAskNote,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0x98, 0xA0)),
        });
        return dontAsk;
    }

    private static void TurnOffQuestionIfTicked(SettingsService settingsService, System.Windows.Controls.CheckBox dontAsk)
    {
        if (dontAsk.IsChecked != true) return;

        var settings = settingsService.Load();
        settings.ConfirmUnsubscribe = false;
        settingsService.Save(settings);
        AppLog.Info("Options", "unsubscribe question turned off from the question");
    }

    private static System.Windows.Controls.TextBlock Paragraph(string text, double top = 0) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 21,
        Margin = new Thickness(0, top, 0, 12),
    };

    //
    // What a removal did, in order of what the user most needs to know: whether it went, what it left
    // in place and why, what it put back, where configs went, and that it can be undone.
    //
    private static string DescribeRemoval(
        string modName, int failedFiles, int configsKept, string? configsFolder, UninstallResult? result = null, bool held = false)
    {
        var parts = new List<string>
        {
            failedFiles == 0
                ? Text(Strings.Installed_RemovedFormat, modName)
                : Strings.Installed_RemovedFailed(failedFiles, modName, failedFiles),
        };

        if (result is not null)
        {
            if (result.KeptChanged.Count > 0)
                parts.Add(Strings.Installed_RemovedKeptChanged(result.KeptChanged.Count, result.KeptChanged.Count));
            if (result.KeptOwned.Count > 0)
                parts.Add(Strings.Installed_RemovedKeptOwned(result.KeptOwned.Count, result.KeptOwned.Count));
            if (result.RefusedFiles is { Count: > 0 } refused)
                parts.Add(Strings.Installed_RemovedRefused(refused.Count, refused.Count));
            if (result.OriginalsRestored > 0)
                parts.Add(Strings.Installed_RemovedRestored(result.OriginalsRestored, result.OriginalsRestored));

            // A folder that had to stay for files the mod didn't install - otherwise it looks like a
            // mod still installed, with nothing saying why.
            foreach (var left in result.FoldersLeft)
                parts.Add(Strings.Installed_RemovedFolderLeft(left.Files, left.Files, left.Folder.Replace('/', '\\')));
        }

        if (configsKept > 0 && configsFolder is not null)
            parts.Add(Strings.Installed_RemovedConfigsKept(configsKept, configsKept, configsFolder));

        if (held || result?.HoldingFolder is not null)
            parts.Add(Strings.Installed_RemovedUndoHint);

        return string.Join(Strings.Common_SentenceSeparator, parts);
    }

    //
    // The sentence the removal confirmation ends with: how long removed files are kept, from the
    // Keep removed mods setting (D27).
    //
    internal static string HeldSentence() => new SettingsService().Load().RemovedModsRetention switch
    {
        RemovedModsRetention.DeleteStraightAway => Strings.Installed_RemoveNotHeld,
        RemovedModsRetention.UntilCleared => Strings.Installed_RemoveHeldUntilCleared,
        var days => Text(Strings.Installed_RemoveHeldFormat, OptionsViewModel.RetentionLabel(days)),
    };

    // "N conflicts - see Dependencies and Conflicts" on the status line, or null when there are none.
    [ObservableProperty]
    private string? _conflictCountLabel;

    [RelayCommand]
    private static void ShowConflicts() => AppNavigation.Navigate(typeof(DependenciesPage));

    // The Undo button's text, or null to hide it. One removal held: "Undo removing <mod>", which
    // undoes it on click. Several: "Undo a removal (n)", which opens HeldRemovals to pick from.
    [ObservableProperty]
    private string? _undoRemovalLabel;

    // Every removal still held for this install that can be undone, newest first - the Undo menu.
    [ObservableProperty]
    private IReadOnlyList<HeldRemovalItem> _heldRemovals = [];

    private void RefreshUndo()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        var held = string.IsNullOrWhiteSpace(installPath) ? [] : RemovedMods.Undoable(installPath);

        HeldRemovals =
        [
            .. held.Select(h => new HeldRemovalItem(
                h.Folder, Text(Strings.Installed_UndoRemovalItemFormat, h.Log.ModName, WhenLabel(h.Log.RemovedAt)))),
        ];

        UndoRemovalLabel = held.Count switch
        {
            0 => null,
            1 => Text(Strings.Installed_UndoRemovalFormat, held[0].Log.ModName),
            var count => Text(Strings.Installed_UndoRemovalPickFormat, count),
        };
    }

    private static string WhenLabel(DateTimeOffset at) =>
        at.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);

    //
    // Marks the card of each folder a removal had to leave behind (RemovalLog.FoldersLeft) while that
    // removal is still held, so a leftover doesn't pass for a mod that's still installed.
    //
    private static void ApplyLeftovers(IReadOnlyList<InstalledModCardViewModel> cards)
    {
        foreach (var card in cards) card.LeftoverSummary = null;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var left = new Dictionary<string, RemovalLog>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, log) in RemovedMods.Undoable(installPath))
            foreach (var folder in log.FoldersLeft)
                left.TryAdd(SamePath(Path.Combine(installPath, folder)), log);

        if (left.Count == 0) return;

        foreach (var card in cards.Where(c => !c.IsAppManaged))
        {
            var log = card.Entries
                .Select(e => left.GetValueOrDefault(SamePath(e.FolderPath)))
                .FirstOrDefault(l => l is not null);

            if (log is not null)
                card.LeftoverSummary = Text(Strings.Installed_LeftoverFormat, log.ModName, WhenLabel(log.RemovedAt));
        }
    }

    private static string SamePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    //
    // Puts a removal back (D28): the one picked from the Undo menu, or the only one held. Any can go
    // first, and none ever overwrites: anything that has taken a removed file's place since is
    // reported and stays held.
    //
    [RelayCommand]
    private async Task UndoRemovalAsync(string? folder)
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;

        var undoable = RemovedMods.Undoable(installPath);
        var picked = folder is null
            ? (undoable.Count == 1 ? undoable[0] : default)
            : undoable.FirstOrDefault(u => string.Equals(u.Folder, folder, StringComparison.OrdinalIgnoreCase));

        if (picked.Folder is null)
        {
            StatusMessage = Strings.Installed_UndoNothing;
            RefreshUndo();
            return;
        }

        var latest = picked;

        IsBusy = true;
        try
        {
            // Off the UI thread: putting back a large mod copies a lot of files.
            var result = await Task.Run(() => AppServices.ModInstall.UndoRemoval(installPath, latest.Folder));

            StatusMessage = !result.Ran
                ? Strings.Installed_UndoNothing
                : result.Blocked.Count == 0
                    ? Text(Strings.Installed_UndoneFormat, latest.Log.ModName)
                    : Strings.Installed_UndoneBlocked(result.Blocked.Count, result.Blocked.Count, latest.Log.ModName, latest.Folder);

            ModRemoved?.Invoke(this, EventArgs.Empty);
        }
        catch (ModInstallException ex)
        {
            StatusMessage = ModInstallProblems.Describe(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Text(Strings.Installed_RemoveFailedFormat, latest.Log.ModName, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }

        await ScanAsync();
    }

    private bool CanGoToPreviousPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage() => GoToPage(CurrentPage - 1);

    private bool CanGoToNextPage() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage() => GoToPage(CurrentPage + 1);

    private void GoToPage(int page)
    {
        // The infinite list has no pages: it is always "page 1", so going back to pages starts there.
        var target = IsInfinite ? 1 : Math.Clamp(page, 1, TotalPages);

        // Unlike the other two views this one also depends on which page is asked for, so being
        // clean isn't enough on its own - paging forward and back has to rebuild even though
        // _filtered hasn't moved. TotalPages only changes inside ApplyFilter, which marks this
        // dirty, so a clamp landing somewhere new can't be missed here.
        if (!_cardsDirty && target == CurrentPage) return;

        CurrentPage = target;
        ItemsSync.Apply(
            Results,
            IsInfinite
                // As many as were loaded, so a card leaving the results is followed by the next.
                ? _filtered.Take(Math.Max(Results.Count, PageStep)).ToList()
                : _filtered.Skip((CurrentPage - 1) * PageStep).Take(PageStep).ToList());

        _cardsDirty = false;
    }

    /// <summary>Adds the next cards to the infinite list - called as Cards view nears its bottom.</summary>
    public void LoadMore()
    {
        if (!IsInfinite || !ShowCardsFlat || _cardsDirty || Results.Count >= _filtered.Count) return;

        foreach (var card in _filtered.Skip(Results.Count).Take(PageStep).ToList()) Results.Add(card);
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();

        // A leading "@" switches the search text to matching against the matched catalog listing's
        // author instead of name - e.g. "@Acidphantasm".
        var authorQuery = query.StartsWith('@') ? query[1..].Trim() : null;

        var recentSince = DateTimeOffset.Now.AddDays(-RecentDays);

        var matched = _all
            .Where(m => SelectedUpdateFilter.Value switch
            {
                UpdateFilter.NeedsUpdate => m.UpdateAvailable == true,
                UpdateFilter.UpToDate => m.UpdateAvailable == false,
                UpdateFilter.NotFound => m.MatchedModName is null,
                UpdateFilter.RecentlyInstalled => m.InstalledAt is { } at && at >= recentSince,
                _ => true, // All - no restriction
            })
            .Where(m => SelectedEnabledFilter.Value switch
            {
                EnabledFilter.EnabledOnly => !m.IsDisabled,
                EnabledFilter.DisabledOnly => m.IsDisabled,
                _ => true,
            })
            .Where(m => SelectedGroupFilter.AllGroups
                || (SelectedGroupFilter.GroupId is { } groupId ? m.GroupId == groupId : !m.IsGrouped))
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.DisplayTitle, query) || Matches(m.Name, query))
            .Where(m => SelectedCategory.Title is not { } category
                || string.Equals(m.CategoryTag, category, StringComparison.OrdinalIgnoreCase))
            .Where(m => !IsOn(ModAttributeFilter.FikaCompatible) || m.IsFikaCompatible)
            .Where(m => !IsOn(ModAttributeFilter.HideAds) || !m.ContainsAds)
            .Where(m => !IsOn(ModAttributeFilter.HasDependencies) || m.HasDependencies)
            .Where(m => !IsOn(ModAttributeFilter.HasAddons) || m.HasAddons)
            .Where(m => !IsOn(ModAttributeFilter.DownloadedNotConfirmed) || m.HasPendingDownload)
            .Where(m => !IsOn(ModAttributeFilter.HasConflicts) || m.HasConflicts);

        _filtered = SelectedUpdateFilter.Value == UpdateFilter.RecentlyInstalled
            ? matched
                .OrderByDescending(m => m.InstalledAt)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : SortMods(matched, SelectedSortOption.Value).ToList();

        // Fork: pinned mods first, each run in the order above (OrderBy is stable).
        _filtered = PinnedFirst(_filtered);

        // Every view now disagrees with _filtered, including the two that aren't on screen - they
        // catch up when switched to.
        _cardsDirty = _listDirty = _sectionsDirty = _viewSectionsDirty = true;

        TotalPages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageStep));

        // The button names the number it would select, so narrowing the filter has to re-label it -
        // and re-decide whether everything it now matches is already ticked.
        OnPropertyChanged(nameof(SelectAllLabel));
        OnPropertyChanged(nameof(AllSelected));

        // Expand/collapse all are disabled when the filters match nothing, and a different set of
        // mods can be all-open or all-closed when the previous one was neither.
        ExpandAllCommand.NotifyCanExecuteChanged();
        CollapseAllCommand.NotifyCanExecuteChanged();
        NotifyExpandedState();

        // Keeps the status line up to date as soon as a filter/search control changes; skipped when
        // _all is empty since ScanAsync's own "No mods found under ..." message is more useful there.
        if (_all.Count > 0) StatusMessage = DescribeCounts();

        // Fork: the Filters button's count and the empty states (InstalledViewModel.Toolbar.cs).
        UpdateToolbarState();
    }

    // How many mods are shown out of how many are installed, plus how many of them are disabled.
    private string DescribeCounts()
    {
        var shown = _filtered.Count == _all.Count
            ? Strings.Installed_CountFound(_all.Count)
            : Strings.Installed_CountShown(_filtered.Count, _filtered.Count, _all.Count);

        var disabled = _all.Count(m => m.IsDisabled);
        if (disabled == 0) return shown;

        return Sentences(
            shown,
            Strings.Installed_CountDisabled(disabled));
    }

    private static IEnumerable<InstalledModCardViewModel> SortMods(IEnumerable<InstalledModCardViewModel> mods, ModSortOption sort) =>
        sort switch
        {
            ModSortOption.NameAscending => mods.OrderBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.NameDescending => mods.OrderByDescending(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            // No-author mods sort last in both directions rather than clumping at whichever end an
            // empty/null string would fall on.
            ModSortOption.AuthorAscending => mods
                .OrderBy(m => m.Author is null)
                .ThenBy(m => m.Author, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.AuthorDescending => mods
                .OrderBy(m => m.Author is null)
                .ThenByDescending(m => m.Author, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            // Ungrouped mods sort last in both directions, same treatment as a missing author.
            ModSortOption.GroupAscending => mods
                .OrderBy(m => m.GroupName is null)
                .ThenBy(m => m.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.GroupDescending => mods
                .OrderBy(m => m.GroupName is null)
                .ThenByDescending(m => m.GroupName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            ModSortOption.RecentlyInstalled => mods
                .OrderBy(m => m.InstalledAt is null)
                .ThenByDescending(m => m.InstalledAt)
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            _ => mods,
        };

    //
    // Copies each mod's group assignment onto its card and rebuilds the Group dropdown, from the
    // one place groups are stored. Run after every scan and every group change, so the cards, the
    // filter and the store can't drift apart. The current filter selection is carried across by
    // value rather than by instance, since the dropdown's items are replaced each time.
    //
    private void RefreshGroups()
    {
        var data = AppServices.ModGroups.Load();
        var namesById = data.Groups.ToDictionary(g => g.Id, g => g.Name);

        foreach (var card in _all)
        {
            // An assignment pointing at a group that's since been deleted reads as ungrouped, the
            // same way RebuildSections already treats it.
            Guid? assigned = data.Assignments.TryGetValue(ModGroupStore.KeyFor(card.Name), out var id)
                && namesById.ContainsKey(id)
                ? id
                : null;

            card.GroupId = assigned;
            card.GroupName = assigned is { } value ? namesById[value] : null;
        }

        // Same as the category list above: the saved default the first time, the current selection
        // every time after.
        var previous = _groupDefaultApplied ? SelectedGroupFilter : DefaultGroupFilter();
        _groupDefaultApplied = true;

        _suppressAutoApplyFilter = true;
        try
        {
            GroupFilterOptions.Clear();
            GroupFilterOptions.Add(GroupFilterItem.All);
            GroupFilterOptions.Add(GroupFilterItem.Ungrouped);

            foreach (var group in data.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
                GroupFilterOptions.Add(new GroupFilterItem(group.Name, group.Id, allGroups: false));

            SelectedGroupFilter = GroupFilterOptions.FirstOrDefault(o => o.SameAs(previous)) ?? GroupFilterItem.All;
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }

        RememberIfPending(); // Fork
    }

    // Everything that has to happen after groups are added, renamed, deleted, reordered, or a mod
    // is moved between them.
    private void GroupsChanged()
    {
        RefreshGroups();
        ApplyFilter();
        RefreshActiveView(CurrentPage);
    }

    private static bool Matches(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Matches a "@name" query against the matched catalog listing's author. An empty query matches
    /// everything, same as an empty plain-text query.</summary>
    private static bool MatchesAuthor(InstalledModCardViewModel mod, string authorQuery) =>
        authorQuery.Length == 0 || Matches(mod.Author, authorQuery);

    // Rebuilds Sections from the current group store contents and _filtered (so group view honors
    // the same search/update-status/Fika/ads/AI filters and sort as the flat grid) - called after
    // every filter/sort change and every group mutation (add/rename/delete/move/assign) rather than
    // patched incrementally; Installed's mod counts are small enough that a full rebuild is simpler
    // and can't drift out of sync with the store.
    private void RebuildSections()
    {
        BuildSections(Sections, GroupsByCategory, SelectedGroupSortOption.Value);
        _sectionsDirty = false;
    }

    // Cards' and List's sections. Your groups come in the Groups view's order, or their own manual
    // order while that view is sorted by category (the move buttons show then too).
    private void RebuildViewSections()
    {
        var order = SelectedGroupSortOption.Value == GroupSortOption.Category ? GroupSortOption.Manual : SelectedGroupSortOption.Value;
        // Every group shows, empty ones too, as in the Groups view: an empty group is where a mod is
        // dragged to. (Categories are only ever made from the mods in them.)
        if (SelectedGrouping.Value == InstalledGrouping.InstallState) BuildStateSections(ViewSections); // Fork
        else BuildSections(ViewSections, SelectedGrouping.Value == InstalledGrouping.Category, order);
        _viewSectionsDirty = false;
    }

    //
    // Brings a section list in line with _filtered - synced, never cleared and refilled, for the
    // same reason the flat views are (see ItemsSync): Cards' and List's sections hold expanders,
    // and a regenerated one replays its open animation. A section that is still wanted keeps its
    // object, its name and fold are re-read, and its mods are synced into it the same way.
    //
    private void BuildSections(
        ObservableCollection<ModGroupSectionViewModel> sections, bool byCategory, GroupSortOption order)
    {
        var existing = new Dictionary<string, ModGroupSectionViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections) existing.TryAdd(SectionKey(section), section);

        ModGroupSectionViewModel Reuse(string key, Func<ModGroupSectionViewModel> create) =>
            existing.TryGetValue(key, out var found) ? found : create();

        var wanted = new List<(ModGroupSectionViewModel Section, List<InstalledModCardViewModel> Items)>();

        if (byCategory)
        {
            // A to Z by category, what has none last; within each the page's own sort order.
            foreach (var category in _filtered
                         .GroupBy(m => m.CategoryTag, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key is null)
                         .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var collapsed = _collapsedCategories.Contains(category.Key ?? string.Empty);
                var section = Reuse(CategorySectionKey(category.Key ?? string.Empty),
                    () => ModGroupSectionViewModel.ForCategory(category.Key, collapsed));
                section.IsCollapsed = collapsed;
                wanted.Add((section, category.ToList()));
            }
        }
        else
        {
            var data = AppServices.ModGroups.Load();

            IEnumerable<ModGroup> ordered = order switch
            {
                GroupSortOption.NameAscending => data.Groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                GroupSortOption.NameDescending => data.Groups.OrderByDescending(g => g.Name, StringComparer.OrdinalIgnoreCase),
                _ => data.Groups.OrderBy(g => g.SortOrder),
            };

            var byId = new Dictionary<Guid, List<InstalledModCardViewModel>>();
            foreach (var group in ordered)
            {
                var section = Reuse(GroupSectionKey(group.Id), () => ModGroupSectionViewModel.FromGroup(group));

                // From the store every time: a rename that was cancelled or left blank goes back to
                // the saved name this way, as a fresh section used to.
                section.Name = group.Name;
                section.IsEditing = false;
                section.IsCollapsed = group.IsCollapsed;
                section.CanReorder = order == GroupSortOption.Manual;

                var items = new List<InstalledModCardViewModel>();
                byId[group.Id] = items;
                wanted.Add((section, items));
            }

            var ungrouped = Reuse(UngroupedSectionKey, ModGroupSectionViewModel.Ungrouped);
            var ungroupedItems = new List<InstalledModCardViewModel>();
            wanted.Add((ungrouped, ungroupedItems));

            foreach (var mod in _filtered)
            {
                var key = ModGroupStore.KeyFor(mod.Name);
                var items = data.Assignments.TryGetValue(key, out var groupId) && byId.TryGetValue(groupId, out var found)
                    ? found
                    : ungroupedItems;

                items.Add(mod);
            }
        }

        ItemsSync.Apply(sections, [.. wanted.Select(w => w.Section)]);
        foreach (var (section, items) in wanted)
        {
            ItemsSync.Apply(section.Items, items);

            // The same mods can have changed underneath - one disabled - with nothing added or taken
            // away, which is all the counts would otherwise follow.
            section.RefreshCounts();
        }
    }

    private const string UngroupedSectionKey = "u";

    private static string GroupSectionKey(Guid id) => "g:" + id;

    private static string CategorySectionKey(string category) => "c:" + category;

    private static string SectionKey(ModGroupSectionViewModel section) =>
        section.StateKey is { } state ? StateSectionKey(state)
        : section.GroupId is { } id ? GroupSectionKey(id)
        : section.CategoryKey is { } category ? CategorySectionKey(category)
        : UngroupedSectionKey;

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupName.Trim();
        if (name.Length == 0) return;

        AppServices.ModGroups.AddGroup(name);
        NewGroupName = string.Empty;
        GroupsChanged();
    }

    [RelayCommand]
    private void BeginRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        section.IsEditing = true;
    }

    [RelayCommand]
    private void CommitRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        section.IsEditing = false;

        var name = section.Name.Trim();
        if (name.Length == 0)
        {
            // Blank name isn't allowed - reload the real name from disk rather than saving it.
            GroupsChanged();
            return;
        }

        AppServices.ModGroups.RenameGroup(section.GroupId!.Value, name);
        GroupsChanged();
    }

    // Discards an in-progress rename without saving - the edited Name only lives on the section VM
    // in memory, so reloading fresh ones from the store (still holding the old name) undoes it.
    [RelayCommand]
    private void CancelRename(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;
        GroupsChanged();
    }

    [RelayCommand]
    private void DeleteGroup(ModGroupSectionViewModel? section)
    {
        if (section is not { IsRealGroup: true }) return;

        var message = section.Items.Count == 0
            ? Text(Strings.Installed_DeleteGroupEmptyFormat, section.Name)
            : Strings.Installed_DeleteGroup(
                section.Items.Count, section.Name, section.Items.Count);

        if (TCFModManager.App.Views.SteamMessageBox.Show(
                message,
                Strings.Installed_DeleteGroupTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        AppServices.ModGroups.DeleteGroup(section.GroupId!.Value);
        GroupsChanged();
    }

    [RelayCommand]
    private void ToggleCollapsed(ModGroupSectionViewModel? section)
    {
        if (section is not { CanCollapse: true }) return;

        section.IsCollapsed = !section.IsCollapsed;

        // The same section in the other views' sections folds with it - it is the same group or
        // category, and coming back to that view should not find it the other way.
        foreach (var twin in Sections.Concat(ViewSections).Where(s => !ReferenceEquals(s, section)
                     && s.GroupId == section.GroupId && s.CategoryKey == section.CategoryKey && s.StateKey == section.StateKey))
        {
            twin.IsCollapsed = section.IsCollapsed;
        }

        if (section.StateKey is not null) // Fork: Enabled / Disabled sections
        {
            RememberStateFold(section);
            return;
        }

        if (section.CategoryKey is { } category)
        {
            if (section.IsCollapsed) _collapsedCategories.Add(category);
            else _collapsedCategories.Remove(category);
            return;
        }

        AppServices.ModGroups.SetCollapsed(section.GroupId!.Value, section.IsCollapsed);
    }

    [RelayCommand]
    private void MoveGroupUp(ModGroupSectionViewModel? section) => MoveGroup(section, -1);

    [RelayCommand]
    private void MoveGroupDown(ModGroupSectionViewModel? section) => MoveGroup(section, 1);

    //
    // Swaps the group with the next group shown in that direction - in the list of sections the
    // button was pressed in. Every view shows every group now, so that is its stored neighbour; the
    // swap by id is what keeps it right should a view ever leave one out.
    //
    private void MoveGroup(ModGroupSectionViewModel? section, int direction)
    {
        if (section is not { IsRealGroup: true }) return;

        var shown = ViewSections.Contains(section) ? ViewSections : Sections;
        var index = shown.IndexOf(section);
        if (index < 0) return;

        ModGroupSectionViewModel? neighbour = null;
        for (var i = index + direction; i >= 0 && i < shown.Count; i += direction)
        {
            if (shown[i].IsRealGroup)
            {
                neighbour = shown[i];
                break;
            }
        }

        if (neighbour is null) return;

        AppServices.ModGroups.SwapOrder(section.GroupId!.Value, neighbour.GroupId!.Value);
        GroupsChanged();
    }

    // Called by InstalledPage's drag-drop code-behind when a mod card is dropped on a section
    // (groupId null for the Ungrouped bucket).
    public void MoveModToGroup(InstalledModCardViewModel mod, Guid? groupId)
    {
        AppServices.ModGroups.AssignMod(mod.Name, groupId);
        GroupsChanged();
    }
}
