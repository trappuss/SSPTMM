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

// One required item on the item page's right: another mod the shown version needs.
public sealed record WorkshopRequiredItem(int ModId, string Label);

// Someone credited on the mod, with their sp-mod.com picture when they have one.
public sealed record WorkshopAuthor(string Name, string? Photo);

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
public sealed partial class WorkshopItemViewModel : LocalizedViewModel
{
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

    private void OnQueueItemChanged(object? sender, PropertyChangedEventArgs e) => AnnounceQueue();

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
        AppServices.Browse.PageChanged -= OnBrowsePageChanged;
        AppServices.Browse.PropertyChanged -= OnBrowseChanged;
        AppServices.DownloadQueue.Items.CollectionChanged -= OnQueueChanged;
        if (_queueItem is not null) _queueItem.PropertyChanged -= OnQueueItemChanged;
    }

    private void OnBrowsePageChanged(object? sender, EventArgs e) => RefreshInstallState();

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

    public string? License => Mod.License?.Name;

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
    public IReadOnlyList<WorkshopAuthor> Authors =>
        new[] { Mod.Owner }
            .Concat(Mod.AdditionalAuthors ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o?.Name))
            .Select(o => new WorkshopAuthor(o!.Name!, o.ProfilePhotoUrl))
            .DistinctBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    // A name under CREATED BY: everything by them, in Browse.
    [RelayCommand]
    private void OpenAuthor(WorkshopAuthor? author)
    {
        if (author is null) return;

        AppServices.Browse.ShowSearch("@" + author.Name);
        AppNavigation.Navigate(typeof(BrowsePage));
    }

    public int Downloads => Mod.Downloads ?? 0;

    public int Favourites => Mod.FavouritesCount ?? 0;

    public int Endorsements => Mod.EndorsementsCount ?? 0;

    public bool HasEndorsements => Endorsements > 0;

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
    [NotifyPropertyChangedFor(nameof(IsSubscribed), nameof(CanUpdate), nameof(IsDisabled), nameof(StatusLine))]
    private ModCardViewModel _card = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubscribed), nameof(CanUpdate))]
    private InstalledModCardViewModel? _installed;

    public bool IsSubscribed => Card.IsInstalled;

    public bool IsDisabled => Card.IsDisabled;

    public bool CanUpdate => Installed is not null && Card.UpdateAvailable == true && !Card.IsDisabled;

    // "Installed", "Update available", "Disabled" - the same words the rest of the app uses.
    public string? StatusLine => Card.IsInstalled ? Card.StatusTooltip : null;

    private void RefreshInstallState()
    {
        var wasInstalled = Installed is not null;

        Card = AppServices.Browse.BuildCard(Mod);
        Installed = AppServices.Browse.InstalledMatchFor(Mod);

        // "Queued ..." is out of date once the install lands; the installed line says the rest.
        if (!wasInstalled && Installed is not null) Message = null;

        foreach (var row in Versions) row.Refresh(Installed?.InstalledVersion);
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
        OnPropertyChanged(nameof(IsVersionsShown));
        OnPropertyChanged(nameof(IsCommentsShown));
    }

    // ------------------------------------------------------------------ versions

    public ObservableCollection<WorkshopChangeNote> ChangeNotes { get; } = [];

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
                new ModVersionsQuery { Include = "dependencies", Sort = "-published_at", PerPage = VersionsPageSize, Page = page });

            _versionsPage = page;
            TotalVersions = result.Meta?.Total ?? result.Data.Count;

            var installedSpt = AppServices.SptEnvironment.InstalledVersion;
            var first = Versions.Count == 0;

            foreach (var v in result.Data)
            {
                Versions.Add(new WorkshopVersionRow(this, v, installedSpt, Installed?.InstalledVersion ?? InstalledVersion,
                    DependencyNames(v), v.ContentLength is { } size ? DescribeSize(size) : null));
            }

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

            RequiredItems.Clear();
            foreach (var dependency in (shown.Dependencies ?? []).OrderBy(d => d.IsOptional))
            {
                var modId = dependency.ModId != 0 ? dependency.ModId : dependency.Id;
                var name = DependencyName(dependency);
                RequiredItems.Add(new WorkshopRequiredItem(
                    modId,
                    dependency.IsOptional ? Text(Strings.Item_OptionalFormat, name) : name));
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

    private static string? DependencyNames(ModVersion version) =>
        version.Dependencies is { Count: > 0 } dependencies
            ? string.Join(Strings.Common_ListSeparator, dependencies.Select(DependencyName))
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

        _subscribing = true;
        try
        {
            await AppServices.Browse.InstallCommand.ExecuteAsync(Card);
        }
        finally
        {
            _subscribing = false;
            OnPropertyChanged(nameof(IsCheckingRequirements));
        }

        Message = AppServices.Browse.StatusMessage;
    }

    // Through the Installed page's own removal, confirmations and all.
    [RelayCommand]
    private async Task UnsubscribeAsync()
    {
        if (Installed is null) return;

        var installed = InstalledViewModel.Current ?? new InstalledViewModel();

        // The removal's own message is the first thing that page says; the reload it sets off
        // then replaces it with the page's mod count, which means nothing here.
        string? said = null;
        void Listen(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(InstalledViewModel.StatusMessage)) said ??= installed.StatusMessage;
        }

        installed.PropertyChanged += Listen;
        try
        {
            await installed.RemoveCommand.ExecuteAsync(Installed);
        }
        finally
        {
            installed.PropertyChanged -= Listen;
        }

        if (said is not null) Message = said;
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

    // The last breadcrumb: everything by this author, in Browse.
    [RelayCommand]
    private void OpenAuthorsWorkshop()
    {
        if (Author is not { } author) return;

        AppServices.Browse.ShowSearch("@" + author);
        AppNavigation.Navigate(typeof(BrowsePage));
    }
}
