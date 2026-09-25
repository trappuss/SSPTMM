using TCFModManager.Core.Models;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// The authors followed from the Workshop pages (Steam's Follow on an author's Workshop), kept in
// settings.json by sp-mod.com user id. A mod is "from a followed author" when its owner or any of
// its credited authors is followed.
//
public sealed class FollowedAuthors
{
    private readonly SettingsService _settings = new();
    private List<FollowedAuthor> _authors;
    private HashSet<int> _ids;

    public FollowedAuthors()
    {
        _authors = _settings.Load().FollowedAuthors ?? [];
        _ids = _authors.Select(a => a.Id).ToHashSet();
    }

    /// <summary>Raised after a follow or unfollow.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<FollowedAuthor> All => _authors;

    public bool HasAny => _ids.Count > 0;

    public bool IsFollowing(int? id) => id is { } value && _ids.Contains(value);

    public bool IsByFollowed(Mod mod) =>
        IsFollowing(mod.Owner?.Id) || (mod.AdditionalAuthors ?? []).Any(a => IsFollowing(a.Id));

    /// <summary>Follows (or stops following) this author; true when now following. Asked for
    /// explicitly rather than flipped, so a button showing an out-of-date state still does what it
    /// says.</summary>
    public bool Set(int id, string? name, bool follow)
    {
        if (id == 0) return false;
        if (IsFollowing(id) == follow) return follow;

        var settings = _settings.Load();
        var list = settings.FollowedAuthors ?? [];

        list.RemoveAll(a => a.Id == id);
        var following = follow;
        if (following) list.Add(new FollowedAuthor { Id = id, Name = name });

        settings.FollowedAuthors = list;
        _settings.Save(settings);

        _authors = list;
        _ids = list.Select(a => a.Id).ToHashSet();

        AppLog.Info("Workshop", following ? $"following {name} ({id})" : $"stopped following {name} ({id})");
        Changed?.Invoke(this, EventArgs.Empty);
        return following;
    }
}
