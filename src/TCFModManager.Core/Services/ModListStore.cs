using System.Text.Json;
using System.Text.Json.Serialization;
using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

//
// Loads/saves ModListData as JSON under <app folder>\Data\mod_lists.json - every mod list this
// install holds and which one is currently applied.
//
// Nothing here touches the game folder. A list is a description of a set of mods; turning one into
// files on disk is the apply engine's job, and reading which mods are installed is the scanner's.
// A corrupt or hand-edited file falls back to an empty set rather than blocking the app.
//
public sealed class ModListStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    //
    // The shape this store writes. Bumped when a load has to change what is already on disk, which
    // so far has happened once: entries scoped Client were written before a headless was a machine
    // a list could name, and have to be widened to reach one. See Normalise.
    //
    public const int SchemaVersion = 1;

    public ModListStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "mod_lists.json");
    }

    public string FilePath => _filePath;

    public ModListData Load()
    {
        // A damaged file is kept aside and its backup put back - see SafeFile.
        var read = SafeFile.ReadJson(_filePath, json => JsonSerializer.Deserialize<ModListData>(json, Options) ?? new ModListData());
        if (read is null) return new ModListData { SchemaVersion = SchemaVersion };

        {
            var data = read;

            var storedVersion = data.SchemaVersion;

            data = Normalise(data);

            //
            // Written back on the spot, unlike the snapshot tidy-up above it, because the headless
            // widening is NOT idempotent: it turns Client into Client|Headless, and Client is also
            // exactly what an operator picks when they mean "players only, not the headless".
            // Re-running it on every load would undo that choice every time the app started. The
            // version stamp is what makes it run once and never again.
            //
            // A failed write is not a failed load - the data in hand is right either way, and the
            // migration simply gets another go next time.
            //
            if (storedVersion < SchemaVersion)
            {
                try
                {
                    Save(data);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            return data;
        }
    }

    //
    // Snapshots used to be ordinary lists, so a file written before the single slot existed can
    // hold a pile of them - one per apply, each named after the last ("Before Before Before ...").
    // They come out of Lists here: the newest fills the slot if it is empty, and the rest go, since
    // an undo point from three applies ago describes an install that no longer exists.
    //
    // Costs one pass over a handful of entries and does nothing at all once a file has been written
    // by this version, so it stays rather than being a migration to remember to delete.
    //
    private static ModListData Normalise(ModListData data)
    {
        WidenForHeadless(data);

        var snapshots = data.Lists.Where(l => l.IsSnapshot).OrderByDescending(l => l.UpdatedAt).ToList();
        if (snapshots.Count == 0) return data;

        data.Lists.RemoveAll(l => l.IsSnapshot);
        data.Snapshot ??= snapshots[0];

        if (data.ActiveListId is { } active && data.Lists.All(l => l.Id != active)) data.ActiveListId = null;

        return data;
    }

    //
    // Every entry scoped Client in a file written before the headless existed becomes
    // Client|Headless.
    //
    // Back then Client meant "not the server", because those were the only two machines the format
    // could describe. Read literally now it means "players, and not the headless" - so an operator's
    // own published list would stop delivering SAIN and the bot mods to the machine actually hosting
    // the raid, on the strength of a distinction its author never made.
    //
    // Runs once, gated on the file's stored SchemaVersion, because Client is a value the operator
    // can now choose deliberately and this would keep overwriting it.
    //
    private static void WidenForHeadless(ModListData data)
    {
        if (data.SchemaVersion >= SchemaVersion) return;

        foreach (var list in data.Lists.Append(data.Snapshot).OfType<ModList>())
        {
            for (var i = 0; i < list.Entries.Count; i++)
            {
                var entry = list.Entries[i];
                if (entry.Scope != ModListEntryScope.Client) continue;

                list.Entries[i] = new ModListEntry
                {
                    Name = entry.Name,
                    ModId = entry.ModId,
                    IsAddon = entry.IsAddon,
                    VersionId = entry.VersionId,
                    Version = entry.Version,
                    Guid = entry.Guid,
                    Folders = entry.Folders,
                    Scope = ModListEntryScope.Client | ModListEntryScope.Headless,
                };
            }
        }

        data.SchemaVersion = SchemaVersion;
    }

    public void Save(ModListData data)
    {
        data.SchemaVersion = SchemaVersion;

        SafeFile.WriteAllText(_filePath, JsonSerializer.Serialize(data, Options), keepBackup: true);
    }

    public ModList? Find(Guid id) => Load().Lists.FirstOrDefault(l => l.Id == id);

    //
    // Stores a list built elsewhere - a capture, an import, a fork. Replaces an existing list with
    // the same Id, so re-importing a newer revision of a list updates it rather than duplicating it.
    //
    // A snapshot is routed to its own slot rather than added, so there is one place that decides
    // where snapshots live and no caller can put one back among the browsable lists.
    //
    // WITH ONE EXCEPTION: a SERVED list never replaces a local one carrying the same Id. Ids are
    // guids, so that is not a coincidence - it is this machine's own list coming back off the server
    // it was published to, which is what happens the moment an operator points the app at their own
    // server. Replacing it turned the author's list read-only on the machine that wrote it, and
    // "Make a copy" became the only way back into a list they own. The local one is the master here;
    // the served copy is a copy of it, and it is dropped.
    //
    // Returns what is stored rather than what was passed, so a caller that goes on to show or follow
    // the list shows the one that is actually in the store.
    //
    public ModList Add(ModList list)
    {
        if (list.IsSnapshot) return SetSnapshot(list)!;

        var data = Load();

        if (list.Origin == ModListOrigin.Server
            && data.Lists.FirstOrDefault(l => l.Id == list.Id) is { IsEditable: true } mine)
        {
            return mine;
        }

        data.Lists.RemoveAll(l => l.Id == list.Id);
        data.Lists.Add(list);
        Save(data);
        return list;
    }

    //
    // Marks one list as the one this machine publishes, replacing whatever was marked before.
    //
    // Exactly one, because the server serves exactly one file. Two lists both claiming to be
    // published would be a claim the folder cannot honour, and the second publish would silently
    // overwrite the first - which is why this is one pointer rather than a flag per list.
    //
    // Refuses an id this store does not hold, like SetActive: a pointer at a list nobody can open
    // is a badge that never appears and a state nothing can clear.
    //
    public ModList? SetPublished(Guid id)
    {
        var data = Load();

        var published = data.Lists.FirstOrDefault(l => l.Id == id);
        if (published is null) return null;

        data.PublishedListId = id;
        Save(data);
        return published;
    }

    // The list this machine publishes, or null when it has never published one - or when the list
    // it published has since been deleted.
    public ModList? GetPublished()
    {
        var data = Load();
        return data.PublishedListId is null
            ? null
            : data.Lists.FirstOrDefault(l => l.Id == data.PublishedListId);
    }

    // Replaces the one undo point. Null clears it - what reverting does, since you cannot revert
    // a revert.
    public ModList? SetSnapshot(ModList? snapshot)
    {
        var data = Load();
        data.Snapshot = snapshot;
        Save(data);
        return snapshot;
    }

    public ModList? GetSnapshot() => Load().Snapshot;

    public IReadOnlySet<string> GetPins() =>
        new HashSet<string>(Load().NeverAutoDisable, StringComparer.OrdinalIgnoreCase);

    public void SetPinned(IEnumerable<string> keys, bool pinned)
    {
        var normalised = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (normalised.Count == 0) return;

        var data = Load();

        var existing = new HashSet<string>(data.NeverAutoDisable, StringComparer.OrdinalIgnoreCase);

        if (pinned)
            data.NeverAutoDisable.AddRange(normalised.Where(k => !existing.Contains(k)));
        else
            data.NeverAutoDisable.RemoveAll(k => normalised.Contains(k, StringComparer.OrdinalIgnoreCase));

        Save(data);
    }

    public void Rename(Guid id, string name)
    {
        var data = Load();
        var list = data.Lists.FirstOrDefault(l => l.Id == id);
        if (list is null || !list.IsEditable || list.Name == name) return;

        list.Name = name;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        Save(data);
    }

    public void SetPolicy(Guid id, ModListPolicy policy)
    {
        var data = Load();
        var list = data.Lists.FirstOrDefault(l => l.Id == id);
        if (list is null || !list.IsEditable || list.Policy == policy) return;

        list.Policy = policy;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        Save(data);
    }

    //
    // Replaces a local list's contents - the one write path for an edit, called when the user saves.
    // Refused for an imported or served list; Fork is the way to edit one of those.
    //
    // Entries are stored in the order given, so the caller decides it (ModListEntries.Sorted is what
    // capture and the page both use). Does not touch Revision - see BumpRevision for what moves it -
    // and does not touch a single file in the game folder. Applying the list is what does that.
    //
    public ModList? ReplaceEntries(Guid id, IEnumerable<ModListEntry> entries)
    {
        var data = Load();
        var list = data.Lists.FirstOrDefault(l => l.Id == id);
        if (list is null || !list.IsEditable) return null;

        list.Entries.Clear();
        list.Entries.AddRange(entries);
        list.UpdatedAt = DateTimeOffset.UtcNow;
        Save(data);
        return list;
    }

    //
    // Counts one more time this list has been put into effect.
    //
    // The revision moves here and nowhere else. Editing a list is thinking about it; applying one
    // is the arrangement of mods that actually existed on disk, and that is the thing worth
    // numbering - both for the person reading "rev 4" and for a receiver deciding whether the copy
    // they were sent is newer than the one they have.
    //
    // Local lists only. The revision of a list somebody else wrote is theirs to number: bumping it
    // here would leave their next revision looking older than this copy of it.
    //
    public ModList? BumpRevision(Guid id)
    {
        var data = Load();
        var list = data.Lists.FirstOrDefault(l => l.Id == id);
        if (list is null || !list.IsEditable) return null;

        list.Revision++;
        list.UpdatedAt = DateTimeOffset.UtcNow;
        Save(data);
        return list;
    }

    // Copies an imported or served list into a new local list carrying a DerivedFrom pointer back
    // at it, so the original stays exactly as it was received.
    public ModList Fork(Guid id, string name, DateTimeOffset timestamp)
    {
        var data = Load();
        var source = data.Lists.FirstOrDefault(l => l.Id == id)
            ?? throw new InvalidOperationException($"No mod list with id {id}.");

        var fork = new ModList
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = source.Description,
            Revision = 1,
            Origin = ModListOrigin.Local,
            Policy = source.Policy,
            DerivedFrom = source.Id,
            SptVersion = source.SptVersion,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };

        fork.Entries.AddRange(source.Entries);
        data.Lists.Add(fork);
        Save(data);
        return fork;
    }

    // Removes the list. Clears the active pointer too when it was the one being followed, so the
    // install stops claiming to follow a list that no longer exists.
    public void Delete(Guid id)
    {
        var data = Load();
        if (data.Lists.RemoveAll(l => l.Id == id) == 0) return;

        if (data.ActiveListId == id) data.ActiveListId = null;
        if (data.ActiveServerListId == id) data.ActiveServerListId = null;

        // The file in the server's config folder is left exactly where it is. Deleting the list here
        // is not a decision to stop serving, and a server quietly unpublishing because somebody
        // tidied their mod lists page would be a raid nobody could explain.
        if (data.PublishedListId == id) data.PublishedListId = null;

        Save(data);
    }

    // Marks which list the install is currently following, or null for none. One at a time.
    public void SetActive(Guid? id)
    {
        var data = Load();
        if (id is not null && data.Lists.All(l => l.Id != id)) return;
        if (data.ActiveListId == id) return;

        data.ActiveListId = id;
        Save(data);
    }

    public ModList? GetActive()
    {
        var data = Load();
        return data.ActiveListId is null ? null : data.Lists.FirstOrDefault(l => l.Id == data.ActiveListId);
    }

    //
    // Marks which SERVER list the install is following. Its own slot, so following a server does not
    // cost the user the personal list they were already following - see ModListData.
    //
    public void SetActiveServer(Guid? id)
    {
        var data = Load();
        if (id is not null && data.Lists.All(l => l.Id != id)) return;
        if (data.ActiveServerListId == id) return;

        data.ActiveServerListId = id;
        Save(data);
    }

    public ModList? GetActiveServer()
    {
        var data = Load();

        return data.ActiveServerListId is null
            ? null
            : data.Lists.FirstOrDefault(l => l.Id == data.ActiveServerListId);
    }
}
