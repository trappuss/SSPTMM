using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Markup;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime signal for a collection's Quick View. MainWindow subscribes to Requested and
// shows CollectionQuickViewOverlay; Sequence is the list the collection was picked from, for the
// arrows beside it.
public sealed record CollectionQuickViewRequest(SpModListSummary Collection, IReadOnlyList<SpModListSummary> Sequence);

public sealed class CollectionQuickViewSignal
{
    public event EventHandler<CollectionQuickViewRequest>? Requested;

    public void Show(SpModListSummary collection, IReadOnlyList<SpModListSummary>? sequence = null) =>
        Requested?.Invoke(this, new CollectionQuickViewRequest(collection, sequence is { Count: > 0 } ? sequence : [collection]));
}

/// <summary>One square under CONTAINS N ITEMS: an item's picture, or "+N" for the rest.</summary>
public sealed record CollectionQuickViewTile(string? Picture, string? MoreText, SpModListItem? Item)
{
    public bool IsMore => MoreText is not null;
}

//
// Steam's Quick View of a collection (the magnifier on a Browsing: Collections card): its title and
// who made it, when it was updated, its description, CONTAINS N ITEMS over the first items'
// pictures and "+N" for the rest, and Favorite - over the page, with arrows either side that step
// through the list it was opened from. What the card knows shows at once; the rest once the list's
// page is read (kept a while, so the hover popup and the collection page read it only once).
//
// Steam's ratings, visitors, favorites count, tags, votes and awards have nothing behind them on
// sp-mod.com: left out. Where Steam's stars sit, the SPT version it was made for, as on the cards.
//
public sealed partial class CollectionQuickViewViewModel : LocalizedViewModel
{
    // Steam's row of squares: ten, the last "+N" when there are more.
    private const int Slots = 10;

    private readonly IReadOnlyList<SpModListSummary> _sequence;

    // Bumped on every step, so a slow page read for a collection already stepped past is dropped.
    private int _generation;

    private readonly CancellationTokenSource _closing = new();

    public CollectionQuickViewViewModel(CollectionQuickViewRequest request)
    {
        _sequence = request.Sequence;
        var index = 0;
        for (var i = 0; i < _sequence.Count; i++)
        {
            if (_sequence[i].Id != request.Collection.Id) continue;
            index = i;
            break;
        }

        _index = index;
        _ = LoadAsync();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Listing), nameof(CanGoPrevious), nameof(CanGoNext), nameof(Title), nameof(Author),
        nameof(HasAuthor), nameof(SptText), nameof(UpdatedText), nameof(Description), nameof(ContainsText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    private int _index;

    /// <summary>The card being shown - drawn at once, before its page is read.</summary>
    public SpModListSummary Listing => _sequence[Index];

    public bool CanGoPrevious => Index > 0;

    public bool CanGoNext => Index < _sequence.Count - 1;

    // The list's page, once read.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Author), nameof(HasAuthor), nameof(SptText), nameof(UpdatedText),
        nameof(Description), nameof(ContainsText))]
    private SpModListDetails? _details;

    [ObservableProperty]
    private bool _isLoading;

    // Why the page could not be read, or what the last action said.
    [ObservableProperty]
    private string? _message;

    public string Title => Details?.Title ?? Listing.Title;

    public string? Author => Details?.Author ?? Listing.Author;

    public bool HasAuthor => Author is not null;

    public string? SptText => (Details?.SptVersion ?? Listing.SptVersion) is { } spt
        ? LocalizationService.Text(Strings.Collection_ForSptFormat, spt)
        : null;

    public string? UpdatedText => SteamDates.Short(Details?.UpdatedAt ?? Listing.UpdatedAt);

    // Steam shows the description as plain text, cut after a few lines.
    public string? Description => Details?.DescriptionHtml is { } html
        && SpModMarkup.PlainText(SpModMarkup.Parse(html)) is { Length: > 0 } text
            ? text
            : Listing.Teaser;

    // How many entries it has, as its card says: sp-mod.com counts the ones no longer available too.
    private int Total => Math.Max(Listing.ItemCount, Details is { } details ? details.Items.Count + details.Unavailable : 0);

    // "CONTAINS 235 ITEMS": Steam's words, upper case in its design.
    public string ContainsText => Strings.Collections_Contains(Total, Total).ToUpper(System.Globalization.CultureInfo.CurrentCulture);

    public ObservableCollection<CollectionQuickViewTile> Tiles { get; } = [];

    [ObservableProperty]
    private bool _isFavorite;

    private async Task LoadAsync()
    {
        var generation = ++_generation;
        var listing = Listing;

        Details = null;
        Tiles.Clear();
        Message = null;
        IsFavorite = AppServices.Favorites.IsFavorite(listing.Id);
        IsLoading = true;

        try
        {
            var details = await AppServices.CollectionDetails.GetAsync(listing.Id, listing.Slug, _closing.Token);
            if (generation != _generation) return;

            if (details is null)
            {
                Message = Strings.Collection_PublicGone;
                return;
            }

            Details = details;
            FillTiles(details);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closed.
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            AppLog.Debug("Collections", $"quick view of {listing.Id}: {ex.Message}");
            Message = LocalizationService.Text(Strings.Collection_PublicLoadFailedFormat, ex.Message);
        }
        finally
        {
            if (generation == _generation) IsLoading = false;
        }
    }

    // Each item's own picture from the catalog where it has one, else the list page's.
    private void FillTiles(SpModListDetails details)
    {
        var catalog = PublicCollections.CatalogById();
        var items = details.Items;

        // "+N" counts every entry not shown, those no longer available included - so the squares and
        // CONTAINS N ITEMS add up.
        var shown = Total > Slots ? Math.Min(Slots - 1, items.Count) : items.Count;

        foreach (var item in items.Take(shown))
        {
            var picture = catalog.GetValueOrDefault(item.ModId)?.Thumbnail is { Length: > 0 } own ? own : item.Thumbnail;
            Tiles.Add(new CollectionQuickViewTile(picture, null, item));
        }

        if (Total > shown)
            Tiles.Add(new CollectionQuickViewTile(null, LocalizationService.Text(Strings.Collections_MoreItemsFormat, Total - shown), null));
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous()
    {
        if (!CanGoPrevious) return;
        Index--;
        _ = LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (!CanGoNext) return;
        Index++;
        _ = LoadAsync();
    }

    /// <summary>Raised before a page opens from here (the collection, an item, the author): the
    /// overlay closes.</summary>
    public event EventHandler? Leaving;

    // The title, and See More: the collection's own page.
    [RelayCommand]
    private void OpenCollection()
    {
        Leaving?.Invoke(this, EventArgs.Empty);
        AppServices.CollectionOverlay.ShowPublic(Listing.Id, Listing.Slug);
    }

    [RelayCommand]
    private void OpenAuthor()
    {
        if (Author is not { } author) return;

        Leaving?.Invoke(this, EventArgs.Empty);
        ModActions.ShowAuthor(author, SpModListAddress.UserId(Details?.AuthorUrl));
    }

    // A square: that item's page - here when the catalog has it, on sp-mod.com otherwise; "+N" opens
    // the collection.
    [RelayCommand]
    private async Task OpenTileAsync(CollectionQuickViewTile? tile)
    {
        if (tile is null) return;

        if (tile.Item is not { } item)
        {
            OpenCollection();
            return;
        }

        if (AppServices.Browse.FindInCatalog(item.ModId) is { } mod)
        {
            Leaving?.Invoke(this, EventArgs.Empty);
            if (await ModActions.OpenAsync(mod) is { } failed) AppServices.Browse.StatusMessage = failed;
            return;
        }

        MarkupActions.OpenInBrowser(SpModListAddress.ForMod(item.ModId, item.Slug));
    }

    // Steam's Favorite - see FavoriteCollections.
    [RelayCommand]
    private void ToggleFavorite() =>
        IsFavorite = Details is { } details
            ? AppServices.Favorites.Set(details, !IsFavorite)
            : AppServices.Favorites.Set(Listing, !IsFavorite);

    /// <summary>Stops a page read still on its way; the overlay calls this as it closes.</summary>
    public void Detach()
    {
        _generation++;
        _closing.Cancel();
    }
}
