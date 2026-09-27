using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;
using TCFModManager.Core.Markup;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// One required item on the item page's right: another mod the shown version needs, ticked while
// it is installed.
public sealed partial class WorkshopRequiredItem(int modId, string? guid, string? name, string label) : ObservableObject
{
    public int ModId { get; } = modId;

    public string Label { get; } = label;

    // Installed and enabled: Steam's tick.
    [ObservableProperty]
    private bool _isInstalled;

    // Installed, but only in a disabled folder - SPT does not load it, so it is not ticked.
    [ObservableProperty]
    private bool _isDisabled;

    /// <summary>Looks again at whether it is installed: by the catalog's record of it, else its guid or name.</summary>
    public void Refresh()
    {
        var mod = AppServices.Browse.FindInCatalog(ModId) ?? new Mod { Id = ModId, Guid = guid, Name = name };
        var match = AppServices.Browse.InstalledMatchFor(mod);
        IsDisabled = match is { IsDisabled: true };
        IsInstalled = match is not null && !IsDisabled;
    }
}

// Someone credited on the mod, with their sp-mod.com picture when they have one.
public sealed partial class WorkshopAuthor : ObservableObject
{
    public WorkshopAuthor(int id, string name, string? photo)
    {
        Id = id;
        Name = name;
        Photo = photo;
        _isFollowing = AppServices.Followed.IsFollowing(id);
    }

    public int Id { get; }

    public string Name { get; }

    public string? Photo { get; }

    // Steam's Follow, for this author: their new items head the Workshop's front page.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowToolTip))]
    private bool _isFollowing;

    public bool CanFollow => Id != 0;

    public string FollowToolTip => LocalizationService.Text(
        IsFollowing ? Strings.Workshop_UnfollowFormat : Strings.Workshop_FollowFormat, Name);

    [RelayCommand]
    private void ToggleFollow() => IsFollowing = AppServices.Followed.Set(Id, Name, !IsFollowing);

    /// <summary>Re-reads whether this author is followed - after a follow made elsewhere.</summary>
    public void Refresh() => IsFollowing = AppServices.Followed.IsFollowing(Id);
}

// A mod listed in one of the right-hand panels: more by the author, or required by.
public sealed record WorkshopRelatedItem(Mod Mod)
{
    public string? Name => Mod.Name;
    public string? Thumbnail => Mod.Thumbnail;
    public string? Author => Mod.Owner?.Name;
}

public enum WorkshopItemTab
{
    Description,
    ChangeNotes,
    Versions,
    Comments,
}

// One of your collections holding the item - a row of Steam's "In N Collections" block.
public sealed record WorkshopCollectionRow(Guid Id, string Name, int Count)
{
    public string CountText => Strings.Collection_ItemCount(Count, Count);
}

// A link to somewhere off sp-mod.com's mod pages - source code, a VirusTotal scan, a licence.
public sealed record WorkshopLink(string Text, string Url)
{
    /// <summary>The links a version carries, named by their label - "Client", "Server" - or, when
    /// it has none, by <paramref name="unlabelled"/>.</summary>
    public static IReadOnlyList<WorkshopLink> From(IEnumerable<SourceCodeLink>? links, string unlabelled) =>
        Valid(links)
            .Select(l => new WorkshopLink(string.IsNullOrWhiteSpace(l.Label) ? unlabelled : l.Label.Trim(), l.Url))
            .ToList();

    /// <summary>Source code links as the site's Details panel writes them: "4.2.0+: github.com/x/y",
    /// or just the address when the author gave no label.</summary>
    public static IReadOnlyList<WorkshopLink> SourceCode(IEnumerable<SourceCodeLink>? links) =>
        Valid(links)
            .Select(l =>
            {
                var uri = new Uri(l.Url);
                var address = (uri.Host + uri.AbsolutePath).TrimEnd('/');
                return new WorkshopLink(
                    string.IsNullOrWhiteSpace(l.Label)
                        ? address
                        : LocalizationService.Text(Strings.Item_SourceCodeLinkFormat, l.Label.Trim(), address),
                    l.Url);
            })
            .ToList();

    // Only web addresses: anything else is not something to hand to the browser.
    private static IEnumerable<(string Url, string? Label)> Valid(IEnumerable<SourceCodeLink>? links) =>
        (links ?? [])
            .Where(l => Uri.TryCreate(l.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            .Select(l => (l.Url!, l.Label));
}

// One entry on the Change Notes tab.
public sealed record WorkshopChangeNote(
    string? Version,
    DateTimeOffset? PublishedAt,
    string? SptVersionConstraint,
    string? Html,
    bool IsShown)
{
    // Steam's "Update: Sep 24 @ 2:34AM" headline.
    public string? Headline => PublishedAt is null
        ? null
        : LocalizationService.Text(Strings.Item_ChangeNoteUpdateFormat, SteamDates.Short(PublishedAt));

    // Where Steam writes "by <author>": which version, and the SPT it declares.
    public string? Byline => string.Join(
        Strings.Common_FactSeparator,
        new[]
        {
            Version is null ? null : LocalizationService.Text(Strings.Item_ChangeNoteVersionFormat, Version),
            SptVersionConstraint is null ? null : LocalizationService.Text(Strings.Item_ChangeNoteSptFormat, SptVersionConstraint),
        }.Where(part => part is not null));
}

//
// The Workshop item page (steamcommunity.com/sharedfiles/filedetails) for one mod.
//
// What it shows comes from three places, all of which the app already had: the mod record the
// details request carries (description, licence, category, counts), the same card Browse builds
// for the mod (install state, the version it would install, SPT compatibility), and one call for
// the mod's published versions, which is where changelogs, the download size and each version's
// dependencies live.
//
// It installs, updates and removes through the code the rest of the app uses - Browse's install
// queue, the update dialog, Installed's removal - so Subscribe here is not a second install path.
//
public sealed partial class WorkshopItemViewModel : LocalizedViewModel, IModActionHost
{
    void IModActionHost.ShowActionMessage(string? message) => Message = message;

    private static string Text(string format, params object?[] values) =>
        LocalizationService.Text(format, values);

    public WorkshopItemViewModel(ModDetailsRequest request)
    {
        Mod = request.Mod;
        InstalledVersion = request.InstalledVersion;
        Addons = new AddonsSectionViewModel();

        RefreshInstallState();

        // The screenshot strip: the mod's own picture first, then every picture and video in its
        // description, in the order they appear there.
        if (!string.IsNullOrWhiteSpace(Mod.Thumbnail))
            Gallery.Add(new MarkupMedia(MarkupMediaKind.Image, Mod.Thumbnail!, Mod.Thumbnail!, Mod.Name, null));
        foreach (var media in SpModMarkup.Media(SpModMarkup.Parse(DescriptionHtml)))
        {
            if (!Gallery.Any(g => string.Equals(g.Url, media.Url, StringComparison.OrdinalIgnoreCase))) Gallery.Add(media);
        }

        SelectedMedia = Gallery.FirstOrDefault();

        RefreshCollections();

        // More by the same author, most downloaded first.
        if (Author is { } author)
        {
            foreach (var other in AppServices.Browse.Catalog
                         .Where(m => m.Id != Mod.Id && string.Equals(m.Owner?.Name, author, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(m => m.Downloads ?? 0)
                         .Take(MoreByAuthorCount))
            {
                MoreByAuthor.Add(new WorkshopRelatedItem(other));
            }
        }

        // Browse redraws its page after every install and removal, once its installed index has
        // caught up - the moment this page's own state is worth reading again too.
        AppServices.Browse.PageChanged += OnBrowsePageChanged;
        AppServices.Followed.Changed += OnFollowedChanged;
        AppServices.HeldBack.Changed += OnHeldBackChanged;

        // The Subscribe button follows this mod's download while it is in the queue.
        AppServices.DownloadQueue.Items.CollectionChanged += OnQueueChanged;
        AppServices.Browse.PropertyChanged += OnBrowseChanged;
        TrackQueueItem();
    }

    // ------------------------------------------------------------------ progress on Subscribe

    private DownloadQueueItemViewModel? _queueItem;

    /// <summary>True while this mod is being fetched or installed; the Subscribe button shows how far.</summary>
    public bool IsInQueue => _queueItem is { IsFinished: false };

    /// <summary>0 to 1 while downloading.</summary>
    public double QueueProgress => _queueItem?.Progress ?? 0;

    public bool IsQueueIndeterminate => _queueItem?.IsIndeterminateProgress ?? false;

    public string? QueueStatus => _queueItem?.StatusMessage;

    /// <summary>True between the Subscribe click and its gate, while the mod's requirements are looked up.</summary>
    public bool IsCheckingRequirements => AppServices.Browse.IsCheckingRequirements && _subscribing;

    private bool _subscribing;

    private void OnBrowseChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowseViewModel.IsCheckingRequirements)) OnPropertyChanged(nameof(IsCheckingRequirements));
    }

    private void OnQueueChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => TrackQueueItem();

    private void TrackQueueItem()
    {
        var latest = AppServices.DownloadQueue.Items.LastOrDefault(i => !i.Target.IsAddon && i.Target.Id == Mod.Id);
        if (ReferenceEquals(latest, _queueItem)) return;

        if (_queueItem is not null) _queueItem.PropertyChanged -= OnQueueItemChanged;
        _queueItem = latest;
        if (_queueItem is not null) _queueItem.PropertyChanged += OnQueueItemChanged;

        AnnounceQueue();
    }

    private void OnQueueItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        AnnounceQueue();

        // Steam's "added to your Subscriptions" bar, once this page's own download has installed.
        if (e.PropertyName == nameof(DownloadQueueItemViewModel.Status)
            && _queueItem is { Status: DownloadQueueItemStatus.Completed }
            && _watchingForInstall)
        {
            _watchingForInstall = false;
            JustSubscribed = true;
        }
    }

    // Set by Subscribe on this page, so the bar is for an install asked for here and not for one
    // that happened to finish while the page was open.
    private bool _watchingForInstall;

    /// <summary>Shows Steam's "This item has been added to your Subscriptions" bar under the
    /// subscribe box.</summary>
    [ObservableProperty]
    private bool _justSubscribed;

    // The bar's sentence either side of its link, so a translation can put the link anywhere.
    public string JustSubscribedBefore => Strings.Item_JustSubscribedFormat.Split("{0}", 2)[0];

    public string JustSubscribedAfter => Strings.Item_JustSubscribedFormat.Split("{0}", 2) is { Length: 2 } parts ? parts[1] : string.Empty;

    [RelayCommand]
    private void DismissJustSubscribed() => JustSubscribed = false;

    // The bar's "Subscriptions" link: the Subscribed items page (navigating closes this page).
    [RelayCommand]
    private static void OpenSubscriptions() => AppNavigation.Navigate(typeof(InstalledPage));

    private void AnnounceQueue()
    {
        OnPropertyChanged(nameof(IsInQueue));
        OnPropertyChanged(nameof(QueueProgress));
        OnPropertyChanged(nameof(IsQueueIndeterminate));
        OnPropertyChanged(nameof(QueueStatus));
    }

    public Mod Mod { get; }

    public string? InstalledVersion { get; }

    public AddonsSectionViewModel Addons { get; }

    // Stops listening once the page is closed or replaced.
    public void Detach()
    {
        _detached = true;
        AppServices.Followed.Changed -= OnFollowedChanged;
        AppServices.HeldBack.Changed -= OnHeldBackChanged;
        AppServices.Browse.PageChanged -= OnBrowsePageChanged;
        AppServices.Browse.PropertyChanged -= OnBrowseChanged;
        AppServices.DownloadQueue.Items.CollectionChanged -= OnQueueChanged;
        if (_queueItem is not null) _queueItem.PropertyChanged -= OnQueueItemChanged;
    }

    private void OnBrowsePageChanged(object? sender, EventArgs e) => RefreshInstallState();

    // Set once the page has let go of this view model: background work started for it stops.
    private bool _detached;

    // A follow made elsewhere (the right-click menu) shows on this page's CREATED BY buttons.
    private void OnFollowedChanged(object? sender, EventArgs e)
    {
        foreach (var author in Authors) author.Refresh();
    }

    // sp-mod.com's update check may answer after this page opened.
    private void OnHeldBackChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(HeldBackNote));

    // ------------------------------------------------------------------ what the page shows

    public string? Name => Mod.Name;

    public string? Author => Mod.Owner?.Name;

    public string? Thumbnail => Mod.Thumbnail;

    // Steam's third breadcrumb: "<author>'s Workshop".
    public string? AuthorsWorkshop => Author is { } author ? Text(Strings.Item_AuthorsWorkshopFormat, author) : null;

    // The description is HTML from sp-mod.com; a record fetched without it falls back to the teaser.
    public string? DescriptionHtml => string.IsNullOrWhiteSpace(Mod.Description)
        ? System.Net.WebUtility.HtmlEncode(Mod.Teaser ?? string.Empty)
        : Mod.Description;

    public string? Category => Mod.Category?.Title;

    public string? CategoryLinkToolTip => Category is { } category ? LocalizationService.Text(Strings.Item_ContentTypeLinkToolTipFormat, category) : null;

    // The Content Type value is a link, as on Steam: Browse, showing that type, top rated first.
    // Navigating closes this page.
    [RelayCommand]
    private async Task OpenContentTypeAsync()
    {
        if (Category is not { } category) return;

        // Browse's content types come from the catalog - there when this page is, but Browse builds
        // its list the first time it loads, which may not have happened yet.
        await AppServices.Browse.EnsureLoadedAsync();
        AppServices.Browse.ShowContentType(category);
        AppNavigation.Navigate(typeof(BrowsePage));
    }

    public string? License => Mod.License?.Name;

    // Web addresses only: the link is handed to the shell.
    public bool HasLicenseLink =>
        Uri.TryCreate(Mod.License?.Link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    // The BepInEx plugin id - what the game's logs call the mod.
    public string? Guid => string.IsNullOrWhiteSpace(Mod.Guid) ? null : Mod.Guid;

    // "4.2.0+: github.com/...", as the site's Details panel lists them.
    public IReadOnlyList<WorkshopLink> SourceCode => WorkshopLink.SourceCode(Mod.SourceCodeLinks);

    public bool HasSourceCode => SourceCode.Count > 0;

    // sp-mod.com's two notices, in its own words: a mod that behaves like a multiplayer cheat, and
    // one that may change a profile for good.
    public bool HasCheatNotice => Mod.CheatNotice == true;

    public bool HasProfileNotice => Mod.ShowsProfileBindingNotice == true;

    // Where each notice's "more information" goes, as on the site.
    private const string CheatGuidelinesUrl = "https://sp-mod.com/content-guidelines#anti-cheat-policy";
    private const string ProfileNoticeUrl = "https://wiki.sp-tushonka.com/SPT_4x/Profiles#mods";

    [RelayCommand]
    private void OpenCheatGuidelines() => MarkupActions.OpenInBrowser(CheatGuidelinesUrl);

    [RelayCommand]
    private void OpenProfileNotice() => MarkupActions.OpenInBrowser(ProfileNoticeUrl);

    [RelayCommand]
    private void OpenLicense()
    {
        if (HasLicenseLink) MarkupActions.OpenInBrowser(Mod.License!.Link!);
    }

    [RelayCommand]
    private void OpenWebLink(WorkshopLink? link)
    {
        if (link is not null) MarkupActions.OpenInBrowser(link.Url);
    }

    // The VirusTotal scans of the version Subscribe would install, once the versions have loaded.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVirusTotal))]
    private IReadOnlyList<WorkshopLink> _virusTotal = [];

    public bool HasVirusTotal => VirusTotal.Count > 0;

    // The mod's flags, in the header's Tags line.
    public string? Tags
    {
        get
        {
            var tags = new List<string>();
            if (Mod.FikaCompatibility == true) tags.Add(Strings.Common_FlagFikaCompatible);
            if (Mod.ContainsAds == true) tags.Add(Strings.Common_FlagContainsAds);
            if (Mod.ContainsAiContent == true) tags.Add(Strings.Common_FlagContainsAiContent);
            return tags.Count == 0 ? null : string.Join(Strings.Common_ListSeparator, tags);
        }
    }

    // Everyone credited, owner first.
    public IReadOnlyList<WorkshopAuthor> Authors => _authors ??=
        new[] { Mod.Owner }
            .Concat(Mod.AdditionalAuthors ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o?.Name))
            .Select(o => new WorkshopAuthor(o!.Id, o.Name!, o.ProfilePhotoUrl))
            .DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<WorkshopAuthor>? _authors;

    // ------------------------------------------------------------------ screenshot strip

    public ObservableCollection<MarkupMedia> Gallery { get; } = [];

    public bool HasGallery => Gallery.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedIsVideo))]
    private MarkupMedia? _selectedMedia;

    public bool SelectedIsVideo => SelectedMedia?.Kind == MarkupMediaKind.Video;

    [RelayCommand]
    private void SelectMedia(MarkupMedia? media)
    {
        if (media is not null) SelectedMedia = media;
    }

    // The big preview opens in the viewer, as Steam's does.
    [RelayCommand]
    private void OpenSelectedMedia()
    {
        if (SelectedMedia is { } media) AppServices.MediaViewer.Show(Gallery, media.Url);
    }

    // The cover at the head of the right-hand column: the mod's own picture, in the viewer with the
    // rest of the gallery (it is the gallery's first item).
    [RelayCommand]
    private void OpenCover()
    {
        if (Thumbnail is { Length: > 0 } cover) AppServices.MediaViewer.Show(Gallery, cover);
    }

    // ------------------------------------------------------------------ related items

    private const int MoreByAuthorCount = 5;

    public ObservableCollection<WorkshopRelatedItem> MoreByAuthor { get; } = [];

    public bool HasMoreByAuthor => MoreByAuthor.Count > 0;

    public string? MoreByAuthorTitle => Author is { } author ? Text(Strings.Item_MoreByFormat, author) : null;

    // Filled once the catalog-wide lookup has answered - see RequiredByIndex.
    public ObservableCollection<WorkshopRelatedItem> RequiredBy { get; } = [];

    public bool HasRequiredBy => RequiredBy.Count > 0;

    public string RequiredByTitle => Strings.Item_RequiredBy(RequiredByTotal);

    [RelayCommand]
    private async Task OpenRelatedAsync(WorkshopRelatedItem? item)
    {
        if (item is not null) await OpenAsync(item.Mod);
    }

    // A name under CREATED BY: their Workshop page.
    [RelayCommand]
    private void OpenAuthor(WorkshopAuthor? author)
    {
        if (author is not null) ModActions.ShowAuthor(author.Name, author.Id);
    }

    public int Downloads => Mod.Downloads ?? 0;

    public int Favourites => Mod.FavouritesCount ?? 0;

    public int Endorsements => Mod.EndorsementsCount ?? 0;

    public bool HasEndorsements => Endorsements > 0;

    public string EndorsementsText => Strings.Browse_Endorsements(Endorsements, Endorsements);

    public DateTimeOffset? PostedAt => Mod.PublishedAt ?? Mod.CreatedAt;

    public DateTimeOffset? UpdatedAt => Mod.UpdatedAt;

    // As Steam writes them, in this PC's time zone; the tooltip has the full date.
    public string? PostedText => SteamDates.Short(PostedAt);

    public string? PostedToolTip => SteamDates.Full(PostedAt);

    public string? UpdatedText => SteamDates.Short(UpdatedAt);

    public string? UpdatedToolTip => SteamDates.Full(UpdatedAt);

    public bool HasModPage => !string.IsNullOrWhiteSpace(Mod.DetailUrl);

    // ------------------------------------------------------------------ install state

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubscribed), nameof(CanUpdate), nameof(IsDisabled), nameof(StatusLine), nameof(HeldBackNote))]
    private ModCardViewModel _card = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubscribed), nameof(CanUpdate), nameof(HeldBackNote))]
    private InstalledModCardViewModel? _installed;

    public bool IsSubscribed => Card.IsInstalled;

    public bool IsDisabled => Card.IsDisabled;

    public bool CanUpdate => Installed is not null && Card.UpdateAvailable == true && !Card.IsDisabled;

    // "Installed", "Update available", "Disabled" - the same words the rest of the app uses.
    public string? StatusLine => Card.IsInstalled ? Card.StatusTooltip : null;

    // sp-mod.com holds this mod's update back: it would break another installed mod.
    public string? HeldBackNote => Installed is not null
        ? AppServices.HeldBack.NotForSptNote(Mod.Id, Installed.InstalledVersion) ?? AppServices.HeldBack.Note(Mod.Id)
        : null;

    private void RefreshInstallState()
    {
        var wasInstalled = Installed is not null;

        Card = AppServices.Browse.BuildCard(Mod);
        Installed = AppServices.Browse.InstalledMatchFor(Mod);

        // "Queued ..." is out of date once the install lands; the installed line says the rest.
        if (!wasInstalled && Installed is not null) Message = null;

        // Unsubscribing hides the "added to your Subscriptions" bar, as on Steam.
        if (wasInstalled && Installed is null) JustSubscribed = false;

        foreach (var row in Versions) row.Refresh(Installed?.InstalledVersion);
        foreach (var required in RequiredItems) required.Refresh();
    }

    // What the last action said - queued, removed, failed.
    [ObservableProperty]
    private string? _message;

    // ------------------------------------------------------------------ tabs

    [ObservableProperty]
    private WorkshopItemTab _tab;

    public bool IsDescriptionShown => Tab == WorkshopItemTab.Description;

    public bool IsChangeNotesShown => Tab == WorkshopItemTab.ChangeNotes;

    public bool IsVersionsShown => Tab == WorkshopItemTab.Versions;

    public bool IsCommentsShown => Tab == WorkshopItemTab.Comments;

    /// <summary>The mod's comments on sp-mod.com, opened at the comments tab.</summary>
    public string? CommentsUrl => HasModPage ? Mod.DetailUrl + "#comments" : null;

    // Every tab is re-announced every time: a click on the tab already showing un-ticks it before
    // this runs, and only a fresh notification ticks it back.
    [RelayCommand]
    private void ShowDescription() => ShowTab(WorkshopItemTab.Description);

    [RelayCommand]
    private void ShowChangeNotes() => ShowTab(WorkshopItemTab.ChangeNotes);

    [RelayCommand]
    private void ShowVersions() => ShowTab(WorkshopItemTab.Versions);

    [RelayCommand]
    private void ShowComments() => ShowTab(WorkshopItemTab.Comments);

    private void ShowTab(WorkshopItemTab tab)
    {
        Tab = tab;
        OnPropertyChanged(nameof(Tab));
        OnPropertyChanged(nameof(IsDescriptionShown));
        OnPropertyChanged(nameof(IsChangeNotesShown));
        OnPropertyChanged(nameof(ShownChangeNotes));
        OnPropertyChanged(nameof(IsVersionsShown));
        OnPropertyChanged(nameof(IsCommentsShown));

        if (tab == WorkshopItemTab.Versions) _ = CheckVersionsAsync();
    }

    // ------------------------------------------------------------------ versions

    // Which versions passed sp-mod.com's file check, one row after another - only while the
    // Versions tab is what is being looked at.
    private bool _checkingVersions;

    private async Task CheckVersionsAsync()
    {
        if (_checkingVersions) return;
        _checkingVersions = true;
        try
        {
            for (var i = 0; i < Versions.Count && IsVersionsShown && !_detached; i++) await Versions[i].CheckAsync();
        }
        finally
        {
            _checkingVersions = false;
        }
    }

    public ObservableCollection<WorkshopChangeNote> ChangeNotes { get; } = [];

    //
    // The change notes the tab lists: none until the tab is opened. Each note is a document of its
    // own, and a hidden tab still had all of them built as the page opened - measured on SAIN's
    // page, twenty notes built with its description took the description's first paint from 241ms
    // to 495ms and stopped the window for 301ms.
    //
    public ObservableCollection<WorkshopChangeNote>? ShownChangeNotes => IsChangeNotesShown ? ChangeNotes : null;

    public ObservableCollection<WorkshopRequiredItem> RequiredItems { get; } = [];

    public bool HasRequiredItems => RequiredItems.Count > 0;

    // Every version the API lists, not the twenty loaded so far.
    public string ChangeNotesLink => Text(Strings.Item_ChangeNotesFormat, Math.Max(TotalVersions, ChangeNotes.Count));

    // The bar over the notes: "Showing 1-20 of 30 entries".
    public string? ChangeNotesShowing => ChangeNotes.Count == 0
        ? null
        : Text(Strings.Item_ChangeNotesShowingFormat, 1, ChangeNotes.Count, Math.Max(TotalVersions, ChangeNotes.Count));

    [ObservableProperty]
    private string? _fileSize;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string? _versionsProblem;

    // ------------------------------------------------------------------ versions tab

    public ObservableCollection<WorkshopVersionRow> Versions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreVersions))]
    [NotifyPropertyChangedFor(nameof(ChangeNotesLink))]
    [NotifyPropertyChangedFor(nameof(ChangeNotesShowing))]
    private int _totalVersions;

    private int _versionsPage;

    public bool CanLoadMoreVersions => Versions.Count < TotalVersions;

    // Without a count: the API lists fewer versions than sp-mod.com's own tab counts (30 against
    // 74 for SAIN, 2026-09-24 - the site includes versions the API does not serve), and a number
    // that disagrees with the site would read as a fault.
    public string VersionsTabTitle => Strings.Item_TabVersions;

    [RelayCommand]
    private async Task LoadMoreVersionsAsync()
    {
        if (!IsLoadingVersions && CanLoadMoreVersions) await LoadVersionsPageAsync(_versionsPage + 1);
    }

    // A version from the Versions tab: installed through Subscribe's own gate and queue when the
    // mod is not installed, and through the update dialog - which lists every version - when it is.
    public async Task InstallVersionAsync(WorkshopVersionRow row)
    {
        if (Installed is not null)
        {
            await UpdateAsync();
            return;
        }

        await AppServices.Browse.InstallVersionAsync(Mod, row.Source);
        Message = AppServices.Browse.StatusMessage;
    }

    // Newest first, twenty at a time: what the Change Notes, the header's size and the required
    // items read, and the first page of the Versions tab.
    public async Task LoadAsync()
    {
        _ = Addons.LoadAsync(Mod.Id, Mod.Name, InstalledVersion);
        _ = LoadRequiredByAsync();

        await LoadVersionsPageAsync(1);
    }

    private async Task LoadRequiredByAsync()
    {
        try
        {
            var dependents = await AppServices.RequiredBy.DependentsOfAsync(Mod.Id);
            foreach (var mod in dependents.Take(RequiredByCount)) RequiredBy.Add(new WorkshopRelatedItem(mod));
            RequiredByTotal = dependents.Count;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Workshop", $"required-by lookup failed: {ex.Message}");
        }

        OnPropertyChanged(nameof(HasRequiredBy));
        OnPropertyChanged(nameof(RequiredByTitle));
    }

    private const int RequiredByCount = 8;

    // How many need this in all; the panel lists the most downloaded few.
    [ObservableProperty]
    private int _requiredByTotal;

    private async Task LoadVersionsPageAsync(int page)
    {
        IsLoadingVersions = true;
        try
        {
            var result = await AppServices.SpModApi.GetModVersionsAsync(
                Mod.Id.ToString(),
                new ModVersionsQuery { Include = "dependencies,virus_total_links", Sort = "-published_at", PerPage = VersionsPageSize, Page = page });

            _versionsPage = page;
            TotalVersions = result.Meta?.Total ?? result.Data.Count;

            var installedSpt = AppServices.SptEnvironment.InstalledVersion;
            var first = Versions.Count == 0;

            foreach (var v in result.Data)
            {
                Versions.Add(new WorkshopVersionRow(this, v, installedSpt, Installed?.InstalledVersion ?? InstalledVersion,
                    DependencyNames(v), v.ContentLength is { } size ? DescribeSize(size) : null));
            }

            if (IsVersionsShown) _ = CheckVersionsAsync();

            OnPropertyChanged(nameof(CanLoadMoreVersions));

            // The version Subscribe would install - the one the card advertises.
            // Until it is found - it can be on a later page - the newest stands in for the header's
            // size and required items, but only the real one is marked as shown.
            var shownVersion = Card.DisplayReleaseVersion;
            var exact = result.Data.FirstOrDefault(v => string.Equals(v.Version, shownVersion, StringComparison.OrdinalIgnoreCase));
            var shown = exact ?? (first ? result.Data.FirstOrDefault() : null);

            foreach (var v in result.Data)
            {
                ChangeNotes.Add(new WorkshopChangeNote(v.Version, v.PublishedAt, v.SptVersionConstraint, v.Description, ReferenceEquals(v, exact)));
            }

            OnPropertyChanged(nameof(ChangeNotesLink));
            OnPropertyChanged(nameof(ChangeNotesShowing));

            if (shown is null) return;

            FileSize = shown.ContentLength is { } bytes ? DescribeSize(bytes) : null;
            VirusTotal = WorkshopLink.From(shown.VirusTotalLinks, Strings.Item_VirusTotalOpen);

            RequiredItems.Clear();
            foreach (var dependency in (shown.Dependencies ?? []).OrderBy(d => d.IsOptional))
            {
                var modId = dependency.ModId != 0 ? dependency.ModId : dependency.Id;
                var name = DependencyName(dependency);
                var required = new WorkshopRequiredItem(
                    modId,
                    dependency.Guid ?? dependency.ModGuid,
                    dependency.Name ?? dependency.ModName,
                    dependency.IsOptional ? Text(Strings.Item_OptionalFormat, name) : name);
                required.Refresh();
                RequiredItems.Add(required);
            }

            OnPropertyChanged(nameof(HasRequiredItems));
        }
        catch (SpModApiException ex)
        {
            VersionsProblem = ApiProblems.Describe(ex);
        }
        catch (HttpRequestException ex)
        {
            VersionsProblem = ApiProblems.Describe(ex);
        }
        catch (OperationCanceledException)
        {
            VersionsProblem = Strings.Browse_TimedOutLoading;
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    private const int VersionsPageSize = 20;

    private static string DependencyName(ModVersionDependency dependency) =>
        dependency.ModName ?? dependency.Name ?? dependency.ModGuid ?? dependency.Guid ??
        (dependency.ModId != 0 ? dependency.ModId : dependency.Id).ToString();

    // "BigBrain (1.4.0)": the name and the newest version that satisfies it, as sp-mod.com's
    // version cards write it; the name alone when the API sent no versions.
    private static string DependencyNameAndVersion(ModVersionDependency dependency)
    {
        // Highest of those that read as versions; a label that does not ("beta") only when none do.
        string? newest = null;
        foreach (var version in (dependency.Versions ?? []).Select(v => v.Version).Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (newest is null
                || ModVersionComparer.IsUpdateAvailable(newest, version) == true
                || (!ReadsAsVersion(newest) && ReadsAsVersion(version)))
            {
                newest = version;
            }
        }

        var name = DependencyName(dependency);
        return newest is null ? name : Text(Strings.Item_DependencyVersionFormat, name, newest);
    }

    // Whether the comparer can read it at all: anything it can read compares against "0".
    private static bool ReadsAsVersion(string? version) =>
        ModVersionComparer.IsUpdateAvailable("0", version) is not null;

    private static string? DependencyNames(ModVersion version) =>
        version.Dependencies is { Count: > 0 } dependencies
            ? string.Join(Strings.Common_ListSeparator, dependencies.Select(DependencyNameAndVersion))
            : null;

    // Steam writes sizes as "22.946 KB": thousands of bytes to three places. (Whether Steam counts
    // in 1000s or 1024s could not be told from the page; 1000 matches the digits it shows.)
    private static string DescribeSize(long bytes) => bytes >= 1_000_000
        ? Text(Strings.Item_FileSizeMb, bytes / 1_000_000d)
        : Text(Strings.Item_FileSizeKb, bytes / 1_000d);

    // ------------------------------------------------------------------ actions

    // Through Browse's own install command: same version pick, same mod-page gate, same queue.
    [RelayCommand]
    private async Task SubscribeAsync()
    {
        // The button shows the download's progress while it runs; a click on it then must not
        // queue the mod a second time. (Refused here rather than by disabling the button, which
        // would dim the progress it is showing.)
        if (IsInQueue) return;

        JustSubscribed = false;
        _subscribing = true;
        try
        {
            await AppServices.Browse.SubscribeFromItemPageAsync(Card);
        }
        finally
        {
            _subscribing = false;
            OnPropertyChanged(nameof(IsCheckingRequirements));
        }

        // Queued: the bar shows once that download has installed.
        _watchingForInstall = IsInQueue;

        Message = AppServices.Browse.StatusMessage;
    }

    // Through the Installed page's own removal, confirmations and all.
    [RelayCommand]
    private async Task UnsubscribeAsync()
    {
        if (Installed is null) return;

        var installed = InstalledViewModel.Current ?? new InstalledViewModel();

        // What the removal itself said (null when cancelled) - not the page's count after it.
        if (await installed.RemoveOneAsync(Installed) is { } said) Message = said;
    }

    // The same dialog Subscribed items opens for a mod: every version, its changelog, Update.
    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (Installed is null || AppServices.ModUpdateOverlay.ShowAsync is not { } show) return;
        await show(Installed);
    }

    [RelayCommand]
    private void ViewModPage()
    {
        if (!HasModPage) return;
        Process.Start(new ProcessStartInfo(Mod.DetailUrl!) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ collections

    // Steam's "In N Collections" block: your collections holding this item.
    public ObservableCollection<WorkshopCollectionRow> InCollections { get; } = [];

    public bool HasInCollections => InCollections.Count > 0;

    public string InCollectionsTitle => Strings.Item_InCollections(InCollections.Count, InCollections.Count);

    private void RefreshCollections()
    {
        InCollections.Clear();
        foreach (var list in ModListService.CollectionsWith(Mod))
            InCollections.Add(new WorkshopCollectionRow(list.Id, list.Name, list.Entries.Count));

        OnPropertyChanged(nameof(HasInCollections));
        OnPropertyChanged(nameof(InCollectionsTitle));
    }

    [RelayCommand]
    private void AddToCollection()
    {
        if (AddToCollectionDialog.Ask(Mod) is { } said) Message = said;
        RefreshCollections();
    }

    [RelayCommand]
    private static void OpenCollection(WorkshopCollectionRow? row)
    {
        if (row is not null) AppServices.CollectionOverlay.Show(row.Id);
    }

    [RelayCommand]
    private void CopyLink()
    {
        if (!HasModPage) return;

        try
        {
            Clipboard.SetText(Mod.DetailUrl!);
            Message = Strings.Item_LinkCopied;
        }
        catch (Exception ex)
        {
            // The clipboard can be held by another process; that is worth a log line, not a crash.
            AppLog.Warn("Workshop", $"couldn't copy the mod link: {ex.Message}");
        }
    }

    // A required item opens as its own item page, the way Steam's links do.
    [RelayCommand]
    private async Task OpenRequiredAsync(WorkshopRequiredItem? item)
    {
        if (item is null) return;

        if (AppServices.Browse.FindInCatalog(item.ModId) is { } mod) await OpenAsync(mod);
    }

    // Another item page in this one's place. A failure is said here: Browse's status line, where
    // it also goes, is not on screen.
    private async Task OpenAsync(Mod mod)
    {
        if (await AppServices.Browse.LoadDetailsAsync(mod) is { } failed) Message = failed;
    }

    // The last breadcrumb: this author's Workshop page.
    [RelayCommand]
    private void OpenAuthorsWorkshop()
    {
        if (Author is { } author) ModActions.ShowAuthor(author, Mod.Owner?.Id);
    }
}
