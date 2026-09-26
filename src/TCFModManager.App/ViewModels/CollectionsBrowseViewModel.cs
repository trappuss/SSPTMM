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
}

// One choice in the SPT version filter: every version, or one, by the id sp-mod.com filters on.
public sealed class CollectionSptChoice(int? id, string? version) : LocalizedViewModel
{
    public int? Id { get; } = id;

    public string Label => version ?? Strings.Collections_AllSptVersions;

    public override string ToString() => Label;
}

//
// Browsing: Collections - Steam's collections browse page, over sp-mod.com's public lists
// (sp-mod.com/lists). Twelve to a page, newest first, which is the one order sp-mod.com has; a
// search and an SPT version narrow them the way the site's own page does, through the site. A list's
// address pasted into the search box opens that list.
//
public sealed partial class CollectionsBrowseViewModel : LocalizedViewModel
{
    public ObservableCollection<CollectionCardViewModel> Lists { get; } = [];

    public ObservableCollection<CollectionSptChoice> SptChoices { get; } = [new(null, null)];

    public ObservableCollection<PageLink> PageLinks { get; } = [];

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

    [ObservableProperty]
    private string? _statusMessage;

    public bool ShowNoMatches => !IsLoading && _loaded && Lists.Count == 0 && StatusMessage is null;

    public bool HasPages => LastPage > 1;

    // Most Recent: the one sort order there is.
    public string SortLabel => Strings.Collections_SortMostRecent;

    private bool _loaded;

    // What the page is showing results for, so an address pasted over it can be put back.
    private string _lastSearch = string.Empty;
    private bool _suppress;
    private CancellationTokenSource? _loading;
    private CancellationTokenSource? _typing;

    public CollectionsBrowseViewModel()
    {
        _selectedSpt = SptChoices[0];
    }

    /// <summary>The first page, once per session - the page calls this each time it is shown, and
    /// the cards' Subscribed marks are brought up to date then (a collection subscribed to since).</summary>
    public Task EnsureLoadedAsync()
    {
        if (!_loaded) return LoadAsync(1);

        var stored = StoredIds();
        foreach (var card in Lists) card.IsSubscribed = stored.Contains(PublicCollections.IdFor(card.Summary.Id));
        return Task.CompletedTask;
    }

    // One read of the store for a page of cards, rather than one per card.
    private static HashSet<Guid> StoredIds() => AppServices.ModLists.Load().Lists.Select(l => l.Id).ToHashSet();

    partial void OnSelectedSptChanged(CollectionSptChoice? value)
    {
        if (!_suppress) _ = LoadAsync(1);
    }

    //
    // A search waits for the typing to stop - each one is a request to sp-mod.com, and a request
    // per key would be a dozen for one word. An address pasted in opens that collection instead.
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
                if (SearchText == value) SearchText = _lastSearch;
            });
            return;
        }

        var typing = _typing = new CancellationTokenSource();
        _ = SearchSoonAsync(typing.Token);
    }

    private async Task SearchSoonAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(450, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // Back to what is already showing (an address pasted and put back): nothing to ask.
        if (_loaded && SearchText.Trim() == _lastSearch.Trim()) return;

        await LoadAsync(1);
    }

    private async Task LoadAsync(int page)
    {
        _loading?.Cancel();
        var loading = _loading = new CancellationTokenSource();

        IsLoading = true;
        StatusMessage = null;
        try
        {
            var search = SpModListAddress.TryParse(SearchText, out _, out _) ? _lastSearch : SearchText;
            _lastSearch = search;
            var result = await AppServices.SpModLists.BrowseAsync(page, search, SelectedSpt?.Id, loading.Token);
            if (loading.IsCancellationRequested) return;

            Show(result);
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            // Another page or search asked for since.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModListsException
                                   && ReferenceEquals(_loading, loading))
        {
            AppLog.Info("Collections", $"sp-mod.com/lists could not be read: {ex.Message}");
            StatusMessage = LocalizationService.Text(Strings.Collections_LoadFailedFormat, ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                IsLoading = false;
                OnPropertyChanged(nameof(ShowNoMatches));
            }
        }
    }

    private void Show(SpModListPage result)
    {
        _loaded = true;

        var installed = AppServices.SptEnvironment.InstalledVersion;
        var stored = StoredIds();
        Lists.Clear();
        foreach (var summary in result.Lists)
            Lists.Add(new CollectionCardViewModel(summary, installed, stored.Contains(PublicCollections.IdFor(summary.Id))));

        CurrentPage = result.Page;
        LastPage = result.LastPage;
        OnPropertyChanged(nameof(HasPages));

        PageLinks.Clear();
        if (LastPage > 1)
        {
            foreach (var link in PageLink.For(CurrentPage, LastPage)) PageLinks.Add(link);
        }

        EntriesText = result.Total == 0 ? null : Strings.Browse_CountFound(result.Total);
        SyncSptChoices(result.SptOptions);

        NavigatedToPage?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when a new page of cards is showing, so the page can scroll to them.</summary>
    public event EventHandler? NavigatedToPage;

    // The site's own list of SPT versions, kept once read: every answer carries it again.
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

    [RelayCommand]
    private Task GoToPageNumber(int? number) =>
        number is { } n && n != CurrentPage ? LoadAsync(n) : Task.CompletedTask;

    private bool CanGoBack() => CurrentPage > 1;

    private bool CanGoForward() => CurrentPage < LastPage;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task PreviousPage() => LoadAsync(CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task NextPage() => LoadAsync(CurrentPage + 1);

    [RelayCommand]
    private static void Open(CollectionCardViewModel? card)
    {
        if (card is not null) AppServices.CollectionOverlay.ShowPublic(card.Summary.Id, card.Summary.Slug);
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync(CurrentPage);

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

        _ = LoadAsync(1);
    }
}
