using TCFModManager.Core.Models;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.Services;

/// <summary>An sp-mod.com member as far as the app knows them without asking: their id when known,
/// their name, and their pictures.</summary>
public sealed record AuthorRef(int? Id, string Name, string? Avatar, string? Cover);

//
// Who the authors are. Most are already in the catalog - every mod and addon carries its owner and
// co-authors, with their ids and pictures - so a name is looked up there without a request. A
// member's own page (followers, member since, their public lists) is read from sp-mod.com when an
// author page asks, and kept for the session.
//
public sealed class AuthorDirectory
{
    private readonly Dictionary<int, Task<SpModUserPage?>> _pages = [];

    /// <summary>The author of that name among the catalog's mods and addons, if any.</summary>
    public AuthorRef? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var owner = Owners().FirstOrDefault(o => string.Equals(o.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        return owner is null ? null : From(owner);
    }

    /// <summary>The author with that sp-mod.com id among the catalog's mods and addons, if any.</summary>
    public AuthorRef? FindById(int id)
    {
        var owner = Owners().FirstOrDefault(o => o.Id == id);
        return owner is null ? null : From(owner);
    }

    private static AuthorRef From(Owner owner) =>
        new(owner.Id > 0 ? owner.Id : null, owner.Name ?? string.Empty,
            string.IsNullOrWhiteSpace(owner.ProfilePhotoUrl) ? null : owner.ProfilePhotoUrl,
            string.IsNullOrWhiteSpace(owner.CoverPhotoUrl) ? null : owner.CoverPhotoUrl);

    // Owners first: a co-author entry can carry less than the same person's own listing.
    private static IEnumerable<Owner> Owners()
    {
        foreach (var mod in AppServices.ModCache.AllMods)
        {
            if (mod.Owner is { } owner) yield return owner;
        }

        foreach (var addon in AppServices.Addons.AllAddons)
        {
            if (addon.Owner is { } owner) yield return owner;
        }

        foreach (var mod in AppServices.ModCache.AllMods)
        {
            foreach (var coAuthor in mod.AdditionalAuthors ?? []) yield return coAuthor;
        }
    }

    /// <summary>A member's page from sp-mod.com, read once a session. UI thread only.</summary>
    public Task<SpModUserPage?> GetPageAsync(int id)
    {
        if (_pages.TryGetValue(id, out var page) && !page.IsFaulted && !page.IsCanceled) return page;

        page = AppServices.SpModLists.GetUserAsync(id);
        _pages[id] = page;
        return page;
    }

    /// <summary>Reads the member's page again next time it is asked for.</summary>
    public void Forget(int id) => _pages.Remove(id);
}
