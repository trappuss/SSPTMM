using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.ViewModels;

/// <summary>One favorited collection on the Favorites page: its card, whether its page has changed
/// since it was last opened here, and Favorite.</summary>
public sealed partial class FavoriteCollectionRow : ObservableObject
{
    public FavoriteCollectionRow(FavoriteCollection favorite)
    {
        Id = favorite.Id;
        Slug = favorite.Slug;
        _card = CardFor(favorite, favorite.SeenItemCount ?? 0);
    }

    public int Id { get; }

    public string Slug { get; }

    // Steam's collection row, with the hover popup - drawn from what was stored until the page is read.
    [ObservableProperty]
    private CollectionCardViewModel _card;

    // Its author has changed it since it was last opened here.
    [ObservableProperty]
    private bool _hasChanged;

    // Its page could not be read just now; the stored card stays.
    [ObservableProperty]
    private bool _couldNotCheck;

    // Unfavoriting keeps the row until the page is opened again, so a slip can be undone where it
    // was made - as on Followed authors.
    [ObservableProperty]
    private bool _isFavorite = true;

    // The page as last read, to say again whether it has changed once it has been opened.
    private SpModListDetails? _details;

    /// <summary>Says again whether it has changed, against what is now marked seen.</summary>
    public void Recheck(FavoriteCollection stored)
    {
        if (_details is not null) HasChanged = FavoriteCollections.HasChanged(stored, _details);
    }

    public void Read(SpModListDetails details, FavoriteCollection stored)
    {
        _details = details;
        HasChanged = FavoriteCollections.HasChanged(stored, details);

        var summary = new SpModListSummary
        {
            Id = details.Id,
            Slug = details.Slug,
            Title = details.Title,
            SptVersion = details.SptVersion,
            Author = details.Author,
            Teaser = Card.Summary.Teaser,
            ItemCount = details.Items.Count + details.Unavailable,
            UpdatedAt = details.UpdatedAt,
            Cover = details.Cover,
        };
        Card = new CollectionCardViewModel(summary, AppServices.SptEnvironment.InstalledVersion, PublicCollections.Stored(details.Id) is not null);
    }

    private static CollectionCardViewModel CardFor(FavoriteCollection favorite, int items) =>
        new(new SpModListSummary
            {
                Id = favorite.Id,
                Slug = favorite.Slug,
                Title = favorite.Title ?? favorite.Id.ToString(),
                Author = favorite.Author,
                Teaser = favorite.Teaser,
                ItemCount = items,
                UpdatedAt = favorite.SeenUpdatedAt,
                Cover = favorite.Cover,
            },
            AppServices.SptEnvironment.InstalledVersion,
            PublicCollections.Stored(favorite.Id) is not null);

    // What was taken out, to put back exactly as it was - what had been seen included.
    private FavoriteCollection? _removed;

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (IsFavorite)
        {
            _removed = AppServices.Favorites.Remove(Id);
            IsFavorite = false;
        }
        else
        {
            if (_removed is not null) AppServices.Favorites.Restore(_removed);
            else AppServices.Favorites.Set(Card.Summary, true);
            IsFavorite = true;
        }
    }
}

//
// The collections favorited from the Workshop pages, from Your Items. Each one's page is read when
// the page opens, so one its author has changed since it was last opened here says so.
//
public sealed partial class FavoriteCollectionsViewModel : LocalizedViewModel
{
    public ObservableCollection<FavoriteCollectionRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public string CountText => LocalizationService.Text(Strings.Collection_ItemsCountFormat, Rows.Count);

    [ObservableProperty]
    private bool _isChecking;

    private CancellationTokenSource? _reading;

    /// <summary>Re-reads the favorites, then each one's page.</summary>
    public async Task RefreshAsync()
    {
        _reading?.Cancel();
        var reading = _reading = new CancellationTokenSource();

        Rows.Clear();
        foreach (var favorite in AppServices.Favorites.All.OrderByDescending(f => f.AddedAt))
            Rows.Add(new FavoriteCollectionRow(favorite));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));

        IsChecking = Rows.Count > 0;
        try
        {
            // One at a time: a handful of pages, and sp-mod.com is not asked for them all at once.
            foreach (var row in Rows.ToList())
            {
                if (reading.IsCancellationRequested) return;

                if (!AppServices.Favorites.IsFavorite(row.Id)) continue;

                try
                {
                    var details = await AppServices.CollectionDetails.GetAsync(row.Id, row.Slug, reading.Token);

                    // What is seen now - it may have been opened while its page was being read.
                    var stored = AppServices.Favorites.All.FirstOrDefault(f => f.Id == row.Id);
                    if (details is null) row.CouldNotCheck = true;
                    else if (stored is not null) row.Read(details, stored);
                }
                catch (OperationCanceledException) when (reading.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Debug("Collections", $"favorite {row.Id} could not be read: {ex.Message}");
                    row.CouldNotCheck = true;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_reading, reading)) IsChecking = false;
        }
    }

    [RelayCommand]
    private static void Open(FavoriteCollectionRow? row)
    {
        if (row is not null) AppServices.CollectionOverlay.ShowPublic(row.Id, row.Slug);
    }

    [RelayCommand]
    private static void BrowseCollections() => AppNavigation.Navigate(typeof(Views.CollectionsBrowsePage));

    public FavoriteCollectionsViewModel() => AppServices.Favorites.Changed += OnFavoritesChanged;

    // One opened from here (its page marks it seen) no longer says it has changed.
    private void OnFavoritesChanged(object? sender, EventArgs e)
    {
        foreach (var row in Rows)
        {
            if (AppServices.Favorites.All.FirstOrDefault(f => f.Id == row.Id) is { } stored) row.Recheck(stored);
        }
    }

    /// <summary>Stops reading pages once the page is left.</summary>
    public void Stop() => _reading?.Cancel();
}
