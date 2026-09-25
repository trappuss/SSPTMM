using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TCFModManager.App.Localization;
using TCFModManager.App.Services;
using TCFModManager.App.Views;

namespace TCFModManager.App.ViewModels;

// One author on the followed-authors page: their picture and name, how many items the catalog has
// by them and when the newest was posted, their items in Browse, and Follow.
public sealed partial class FollowedAuthorRow : ObservableObject
{
    public FollowedAuthorRow(int id, string name, string? photo, int items, DateTimeOffset? newest)
    {
        Id = id;
        Name = name;
        Photo = photo;
        ItemsText = Strings.FollowedAuthors_Items(items, items);
        NewestText = newest is { } when ? LocalizationService.Text(Strings.FollowedAuthors_NewestFormat, SteamDates.Short(when)) : null;
        _isFollowing = AppServices.Followed.IsFollowing(id);
    }

    public int Id { get; }

    public string Name { get; }

    public string? Photo { get; }

    public string ItemsText { get; }

    public string? NewestText { get; }

    // The Follow button's state. Unfollowing keeps the row until the page is opened again, so a
    // slip can be undone where it was made.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowToolTip))]
    private bool _isFollowing;

    public string FollowToolTip => LocalizationService.Text(
        IsFollowing ? Strings.Workshop_UnfollowFormat : Strings.Workshop_FollowFormat, Name);

    [RelayCommand]
    private void ToggleFollow() => IsFollowing = AppServices.Followed.Set(Id, Name, !IsFollowing);

    [RelayCommand]
    private void OpenItems() => ModActions.ShowAuthor(Name);
}

//
// The authors followed from the Workshop pages, reached from Your Items. Steam keeps its list of
// followed authors on your profile, not in the Workshop; this app has no profile, so the list is
// kept here, drawn as a collection's rows are.
//
public sealed partial class FollowedAuthorsViewModel : LocalizedViewModel
{
    public ObservableCollection<FollowedAuthorRow> Authors { get; } = [];

    public bool IsEmpty => Authors.Count == 0;

    public string CountText => LocalizationService.Text(Strings.Collection_ItemsCountFormat, Authors.Count);

    /// <summary>Re-reads the followed authors and what the catalog has by each.</summary>
    public async Task RefreshAsync()
    {
        await AppServices.Browse.EnsureLoadedAsync();

        var catalog = AppServices.Browse.Catalog.ToList();

        Authors.Clear();
        foreach (var author in AppServices.Followed.All.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            var theirs = catalog
                .Where(m => m.Owner?.Id == author.Id || (m.AdditionalAuthors ?? []).Any(a => a.Id == author.Id))
                .ToList();

            // Their picture and current name from any listing of theirs; the stored name otherwise.
            var owner = theirs.Select(m => m.Owner).FirstOrDefault(o => o?.Id == author.Id)
                ?? theirs.SelectMany(m => m.AdditionalAuthors ?? []).FirstOrDefault(o => o.Id == author.Id);

            Authors.Add(new FollowedAuthorRow(
                author.Id,
                owner?.Name ?? author.Name ?? author.Id.ToString(),
                owner?.ProfilePhotoUrl,
                theirs.Count,
                theirs.Select(m => m.PublishedAt ?? m.CreatedAt).Max()));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    // Everything they made, newest first - Browse with Created by Followed.
    [RelayCommand]
    private static void ViewAllItems()
    {
        AppServices.Browse.ShowFollowed();
        AppNavigation.Navigate(typeof(BrowsePage));
    }
}
