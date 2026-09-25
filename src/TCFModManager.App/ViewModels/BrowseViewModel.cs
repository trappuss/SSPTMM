using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Behaviors;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

public partial class BrowseViewModel : LocalizedViewModel
{
    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    private readonly SpModApiClient _spModApi;
    private List<Mod> _filtered = [];

    /// <summary>The SPT release lines ticked in the version filter, kept so each card can describe
    /// what the mod supports on exactly those lines.</summary>
    private List<(int Major, int Minor)> _selectedLines = [];

    // Populated by RefreshInstalledIndexAsync, consumed by GoToPage/FindInstalledMatch to drive
    // each card's install/update status dot.
    private Dictionary<string, InstalledModCardViewModel> _installedByGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, InstalledModCardViewModel> _installedByName = new(StringComparer.OrdinalIgnoreCase);

    public BrowseViewModel() : this(AppServices.SpModApi)
    {
    }

    public BrowseViewModel(SpModApiClient spModApi)
    {
        _spModApi = spModApi;

        _defaults = new SettingsService().Load().BrowseDefaults;

        //
        // Backing fields rather than the properties: this is the page opening at its default, not
        // someone changing a filter, so nothing here should run a filter pass over a catalog that
        // has not loaded yet.
        //
        // With nothing saved these are the app's own defaults - Newest sort, no Featured
        // restriction, twelve per page.
        //
        _selectedSortOption = DefaultSortOption();
        _selectedFeaturedFilter = DefaultFeaturedFilter();
        _pageSize = DefaultPageSize();

        // Category and SPT version are resolved later: both lists are built from the catalog, and
        // neither exists yet - see EnsureCategoryOptionsBuilt/EnsureSptVersionOptionsBuilt.

        // Refreshes each card's install/update status dot once a queued install completes.
        AppServices.DownloadQueue.ItemInstalled += async (_, _) =>
        {
            await RefreshInstalledIndexAsync();
            ShowInstalledChange();
        };

        // Refreshes the status dots when a mod is removed from the Installed page.
        InstalledViewModel.ModRemoved += async (_, _) =>
        {
            await RefreshInstalledIndexAsync();
            ShowInstalledChange();
        };

        // Before the subscription below, so applying a saved default doesn't count as a change.
        SavedFilterDefaults.ApplyAttributes(AttributeOptions, _defaults?.Attributes);

        // Each tick box drives the same re-filter a dropdown selection does. Subscribed rather
        // than bound through a property apiece, so adding an option is one line in the list above.
        foreach (var option in AttributeOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                UpdateAttributeFilterSummary();
                AutoApplyFilter();
            };
        }

        UpdateAttributeFilterSummary();

        // Steam's [+]/[-] tag rows over the same options - see WorkshopTagRow. Neutral names, so the
        // box that is lit says which way the row filters: [-] beside "Contains ads" hides them.
        TagRows =
        [
            new(nameof(Strings.Common_FlagFikaCompatible), Option(ModAttributeFilter.FikaCompatible), null),
            new(nameof(Strings.Filter_HasDependencies), Option(ModAttributeFilter.HasDependencies), null),
            new(nameof(Strings.Filter_HasAddons), Option(ModAttributeFilter.HasAddons), null),
            new(nameof(Strings.Common_FlagContainsAds), null, Option(ModAttributeFilter.HideAds)),
            new(nameof(Strings.Common_FlagContainsAiContent), null, Option(ModAttributeFilter.HideAiContent)),
            new(nameof(Strings.Workshop_TagSubscribed), null, Option(ModAttributeFilter.HideInstalled)),
        ];

        // The addon catalog usually settles after the first page has already rendered, so the
        // "N addons" badges are redrawn once it does rather than waiting for a page change.
        AppServices.Addons.AddonsChanged += (_, _) =>
        {
            if (HasLoadedResults) GoToPage(CurrentPage);
        };
    }

    //
    // What this page opens filtered and sorted to, saved from the page itself by SaveAsDefault
    // rather than set from Options - see Core's PageDefaults. Null on an install that has never
    // saved one, in which case every Default* helper below answers with the app's own default.
    //
    private readonly BrowsePageDefaults? _defaults;

    // Whether the saved category default has reached its dropdown yet. The list is built from the
    // catalog, so the first build is the earliest moment it can be resolved to a real entry.
    private bool _categoryDefaultApplied;

    private SortOptionItem DefaultSortOption() =>
        SavedFilterDefaults.Parse<ModSortOrder>(_defaults?.Sort) is { } value
            ? SortOptions.FirstOrDefault(o => o.Value == value) ?? SortOptions[0]
            : SortOptions[0];

    private FeaturedFilterItem DefaultFeaturedFilter() =>
        SavedFilterDefaults.Parse<FeaturedFilter>(_defaults?.Featured) is { } value
            ? FeaturedFilterOptions.FirstOrDefault(o => o.Value == value) ?? FeaturedFilterOptions[0]
            : FeaturedFilterOptions[0];

    private int DefaultPageSize() =>
        SavedFilterDefaults.PageSize(_defaults?.PageSize, PageSizeOptions, DefaultPageSizeValue);

    // Described rather than looked up: the entry may not be in the list yet, or at all if The Forge
    // has stopped using that category. CategoryFilterItem.SameAs matches on the title.
    private CategoryFilterItem DefaultCategory() =>
        string.IsNullOrWhiteSpace(_defaults?.Category)
            ? CategoryFilterItem.All
            : new CategoryFilterItem(_defaults.Category!, _defaults.Category);

    // Set while ClearFilters is resetting several properties at once, so each individual
    // OnXxxChanged below doesn't run its own ApplyFilter.
    private bool _suppressAutoApplyFilter;

    partial void OnSearchTextChanged(string value) => AutoApplyFilter();

    partial void OnSelectedSortOptionChanged(SortOptionItem value) => AutoApplyFilter();

    partial void OnPageSizeChanged(int value) => AutoApplyFilter();

    partial void OnSelectedFeaturedFilterChanged(FeaturedFilterItem value) => AutoApplyFilter();

    partial void OnSelectedCategoryChanged(CategoryFilterItem value) => AutoApplyFilter();

    private void AutoApplyFilter()
    {
        if (!_suppressAutoApplyFilter && HasLoadedResults) ApplyFilter();
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    public List<SortOptionItem> SortOptions { get; } =
    [
        // Read as orderings without the "Sort:" label that used to sit beside them.
        new(nameof(Strings.Sort_BrowseNewest), ModSortOrder.Newest),
        new(nameof(Strings.Sort_BrowseLastUpdated), ModSortOrder.LastUpdated),
        new(nameof(Strings.Sort_BrowseMostDownloaded), ModSortOrder.MostDownloaded),
        new(nameof(Strings.Sort_BrowseMostFavourited), ModSortOrder.MostFavourited),
        new(nameof(Strings.Sort_BrowseMostEndorsed), ModSortOrder.MostEndorsed),
    ];

    [ObservableProperty]
    private SortOptionItem _selectedSortOption;

    // How many matching cards make up one page, when nothing has been saved as this page's
    // default - see DefaultPageSize(). Steam's own choices, and its default of 30; a default saved
    // from the upstream app's list (8/12/16/24/32) is not among them and falls back to this.
    private const int DefaultPageSizeValue = 30;

    public List<int> PageSizeOptions { get; } = [10, 15, DefaultPageSizeValue, 50];

    [ObservableProperty]
    private int _pageSize = DefaultPageSizeValue;

    public List<FeaturedFilterItem> FeaturedFilterOptions { get; } =
    [
        // The worst case for dropping a label: "Include" / "Exclude" / "Only" say nothing at all
        // on their own about what is being included.
        new(nameof(Strings.Filter_FeaturedIncluded), FeaturedFilter.Include),
        new(nameof(Strings.Filter_FeaturedExcluded), FeaturedFilter.Exclude),
        new(nameof(Strings.Filter_FeaturedOnly), FeaturedFilter.Only),
    ];

    [ObservableProperty]
    private FeaturedFilterItem _selectedFeaturedFilter;

    //
    // The tick-box filters that describe the mod itself, in one dropdown rather than three toggle
    // switches strung across the top of the page. Every one of them narrows the result set.
    //
    public ObservableCollection<ModAttributeOption> AttributeOptions { get; } =
    [
        .. ModAttributeOption.Standard(nameof(Strings.Filter_HasDependenciesBrowseToolTip)),
        new(ModAttributeFilter.HideInstalled,
            nameof(Strings.Filter_HideInstalled),
            nameof(Strings.Filter_HideInstalledToolTip)),
    ];

    [ObservableProperty]
    private string _attributeFilterSummary = Strings.Filter_AnyMod;

    /// <summary>The Category dropdown's entries, rebuilt from the cached catalog once it has loaded.</summary>
    public ObservableCollection<CategoryFilterItem> CategoryOptions { get; } = [CategoryFilterItem.All];

    [ObservableProperty]
    private CategoryFilterItem _selectedCategory = CategoryFilterItem.All;

    /// <summary>The sidebar's tag rows, over <see cref="AttributeOptions"/>.</summary>
    public IReadOnlyList<WorkshopTagRow> TagRows { get; }

    private ModAttributeOption Option(ModAttributeFilter filter) =>
        AttributeOptions.First(o => o.Value == filter);

    private bool IsOn(ModAttributeFilter filter) =>
        AttributeOptions.Any(o => o.Value == filter && o.IsSelected);

    [ObservableProperty]
    private bool _isBusy;

    //
    // Drives the startup panel over the results area, and only on the first load of a session -
    // a later search keeps the results that are already there rather than blanking them.
    //
    // The catalog is cached on disk, so most launches spend well under a second here; a first run
    // with no cache fetches ~3000 mods a page at a time and can take considerably longer, which is
    // the case this exists for. Everything it needed to say was already being tracked and simply
    // had nothing bound to it.
    //
    [ObservableProperty]
    private bool _isStartingUp;

    [ObservableProperty]
    private string _startupStage = Strings.Browse_StageStarting;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>How many columns the results grid should show, driven by the results area's available width.</summary>
    [ObservableProperty]
    private int _columns = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    /// <summary>The SPT version filter's checkable options, built once per session from the distinct
    /// major.minor release lines present in the cached catalog. The detected install's version starts
    /// pre-checked; an empty selection means no filter.</summary>
    public ObservableCollection<SptVersionOption> SptVersionOptions { get; } = [];

    [ObservableProperty]
    private string _sptVersionFilterSummary = Strings.Browse_AllSptVersions;

    private bool _sptVersionOptionsBuilt;

    public ObservableCollection<ModCardViewModel> Results { get; } = [];

    /// <summary>Steam's numbered pager under the grid - see PageLink.For.</summary>
    public ObservableCollection<PageLink> PageLinks { get; } = [];

    /// <summary>"N entries matching filters" under the page title.</summary>
    [ObservableProperty]
    private string? _entriesText;

    /// <summary>The Steam card's width at 1:1 - the grid's columns are counted from it.</summary>
    public const double CardWidth = 245;

    // The gap between cards, 609 - 348 - 245 on the measured page.
    public const double CardGap = 16;

    /// <summary>True once a search has actually completed. Lets BrowsePage skip redundantly re-running the initial search on re-navigation.</summary>
    public bool HasLoadedResults { get; private set; }

    /// <summary>Raised after a page of results is on screen, so the page can scroll back to the top
    /// of the grid the way a Steam page load does.</summary>
    public event EventHandler? PageChanged;

    //
    // As many Steam-sized cards as fit across, each keeping its 16px gap. Steam's page lays the grid
    // out the same way; a wider window gets more columns rather than wider cards.
    //
    public void UpdateLayoutForWidth(double availableWidth)
    {
        if (availableWidth <= 0) return;
        Columns = Math.Max(1, (int)Math.Floor((availableWidth + CardGap) / (CardWidth + CardGap)));
    }

    /// <summary>Ensures the full mod catalog is cached (fetches only on the first call each session), then filters/sorts it locally.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        AppLog.Debug("Browse", "SearchAsync: start");
        IsBusy = true;
        IsStartingUp = !HasLoadedResults;
        try
        {
            StartupStage = Strings.Browse_StageSptVersions;
            await AppServices.SptCatalog.EnsureLoadedAsync();

            StartupStage = Strings.Browse_StageCatalog;
            await AppServices.ModCache.EnsureLoadedAsync();

            StartupStage = Strings.Browse_StageAddons;
            await AppServices.Addons.EnsureLoadedAsync();

            StartupStage = Strings.Browse_StageInstalled;
            await RefreshInstalledIndexAsync();

            AppLog.Debug("Browse", "SearchAsync: ModCache ready, applying filter");
            StartupStage = Strings.Browse_StageSorting;
            EnsureSptVersionOptionsBuilt();
            EnsureCategoryOptionsBuilt();
            ApplyFilter();
            HasLoadedResults = true;
        }
        catch (SpModApiException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (OperationCanceledException)
        {
            // HttpClient throws this (not HttpRequestException) on a request timeout.
            StatusMessage = Strings.Browse_TimedOutLoading;
        }
        catch (Exception ex)
        {
            // Last-resort catch-all so the command never gets stuck without an error message.
            StatusMessage = Text(Strings.Browse_UnexpectedLoadFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsStartingUp = false;
            AppLog.Debug("Browse", "SearchAsync: end");
        }
    }

    /// <summary>Forces a fresh fetch of the whole sp-mod.com catalog, bypassing any cache, then re-applies the current filters.</summary>
    [RelayCommand]
    private async Task RefreshCacheAsync()
    {
        AppLog.Debug("Browse", "RefreshCacheAsync: start");
        IsBusy = true;
        try
        {
            await AppServices.ModCache.RefreshAsync();
            await AppServices.Addons.RefreshAsync();
            await RefreshInstalledIndexAsync();
            AppLog.Debug("Browse", "RefreshCacheAsync: ModCache refreshed, applying filter");
            ApplyFilter();
            HasLoadedResults = true;
        }
        catch (SpModApiException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = ApiProblems.Describe(ex);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Strings.Browse_TimedOutRefreshing;
        }
        catch (Exception ex)
        {
            StatusMessage = Text(Strings.Browse_UnexpectedRefreshFormat, ex.Message);
        }
        finally
        {
            IsBusy = false;
            AppLog.Debug("Browse", "RefreshCacheAsync: end");
        }
    }

    //
    // Resets every filter/sort control back to this page's opening default, then re-applies once
    // immediately.
    //
    // "Default" means whatever SaveAsDefault last captured, not the app's own - once you have told
    // the page how you want it to open, that is what clearing the filters should give you back.
    // The SPT version boxes already worked this way, through SptVersionOption.IsDefault; the saved
    // defaults are folded into that same flag as the options are built, so this line is unchanged.
    //
    [RelayCommand]
    private void ClearFilters()
    {
        _suppressAutoApplyFilter = true;
        try
        {
            SearchText = string.Empty;
            foreach (var option in SptVersionOptions) option.IsSelected = option.IsDefault;
            UpdateSptVersionFilterSummary();
            SelectedSortOption = DefaultSortOption();
            PageSize = DefaultPageSize();
            SelectedFeaturedFilter = DefaultFeaturedFilter();
            SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(DefaultCategory()))
                ?? CategoryOptions[0];
            SavedFilterDefaults.ApplyAttributes(AttributeOptions, _defaults?.Attributes ?? []);
            UpdateAttributeFilterSummary();
        }
        finally
        {
            _suppressAutoApplyFilter = false;
        }

        // Only re-run the filter if there's actually a catalog to filter against yet.
        if (HasLoadedResults) ApplyFilter();
    }

    //
    // Captures the page exactly as it currently stands as what it opens at next time. Same
    // arrangement as Installed's - see InstalledViewModel.SaveAsDefault for why this lives on the
    // page rather than as a second set of dropdowns in Options.
    //
    // The search box is left out on purpose. The SPT version ticks are included, and saving them
    // replaces the app's own behaviour of pre-ticking whichever line your install is on - which is
    // the point for anyone who browses for a version they are not currently running.
    //
    [RelayCommand]
    private void SaveAsDefault()
    {
        var service = new SettingsService();
        var settings = service.Load();

        settings.BrowseDefaults = new BrowsePageDefaults
        {
            Sort = SelectedSortOption.Value.ToString(),
            Featured = SelectedFeaturedFilter.Value.ToString(),
            Category = SelectedCategory.Title,
            PageSize = PageSize,
            Attributes = SavedFilterDefaults.CapturedAttributes(AttributeOptions),

            // Only once the options exist. Saving an empty list from a page whose version filter
            // has not been built yet would read back as "show every SPT version", which is a real
            // setting and not what was on screen.
            SptVersions = _sptVersionOptionsBuilt
                ? SptVersionOptions.Where(o => o.IsSelected).Select(o => o.Label).ToList()
                : _defaults?.SptVersions,
        };

        service.Save(settings);

        StatusMessage = Strings.Browse_SavedAsDefault;
        AppLog.Info("Browse", "saved the current filters as this page's default");
    }

    private bool CanGoToPreviousPage() => CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void PreviousPage() => GoToPage(CurrentPage - 1);

    private bool CanGoToNextPage() => CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void NextPage() => GoToPage(CurrentPage + 1);

    // int? because the pager's "..." entries carry no number; they are not clickable, but the
    // command is still asked whether it can run for them.
    [RelayCommand]
    private void GoToPageNumber(int? page)
    {
        if (page is { } number) GoToPage(number);
    }

    /// <summary>Replaces Results with exactly one page's worth of cards - never grows it, so only one page's thumbnails are ever in flight.</summary>
    private void GoToPage(int page)
    {
        var sw = Stopwatch.StartNew();
        CurrentPage = Math.Clamp(page, 1, TotalPages);
        var installedVersion = AppServices.SptEnvironment.InstalledVersion;

        var pins = AppServices.ModLists.GetPins();

        Results.Clear();
        foreach (var mod in _filtered.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
        {
            var installed = FindInstalledMatch(mod);

            var card = ModCardViewModel.From(
                mod, installedVersion, installed, _selectedLines, AppServices.SptCatalog.Releases,
                AppServices.Addons.CountFor(mod.Id),
                installed is null ? null : ModListPlanner.PinKeys(ModListCandidates.From(installed)));

            card.RefreshPin(pins);
            Results.Add(card);
        }

        PageLinks.Clear();
        foreach (var link in PageLink.For(CurrentPage, TotalPages)) PageLinks.Add(link);

        PageChanged?.Invoke(this, EventArgs.Empty);

        AppLog.Debug("Browse", $"GoToPage: page {CurrentPage}/{TotalPages} rendered in {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>Scans the configured SPT install folder and matches it against the cached catalog to drive the
    /// install/update status dot on Browse's cards. Best-effort: no install path or nothing found just means
    /// no dot shows, not an error.</summary>
    private async Task RefreshInstalledIndexAsync()
    {
        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            _installedByGuid = new(StringComparer.OrdinalIgnoreCase);
            _installedByName = new(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var catalog = AppServices.ModCache.AllMods;
        var addons = AppServices.Addons.AllAddons;
        var sptVersion = AppServices.SptEnvironment.InstalledVersion;
        var records = AppServices.InstallManifest.Load().Mods;

        // Scan and match together off the UI thread. The match is the slow half - InstalledViewModel
        // has run it this way since the matching rewrite, and doing it inline here was the last
        // thing left holding the window blank while Browse loaded.
        var matched = await Task.Run(() =>
        {
            var scanned = InstalledModScanner.Scan(installPath);

            // The manifest is passed even though Browse ignores IsAppManaged: it's what lets an
            // app-installed mod resolve to its exact catalog listing rather than a folder-name guess.
            return InstalledModCardViewModel.BuildFrom(scanned, catalog, sptVersion, records, addons)
                // Browse's cards are mods, so addon cards are left out of both indexes below - an
                // addon sharing a mod's name would otherwise mark that mod as installed.
                .Where(m => !m.IsAddon)
                .ToList();
        });

        // Keyed by Guid when available, MatchedModName as a fallback. Only matched entries are indexed.
        _installedByGuid = matched
            .Where(m => !string.IsNullOrWhiteSpace(m.Guid))
            .GroupBy(m => m.Guid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        _installedByName = matched
            .Where(m => !string.IsNullOrWhiteSpace(m.MatchedModName))
            .GroupBy(m => m.MatchedModName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    //
    // After the installed index changes. With Hide installed ticked the result set itself has
    // changed - a mod just installed has to leave it - so the filter runs again, on the same page
    // where there still is one.
    //
    private void ShowInstalledChange()
    {
        if (!IsOn(ModAttributeFilter.HideInstalled) || !HasLoadedResults)
        {
            GoToPage(CurrentPage);
            return;
        }

        var page = CurrentPage;
        ApplyFilter();
        if (page > 1) GoToPage(page);
    }

    // Re-reads the pins for the cards on screen, for a pin made on another page since.
    public void RefreshPins()
    {
        var pins = AppServices.ModLists.GetPins();
        foreach (var card in Results) card.RefreshPin(pins);
    }

    private InstalledModCardViewModel? FindInstalledMatch(Mod mod)
    {
        if (!string.IsNullOrWhiteSpace(mod.Guid) && _installedByGuid.TryGetValue(mod.Guid, out var byGuid))
            return byGuid;

        if (!string.IsNullOrWhiteSpace(mod.Name) && _installedByName.TryGetValue(mod.Name, out var byName))
            return byName;

        return null;
    }

    private void ApplyFilter()
    {
        var sw = Stopwatch.StartNew();
        var query = SearchText.Trim();

        // A leading "@" switches the search text to matching against the mod's author(s) instead
        // of name/teaser/slug - e.g. "@Acidphantasm".
        var authorQuery = query.StartsWith('@') ? query[1..].Trim() : null;

        // Kept for GoToPage, so each card can describe what the mod supports on the ticked lines.
        _selectedLines = SptVersionOptions
            .Where(o => o.IsSelected)
            .Select(o => ExtractMajorMinor(o.Label))
            .Where(v => v is not null)
            .Select(v => (v!.Value.Major, v.Value.Minor))
            .ToList();
        var selectedLines = _selectedLines;
        var featured = SelectedFeaturedFilter.Value;

        var matched = AppServices.ModCache.AllMods
            // This app's own sp-mod.com listing is hidden here rather than dropped from the cached
            // catalog, which the self-updater still needs to be able to read. It just has no
            // business appearing among the mods this app installs into an SPT folder: it isn't a
            // mod, and installing it from here would drop a second copy of the manager into
            // BepInEx\plugins where SPT would try to load it.
            .Where(m => !string.Equals(m.Id.ToString(), SelfMod.ModId, StringComparison.Ordinal))
            .Where(m => authorQuery is not null
                ? MatchesAuthor(m, authorQuery)
                : query.Length == 0 || Matches(m.Name, query) || Matches(m.Teaser, query) || Matches(m.Slug, query))
            .Where(m => selectedLines.Count == 0 || MatchesSptVersionFilter(m, selectedLines))
            .Where(m => featured switch
            {
                FeaturedFilter.Only => m.Featured == true,
                FeaturedFilter.Exclude => m.Featured != true,
                _ => true, // Include - no restriction
            })
            .Where(m => SelectedCategory.Title is not { } category
                || string.Equals(m.Category?.Title, category, StringComparison.OrdinalIgnoreCase))
            .Where(m => !IsOn(ModAttributeFilter.FikaCompatible) || m.FikaCompatibility == true)
            .Where(m => !IsOn(ModAttributeFilter.HideAds) || m.ContainsAds != true)
            .Where(m => !IsOn(ModAttributeFilter.HideAiContent) || m.ContainsAiContent != true)
            .Where(m => !IsOn(ModAttributeFilter.HasAddons) || AppServices.Addons.CountFor(m.Id) > 0)
            .Where(m => !IsOn(ModAttributeFilter.HideInstalled) || FindInstalledMatch(m) is null)
            // Only mods already known to have dependencies. A mod nobody has looked at yet is not
            // claimed either way, so it drops out of this filter rather than being asserted clean.
            .Where(m => !IsOn(ModAttributeFilter.HasDependencies)
                || DependencyBadgeLoader.KnownHasDependencies(m) == true);

        var installedSptVersion = AppServices.SptEnvironment.InstalledVersion;

        _filtered = SelectedSortOption.Value switch
        {
            ModSortOrder.LastUpdated => matched.OrderByDescending(m => LastUpdatedDate(m, installedSptVersion)).ToList(),
            ModSortOrder.MostDownloaded => matched.OrderByDescending(m => m.Downloads ?? 0).ToList(),
            ModSortOrder.MostFavourited => matched.OrderByDescending(m => m.FavouritesCount ?? 0).ToList(),
            // Endorsements are new enough that only a few dozen mods have any at all and the counts
            // are in the low tens, so the overwhelming majority tie on 0. Downloads break the tie to
            // keep that long tail in a sensible order instead of an arbitrary one.
            ModSortOrder.MostEndorsed => matched
                .OrderByDescending(m => m.EndorsementsCount ?? 0)
                .ThenByDescending(m => m.Downloads ?? 0)
                .ToList(),
            // Newest - by the most recent release that runs on the installed SPT, so a mod that
            // shipped an update today sorts above an older mod that happened to be created more
            // recently, while an update for an SPT line this install isn't on doesn't count as new.
            _ => matched.OrderByDescending(m => NewestReleaseDate(m, installedSptVersion)).ToList(),
        };
        AppLog.Debug("Browse", $"ApplyFilter: filter/sort took {sw.ElapsedMilliseconds}ms over {AppServices.ModCache.AllMods.Count} cached mods, {_filtered.Count} matched");

        // A fresh search always jumps back to page 1 of the new result set.
        TotalPages = Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));
        GoToPage(1);

        // Steam's "N entries matching filters" line under the title. A message from before the
        // filter ran (queued, saved, failed) is left where it is below it.
        EntriesText = _filtered.Count switch
        {
            0 => Strings.Browse_NoMatches,
            _ => Strings.Browse_CountFound(_filtered.Count),
        };

        // Said plainly rather than left for someone to work out from a short list.
        if (IsOn(ModAttributeFilter.HasDependencies))
        {
            EntriesText = string.Join(
                Strings.Common_SentenceSeparator,
                EntriesText,
                Strings.Browse_DependencyNote);
        }
    }

    /// <summary>True if any of the mod's cached versions actually runs on one of the selected SPT
    /// release lines. Checking every cached version rather than only the newest is what keeps a mod
    /// whose latest release targets 4.1 visible under a 4.0 filter when it still has a 4.0 release.
    /// A mod with no cached version data is never hidden.</summary>
    /// <summary>The publish date of the version this mod's card represents - the newest cached
    /// version that runs on the installed SPT, or the newest overall when none of them do - falling
    /// back to the mod's own dates when it carries no usable version data. Dating a mod by a release
    /// that doesn't run on this install is what let a 4.1-only update sort above a mod that shipped
    /// a usable update more recently.</summary>
    internal static DateTimeOffset NewestReleaseDate(Mod mod, string? installedSptVersion)
    {
        if (ModCardViewModel.PickDisplayVersion(mod, installedSptVersion)?.PublishedAt is { } shown)
            return shown;

        var newest = (mod.Versions ?? [])
            .Select(v => v.PublishedAt)
            .Where(d => d is not null)
            .DefaultIfEmpty(null)
            .Max();

        return newest ?? mod.PublishedAt ?? mod.CreatedAt ?? DateTimeOffset.MinValue;
    }

    /// <summary>The date "Recently updated" sorts by. The Forge's own record date stands while the
    /// mod's newest release is one that runs on the installed SPT - it moves for description and
    /// metadata edits too, which is part of what this sort is for - but when the newest release
    /// targets a line this install isn't on, that date is describing an update that can't be used
    /// here, so the newest usable release's date is used instead.</summary>
    internal static DateTimeOffset LastUpdatedDate(Mod mod, string? installedSptVersion)
    {
        var newest = ModCardViewModel.LatestVersion(mod);

        var newestRunsHere = newest is not null
            && SptVersionMatcher.IsSatisfiedBy(newest.SptVersionConstraint, installedSptVersion) == true;

        if (newest is null || newestRunsHere)
            return mod.UpdatedAt ?? NewestReleaseDate(mod, installedSptVersion);

        return NewestReleaseDate(mod, installedSptVersion);
    }

    private static bool MatchesSptVersionFilter(Mod mod, List<(int Major, int Minor)> selectedLines)
    {
        var versions = mod.Versions ?? [];
        if (versions.Count == 0 || selectedLines.Count == 0) return true;

        var readable = versions
            .Select(v => v.SptVersionConstraint)
            .Where(c => SptVersionRange.TryParse(c, out _))
            .ToList();

        // Only when nothing about this mod is readable do we give it the benefit of the doubt.
        // Doing that per-version let one blank constraint pull a mod through every filter.
        if (readable.Count == 0) return true;

        return selectedLines.Any(line =>
            readable.Any(c => SptVersionRange.IntersectsReleaseLine(c, line.Major, line.Minor)));
    }

    /// <summary>Builds the checkable SPT version list from the release lines sp-mod.com publishes,
    /// once per session. Using the published list rather than scraping mod constraints keeps
    /// boundary versions nobody ever shipped (e.g. "4.0.4" from a "~4.0.4" constraint) out of the
    /// dropdown. The option matching the detected install starts checked; the rest start unchecked.</summary>
    private void EnsureSptVersionOptionsBuilt()
    {
        if (_sptVersionOptionsBuilt) return;

        var lines = AppServices.SptCatalog.Lines;
        if (lines.Count == 0) return; // Release list not loaded yet; try again on the next search.

        _sptVersionOptionsBuilt = true;

        var installedVersion = AppServices.SptEnvironment.InstalledVersion;
        var installedMajorMinor = ExtractMajorMinor(installedVersion);

        var majorMinors = lines.Select(l => $"{l.Major}.{l.Minor}").ToList();

        if (installedMajorMinor is not null && !majorMinors.Contains(installedMajorMinor.Value.Label))
            majorMinors.Insert(0, installedMajorMinor.Value.Label);

        //
        // A saved default replaces the pre-ticking entirely rather than adding to it, which is what
        // lets an empty saved list mean "browse every version". Null means nothing was saved, and
        // the app's own behaviour - pre-tick whichever line this install is on - stands.
        //
        var saved = _defaults?.SptVersions;

        foreach (var label in majorMinors)
        {
            var isInstalled = label == installedMajorMinor?.Label;

            // Also becomes the option's IsDefault, which is what Clear filters puts back.
            var isSelected = saved is null ? isInstalled : saved.Contains(label);

            // The installed option uses the exact detected version; every other option uses ".0"
            // as that release line's representative version.
            var value = isInstalled && !string.IsNullOrWhiteSpace(installedVersion) ? installedVersion! : $"{label}.0";
            var option = new SptVersionOption(label, value, isSelected);
            option.PropertyChanged += (_, _) =>
            {
                UpdateSptVersionFilterSummary();
                AutoApplyFilter();
            };
            SptVersionOptions.Add(option);
        }

        UpdateSptVersionFilterSummary();
    }

    // "Any mod", the one option's own label, or a count. Same shape as the SPT version summary.
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
    // Rebuilt from the catalog rather than hardcoded, so the list is whatever The Forge is
    // currently using. Called after the catalog loads; the current selection is kept if it's still
    // a category that exists.
    //
    private void EnsureCategoryOptionsBuilt()
    {
        if (_categoryOptionsBuilt) return;
        _categoryOptionsBuilt = true;

        var categories = AppServices.ModCache.AllMods
            .Select(m => m.Category?.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

        foreach (var category in categories) CategoryOptions.Add(new CategoryFilterItem(category, category));

        // Now that the list exists, the saved default can be resolved against it. Only on the first
        // build, which is the only one there is - a category chosen since must not be overridden.
        if (!_categoryDefaultApplied)
        {
            _categoryDefaultApplied = true;
            SelectedCategory = CategoryOptions.FirstOrDefault(c => c.SameAs(DefaultCategory()))
                ?? CategoryOptions[0];
        }
    }

    private bool _categoryOptionsBuilt;

    private void UpdateSptVersionFilterSummary()
    {
        // Carries its own "SPT" now: the leading label that used to supply it is gone, and
        // "All versions" on its own doesn't say versions of what.
        var selected = SptVersionOptions.Where(o => o.IsSelected).Select(o => o.Label).ToList();
        SptVersionFilterSummary = selected.Count switch
        {
            0 => Strings.Browse_AllSptVersions,
            <= 3 => Text(
                Strings.Browse_SptVersionsFormat,
                string.Join(Strings.Common_ListSeparator, selected)),
            _ => Text(Strings.Browse_SptVersionCountFormat, selected.Count),
        };
    }

    /// <summary>Pulls the major.minor out of a version or constraint string, ignoring any leading
    /// operator (^, ~, &gt;=, etc.) - e.g. "^3.9.0" and "3.9.4" both yield (3, 9).</summary>
    private static (int Major, int Minor, string Label)? ExtractMajorMinor(string? versionOrConstraint)
    {
        if (string.IsNullOrWhiteSpace(versionOrConstraint)) return null;

        var match = MajorMinorPattern.Match(versionOrConstraint);
        if (!match.Success) return null;

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        return (major, minor, $"{major}.{minor}");
    }

    private static readonly Regex MajorMinorPattern = new(@"(\d+)\.(\d+)", RegexOptions.Compiled);

    private static bool Matches(string? haystack, string needle) =>
        haystack?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Matches a "@name" query against the mod's owner and any additional authors. An
    /// empty query matches everything, same as an empty plain-text query.</summary>
    private static bool MatchesAuthor(Mod mod, string authorQuery) =>
        authorQuery.Length == 0
        || Matches(mod.Owner?.Name, authorQuery)
        || (mod.AdditionalAuthors?.Any(a => Matches(a.Name, authorQuery)) ?? false);

    /// <summary>Queues <paramref name="card"/>'s mod for download and install. Picks the newest version
    /// that targets the installed SPT version when known, otherwise the newest version overall. The
    /// version's download link is resolved lazily once the queue reaches this item, so clicking
    /// Install never waits on a network call. Gated on ReadModPageConfirmationWindow first; declining
    /// leaves the card alone.</summary>
    [RelayCommand]
    private void Install(ModCardViewModel? card) => QueueForDownload(card, DownloadAction.Install);

    /// <summary>Re-queues an already-installed mod's currently displayed version - the same pick
    /// Install would make - for a fresh download and reinstall. Shown on the card in Install's place
    /// once a mod is installed, e.g. to recover from corrupted or hand-edited files.</summary>
    [RelayCommand]
    private void Redownload(ModCardViewModel? card) => QueueForDownload(card, DownloadAction.Redownload);

    // Which of the two buttons asked, rather than the word one of them is labelled with: the
    // cancellation message is a whole sentence per action, not a verb dropped into a shared one.
    private enum DownloadAction
    {
        Install,
        Redownload,
    }

    private void QueueForDownload(ModCardViewModel? card, DownloadAction action)
    {
        if (card is null) return;

        var installPath = AppServices.SptEnvironment.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            StatusMessage = AppMessages.NoSptInstallFolder;
            return;
        }

        var mod = card.Mod;

        // Same rule as the Installed page: a disabled mod's install record points at folders it no
        // longer occupies, so reinstalling over it would place files where nothing loads them and
        // leave the disabled copy behind as a duplicate.
        if (card.IsDisabled)
        {
            StatusMessage = Text(Strings.Browse_DisabledFormat, mod.Name);
            return;
        }

        if (!ReadModPageConfirmationWindow.Confirm(mod.Name ?? Strings.Browse_ThisMod, mod.DetailUrl))
        {
            StatusMessage = Text(
                action == DownloadAction.Install
                    ? Strings.Browse_InstallCancelledFormat
                    : Strings.Browse_RedownloadCancelledFormat,
                mod.Name);
            return;
        }

        var installedSptVersion = AppServices.SptEnvironment.InstalledVersion;

        // Same pick the card displays, so the queued version is the one it advertised.
        var chosen = ModCardViewModel.PickDisplayVersion(mod, installedSptVersion);

        if (chosen?.Version is null)
        {
            StatusMessage = Text(Strings.Browse_NoVersionFormat, mod.Name);
            return;
        }

        var chosenVersion = chosen.Version;
        AppServices.DownloadQueue.Enqueue(InstallTarget.For(mod), chosenVersion, installPath, () => ResolveVersionLinkAsync(mod, chosenVersion));
        StatusMessage = Text(Strings.Browse_QueuedFormat, mod.Name, chosenVersion);
    }

    /// <summary>Resolves the full ModVersion (with its download Link) for exactly one version string.
    /// Called lazily from the queue so queueing itself never waits on a network call.</summary>
    private async Task<ModVersion?> ResolveVersionLinkAsync(Mod mod, string version)
    {
        var versions = await _spModApi.GetModVersionsAsync(
            mod.Id.ToString(), new ModVersionsQuery { FilterVersion = version, PerPage = 5 });
        return versions.Data.FirstOrDefault(v => v.Version == version) ?? versions.Data.FirstOrDefault();
    }

    //
    // Entry points for the Workshop home page and the Workshop item page, which show the same
    // catalog through the same cards and install through the same queue as this page.
    //

    /// <summary>Loads the catalog, addons and installed index if this session has not yet.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (!HasLoadedResults) await SearchCommand.ExecuteAsync(null);
    }

    /// <summary>A card for any catalog mod, matched against the install exactly as this page's own
    /// cards are.</summary>
    public ModCardViewModel BuildCard(Mod mod)
    {
        var installed = FindInstalledMatch(mod);

        var card = ModCardViewModel.From(
            mod, AppServices.SptEnvironment.InstalledVersion, installed, _selectedLines,
            AppServices.SptCatalog.Releases, AppServices.Addons.CountFor(mod.Id),
            installed is null ? null : ModListPlanner.PinKeys(ModListCandidates.From(installed)));

        card.RefreshPin(AppServices.ModLists.GetPins());
        return card;
    }

    /// <summary>The installed copy of a catalog mod, if this install has one.</summary>
    public InstalledModCardViewModel? InstalledMatchFor(Mod mod) => FindInstalledMatch(mod);

    /// <summary>Every mod this page could show, before any filter - what the home page ranks.</summary>
    public IEnumerable<Mod> Catalog => AppServices.ModCache.AllMods
        .Where(m => !string.Equals(m.Id.ToString(), SelfMod.ModId, StringComparison.Ordinal));

    /// <summary>Opens the results searched for <paramref name="text"/> - the home page's search box.</summary>
    public void ShowSearch(string text) => SearchText = text.Trim();

    /// <summary>Opens the results in one sort order - the home page's "View all".</summary>
    public void ShowSortedBy(ModSortOrder order) =>
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.Value == order) ?? SelectedSortOption;

    /// <summary>Opens one category - the home page's content type list.</summary>
    public void ShowCategory(string title) =>
        SelectedCategory = CategoryOptions.FirstOrDefault(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase))
            ?? SelectedCategory;

    /// <summary>The sort panel's radio rows.</summary>
    [RelayCommand]
    private void SelectSort(SortOptionItem? option)
    {
        if (option is not null) SelectedSortOption = option;
    }

    public async Task LoadDetailsAsync(Mod mod)
    {
        try
        {
            var details = await _spModApi.GetModAsync(mod.Id.ToString(), include: "versions,license,category");

            // The installed version is what this mod's addons check their own constraints against.
            AppServices.ModDetailsOverlay.Show(details, FindInstalledMatch(mod)?.InstalledVersion);
        }
        catch (SpModApiException ex)
        {
            StatusMessage = Text(Strings.Browse_DetailsFailedFormat, mod.Name, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = Text(Strings.Browse_DetailsNetworkFormat, mod.Name, ex.Message);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Text(Strings.Browse_DetailsTimedOutFormat, mod.Name);
        }
        catch (Exception ex)
        {
            // Last-resort catch-all so a failure here doesn't silently look like a no-op click.
            StatusMessage = Text(Strings.Browse_DetailsUnexpectedFormat, mod.Name, ex.Message);
        }
    }
}
