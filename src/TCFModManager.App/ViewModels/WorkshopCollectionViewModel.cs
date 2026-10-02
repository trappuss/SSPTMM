using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.ViewModels;

//
// One item on a collection's page (Steam's collectionItem): the mod's picture, name, author and
// short description, and the square subscribe button - or, for an entry sp-mod.com cannot supply,
// the name alone and a note that it is installed by hand.
//
// An item of a public collection (sp-mod.com's lists) says a little more, because its collection
// does not pin versions: which version it would get on this install's SPT version, and whether the
// one installed is older than that - or that there is none for this SPT version at all.
//
public sealed partial class WorkshopCollectionItem : ObservableObject
{
    public WorkshopCollectionItem(ModListEntry entry, Mod? mod, string? addonName)
    {
        Entry = entry;
        Mod = mod;
        _name = mod?.Name ?? addonName ?? entry.Name;
        IsAddon = entry.IsAddon;
        Note = mod is not null
            ? null
            : IsAddon ? Strings.Collection_AddonNote : Strings.Collection_ByHandNote;
    }

    /// <summary>A mod on a public collection.</summary>
    public WorkshopCollectionItem(SpModListItem item, Mod? mod)
    {
        Mod = mod;
        ModId = item.ModId;
        SiteUrl = SpModListAddress.ForMod(item.ModId, item.Slug);
        _name = mod?.Name ?? item.Name;
        _author = item.Author;
        _thumbnail = item.Thumbnail;
        AuthorNote = item.Note;
        IsPublic = true;
        Note = mod is null ? Strings.Collection_NotInCatalogNote : null;
    }

    /// <summary>An addon on a public collection, shown after the mod it is for.</summary>
    public WorkshopCollectionItem(SpModListAddon addon, SpModListItem parent)
    {
        ModId = addon.AddonId;
        SiteUrl = SpModListAddress.ForAddon(addon.AddonId, addon.Slug);
        IsAddon = true;
        _name = addon.Name;
        _author = addon.Author;
        _thumbnail = addon.Thumbnail;
        AuthorNote = addon.Note;
        IsPublic = true;
        Note = LocalizationService.Text(Strings.Collection_AddonForFormat, parent.Name);
    }

    private readonly string _name;
    private readonly string? _author;
    private readonly string? _thumbnail;

    // The stored entry, for an item of one of this install's own collections.
    public ModListEntry? Entry { get; }

    // The catalog listing, when the entry names a mod sp-mod.com has.
    public Mod? Mod { get; }

    // For a public collection's item: its sp-mod.com id.
    public int ModId { get; }

    public bool IsPublic { get; }

    // A public collection's item's own page on sp-mod.com.
    public string? SiteUrl { get; }

    public string Name => _name;

    public string? Author => Mod?.Owner?.Name ?? _author;

    // Their sp-mod.com id, when the catalog has the item.
    public int? AuthorId => Mod?.Owner?.Id is > 0 ? Mod.Owner.Id : null;

    public string? Teaser => Mod?.Teaser;

    public string? Thumbnail => Mod?.Thumbnail ?? _thumbnail;

    public bool IsAddon { get; }

    // Subscribe here works for a mod the catalog has; an addon installs from its mod's page, and an
    // entry with no listing at all is installed by hand.
    public bool CanSubscribe => Mod is not null && !IsAddon;

    public string? Note { get; }

    // What the collection's author wrote about this item on sp-mod.com - "OPTIONAL", "[DISABLED]".
    public string? AuthorNote { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SubscribeToolTip))]
    private bool _isInstalled;

    public string SubscribeToolTip => IsInstalled ? Strings.Collection_UnsubscribeToolTip : Strings.Collection_SubscribeToolTip;

    private string? _installedVersion;

    // The version this item would get on this install's SPT version; null until it is known.
    public CollectionVersionPick? Pick { get; private set; }

    private string? _spt;

    /// <summary>A line about the version, for a public collection's item; null when there is
    /// nothing worth saying.</summary>
    [ObservableProperty]
    private string? _versionText;

    /// <summary>True when that line is a warning: no version for this SPT version.</summary>
    [ObservableProperty]
    private bool _versionIsWarning;

    /// <summary>An update is available for the installed copy, to the version for this SPT.</summary>
    public bool NeedsUpdate => IsInstalled && Pick is { Found: true } pick && IsNewer(pick.Version);

    // Newer than what is installed - where a version read from a DLL (the mod installed by hand)
    // is taken to be the same release as a published one with the same numbers and a label.
    private bool IsNewer(string? version) =>
        ModVersionComparer.IsUpdateAvailable(_installedVersion, version) == true
        && !(_installed is { IsAppManaged: false, IsManualOverride: false } && ModVersionComparer.IsSameRelease(_installedVersion, version));

    public bool HasNoVersion => Pick is { Found: false, CouldNotCheck: false };

    public void Refresh()
    {
        var installed = Mod is null ? null : AppServices.Browse.InstalledMatchFor(Mod);
        _installed = installed;
        IsInstalled = installed is not null;
        _installedVersion = installed?.InstalledVersion;
        UpdateVersionText();
    }

    private InstalledModCardViewModel? _installed;

    //
    // What Update all would update this item to, when it would update it at all.
    //
    // A public collection's item: the version this page names for this install's SPT version (read
    // from sp-mod.com for exactly that SPT), when it is newer than the one installed. One of your own
    // collections: exactly what Subscribed items' Update all would do - only when its card says an
    // update for this SPT is available (a newer release for the next SPT line only does not count),
    // to the version it would pick.
    //
    // Never an addon (updated from its mod's page), a disabled mod, or a version sp-mod.com holds
    // back - Subscribed items leaves those too.
    //
    public UpdateTarget? PendingUpdate()
    {
        if (IsAddon || Mod is not { } mod || _installed is not { IsDisabled: false } installed) return null;

        string? version;
        if (IsPublic)
        {
            version = Pick is { Found: true } pick ? pick.Version : null;
            if (version is null || !IsNewer(version)) return null;
        }
        else
        {
            if (installed.UpdateAvailable != true) return null;
            version = ModCardViewModel.PickDisplayVersion(mod, AppServices.SptEnvironment.InstalledVersion)?.Version;
            if (version is null) return null;
        }

        if (AppServices.HeldBack.HeldVersion(mod.Id) is { } held && string.Equals(held, version, StringComparison.OrdinalIgnoreCase)) return null;

        return new UpdateTarget(Name, mod, version, installed.IsAppManaged);
    }

    public void SetPick(CollectionVersionPick pick, string spt)
    {
        Pick = pick;
        _spt = spt;
        UpdateVersionText();
    }

    private void UpdateVersionText()
    {
        if (!IsPublic || IsAddon || Pick is not { } pick)
        {
            VersionText = null;
            VersionIsWarning = false;
            return;
        }

        VersionIsWarning = HasNoVersion;
        VersionText = pick switch
        {
            { CouldNotCheck: true } => null,
            { Found: false } => LocalizationService.Text(Strings.Collection_NoVersionForSptFormat, _spt),
            _ when NeedsUpdate => LocalizationService.Text(Strings.Collection_UpdateToFormat, _installedVersion, pick.Version, _spt),
            _ when IsInstalled => LocalizationService.Text(Strings.Collection_InstalledVersionFormat, _installedVersion),
            _ => LocalizationService.Text(Strings.Collection_VersionForSptFormat, pick.Version, _spt),
        };
    }
}

//
// A collection's Workshop page (sharedfiles/filedetails for a collection): the title, the items
// with Subscribe to all / Unsubscribe from all / Save to Collection over them, and who made the
// items, beside them.
//
// Two kinds of collection open here: one of this install's own mod lists, and one of sp-mod.com's
// public lists, read from its page each time it is opened. Applying either is the Collections
// page's work, and this page hands over to it rather than doing it a second way - see
// SubscribeToAllAsync.
//
public sealed partial class WorkshopCollectionViewModel : LocalizedViewModel, IModActionHost
{
    void IModActionHost.ShowActionMessage(string? message) => Message = message;

    public WorkshopCollectionViewModel(ModList list)
    {
        List = list;
        Name = list.Name;

        var catalog = PublicCollections.CatalogById();

        foreach (var entry in ModListEntries.Sorted(list.Entries))
        {
            Mod? mod = null;
            string? addonName = null;

            if (entry is { ModId: { } id, IsAddon: false } && catalog.TryGetValue(id, out var found)
                && !BrowseViewModel.IsSelf(found))
            {
                mod = found;
            }
            else if (entry is { ModId: { } addonId, IsAddon: true })
            {
                addonName = AppServices.Addons.ById(addonId)?.Name;
            }

            Items.Add(new WorkshopCollectionItem(entry, mod, addonName));
        }

        PostedText = SteamDates.Short(list.CreatedAt);
        UpdatedText = SteamDates.Short(list.UpdatedAt);
        LocalDescription = string.IsNullOrWhiteSpace(list.Description) ? null : list.Description.Trim();

        Finish();
    }

    /// <summary>A public collection, while its page is read from sp-mod.com.</summary>
    public WorkshopCollectionViewModel(int id, string slug)
    {
        _publicId = id;
        _publicSlug = slug;
        IsPublic = true;
        IsLoading = true;
        List = PublicCollections.Stored(id);
        AppServices.Browse.InstalledIndexChanged += OnInstalledIndexChanged;
    }

    private void Finish()
    {
        Authors = Items
            .Select(i => i.Author)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList()!;

        OnPropertyChanged(nameof(Authors));
        OnPropertyChanged(nameof(HasAuthors));
        OnPropertyChanged(nameof(ItemsCount));
        OnPropertyChanged(nameof(ItemsNumber));
        OnPropertyChanged(nameof(CreatedByTitle));
        OnPropertyChanged(nameof(IsEmpty));
        BuildHeaderArt();

        RefreshInstallState();
        if (!IsPublic) AppServices.Browse.InstalledIndexChanged += OnInstalledIndexChanged;
    }

    // ------------------------------------------------------------------ a public collection

    private readonly int _publicId;
    private readonly string? _publicSlug;
    private readonly CancellationTokenSource _closing = new();

    /// <summary>Reads the public collection's page and fills this one in. Returns false when it
    /// could not be read; Message then says why.</summary>
    public async Task<bool> LoadAsync()
    {
        if (!IsPublic || _publicSlug is null) return false;

        IsLoading = true;
        try
        {
            await AppServices.ModCache.EnsureLoadedAsync();
            await AppServices.Addons.EnsureLoadedAsync();

            var details = await AppServices.CollectionDetails.GetAsync(_publicId, _publicSlug, _closing.Token);
            if (details is null)
            {
                LoadFailed = Strings.Collection_PublicGone;
                return false;
            }

            Show(details);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SpModListsException)
        {
            AppLog.Info("Collections", $"list {_publicId} could not be read: {ex.Message}");
            LoadFailed = LocalizationService.Text(Strings.Collection_PublicLoadFailedFormat, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            // Anything else (a page read wrong, the catalog failing to load) still ends in a message
            // and Try again, not a page with a header and nothing under it.
            AppLog.Warn("Collections", $"list {_publicId} could not be shown: {ex}");
            LoadFailed = LocalizationService.Text(Strings.Collection_PublicLoadFailedFormat, ex.Message);
            return false;
        }
        finally
        {
            IsLoading = false;
        }

        _ = PickVersionsAsync();
        return true;
    }

    public SpModListDetails? Details { get; private set; }

    // Everything under the header: always for a stored collection, once read for a public one.
    public bool HasContent => !IsPublic || Details is not null;

    // A public collection read from sp-mod.com: it has a Comments tab and a Share button.
    public bool HasContentPublic => IsPublic && Details is not null;

    private void Show(SpModListDetails details)
    {
        Details = details;
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(HasContentPublic));
        OnPropertyChanged(nameof(ShowsDescriptionTab));
        OnPropertyChanged(nameof(CommentsUrl));
        Name = details.Title;
        ListAuthor = details.Author;
        ListSpt = details.SptVersion;
        DescriptionHtml = details.DescriptionHtml;
        Cover = details.Cover;
        UpdatedText = details.UpdatedAt is { } updated ? SteamDates.Short(updated) : null;
        AppServices.CollectionDetails.Remember(details);

        // A favorite, opened: what it says now has been seen.
        IsFavorite = AppServices.Favorites.IsFavorite(details.Id);
        try
        {
            AppServices.Favorites.MarkSeen(details);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // Only a note of what was seen: the page shows all the same.
            AppLog.Warn("Collections", $"favorite {details.Id} not marked seen: {ex.Message}");
        }

        // Who made it, with their picture: from the catalog when they have mods there, otherwise
        // from their own page.
        ListAuthorId = SpModListAddress.UserId(details.AuthorUrl);
        ListAuthorAvatar = (ListAuthorId is { } authorId ? AppServices.Authors.FindById(authorId) : AppServices.Authors.FindByName(details.Author))?.Avatar;
        if (ListAuthorAvatar is null && ListAuthorId is { } lookUp) _ = LoadAuthorAvatarAsync(lookUp);

        var catalog = PublicCollections.CatalogById();

        Items.Clear();
        foreach (var item in details.Items)
        {
            var mod = catalog.GetValueOrDefault(item.ModId);
            if (mod is not null && BrowseViewModel.IsSelf(mod)) mod = null;

            Items.Add(new WorkshopCollectionItem(item, mod));
            foreach (var addon in item.Addons) Items.Add(new WorkshopCollectionItem(addon, item));
        }

        Finish();
        UpdateSummary();
    }

    // The version of every item for this install's SPT version, so each row can say what it would
    // get - worked out as the page opens, and reused by Subscribe to all when that is the answer.
    private Dictionary<int, CollectionVersionPick>? _picks;
    private string? _picksFor;

    private async Task PickVersionsAsync()
    {
        if (Details is null || AppServices.SptEnvironment.InstalledVersion is not { } spt) return;

        IsCheckingVersions = true;
        UpdateSummary();
        try
        {
            var picks = await PublicCollections.PickAllAsync(
                Details.Items.Select(i => i.ModId), PublicCollections.CatalogById(), spt, ct: _closing.Token);

            _picks = picks;
            _picksFor = spt;

            foreach (var item in Items.Where(i => i is { IsPublic: true, IsAddon: false }))
            {
                if (picks.TryGetValue(item.ModId, out var pick)) item.SetPick(pick, spt);
            }
        }
        catch (OperationCanceledException)
        {
            // Closed.
        }
        finally
        {
            IsCheckingVersions = false;
            UpdateSummary();
        }
    }

    //
    // One line over the items, the way a Nexus collection page opens: how much of it this install
    // already has, and what stands between it and the rest.
    //
    private void UpdateSummary()
    {
        AnnounceUpdates();

        if (!IsPublic || Details is null)
        {
            Summary = null;
            return;
        }

        var mods = Items.Where(i => !i.IsAddon).ToList();
        var parts = new List<string>
        {
            LocalizationService.Text(Strings.Collection_SummarySubscribedFormat, mods.Count(i => i.IsInstalled), mods.Count),
        };

        if (IsCheckingVersions)
        {
            parts.Add(Strings.Collection_SummaryChecking);
        }
        else
        {
            var updates = mods.Count(i => i.NeedsUpdate);
            if (updates > 0) parts.Add(Strings.Collection_SummaryUpdates(updates, updates));

            var none = mods.Count(i => i.HasNoVersion);
            if (none > 0) parts.Add(Strings.Collection_SummaryNoVersion(none, none, _picksFor));
        }

        if (Details.Unavailable > 0) parts.Add(Strings.Collection_SummaryGone(Details.Unavailable, Details.Unavailable));

        Summary = string.Join(Strings.Common_FactSeparator, parts);
    }

    public bool IsPublic { get; }

    private async Task LoadAuthorAvatarAsync(int id)
    {
        try
        {
            var page = await AppServices.Authors.GetPageAsync(id);
            if (page is null) AppServices.Authors.Forget(id);
            else if (page.Profile.Avatar is { } avatar) ListAuthorAvatar = avatar;
        }
        catch (Exception ex)
        {
            // Only a picture: whatever went wrong, the page goes on without it.
            AppServices.Authors.Forget(id);
            AppLog.Debug("Collections", $"member {id}'s picture: {ex.Message}");
        }
    }

    [ObservableProperty]
    private int? _listAuthorId;

    [ObservableProperty]
    private string? _listAuthorAvatar;

    // ------------------------------------------------------------------ header art

    //
    // Steam's collection page stands on a picture 948px wide and 470px high, under the title bar. A
    // list's own picture where its author gave it one; otherwise the collection shown by what is in
    // it rather than a picture made up: eighteen of its items' pictures, six across and three down,
    // when it has that many different ones - or, with fewer, its first item's picture across the
    // whole header (repeating three pictures six times over looked like wallpaper).
    //
    private const int HeaderTiles = 18;

    public ObservableCollection<string> HeaderPictures { get; } = [];

    // The first item's picture, when there are too few for the tiles and no picture of its own.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderImage))]
    [NotifyPropertyChangedFor(nameof(HasHeaderArt))]
    private string? _firstPicture;

    // The one picture across the header: the list's own, or its first item's.
    public string? HeaderImage => Cover ?? (HeaderPictures.Count > 0 ? null : FirstPicture);

    public bool HasHeaderArt => HeaderImage is not null || HeaderPictures.Count > 0;

    public bool HasHeaderTiles => Cover is null && HeaderPictures.Count > 0;

    private void BuildHeaderArt()
    {
        HeaderPictures.Clear();

        var pictures = Items.Select(i => i.Thumbnail).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        if (pictures.Count >= HeaderTiles)
        {
            foreach (var picture in pictures.Take(HeaderTiles)) HeaderPictures.Add(picture!);
        }

        FirstPicture = pictures.FirstOrDefault();
        OnPropertyChanged(nameof(HeaderImage));
        OnPropertyChanged(nameof(HasHeaderArt));
        OnPropertyChanged(nameof(HasHeaderTiles));
    }

    partial void OnCoverChanged(string? value)
    {
        OnPropertyChanged(nameof(HeaderImage));
        OnPropertyChanged(nameof(HasHeaderArt));
        OnPropertyChanged(nameof(HasHeaderTiles));
    }

    // ------------------------------------------------------------------ tabs

    // Description, or - for a public collection - its comments on sp-mod.com.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDescriptionShown))]
    [NotifyPropertyChangedFor(nameof(ShowsDescriptionTab))]
    private bool _isCommentsShown;

    public bool IsDescriptionShown => !IsCommentsShown;

    // The Description tab's columns: on that tab, once there is something to put in them.
    public bool ShowsDescriptionTab => IsDescriptionShown && HasContent;

    public string? CommentsUrl => Details is null ? null : Details.Url + "#comments";

    [RelayCommand]
    private void ShowDescription() => IsCommentsShown = false;

    [RelayCommand]
    private void ShowComments()
    {
        if (IsPublic) IsCommentsShown = true;
    }

    // A collection of this install's own: its description as written on the Collections page.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _localDescription;

    public bool HasDescription => DescriptionHtml is not null || LocalDescription is not null;

    // Steam's Favorite: kept here (sp-mod.com has no favorites for lists), listed under Your Items >
    // Favorites, which says when one has changed since it was last opened.
    [ObservableProperty]
    private bool _isFavorite;

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (Details is not null) IsFavorite = AppServices.Favorites.Set(Details, !IsFavorite);
    }

    // Steam's Share: the collection's address, to paste anywhere.
    [RelayCommand]
    private void CopyLink()
    {
        if (Details is null) return;

        try
        {
            System.Windows.Clipboard.SetText(Details.Url);
            Message = Strings.Item_LinkCopied;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Debug("Collections", $"link not copied: {ex.Message}");
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isCheckingVersions;

    // Why a public collection could not be shown.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadFailed))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadFailed;

    public bool HasLoadFailed => LoadFailed is not null;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasListAuthor))]
    private string? _listAuthor;

    public bool HasListAuthor => ListAuthor is not null;

    // The SPT version the collection's author made it for.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListSptText))]
    private string? _listSpt;

    public string? ListSptText => ListSpt is null ? null : LocalizationService.Text(Strings.Collection_ForSptFormat, ListSpt);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionIsLong))]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _descriptionHtml;

    // Long enough to push the items off the first screen - a rough measure, of the page's HTML.
    public bool DescriptionIsLong => DescriptionHtml is { Length: > 900 };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescriptionToggleText))]
    private bool _isDescriptionOpen;

    public string DescriptionToggleText => IsDescriptionOpen ? Strings.Collection_ShowLess : Strings.Collection_ShowMore;

    [RelayCommand]
    private void ToggleDescription() => IsDescriptionOpen = !IsDescriptionOpen;

    [ObservableProperty]
    private string? _cover;

    // ------------------------------------------------------------------ both kinds

    // The stored list: one of this install's own, or the copy of a public one it has subscribed to.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManage))]
    private ModList? _list;

    public bool CanManage => List is not null;

    [ObservableProperty]
    private string _name = string.Empty;

    public ObservableCollection<WorkshopCollectionItem> Items { get; } = [];

    public IReadOnlyList<string> Authors { get; private set; } = [];

    public bool HasAuthors => Authors.Count > 0;

    public string ItemsTitle => Strings.Collection_ItemsHeader;

    public string ItemsCount => LocalizationService.Text(Strings.Collection_ItemsCountFormat, Items.Count);

    // The figure beside "Items" in the panel's stats, as Steam writes it: 1,234.
    public string ItemsNumber => Items.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    public string CreatedByTitle => Strings.Collection_CreatedBy(Items.Count, Items.Count);

    public bool IsEmpty => Items.Count == 0 && !IsLoading && !HasLoadFailed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPosted))]
    private string? _postedText;

    public bool HasPosted => PostedText is not null;

    [ObservableProperty]
    private string? _updatedText;

    // What the last action said.
    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isBusy;

    private void OnInstalledIndexChanged(object? sender, EventArgs e) => RefreshInstallState();

    private void RefreshInstallState()
    {
        foreach (var item in Items) item.Refresh();
        UpdateSummary();
    }

    // ------------------------------------------------------------------ Update all

    // How many of its subscribed items have a newer version for this SPT - the count on Update all.
    public int UpdatableCount => Items.Count(i => i.PendingUpdate() is not null);

    public bool HasUpdates => UpdatableCount > 0;

    public string UpdateAllLabel => Strings.Installed_UpdateAll(UpdatableCount);

    private void AnnounceUpdates()
    {
        OnPropertyChanged(nameof(UpdatableCount));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdateAllLabel));
    }

    //
    // Subscribed items' Update all, over this collection's items: each to the version for this SPT
    // version, asked and queued exactly as there - see ModUpdates.
    //
    [RelayCommand]
    private void UpdateAll()
    {
        var targets = Items.Select(i => i.PendingUpdate()).OfType<UpdateTarget>().ToList();
        if (targets.Count == 0) return;

        var (said, queued) = ModUpdates.Queue(targets);
        Message = said;
        if (queued) AppLog.Info("Collections", $"queued {targets.Count} update(s) from collection {Name}");
    }

    /// <summary>Stops listening once the page is closed or replaced.</summary>
    public void Detach()
    {
        AppServices.Browse.InstalledIndexChanged -= OnInstalledIndexChanged;
        _closing.Cancel();
    }

    // ------------------------------------------------------------------ one item

    [RelayCommand]
    private async Task OpenItemAsync(WorkshopCollectionItem? item)
    {
        if (item?.Mod is { } mod)
        {
            if (await AppServices.Browse.LoadDetailsAsync(mod) is { } failed) Message = failed;
            return;
        }

        // Not in the catalog here (an addon, or a mod the cache has not caught up with): its page.
        if (item?.SiteUrl is { } url) MarkupActions.OpenInBrowser(url);
    }

    // The square button: Subscribe, or - subscribed - Unsubscribe, through the same code the item
    // page's buttons use.
    // Each row on its own: one row's removal (seconds, with a rescan) must not disable the rest.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleSubscribeAsync(WorkshopCollectionItem? item)
    {
        if (item?.Mod is not { } mod || !item.CanSubscribe) return;

        var said = item.IsInstalled ? await ModActions.UnsubscribeAsync(mod) : await ModActions.SubscribeAsync(mod);
        if (said is not null) Message = said;
    }

    // An author chip in the right-hand panel: their Workshop page.
    [RelayCommand]
    private static void OpenAuthor(string? author)
    {
        if (author is not null) ModActions.ShowAuthor(author);
    }

    // The name after an item's "Created by": their Workshop page.
    [RelayCommand]
    private static void OpenItemAuthor(WorkshopCollectionItem? item)
    {
        if (item?.Author is { } author) ModActions.ShowAuthor(author, item.AuthorId);
    }

    // The public collection's own page, and its author's.
    [RelayCommand]
    private void OpenOnSite()
    {
        if (Details is not null) MarkupActions.OpenInBrowser(Details.Url);
    }

    // Who made a public collection: their Workshop page, here.
    [RelayCommand]
    private void OpenListAuthor()
    {
        if (ListAuthor is { } name) ModActions.ShowAuthor(name, ListAuthorId);
    }

    // The last breadcrumb of a public collection: back to browsing them.
    [RelayCommand]
    private static void BrowseCollections() => AppNavigation.Navigate(typeof(CollectionsBrowsePage));

    [RelayCommand]
    private async Task RetryAsync()
    {
        LoadFailed = null;
        await LoadAsync();
    }

    // ------------------------------------------------------------------ the whole collection

    //
    // Steam's Subscribe to all: add the items (Add Only), or make the subscriptions exactly this
    // collection (Overwrite My Subscriptions), which Steam asks about twice. Here that is applying
    // the list additively or exclusively - and applying is the Collections page's, which shows what
    // will happen before anything does. So the answer is carried there, as that page's preview.
    //
    // A public collection is made into a stored one first: each item pinned to its version for the
    // SPT version chosen (see PublicCollections), the ones with none left out and named in the
    // question.
    //
    [RelayCommand]
    private async Task SubscribeToAllAsync()
    {
        CollectionResolution? resolution = null;

        if (IsPublic)
        {
            if (Details is null) return;
            if ((resolution = await ResolveAsync()) is null) return;

            // Nothing it names can be had here: said, and nothing stored - an empty copy applied as
            // Overwrite would set aside everything installed.
            if (resolution.Entries.Count == 0)
            {
                SteamDialog.Show(
                    Strings.Collection_SubscribeAllTitle,
                    QuestionBody(LocalizationService.Text(Strings.Collection_NothingToSubscribeFormat, resolution.SptVersion), resolution.Skipped),
                    new SteamDialogChoice(Strings.Common_Close, SteamDialogButton.Blue, IsDefault: true));
                return;
            }
        }
        else if (List is null)
        {
            return;
        }

        var answer = SteamDialog.Show(
            Strings.Collection_SubscribeAllTitle,
            QuestionBody(Strings.Collection_SubscribeAllBody, resolution?.Skipped ?? []),
            new SteamDialogChoice(Strings.Collection_AddOnly, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Collection_Overwrite, SteamDialogButton.Blue),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        ModListPolicy policy;
        switch (answer)
        {
            case 0:
                policy = ModListPolicy.Additive;
                break;
            case 1:
                var sure = SteamDialog.Show(
                    Strings.Collection_Overwrite,
                    Strings.Collection_OverwriteBody,
                    new SteamDialogChoice(Strings.Collection_OverwriteYes, SteamDialogButton.Green),
                    new SteamDialogChoice(Strings.Collection_SaveCurrent, SteamDialogButton.Blue),
                    new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

                if (sure == 1)
                {
                    // Capturing what is installed now is the Collections page's first box.
                    ModListsViewModel.Request(new ModListsRequest(null, null));
                    AppNavigation.Navigate(typeof(ModListsPage));
                    return;
                }

                if (sure != 0) return;
                policy = ModListPolicy.Exclusive;
                break;
            default:
                return;
        }

        // Stored only once the answer is in: a question cancelled leaves nothing behind.
        var target = resolution is not null && Details is not null
            ? List = PublicCollections.Store(Details, resolution)
            : List!;

        ModListsViewModel.Request(new ModListsRequest(target.Id, policy));
        AppNavigation.Navigate(typeof(ModListsPage));
    }

    //
    // Which versions a public collection's items are subscribed at. The newest for this install's
    // SPT version, without asking, when that is what the collection was made for; when it was made
    // for another, the user chooses - their own SPT version (items with no version for it left
    // out), or the collection's.
    //
    private async Task<CollectionResolution?> ResolveAsync()
    {
        if (Details is null) return null;

        if (AppServices.SptEnvironment.InstalledVersion is not { } installed)
        {
            Message = AppMessages.NoSptInstallFolder;
            return null;
        }

        var spt = installed;
        if (Details.SptVersion is { } made && !SameVersion(made, installed))
        {
            var answer = SteamDialog.Show(
                Strings.Collection_WhichVersionsTitle,
                LocalizationService.Text(Strings.Collection_WhichVersionsBodyFormat, made, installed),
                new SteamDialogChoice(LocalizationService.Text(Strings.Collection_VersionsForYoursFormat, installed), SteamDialogButton.Green, IsDefault: true),
                new SteamDialogChoice(LocalizationService.Text(Strings.Collection_VersionsForListFormat, made), SteamDialogButton.Blue),
                new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

            if (answer == 1) spt = made;
            else if (answer != 0) return null;
        }

        IsBusy = true;
        Message = LocalizationService.Text(Strings.Collection_FindingVersionsFormat, spt);
        try
        {
            var resolution = await PublicCollections.ResolveAsync(
                Details, spt, _picksFor == spt ? _picks : null, _closing.Token);
            Message = null;
            return resolution;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool SameVersion(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // The question, and under it the items that are left out.
    private static object QuestionBody(string question, IReadOnlyList<CollectionSkip> skipped)
    {
        if (skipped.Count == 0) return question;

        var body = new System.Windows.Controls.StackPanel { MaxWidth = 560 };
        body.Children.Add(new System.Windows.Controls.TextBlock { Text = question, TextWrapping = System.Windows.TextWrapping.Wrap });
        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Margin = new System.Windows.Thickness(0, 12, 0, 4),
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Text = Strings.Collection_LeftOut(skipped.Count, skipped.Count),
        });

        var lines = skipped.Take(12).Select(s => LocalizationService.Text(Strings.Collection_LeftOutItemFormat, s.Name, s.Reason)).ToList();
        if (skipped.Count > 12) lines.Add(Strings.Collection_AndMore(skipped.Count - 12, skipped.Count - 12));

        body.Children.Add(new System.Windows.Controls.TextBlock
        {
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("SteamTextDim"),
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Text = string.Join("\n", lines),
        });

        return body;
    }

    //
    // Steam's Unsubscribe from all takes every item out of the subscriptions. Here that is asked once
    // for the lot: set the installed ones aside (disabled - each comes back with a click on
    // Subscribed items), or remove them (files deleted, config files copied aside first). Both go
    // through the Subscribed items page's own code.
    //
    [RelayCommand]
    private async Task UnsubscribeFromAllAsync()
    {
        var installed = Items.Where(i => i is { IsInstalled: true, Mod: not null }).Select(i => i.Mod!.Id).ToHashSet();
        if (installed.Count == 0)
        {
            Message = Strings.Collection_NothingInstalled;
            return;
        }

        var answer = SteamDialog.Show(
            Strings.Collection_UnsubscribeAll,
            Strings.Collection_UnsubscribeAllBody(installed.Count, installed.Count),
            new SteamDialogChoice(Strings.Collection_SetAside, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Collection_RemoveAll, SteamDialogButton.Blue),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        if (answer is not (0 or 1)) return;

        var page = InstalledViewModel.ForActions;
        Message = (answer == 0
            ? await page.DisableModsAsync(installed)
            : await page.RemoveModsAsync(installed)) ?? Message;
    }

    // Steam's Save to Collection: a copy of this collection, under a new name - for a public one,
    // one of the user's own collections holding its items at their versions for this SPT version.
    [RelayCommand]
    private async Task SaveToCollectionAsync()
    {
        var name = new System.Windows.Controls.TextBox
        {
            Style = (System.Windows.Style)System.Windows.Application.Current.FindResource("SteamSearchField"),
            Height = 34,
            Text = LocalizationService.Text(Strings.Collection_CopyNameFormat, Name),
            Margin = new System.Windows.Thickness(0, 10, 0, 0),
        };

        var body = new System.Windows.Controls.StackPanel { MinWidth = 420 };
        body.Children.Add(new System.Windows.Controls.TextBlock { Text = Strings.Collection_NamePrompt });
        body.Children.Add(name);

        var answer = SteamDialog.Show(
            Strings.Collection_SaveToCollection,
            body,
            new SteamDialogChoice(Strings.Collection_Save, SteamDialogButton.Green, IsDefault: true),
            new SteamDialogChoice(Strings.Common_Cancel, SteamDialogButton.Grey));

        if (answer != 0 || string.IsNullOrWhiteSpace(name.Text)) return;

        ModList copy;
        if (IsPublic)
        {
            if (Details is null || await ResolveAsync() is not { } resolution) return;
            copy = PublicCollections.SaveCopy(Details, resolution, name.Text.Trim());
        }
        else if (List is { } list)
        {
            copy = AppServices.ModLists.Fork(list.Id, name.Text.Trim(), DateTimeOffset.UtcNow);
        }
        else
        {
            return;
        }

        Message = LocalizationService.Text(Strings.Collection_SavedFormat, copy.Name);
    }

    // The Collections page, with this list chosen, for everything else - renaming, what it applies
    // as, sharing.
    [RelayCommand]
    private void Manage()
    {
        if (List is null) return;

        ModListsViewModel.Request(new ModListsRequest(List.Id, null));
        AppNavigation.Navigate(typeof(ModListsPage));
    }
}
