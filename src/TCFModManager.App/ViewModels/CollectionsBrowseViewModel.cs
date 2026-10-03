using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.ViewModels;

// One card on Browsing: Collections - one of sp-mod.com's public lists.
public sealed partial class CollectionCardViewModel(SpModListSummary summary, string? installedSpt, bool subscribed) : LocalizedViewModel
{
    public SpModListSummary Summary { get; } = summary;

    public string Title => Summary.Title;

    public string? ByText => Summary.Author is { } author ? LocalizationService.Text(Strings.Collections_ByFormat, author) : null;

    public string? Teaser => Summary.Teaser;

    public string ContainsText => Strings.Collections_Contains(Summary.ItemCount, Summary.ItemCount);

    public string? Cover => Summary.Cover;

    public bool HasCover => Summary.Cover is not null;

    public string? SptVersion => Summary.SptVersion;

    // Made for the SPT version installed here - the badge is lit then.
    public bool IsForInstalledSpt =>
        Summary.SptVersion is { } spt && installedSpt is not null && string.Equals(spt, installedSpt.Trim(), StringComparison.OrdinalIgnoreCase);

    public string? SptToolTip => Summary.SptVersion is { } spt ? LocalizationService.Text(Strings.Collection_ForSptFormat, spt) : null;

    // Subscribed to from here before: this install holds a copy of it.
    [ObservableProperty]
    private bool _isSubscribed = subscribed;

    // ------------------------------------------------------------------ the hover popup

    // The description in the popup: the card's start of it, then the list's own once read.
    [ObservableProperty]
    private string? _popupDescription = summary.Teaser;

    // The first items' pictures, and "+N" for the rest - empty until the list's page is read.
    public ObservableCollection<CollectionPreviewTile> PreviewTiles { get; } = [];

    public bool HasPreview => PreviewTiles.Count > 0;

    // Steam's: ten items and "+N" once there are more than eleven; eleven or fewer, all of them.
    private const int Tiles = 11;

    public void ShowPreview(SpModListDetails details)
    {
        if (details.DescriptionHtml is { } html
            && TCFModManager.Core.Markup.SpModMarkup.PlainText(TCFModManager.Core.Markup.SpModMarkup.Parse(html)) is { Length: > 0 } text)
        {
            PopupDescription = text;
        }

        var catalog = AppServices.ModCache.AllMods;
        var pictures = details.Items
            .Select(i => catalog.FirstOrDefault(m => m.Id == i.ModId)?.Thumbnail is { Length: > 0 } t ? t : i.Thumbnail)
            .ToList();

        // Every entry, those no longer available included - the card's "Contains N items" counts
        // them, and "+N" has to add up to it.
        var total = Math.Max(Summary.ItemCount, pictures.Count + details.Unavailable);

        PreviewTiles.Clear();
        var shown = total > Tiles ? Math.Min(Tiles - 1, pictures.Count) : pictures.Count;
        foreach (var picture in pictures.Take(shown)) PreviewTiles.Add(new CollectionPreviewTile(picture, null));
        if (total > shown)
            PreviewTiles.Add(new CollectionPreviewTile(null, LocalizationService.Text(Strings.Collections_MoreItemsFormat, total - shown)));

        OnPropertyChanged(nameof(HasPreview));
    }
}

/// <summary>One square in a collection's hover popup: an item's picture, or "+N" for the rest.</summary>
public sealed record CollectionPreviewTile(string? Picture, string? MoreText)
{
    public bool IsMore => MoreText is not null;
}

// One choice in the SPT version filter: every version, or one.
public sealed class CollectionSptChoice(int? id, string? version) : LocalizedViewModel
{
    public int? Id { get; } = id;

    public string? Version { get; } = version;

    public string Label => Version ?? Strings.Collections_AllSptVersions;

    public override string ToString() => Label;
}

public enum CollectionSort
{
    MostRecent,
    Oldest,
    TitleAscending,
    TitleDescending,
    MostItems,
    FewestItems,
}

// One entry of the collections' sort order: holds a key, so a language change relabels it in place.
public sealed class CollectionSortOption(string key, CollectionSort value) : LocalizedViewModel
{
    public CollectionSort Value { get; } = value;

    public string Label => LocalizationService.Get(key);

    public override string ToString() => Label;
}

//
// Browsing: Collections - Steam's collections browse page, over sp-mod.com's public lists
// (sp-mod.com/lists).
//
// sp-mod.com orders its lists newest first and nothing else, and its search does not look at who
// made a list. So the page works from every list at once (SpModListIndex: read page by page, kept
// for an hour) and does the rest here: other orders, a search that also finds a maker's name, the
// SPT version filter, Steam's page sizes and its infinite list. The site's own search is asked too
// and its answers added in - it reads whole descriptions, where the cards carry only their start.
// Until the lists are all read (a few seconds, the first time in an hour), the newest page shows as
// sp-mod.com gives it. A list's address pasted into the search box opens that list.
//
public sealed partial class CollectionsBrowseViewModel : LocalizedViewModel
{
    public ObservableCollection<CollectionCardViewModel> Lists { get; } = [];

    public ObservableCollection<CollectionSptChoice> SptChoices { get; } = [new(null, null)];

    public ObservableCollection<PageLink> PageLinks { get; } = [];

    public List<CollectionSortOption> SortOptions { get; } =
    [
        new(nameof(Strings.Collections_SortMostRecent), CollectionSort.MostRecent),
        new(nameof(Strings.Collections_SortOldest), CollectionSort.Oldest),
        new(nameof(Strings.Sort_NameAscending), CollectionSort.TitleAscending),
        new(nameof(Strings.Sort_NameDescending), CollectionSort.TitleDescending),
        new(nameof(Strings.Collections_SortMostItems), CollectionSort.MostItems),
        new(nameof(Strings.Collections_SortFewestItems), CollectionSort.FewestItems),
    ];

    // Browse's page sizes, Infinite first, and the size the infinite list grows by.
    public List<int> PageSizeOptions { get; } = [BrowseViewModel.InfinitePageSize, 10, 15, 30, 50];

    private const int InfiniteStep = 30;

    [ObservableProperty]
    private CollectionSortOption _selectedSortOption;

    [ObservableProperty]
    private int _pageSize = BrowseViewModel.InfinitePageSize;

    public bool IsInfinite => PageSize == BrowseViewModel.InfinitePageSize;

    [ObservableProperty]
    private CollectionSptChoice? _selectedSpt;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _lastPage = 1;

    [ObservableProperty]
    private string? _entriesText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches))]
    private bool _isLoading;

    // "Reading collections from sp-mod.com... 5 of 27", while the lists are read.
    [ObservableProperty]
    private string? _progressText;

    [ObservableProperty]
    private string? _statusMessage;

    // The infinite list has more cards to add.
    [ObservableProperty]
    private bool _hasMore;

    public bool ShowNoMatches => !IsLoading && _index is not null && Lists.Count == 0 && StatusMessage is null;

    public bool HasPages => !IsInfinite && LastPage > 1;

    public CollectionsBrowseViewModel()
    {
        _selectedSpt = SptChoices[0];
        _selectedSortOption = SortOptions[0];
        _pageSize = PageSizeMemory.Load(PageSizeMemory.CollectionsBrowse, null, PageSizeOptions); // Fork: Infinite, or the size last picked
    }

    /// <summary>Raised when a new page of cards is showing, so the page can scroll to them.</summary>
    public event EventHandler? NavigatedToPage;

    // ------------------------------------------------------------------ the lists

    private SpModListIndexData? _index;
    private Task? _reading;
    private readonly SpModListIndex _store = new(SpModListIndex.DefaultPath);

    // Every list matching the page's filters, in its order; the cards shown are a page of these.
    private List<SpModListSummary> _matching = [];

    // What sp-mod.com's own search found, by search text: added to what the cards' text matches.
    private readonly Dictionary<string, HashSet<int>> _siteMatches = new(StringComparer.OrdinalIgnoreCase);

    private bool _started;

    /// <summary>The lists, once per session - the page calls this each time it is shown, and the
    /// cards' Subscribed marks are brought up to date then (a collection subscribed to since).</summary>
    public Task EnsureLoadedAsync()
    {
        if (_started)
        {
            var stored = StoredIds();
            foreach (var card in Lists) card.IsSubscribed = stored.Contains(PublicCollections.IdFor(card.Summary.Id));

            // An hour on, read again in the background; the cards stay as they are meanwhile.
            if (_index is not null && !SpModListIndex.IsFresh(_index, DateTimeOffset.UtcNow) && _reading is null)
                _reading = ReadAllAsync(quiet: true);
            return Task.CompletedTask;
        }

        _started = true;
        return StartAsync();
    }

    private async Task StartAsync()
    {
        // Kept from before: shown at once, and read again behind it when it is over an hour old.
        var kept = await Task.Run(_store.Load);
        if (kept is { Lists.Count: > 0 })
        {
            UseIndex(kept);
            if (!SpModListIndex.IsFresh(kept, DateTimeOffset.UtcNow)) _reading = ReadAllAsync(quiet: true);
            return;
        }

        // The first time: sp-mod.com's newest page while the rest are read.
        _reading = ReadAllAsync(quiet: false);
        await ShowSiteFirstPageAsync();
    }

    private async Task ShowSiteFirstPageAsync()
    {
        IsLoading = true;
        try
        {
            var first = await AppServices.SpModLists.BrowseAsync(1);
            if (_index is not null) return;

            SyncSptChoices(first.SptOptions);
            ShowCards(first.Lists);
            EntriesText = first.Total == 0 ? null : Strings.Browse_CountFound(first.Total);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModListsException)
        {
            AppLog.Info("Collections", $"sp-mod.com/lists could not be read: {ex.Message}");
            if (_index is null) StatusMessage = LocalizationService.Text(Strings.Collections_LoadFailedFormat, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Every list, page by page. quiet: cards are already showing - nothing says so unless it fails.
    private async Task ReadAllAsync(bool quiet)
    {
        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                if (!quiet) ProgressText = LocalizationService.Text(Strings.Collections_ReadingFormat, p.Done, p.Total);
            });

            var read = await Task.Run(() => SpModListIndex.FetchAsync(AppServices.SpModLists, progress));
            _ = Task.Run(() => _store.Save(read));
            UseIndex(read);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModListsException)
        {
            AppLog.Info("Collections", $"sp-mod.com's lists could not all be read: {ex.Message}");
            if (_index is null) StatusMessage = LocalizationService.Text(Strings.Collections_LoadFailedFormat, ex.Message);
        }
        finally
        {
            ProgressText = null;
            _reading = null;
        }
    }

    private void UseIndex(SpModListIndexData index)
    {
        _index = index;
        StatusMessage = null;
        SyncSptChoices(index.SptOptions);
        Apply(keepPage: true);
    }

    // ------------------------------------------------------------------ filtering, order, pages

    partial void OnSelectedSptChanged(CollectionSptChoice? value)
    {
        if (!_suppress) Apply();
    }

    partial void OnSelectedSortOptionChanged(CollectionSortOption value)
    {
        if (!_suppress) Apply();
    }

    partial void OnPageSizeChanged(int value)
    {
        OnPropertyChanged(nameof(IsInfinite));
        PageSizeMemory.Save(PageSizeMemory.CollectionsBrowse, value); // Fork
        if (!_suppress) Apply();
    }

    private bool _suppress;
    private CancellationTokenSource? _typing;

    //
    // A search waits for the typing to stop - it also asks sp-mod.com, and a request per key would be
    // a dozen for one word. An address pasted in opens that collection instead.
    //
    partial void OnSearchTextChanged(string value)
    {
        _typing?.Cancel();

        if (SpModListAddress.TryParse(value, out var id, out var slug))
        {
            AppServices.CollectionOverlay.ShowPublic(id, slug);

            // The address has done its job; the box goes back to what it was searching for.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (SearchText == value) SearchText = _shownSearch;
            });
            return;
        }

        var typing = _typing = new CancellationTokenSource();
        _ = SearchSoonAsync(value, typing.Token);
    }

    // The search the cards show.
    private string _shownSearch = string.Empty;

    private async Task SearchSoonAsync(string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(350, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (text.Trim() == _shownSearch.Trim() && _index is not null) return;

        Apply();
        _ = AskSiteAsync(text.Trim(), ct);
    }

    //
    // sp-mod.com's own search, up to three pages of it: whatever it finds beyond what the cards'
    // text matched (a word deep in a description) is added in when it answers.
    //
    private async Task AskSiteAsync(string text, CancellationToken ct)
    {
        if (text.Length == 0 || _siteMatches.ContainsKey(text)) return;

        try
        {
            var found = new HashSet<int>();
            for (var page = 1; page <= 3; page++)
            {
                var answer = await AppServices.SpModLists.BrowseAsync(page, text, ct: ct);
                foreach (var list in answer.Lists) found.Add(list.Id);
                if (page >= answer.LastPage) break;
            }

            _siteMatches[text] = found;
            if (!ct.IsCancellationRequested && SearchText.Trim() == text) Apply(keepPage: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or SpModListsException)
        {
            // The cards' own text has answered already; this only ever adds.
            AppLog.Debug("Collections", $"sp-mod.com's search for \"{text}\" did not answer: {ex.Message}");
        }
    }

    //
    // Works out the matching lists and shows the page asked for. keepPage: the lists changed
    // underneath (read again, a search answered) - stay where the user was, as far as it still goes.
    //
    private void Apply(bool keepPage = false)
    {
        if (_index is null) return;

        _shownSearch = SearchText;
        var words = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fromSite = _siteMatches.GetValueOrDefault(SearchText.Trim());
        var spt = SelectedSpt?.Version;

        var matching = _index.Lists.Where(l =>
            (spt is null || string.Equals(l.SptVersion, spt, StringComparison.OrdinalIgnoreCase))
            && (words.Length == 0 || words.All(w => Mentions(l, w)) || fromSite?.Contains(l.Id) == true));

        _matching = (SelectedSortOption.Value switch
        {
            CollectionSort.Oldest => matching.Reverse(),
            CollectionSort.TitleAscending => matching.OrderBy(l => l.Title, StringComparer.CurrentCultureIgnoreCase),
            CollectionSort.TitleDescending => matching.OrderByDescending(l => l.Title, StringComparer.CurrentCultureIgnoreCase),
            CollectionSort.MostItems => matching.OrderByDescending(l => l.ItemCount),
            CollectionSort.FewestItems => matching.OrderBy(l => l.ItemCount),
            _ => matching,
        }).ToList();

        EntriesText = _matching.Count == 0 ? null : Strings.Browse_CountFound(_matching.Count);

        var pages = IsInfinite ? 1 : Math.Max(1, (int)Math.Ceiling(_matching.Count / (double)PageSize));
        ShowPage(keepPage ? Math.Min(CurrentPage, pages) : 1, pages, scroll: !keepPage);
        OnPropertyChanged(nameof(ShowNoMatches));
    }

    private static bool Mentions(SpModListSummary list, string word) =>
        list.Title.Contains(word, StringComparison.CurrentCultureIgnoreCase)
        || list.Author?.Contains(word, StringComparison.CurrentCultureIgnoreCase) == true
        || list.Teaser?.Contains(word, StringComparison.CurrentCultureIgnoreCase) == true;

    private void ShowPage(int page, int pages, bool scroll)
    {
        CurrentPage = page;
        LastPage = pages;
        OnPropertyChanged(nameof(HasPages));

        var shown = IsInfinite
            ? _matching.Take(Math.Max(InfiniteStep, Lists.Count > 0 && !scroll ? Lists.Count : 0))
            : _matching.Skip((page - 1) * PageSize).Take(PageSize);

        ShowCards(shown);
        HasMore = IsInfinite && Lists.Count < _matching.Count;

        PageLinks.Clear();
        if (!IsInfinite && pages > 1)
        {
            foreach (var link in PageLink.For(page, pages)) PageLinks.Add(link);
        }

        if (scroll) NavigatedToPage?.Invoke(this, EventArgs.Empty);
    }

    private void ShowCards(IEnumerable<SpModListSummary> lists)
    {
        var installed = AppServices.SptEnvironment.InstalledVersion;
        var stored = StoredIds();

        Lists.Clear();
        foreach (var summary in lists)
            Lists.Add(new CollectionCardViewModel(summary, installed, stored.Contains(PublicCollections.IdFor(summary.Id))));
    }

    /// <summary>The infinite list's next cards. False when there were none to add.</summary>
    public bool LoadMore()
    {
        if (!HasMore) return false;

        var installed = AppServices.SptEnvironment.InstalledVersion;
        var stored = StoredIds();
        foreach (var summary in _matching.Skip(Lists.Count).Take(InfiniteStep))
            Lists.Add(new CollectionCardViewModel(summary, installed, stored.Contains(PublicCollections.IdFor(summary.Id))));

        HasMore = Lists.Count < _matching.Count;
        return true;
    }

    // One read of the store for a page of cards, rather than one per card.
    private static HashSet<Guid> StoredIds() => AppServices.ModLists.Load().Lists.Select(l => l.Id).ToHashSet();

    // The site's own list of SPT versions, newest first, kept once read.
    private void SyncSptChoices(IReadOnlyList<SpModListSptOption> options)
    {
        if (options.Count == 0 || SptChoices.Count > 1) return;

        _suppress = true;
        try
        {
            foreach (var option in options) SptChoices.Add(new CollectionSptChoice(option.Id, option.Version));
        }
        finally
        {
            _suppress = false;
        }
    }

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private void SelectSort(CollectionSortOption? option)
    {
        if (option is not null) SelectedSortOption = option;
    }

    [RelayCommand]
    private void GoToPageNumber(int? number)
    {
        if (number is { } n && n != CurrentPage && _index is not null) ShowPage(n, LastPage, scroll: true);
    }

    private bool CanGoBack() => CurrentPage > 1;

    private bool CanGoForward() => CurrentPage < LastPage;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousPage() => GoToPageNumber(CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NextPage() => GoToPageNumber(CurrentPage + 1);

    [RelayCommand]
    private static void Open(CollectionCardViewModel? card)
    {
        if (card is not null) AppServices.CollectionOverlay.ShowPublic(card.Summary.Id, card.Summary.Slug);
    }

    // Everything read again from sp-mod.com now, rather than when the hour is up.
    [RelayCommand]
    private void Refresh()
    {
        if (_reading is not null) return;

        _siteMatches.Clear();
        _reading = ReadAllAsync(quiet: _index is not null);
    }

    // Steam's Clear filters: every SPT version, no search.
    [RelayCommand]
    private void ClearFilters()
    {
        _suppress = true;
        try
        {
            SelectedSpt = SptChoices[0];
            _typing?.Cancel();
            SearchText = string.Empty;
            _typing?.Cancel();
        }
        finally
        {
            _suppress = false;
        }

        Apply();
    }
}
