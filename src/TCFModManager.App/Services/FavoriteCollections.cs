using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.Services;

//
// The public collections favorited from the Workshop pages (Steam's Favorite on a collection), kept
// in settings.json by their sp-mod.com id. sp-mod.com has no favorites for lists, so these are this
// app's alone.
//
// Each remembers what its page said the last time it was opened here - when it was last updated and
// how many items it had - so the Favorites page can say which have changed since. Opening one marks
// it seen.
//
public sealed class FavoriteCollections
{
    private readonly SettingsService _settings = new();
    private List<FavoriteCollection> _favorites;

    public FavoriteCollections() => _favorites = _settings.Load().FavoriteCollections ?? [];

    /// <summary>Raised after a favorite is added, removed or marked seen.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<FavoriteCollection> All => _favorites;

    public bool IsFavorite(int id) => _favorites.Any(f => f.Id == id);

    /// <summary>Favorites (or stops favoriting) a collection read from its page - seen as it is now.
    /// True when now a favorite.</summary>
    public bool Set(SpModListDetails details, bool favorite) =>
        Set(details.Id, favorite, f => Fill(f, details));

    /// <summary>The same, from a card: seen as the card says it is, until its page is opened.</summary>
    public bool Set(SpModListSummary summary, bool favorite) =>
        Set(summary.Id, favorite, f =>
        {
            f.Slug = summary.Slug;
            f.Title = summary.Title;
            f.Author = summary.Author;
            f.Cover = summary.Cover;
            f.Teaser = summary.Teaser;
            f.SeenUpdatedAt = summary.UpdatedAt;
        });

    private bool Set(int id, bool favorite, Action<FavoriteCollection> fill)
    {
        if (id <= 0) return false;
        if (IsFavorite(id) == favorite) return favorite;

        Save(list =>
        {
            list.RemoveAll(f => f.Id == id);
            if (!favorite) return;

            var added = new FavoriteCollection { Id = id, AddedAt = DateTimeOffset.UtcNow };
            fill(added);
            list.Add(added);
        });

        AppLog.Info("Workshop", favorite ? $"favorited collection {id}" : $"unfavorited collection {id}");
        return favorite;
    }

    /// <summary>Takes a favorite out, and gives back what it was - for putting back exactly.</summary>
    public FavoriteCollection? Remove(int id)
    {
        if (_favorites.FirstOrDefault(f => f.Id == id) is not { } removed) return null;

        Save(list => list.RemoveAll(f => f.Id == id));
        AppLog.Info("Workshop", $"unfavorited collection {id}");
        return removed;
    }

    /// <summary>Puts back a favorite just taken out, as it was: what had been seen, and when it was
    /// added.</summary>
    public void Restore(FavoriteCollection favorite)
    {
        if (IsFavorite(favorite.Id)) return;

        Save(list => list.Add(favorite));
        AppLog.Info("Workshop", $"favorited collection {favorite.Id} again");
    }

    /// <summary>A favorite's page was opened: what it says now is what has been seen.</summary>
    public void MarkSeen(SpModListDetails details)
    {
        var favorite = _favorites.FirstOrDefault(f => f.Id == details.Id);
        if (favorite is null) return;
        // Nothing new to note: the file is left alone.
        if (!HasChanged(favorite, details) && favorite.SeenItemCount is not null && favorite.Title == details.Title
            && favorite.Cover == details.Cover && (Teaser(details) ?? favorite.Teaser) == favorite.Teaser) return;

        Save(list =>
        {
            if (list.FirstOrDefault(f => f.Id == details.Id) is { } stored) Fill(stored, details);
        });
    }

    /// <summary>Whether the page now says something other than what was last seen: a later update,
    /// or another number of items.</summary>
    public static bool HasChanged(FavoriteCollection favorite, SpModListDetails details)
    {
        if (details.UpdatedAt is { } updated && favorite.SeenUpdatedAt is { } seen && updated > seen) return true;
        return favorite.SeenItemCount is { } count && count != ItemCount(details);
    }

    // Every entry the page shows, those no longer available included: the same count each time.
    private static int ItemCount(SpModListDetails details) => details.Items.Count + details.Unavailable;

    private static void Fill(FavoriteCollection favorite, SpModListDetails details)
    {
        favorite.Slug = details.Slug;
        favorite.Title = details.Title;
        favorite.Author = details.Author;
        favorite.Cover = details.Cover;
        favorite.Teaser = Teaser(details) ?? favorite.Teaser;
        favorite.SeenUpdatedAt = details.UpdatedAt;
        favorite.SeenItemCount = ItemCount(details);
    }

    // The description's words, for the row - as the Collections cards show a list's.
    private static string? Teaser(SpModListDetails details) =>
        details.DescriptionHtml is { } html
        && TCFModManager.Core.Markup.SpModMarkup.PlainText(TCFModManager.Core.Markup.SpModMarkup.Parse(html)) is { Length: > 0 } text
            ? text
            : null;

    // Read, changed and written back whole, so a setting saved elsewhere since is not lost.
    private void Save(Action<List<FavoriteCollection>> change)
    {
        var settings = _settings.Load();
        var list = settings.FavoriteCollections ?? [];
        change(list);
        settings.FavoriteCollections = list;
        _settings.Save(settings);

        _favorites = list;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
