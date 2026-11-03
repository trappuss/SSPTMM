using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.ViewModels;

// The four lists on the Workshop front page, in Steam's order.
public enum WorkshopHomeTab
{
    TopRated,
    MostSubscribed,
    LastUpdated,
    New,
}

// One content type on the front page's sidebar, with how many mods carry it.
public sealed record WorkshopCategoryCount(string Title, int Count);

//
// The Workshop's front page (steamcommunity.com/app/<id>/workshop/): a row of what is new this week,
// then the catalog four ways - Top Rated, Most Subscribed, Last Updated, New - beside a list of
// content types.
//
// Everything is ranked from the same cached catalog and turned into the same cards Browse uses, so a
// mod looks and installs the same wherever it is opened from. Fork (1.4.6): addons too - they are
// ranked among the mods wherever what they are ranked by is theirs as much as a mod's (released this
// week, downloads, dates), and have a row of their own: the ones made for the mods you have. Ranking is only ever by what
// sp-mod.com actually records: Steam's "Most Popular" is a weekly popularity measure the catalog does
// not have, so the first tab is Top Rated (endorsements), labelled as what it is.
//
public sealed partial class WorkshopHomeViewModel : LocalizedViewModel, IModActionHost
{
    void IModActionHost.ShowActionMessage(string? message) => Message = message;

    // Ten per list, as Steam's front page shows; the carousel holds a few pages of six.
    private const int ListSize = 10;
    private const int FeaturedSize = 24;
    private static readonly TimeSpan FeaturedWindow = TimeSpan.FromDays(7);

    public ObservableCollection<ModCardViewModel> Featured { get; } = [];

    public ObservableCollection<ModCardViewModel> TabItems { get; } = [];

    // Steam's "From Followed Authors": the newest items by the authors followed, six of them.
    public ObservableCollection<ModCardViewModel> FromFollowed { get; } = [];

    public bool HasFromFollowed => FromFollowed.Count > 0;

    private const int FollowedSize = 6;

    // Fork (1.4.6): "Addons for your mods" - six across, as the followed row.
    public ObservableCollection<ModCardViewModel> AddonsForYou { get; } = [];

    public bool HasAddonsForYou => AddonsForYou.Count > 0;

    private const int AddonsForYouSize = 6;

    // A result on this page: a mod, or an addon with the mod it is for.
    private sealed record HomeItem(Mod Mod, Addon? Addon);

    public ObservableCollection<WorkshopCategoryCount> Categories { get; } = [];

    [ObservableProperty]
    private WorkshopHomeTab _selectedTab = WorkshopHomeTab.TopRated;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Why a card did not open, or why the catalog did not load - under the navigation.</summary>
    [ObservableProperty]
    private string? _message;

    // The line under the tabs saying what the list is ranked by.
    public string TabDescription => SelectedTab switch
    {
        WorkshopHomeTab.MostSubscribed => Strings.WorkshopHome_MostSubscribedNote,
        WorkshopHomeTab.LastUpdated => Strings.WorkshopHome_LastUpdatedNote,
        WorkshopHomeTab.New => Strings.WorkshopHome_NewNote,
        _ => Strings.WorkshopHome_TopRatedNote,
    };

    public bool HasFeatured => Featured.Count > 0;

    partial void OnSelectedTabChanged(WorkshopHomeTab value)
    {
        OnPropertyChanged(nameof(TabDescription));
        FillTab();
    }

    public WorkshopHomeViewModel()
    {
        // A mod installed or removed elsewhere changes what these cards should say about it - once
        // Browse has rebuilt the index the cards are matched against.
        AppServices.Browse.InstalledIndexChanged += (_, _) => Refresh();
        AppServices.Followed.Changed += (_, _) => Refresh();
    }

    private bool _loaded;

    /// <summary>Loads the catalog through Browse (once per session) and fills every list.</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            Refresh();
            return;
        }

        IsLoading = true;
        try
        {
            await AppServices.Browse.EnsureLoadedAsync();
            _loaded = AppServices.Browse.HasLoadedResults;

            // The load reports its failures in Browse's status line, which is not on this page.
            Message = _loaded ? null : AppServices.Browse.StatusMessage;
            Refresh();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Refresh()
    {
        if (!AppServices.Browse.HasLoadedResults) return;

        var catalog = AppServices.Browse.Catalog.ToList();
        var addons = AppServices.Browse.AddonCatalog.ToList();
        var installedSpt = AppServices.SptEnvironment.InstalledVersion;
        var since = DateTimeOffset.Now - FeaturedWindow;

        // Released in the past week, the most downloaded first - the nearest honest reading of
        // Steam's "in the past week" row the catalog allows.
        var featured = catalog
            .Where(m => BrowseViewModel.NewestReleaseDate(m, installedSpt) >= since)
            .Select(m => (Item: new HomeItem(m, null), Downloads: m.Downloads ?? 0))
            .Concat(addons
                .Where(a => BrowseViewModel.AddonNewestReleaseDate(a.Addon) >= since)
                .Select(a => (Item: new HomeItem(a.Parent, a.Addon), Downloads: a.Addon.Downloads ?? 0)))
            .OrderByDescending(x => x.Downloads)
            .Select(x => x.Item)
            .Take(FeaturedSize);

        Replace(Featured, featured);
        OnPropertyChanged(nameof(HasFeatured));

        // Addons for mods this install has, that it doesn't have yet: the ones that fit the
        // installed mod first, then the most downloaded.
        var forYou = addons
            .Where(a => !AppServices.Browse.IsAddonInstalled(a.Addon.Id) && AppServices.Browse.InstalledMatchFor(a.Parent) is not null)
            .Select(a => AppServices.Browse.BuildAddonCard(a.Addon, a.Parent))
            .OrderByDescending(c => c.IsCompatibleWithInstalledSpt == true)
            .ThenByDescending(c => c.Downloads ?? 0)
            .Take(AddonsForYouSize)
            .ToList();

        AddonsForYou.Clear();
        foreach (var card in forYou) AddonsForYou.Add(card);
        OnPropertyChanged(nameof(HasAddonsForYou));

        // "Recently posted items from authors you follow": by when they were posted, not updated.
        var followed = AppServices.Followed.HasAny
            ? catalog
                .Where(AppServices.Followed.IsByFollowed)
                .OrderByDescending(m => m.PublishedAt ?? m.CreatedAt ?? DateTimeOffset.MinValue)
                .Take(FollowedSize)
            : [];

        Replace(FromFollowed, followed.Select(m => new HomeItem(m, null)));
        OnPropertyChanged(nameof(HasFromFollowed));

        var categories = catalog
            .Where(m => !string.IsNullOrWhiteSpace(m.Category?.Title))
            .GroupBy(m => m.Category!.Title!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new WorkshopCategoryCount(g.Key, g.Count()))
            .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase);

        Categories.Clear();
        foreach (var category in categories) Categories.Add(category);

        FillTab();
    }

    private void FillTab()
    {
        if (!AppServices.Browse.HasLoadedResults) return;

        var catalog = AppServices.Browse.Catalog;
        var installedSpt = AppServices.SptEnvironment.InstalledVersion;

        // The same orderings Browse sorts by, so "View all" continues exactly this list - addons
        // among the mods, as there. Top Rated has none: an addon has no endorsements.
        var mods = catalog.Select(m => new HomeItem(m, null));
        var all = mods.Concat(AppServices.Browse.AddonCatalog.Select(a => new HomeItem(a.Parent, a.Addon)));
        var ranked = SelectedTab switch
        {
            WorkshopHomeTab.MostSubscribed => all.OrderByDescending(i => i.Addon?.Downloads ?? i.Mod.Downloads ?? 0),
            WorkshopHomeTab.LastUpdated => all.OrderByDescending(i => i.Addon is { } addon
                ? BrowseViewModel.AddonLastUpdatedDate(addon)
                : BrowseViewModel.LastUpdatedDate(i.Mod, installedSpt)),
            WorkshopHomeTab.New => all.OrderByDescending(i => i.Addon is { } addon
                ? BrowseViewModel.AddonNewestReleaseDate(addon)
                : BrowseViewModel.NewestReleaseDate(i.Mod, installedSpt)),
            _ => mods
                .OrderByDescending(i => i.Mod.EndorsementsCount ?? 0)
                .ThenByDescending(i => i.Mod.Downloads ?? 0),
        };

        Replace(TabItems, ranked.Take(ListSize));
    }

    private static void Replace(ObservableCollection<ModCardViewModel> target, IEnumerable<HomeItem> items)
    {
        var list = items.ToList();

        // The mods' cards in one go, for the pins read once (BuildCards); each addon's in its place.
        var modCards = new Queue<ModCardViewModel>(AppServices.Browse.BuildCards(list.Where(i => i.Addon is null).Select(i => i.Mod)));

        target.Clear();
        foreach (var item in list)
        {
            target.Add(item.Addon is { } addon ? AppServices.Browse.BuildAddonCard(addon, item.Mod) : modCards.Dequeue());
        }
    }

    [RelayCommand]
    private void SelectTab(WorkshopHomeTab tab) => SelectedTab = tab;

    // "View All" on the week's row: everything, most recently released first.
    [RelayCommand]
    private void ViewAllFeatured() => OpenBrowse(() => AppServices.Browse.ShowSortedBy(ModSortOrder.Newest));

    // "View all" under a tab: Browse in the same order the tab uses.
    [RelayCommand]
    private void ViewAllTab() => OpenBrowse(() => AppServices.Browse.ShowSortedBy(SelectedTab switch
    {
        WorkshopHomeTab.MostSubscribed => ModSortOrder.MostDownloaded,
        WorkshopHomeTab.LastUpdated => ModSortOrder.LastUpdated,
        WorkshopHomeTab.New => ModSortOrder.Newest,
        _ => ModSortOrder.MostEndorsed,
    }));

    [RelayCommand]
    private void OpenCategory(WorkshopCategoryCount? category)
    {
        if (category is not null) OpenBrowse(() => AppServices.Browse.ShowCategory(category.Title));
    }

    [RelayCommand]
    private void Search() => OpenBrowse(() => AppServices.Browse.ShowSearch(SearchText));

    [RelayCommand]
    private async Task OpenAsync(ModCardViewModel? card)
    {
        if (card is null) return;

        // Browse's status line, where a failure also goes, is not on this page.
        Message = await AppServices.Browse.OpenCardAsync(card);
    }

    // Fork (1.4.6): "View All" on the addons row - every addon in Browse, the most downloaded first.
    [RelayCommand]
    private static void ViewAllAddons() => OpenBrowse(AppServices.Browse.ShowAddons);

    // The followed row's View All: Browse, Created by Followed, newest first.
    [RelayCommand]
    private static void ViewAllFollowed() => OpenBrowse(AppServices.Browse.ShowFollowed);

    private static void OpenBrowse(Action arrange)
    {
        arrange();
        AppNavigation.Navigate(typeof(BrowsePage));
    }
}
