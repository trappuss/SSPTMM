using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.Services;

//
// Public collections' pages read this session, for a while: pointing at a card on Browsing:
// Collections reads its list's page for the hover popup, and opening that collection a moment later
// shows it without reading it again. Ten minutes, and the forty most recent - a page a person is
// looking at, not a copy of the site.
//
public sealed class CollectionDetailsCache
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(10);
    private const int Most = 40;

    private readonly Dictionary<int, (SpModListDetails Details, DateTimeOffset At)> _kept = [];
    private readonly Dictionary<int, Task<SpModListDetails?>> _reading = [];

    /// <summary>The list's page, from what was read in the last ten minutes or from sp-mod.com.
    /// Two asking at once share one read. UI thread only.</summary>
    //
    // The shared read runs to its end whoever stops waiting for it: each caller's token only ends
    // its own wait. Tied to the first caller's token, a Quick View closing as it opened the
    // collection's page cancelled the very read that page had joined.
    //
    public Task<SpModListDetails?> GetAsync(int id, string slug, CancellationToken ct = default)
    {
        if (_kept.TryGetValue(id, out var kept) && DateTimeOffset.UtcNow - kept.At < KeepFor)
            return Task.FromResult<SpModListDetails?>(kept.Details);

        if (!_reading.TryGetValue(id, out var reading))
        {
            reading = ReadAsync(id, slug);

            // Already over (it failed before reaching the network): nothing to share, and its
            // finally has run - kept, it would be handed out from now on.
            if (!reading.IsCompleted) _reading[id] = reading;
        }

        return ct.CanBeCanceled ? reading.WaitAsync(ct) : reading;
    }

    /// <summary>A page read elsewhere (the collection page itself), kept for the popup.</summary>
    public void Remember(SpModListDetails details)
    {
        _kept[details.Id] = (details, DateTimeOffset.UtcNow);
        Trim();
    }

    private async Task<SpModListDetails?> ReadAsync(int id, string slug)
    {
        try
        {
            var details = await AppServices.SpModLists.GetListAsync(id, slug);
            if (details is not null) Remember(details);
            return details;
        }
        finally
        {
            _reading.Remove(id);
        }
    }

    private void Trim()
    {
        if (_kept.Count <= Most) return;

        foreach (var oldest in _kept.OrderBy(k => k.Value.At).Take(_kept.Count - Most).Select(k => k.Key).ToList())
            _kept.Remove(oldest);
    }
}
