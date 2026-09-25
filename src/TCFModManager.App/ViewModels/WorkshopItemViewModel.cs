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
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.ViewModels;

// One required item on the item page's right: another mod the shown version needs.
public sealed record WorkshopRequiredItem(int ModId, string Label);

// One entry on the Change Notes tab.
public sealed record WorkshopChangeNote(
    string? Version,
    DateTimeOffset? PublishedAt,
    string? SptVersionConstraint,
    string? Html,
    bool IsShown);

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

        // Browse redraws its page after every install and removal, once its installed index has
        // caught up - the moment this page's own state is worth reading again too.
        AppServices.Browse.PageChanged += OnBrowsePageChanged;
    }

    public Mod Mod { get; }

    public string? InstalledVersion { get; }

    public AddonsSectionViewModel Addons { get; }

    // Stops listening once the page is closed or replaced.
    public void Detach() => AppServices.Browse.PageChanged -= OnBrowsePageChanged;

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
    public IReadOnlyList<string> Authors =>
        new[] { Mod.Owner?.Name }
            .Concat(Mod.AdditionalAuthors?.Select(a => a.Name) ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public int Downloads => Mod.Downloads ?? 0;

    public int Favourites => Mod.FavouritesCount ?? 0;

    public int Endorsements => Mod.EndorsementsCount ?? 0;

    public bool HasEndorsements => Endorsements > 0;

    public DateTimeOffset? PostedAt => Mod.PublishedAt ?? Mod.CreatedAt;

    public DateTimeOffset? UpdatedAt => Mod.UpdatedAt;

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
    }

    // What the last action said - queued, removed, failed.
    [ObservableProperty]
    private string? _message;

    // ------------------------------------------------------------------ tabs

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDescriptionShown))]
    private bool _isChangeNotesShown;

    public bool IsDescriptionShown => !IsChangeNotesShown;

    // Both tabs are re-announced every time: a click on the tab already showing un-ticks it before
    // this runs, and only a fresh notification ticks it back.
    [RelayCommand]
    private void ShowDescription() => ShowTab(changeNotes: false);

    [RelayCommand]
    private void ShowChangeNotes() => ShowTab(changeNotes: true);

    private void ShowTab(bool changeNotes)
    {
        IsChangeNotesShown = changeNotes;
        OnPropertyChanged(nameof(IsChangeNotesShown));
        OnPropertyChanged(nameof(IsDescriptionShown));
    }

    // ------------------------------------------------------------------ versions

    public ObservableCollection<WorkshopChangeNote> ChangeNotes { get; } = [];

    public ObservableCollection<WorkshopRequiredItem> RequiredItems { get; } = [];

    public bool HasRequiredItems => RequiredItems.Count > 0;

    public string ChangeNotesLink => Text(Strings.Item_ChangeNotesFormat, ChangeNotes.Count);

    [ObservableProperty]
    private string? _fileSize;

    [ObservableProperty]
    private bool _isLoadingVersions;

    [ObservableProperty]
    private string? _versionsProblem;

    // Newest first, twenty deep - the same fetch the update dialog makes.
    public async Task LoadAsync()
    {
        _ = Addons.LoadAsync(Mod.Id, Mod.Name, InstalledVersion);

        IsLoadingVersions = true;
        try
        {
            var result = await AppServices.SpModApi.GetModVersionsAsync(
                Mod.Id.ToString(),
                new ModVersionsQuery { Include = "dependencies", Sort = "-published_at", PerPage = 20 });

            // The version Subscribe would install - the one the card advertises.
            var shownVersion = Card.DisplayReleaseVersion;
            var shown = result.Data.FirstOrDefault(v => string.Equals(v.Version, shownVersion, StringComparison.OrdinalIgnoreCase))
                ?? result.Data.FirstOrDefault();

            ChangeNotes.Clear();
            foreach (var v in result.Data)
            {
                ChangeNotes.Add(new WorkshopChangeNote(v.Version, v.PublishedAt, v.SptVersionConstraint, v.Description, ReferenceEquals(v, shown)));
            }

            OnPropertyChanged(nameof(ChangeNotesLink));

            FileSize = shown?.ContentLength is { } bytes ? DescribeSize(bytes) : null;

            RequiredItems.Clear();
            foreach (var dependency in (shown?.Dependencies ?? []).OrderBy(d => d.IsOptional))
            {
                var modId = dependency.ModId != 0 ? dependency.ModId : dependency.Id;
                var name = dependency.ModName ?? dependency.Name ?? dependency.ModGuid ?? dependency.Guid ?? modId.ToString();
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

    // Steam writes sizes as "22.946 KB": thousands of bytes to three places. (Whether Steam counts
    // in 1000s or 1024s could not be told from the page; 1000 matches the digits it shows.)
    private static string DescribeSize(long bytes) => bytes >= 1_000_000
        ? Text(Strings.Item_FileSizeMb, bytes / 1_000_000d)
        : Text(Strings.Item_FileSizeKb, bytes / 1_000d);

    // ------------------------------------------------------------------ actions

    // Through Browse's own install command: same version pick, same mod-page gate, same queue.
    [RelayCommand]
    private void Subscribe()
    {
        AppServices.Browse.InstallCommand.Execute(Card);
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

        var mod = AppServices.ModCache.AllMods.FirstOrDefault(m => m.Id == item.ModId);
        if (mod is not null) await AppServices.Browse.LoadDetailsAsync(mod);
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
