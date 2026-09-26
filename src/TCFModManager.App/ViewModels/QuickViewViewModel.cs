using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.Core.Markup;
using TCFModManager.Core.Models;

namespace TCFModManager.App.ViewModels;

// Shared, app-lifetime signal for Quick View. MainWindow subscribes to Requested and shows
// QuickViewOverlay; Sequence is the list the mod was picked from, for the arrows beside it.
public sealed record QuickViewRequest(Mod Mod, IReadOnlyList<Mod> Sequence);

public sealed class QuickViewOverlayViewModel
{
    public event EventHandler<QuickViewRequest>? Requested;

    public void Show(Mod mod, IReadOnlyList<Mod>? sequence = null) =>
        Requested?.Invoke(this, new QuickViewRequest(mod, sequence is { Count: > 0 } ? sequence : [mod]));
}

//
// Steam's Quick View (the magnifier on a Browse card): the item's pictures, its name and author,
// posted / updated / file size and its counts, the tags, the start of the description, and
// Subscribe and Add to Collection - over the page, with arrows either side that step through the
// list it was opened from.
//
// The item page's own view model does the work (the pictures, the dates, the size of the release
// Subscribe fetches, Subscribe itself), so the two can never disagree. Steam's Visitors and
// Subscribers have no sp-mod.com figure; Downloads and Favorites stand in their place.
//
public sealed partial class QuickViewViewModel : LocalizedViewModel, IModActionHost
{
    void IModActionHost.ShowActionMessage(string? message) => Message = message;

    private readonly IReadOnlyList<Mod> _sequence;

    // Bumped on every step, so a slow details fetch for an item already stepped past is dropped.
    private int _generation;

    public QuickViewViewModel(QuickViewRequest request)
    {
        _sequence = request.Sequence;
        _index = Math.Max(0, IndexOf(request.Mod));
        _ = LoadAsync();
    }

    private int IndexOf(Mod mod)
    {
        for (var i = 0; i < _sequence.Count; i++)
            if (_sequence[i].Id == mod.Id) return i;
        return -1;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Listing), nameof(CanGoPrevious), nameof(CanGoNext), nameof(Description), nameof(Tags), nameof(HasTags))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    private int _index;

    /// <summary>The catalog listing being shown - drawn at once, before its details arrive.</summary>
    public Mod Listing => _sequence[Index];

    public bool CanGoPrevious => Index > 0;

    public bool CanGoNext => Index < _sequence.Count - 1;

    // The item page's view model for the shown mod, once its details have arrived.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem), nameof(Description), nameof(Tags), nameof(HasTags))]
    private WorkshopItemViewModel? _item;

    public bool HasItem => Item is not null;

    [ObservableProperty]
    private bool _isLoading;

    // What the last action said, or why the details could not be had.
    [ObservableProperty]
    private string? _message;

    // Steam shows the description as plain text, cut after a few lines.
    public string? Description => Item?.DescriptionHtml is { } html
        ? SpModMarkup.PlainText(SpModMarkup.Parse(html))
        : Listing.Teaser;

    // TAGS: the content type, then the mod's flags - the chips the Browse popup shows.
    public IReadOnlyList<string> Tags
    {
        get
        {
            var mod = Item?.Mod ?? Listing;
            var tags = new List<string>();
            if (mod.Category?.Title is { Length: > 0 } category) tags.Add(category);
            if (mod.FikaCompatibility == true) tags.Add(Strings.Common_FlagFikaCompatible);
            if (mod.ContainsAds == true) tags.Add(Strings.Common_FlagContainsAds);
            if (mod.ContainsAiContent == true) tags.Add(Strings.Common_FlagContainsAiContent);
            return tags;
        }
    }

    public bool HasTags => Tags.Count > 0;

    private async Task LoadAsync()
    {
        Detach();
        Item = null;

        var generation = ++_generation;
        var listing = Listing;
        Message = null;
        IsLoading = true;

        var (details, failure) = await AppServices.Browse.FetchDetailsAsync(listing);
        if (generation != _generation) return;

        IsLoading = false;
        if (details is null)
        {
            Message = failure;
            return;
        }

        var item = new WorkshopItemViewModel(new ModDetailsRequest(details, AppServices.Browse.InstalledMatchFor(listing)?.InstalledVersion));
        item.PropertyChanged += OnItemChanged;
        Item = item;
        _ = item.LoadAsync();
    }

    // The item page's own line (what Subscribe said, and so on) is this one too.
    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkshopItemViewModel.Message) && sender is WorkshopItemViewModel item)
            Message = item.Message;
    }

    /// <summary>Lets go of the shown item's view model; the overlay calls this as it closes.</summary>
    public void Detach()
    {
        // A details fetch still on its way is dropped when it lands.
        _generation++;

        if (Item is not { } item) return;

        item.PropertyChanged -= OnItemChanged;
        item.Detach();
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

    // Subscribe, or - subscribed - Unsubscribe, through the item page's own buttons.
    [RelayCommand]
    private async Task ToggleSubscribeAsync()
    {
        if (Item is not { } item) return;

        if (item.IsSubscribed) await item.UnsubscribeCommand.ExecuteAsync(null);
        else await item.SubscribeCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void AddToCollection() => Item?.AddToCollectionCommand.Execute(null);

    /// <summary>Raised by See More, before the item page opens: the overlay closes.</summary>
    public event EventHandler? Leaving;

    // Steam's See More: the whole item page.
    [RelayCommand]
    private async Task SeeMoreAsync()
    {
        Leaving?.Invoke(this, EventArgs.Empty);
        if (await ModActions.OpenAsync(Listing) is { } failed) AppServices.Browse.StatusMessage = failed;
    }

    [RelayCommand]
    private void OpenAuthor()
    {
        if (Listing.Owner?.Name is not { } author) return;

        Leaving?.Invoke(this, EventArgs.Empty);
        ModActions.ShowAuthor(author, Listing.Owner.Id);
    }
}
