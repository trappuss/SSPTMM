using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.ViewModels;

/// <summary>An author to show: their sp-mod.com id when known, and their name.</summary>
public sealed record AuthorRequest(int? Id, string Name);

// Shared, app-lifetime signal for showing an author's Workshop page. MainWindow subscribes to
// Requested and shows AuthorView.
public sealed class AuthorOverlayViewModel
{
    public event EventHandler<AuthorRequest>? Requested;

    public void Show(AuthorRequest request) => Requested?.Invoke(this, request);
}

/// <summary>One item on an author's page: a square of Steam's Workshop Items grid.</summary>
public sealed partial class AuthorItem : ObservableObject
{
    public AuthorItem(Mod mod)
    {
        Mod = mod;
        Refresh();
    }

    public Mod Mod { get; }

    public string Name => Mod.Name ?? string.Empty;

    // Under the name in the hover popup.
    public string? Teaser => Mod.Teaser;

    public string? Thumbnail => Mod.Thumbnail;

    public int? Endorsements => Mod.EndorsementsCount is > 0 ? Mod.EndorsementsCount : null;

    public int? Favourites => Mod.FavouritesCount is > 0 ? Mod.FavouritesCount : null;

    public int Downloads => Mod.Downloads ?? 0;

    [ObservableProperty]
    private bool _isInstalled;

    public void Refresh() => IsInstalled = AppServices.Browse.InstalledMatchFor(Mod) is not null;
}

/// <summary>One of their addons on an author's page: a square like an item's, with the mod it is
/// for under its name.</summary>
public sealed class AuthorAddon(Addon addon, Mod? parent)
{
    public Addon Addon { get; } = addon;

    // The mod it goes with, when the catalog has it: its item page lists the addon and installs it.
    public Mod? Parent { get; } = parent;

    public string Name => Addon.Name ?? string.Empty;

    // Under the name in the hover popup.
    public string? Teaser => Addon.Teaser;

    public string? Thumbnail => Addon.Thumbnail;

    public int Downloads => Addon.Downloads ?? 0;

    public string? ForText => Parent?.Name is { } mod ? LocalizationService.Text(Strings.Author_AddonForFormat, mod) : null;
}

public enum AuthorTab
{
    Items,
    Addons,
    Collections,
}

//
// An author's Workshop page (steamcommunity.com/id/.../myworkshopfiles): who they are, their items,
// their collections, and Follow.
//
// Their items are the catalog's - every mod they own or are credited on, newest first, as Steam's
// page lists them. Who they are (picture, followers, member since) and their public collections come
// from their page on sp-mod.com, which needs their id: a name the catalog does not know (the author
// of a collection with no mods of their own and no link) shows their items only.
//
public sealed partial class AuthorPageViewModel : LocalizedViewModel
{
    private readonly AuthorRequest _request;
    private List<AuthorItem> _all = [];
    private readonly CancellationTokenSource _closing = new();

    public AuthorPageViewModel(AuthorRequest request)
    {
        _request = request;
        _id = request.Id;
        _name = request.Name;
        _isFollowing = AppServices.Followed.IsFollowing(request.Id);
        _pageSize = PageSizeMemory.Load(PageSizeMemory.AuthorItems, null, PageSizes);
        AppServices.Browse.InstalledIndexChanged += OnInstalledIndexChanged;
    }

    // Who they are as far as the catalog knows - their id when only a name was given, their
    // pictures - and their items. The catalog is loaded first when the page opens before it has.
    private async Task ReadCatalogAsync()
    {
        await AppServices.Browse.EnsureLoadedAsync();
        await AppServices.Addons.EnsureLoadedAsync();

        var known = _request.Id is { } id ? AppServices.Authors.FindById(id) : AppServices.Authors.FindByName(_request.Name);

        Id ??= known?.Id;
        if (_request.Id is not null && known?.Name is { Length: > 0 } catalogName) Name = catalogName;
        Avatar ??= known?.Avatar;
        Cover ??= known?.Cover;
        IsFollowing = AppServices.Followed.IsFollowing(Id);

        _all = AppServices.Browse.Catalog
            .Where(IsTheirs)
            .OrderByDescending(m => m.PublishedAt ?? m.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(m => new AuthorItem(m))
            .ToList();

        var catalog = PublicCollections.CatalogById();
        Addons.Clear();
        foreach (var addon in AppServices.Addons.AllAddons
                     .Where(IsTheirs)
                     .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt ?? DateTimeOffset.MinValue))
        {
            Addons.Add(new AuthorAddon(addon, addon.ModId is { } modId ? catalog.GetValueOrDefault(modId) : null));
        }

        OnPropertyChanged(nameof(HasAddons));
        CatalogRead = true;
        ShowPage(1);

        // Addons and nothing else in the catalog: open on what they have.
        if (HasNoItems && HasAddons && Tab == AuthorTab.Items) Tab = AuthorTab.Addons;
    }

    // Until the catalog is read, the page says nothing about their items.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoItems))]
    private bool _catalogRead;

    // Owner or credited author - by id when known, by name otherwise.
    private bool IsTheirs(Mod mod)
    {
        if (Id is { } id) return mod.Owner?.Id == id || (mod.AdditionalAuthors ?? []).Any(a => a.Id == id);

        return SameName(mod.Owner?.Name) || (mod.AdditionalAuthors ?? []).Any(a => SameName(a.Name));
    }

    private bool IsTheirs(Addon addon)
    {
        if (Id is { } id) return addon.Owner?.Id == id || (addon.AdditionalAuthors ?? []).Any(a => a.Id == id);

        return SameName(addon.Owner?.Name) || (addon.AdditionalAuthors ?? []).Any(a => SameName(a.Name));
    }

    private bool SameName(string? name) => string.Equals(name?.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasId))]
    [NotifyPropertyChangedFor(nameof(SiteUrl))]
    private int? _id;

    public bool HasId => Id is not null;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _avatar;

    // The picture across the top of their sp-mod.com page, behind the header here.
    [ObservableProperty]
    private string? _cover;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFollowers))]
    private int? _followers;

    public bool HasFollowers => Followers is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemberSinceText))]
    private DateTimeOffset? _memberSince;

    public string? MemberSinceText => MemberSince is { } since
        ? LocalizationService.Text(Strings.Author_MemberSinceFormat, since.ToLocalTime())
        : null;

    [ObservableProperty]
    private bool _isStaff;

    public string? SiteUrl => Id is { } id ? SpModListAddress.ForUser(id) : null;

    // ------------------------------------------------------------------ reading their page

    [ObservableProperty]
    private bool _isLoadingProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfileFailed))]
    private string? _profileFailed;

    public bool HasProfileFailed => ProfileFailed is not null;

    /// <summary>Reads their items from the catalog, then who they are and their collections from
    /// sp-mod.com - that part only with an id.</summary>
    public async Task LoadAsync()
    {
        IsLoadingProfile = true;
        ProfileFailed = null;
        try
        {
            if (!CatalogRead) await ReadCatalogAsync();
            if (_closing.IsCancellationRequested || Id is not { } id) return;

            var page = await AppServices.Authors.GetPageAsync(id);
            if (_closing.IsCancellationRequested) return;

            if (page is null)
            {
                // Asked again on Try again: a refusal can be passing.
                AppServices.Authors.Forget(id);
                ProfileFailed = Strings.Author_PageNotFound;
                return;
            }

            var profile = page.Profile;
            if (!string.IsNullOrWhiteSpace(profile.Name)) Name = profile.Name;
            Avatar = profile.Avatar ?? Avatar;
            Cover = profile.Cover ?? Cover;
            Followers = profile.Followers;
            IsStaff = profile.IsStaff;
            MemberSince = profile.MemberSince;

            var spt = AppServices.SptEnvironment.InstalledVersion;
            Collections.Clear();
            foreach (var list in page.Lists)
            {
                Collections.Add(new CollectionCardViewModel(list, spt, PublicCollections.Stored(list.Id) is not null));
            }

            CollectionsLoaded = true;
            OnPropertyChanged(nameof(ShowsNoCollections));

            // Someone with collections and nothing in the catalog - most collection authors - opens
            // on what they have.
            if (HasNoItems && !HasAddons && Collections.Count > 0 && Tab == AuthorTab.Items) Tab = AuthorTab.Collections;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModListsException)
        {
            AppLog.Warn("Workshop", $"member {Id}'s page could not be read: {ex.Message}");
            if (Id is { } failed) AppServices.Authors.Forget(failed);
            ProfileFailed = LocalizationService.Text(Strings.Author_PageFailedFormat, ex.Message);
        }
        catch (Exception ex)
        {
            // Anything else (a page read wrong) still ends in a message and Try again.
            AppLog.Warn("Workshop", $"member {Id}'s page could not be shown: {ex}");
            if (Id is { } failed) AppServices.Authors.Forget(failed);
            ProfileFailed = LocalizationService.Text(Strings.Author_PageFailedFormat, ex.Message);
        }
        finally
        {
            IsLoadingProfile = false;
        }
    }

    [RelayCommand]
    private async Task RetryAsync() => await LoadAsync();

    // ------------------------------------------------------------------ tabs

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItemsTab))]
    [NotifyPropertyChangedFor(nameof(IsCollectionsTab))]
    [NotifyPropertyChangedFor(nameof(IsAddonsTab))]
    [NotifyPropertyChangedFor(nameof(ShowsNoCollections))]
    private AuthorTab _tab;

    public bool IsItemsTab => Tab == AuthorTab.Items;

    public bool IsCollectionsTab => Tab == AuthorTab.Collections;

    public bool IsAddonsTab => Tab == AuthorTab.Addons;

    [RelayCommand]
    private void ShowAddons() => Tab = AuthorTab.Addons;

    // ------------------------------------------------------------------ Addons

    public ObservableCollection<AuthorAddon> Addons { get; } = [];

    public bool HasAddons => Addons.Count > 0;

    // Opens the mod it is for - its item page lists its addons, with Install - or, when the catalog
    // does not have that mod, the addon's page on sp-mod.com.
    [RelayCommand]
    private async Task OpenAddonAsync(AuthorAddon? addon)
    {
        if (addon is null) return;

        if (addon.Parent is { } mod)
        {
            if (await AppServices.Browse.LoadDetailsAsync(mod) is { } failed) Message = failed;
            return;
        }

        if (addon.Addon.DetailUrl is { } url) MarkupActions.OpenInBrowser(url);
    }

    [RelayCommand]
    private void ShowItems() => Tab = AuthorTab.Items;

    [RelayCommand]
    private void ShowCollections() => Tab = AuthorTab.Collections;

    // ------------------------------------------------------------------ Workshop Items

    // Steam's "Per page: 9 18 30" on an author's page, then Infinite as every other Per page in
    // this app has it - and, as on them, what the page opens at until a size is picked here.
    public IReadOnlyList<int> PageSizes { get; } = [9, 18, 30, PageSizeMemory.Infinite];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInfinite))]
    private int _pageSize = PageSizeMemory.Infinite;

    public bool IsInfinite => PageSize == PageSizeMemory.Infinite;

    // How many more items the infinite list adds each time the page nears its bottom.
    private const int InfiniteStep = 30;

    // Items per page, or per load of the infinite list.
    private int PageStep => IsInfinite ? InfiniteStep : PageSize;

    public ObservableCollection<AuthorItem> Items { get; } = [];

    public ObservableCollection<PageLink> PageLinks { get; } = [];

    public int TotalItems => _all.Count;

    public bool HasItems => _all.Count > 0;

    public bool HasNoItems => CatalogRead && _all.Count == 0;

    public string NoItemsText => LocalizationService.Text(Strings.Author_NoItemsFormat, Name);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _currentPage = 1;

    private bool CanGoBack => CurrentPage > 1;

    private bool CanGoOn => CurrentPage < TotalPages;

    public int TotalPages => IsInfinite ? 1 : Math.Max(1, (int)Math.Ceiling(_all.Count / (double)PageSize));

    public bool HasPages => TotalPages > 1;

    // "Showing 1-9 of 77 entries".
    public string ShowingText
    {
        get
        {
            var first = _all.Count == 0 ? 0 : IsInfinite ? 1 : (CurrentPage - 1) * PageSize + 1;
            var last = IsInfinite ? Items.Count : Math.Min(_all.Count, CurrentPage * PageSize);
            return LocalizationService.Text(Strings.Author_ShowingFormat, first.ToString("N0"), last.ToString("N0"), _all.Count.ToString("N0"));
        }
    }

    private void ShowPage(int page)
    {
        CurrentPage = Math.Clamp(page, 1, TotalPages);

        Items.Clear();
        foreach (var item in _all.Skip((CurrentPage - 1) * PageStep).Take(PageStep)) Items.Add(item);

        PageLinks.Clear();
        foreach (var link in PageLink.For(CurrentPage, TotalPages)) PageLinks.Add(link);

        OnPropertyChanged(nameof(ShowingText));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(HasPages));
        NextPageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(NoItemsText));
        PageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when another page of items is shown - the view scrolls back up to them.</summary>
    public event EventHandler? PageChanged;

    //
    // The infinite list's next items, when the page nears its bottom (AuthorView's ScrollChanged).
    // Added to what is there, so nothing moves and the view does not scroll. False when there is
    // nothing more to add.
    //
    public bool LoadMore()
    {
        if (!IsInfinite || !CatalogRead || Items.Count >= _all.Count) return false;

        foreach (var item in _all.Skip(Items.Count).Take(InfiniteStep)) Items.Add(item);
        OnPropertyChanged(nameof(ShowingText));
        return true;
    }

    [RelayCommand]
    private void SetPageSize(int? requested)
    {
        if (requested is not { } size || size < 0 || size == PageSize) return;

        // The first item on screen stays on screen: the page it is on, or the top of the infinite
        // list. From the infinite list a size starts at its first page.
        var firstIndex = IsInfinite ? 0 : (CurrentPage - 1) * PageSize;
        PageSize = size;
        PageSizeMemory.Save(PageSizeMemory.AuthorItems, size);
        ShowPage(IsInfinite ? 1 : firstIndex / size + 1);
    }

    [RelayCommand]
    private void GoToPageNumber(int? page)
    {
        if (page is { } number && number != CurrentPage) ShowPage(number);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousPage()
    {
        if (CurrentPage > 1) ShowPage(CurrentPage - 1);
    }

    [RelayCommand(CanExecute = nameof(CanGoOn))]
    private void NextPage()
    {
        if (CurrentPage < TotalPages) ShowPage(CurrentPage + 1);
    }

    [RelayCommand]
    private async Task OpenItemAsync(AuthorItem? item)
    {
        if (item is null) return;
        if (await AppServices.Browse.LoadDetailsAsync(item.Mod) is { } failed) Message = failed;
    }

    private void OnInstalledIndexChanged(object? sender, EventArgs e)
    {
        foreach (var item in _all) item.Refresh();
    }

    // ------------------------------------------------------------------ Collections

    public ObservableCollection<CollectionCardViewModel> Collections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoCollections))]
    private bool _collectionsLoaded;

    public bool ShowsNoCollections => IsCollectionsTab && CollectionsLoaded && Collections.Count == 0;

    [RelayCommand]
    private static void OpenCollection(CollectionCardViewModel? card)
    {
        if (card is not null) AppServices.CollectionOverlay.ShowPublic(card.Summary.Id, card.Summary.Slug);
    }

    // ------------------------------------------------------------------ Follow and the rest

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowToolTip))]
    private bool _isFollowing;

    public string FollowToolTip => LocalizationService.Text(
        IsFollowing ? Strings.Workshop_UnfollowFormat : Strings.Workshop_FollowFormat, Name);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(FollowToolTip));
        OnPropertyChanged(nameof(NoItemsText));
    }

    [RelayCommand]
    private void ToggleFollow()
    {
        if (Id is { } id) IsFollowing = AppServices.Followed.Set(id, Name, !IsFollowing);
    }

    [RelayCommand]
    private void OpenOnSite()
    {
        if (SiteUrl is { } url) MarkupActions.OpenInBrowser(url);
    }

    // Browse, searching by this author - its filters and sorts over their items.
    [RelayCommand]
    private void SearchInBrowse()
    {
        AppServices.Browse.ShowSearch("@" + Name);
        AppNavigation.Navigate(typeof(Views.BrowsePage));
    }

    [ObservableProperty]
    private string? _message;

    /// <summary>Stops listening once the page is closed or replaced.</summary>
    public void Detach()
    {
        AppServices.Browse.InstalledIndexChanged -= OnInstalledIndexChanged;
        _closing.Cancel();
    }
}
