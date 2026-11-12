using TCFModManager.Core.Models;

namespace TCFModManager.Core.Services;

/// <summary>A server mod as an SPT server lists it (/launcher/v2/mods): what its author declared.</summary>
public sealed record SptServerMod
{
    public string ModGuid { get; init; } = "";

    public string Name { get; init; } = "";

    public string Author { get; init; } = "";

    public string Version { get; init; } = "";

    public string? Url { get; init; }
}

//
// Fork (1.4.7): a collection made from the server mods another PC's server has loaded, so the mods
// here can be brought to match it - for Fika, where a mismatch is what most often keeps a player
// out. Any SPT server answers the question; the host needs nothing installed for it.
//
// The collection is an ordinary one, Additive: applying it installs, updates and turns on, and never
// turns anything off - what else is installed here is the player's business. It is previewed like
// any other, and nothing is applied until Apply is pressed.
//
// What it cannot know: the host's game (BepInEx) mods - an SPT server does not see its own
// game's plugins - and which of its server mods a player needs no part of. Those are taken out
// of the collection by hand before it is applied.
//
public static class HostMods
{
    /// <summary>The sp-mod.com mod with this GUID - its id, and its name there - or null.</summary>
    public delegate (int ModId, string? Name)? CatalogLookup(string guid);

    /// <summary>Each of the server's mods as a collection would hold it, at the server's version.
    /// One sp-mod.com does not have is kept, by name: it shows as one to install by hand.</summary>
    public static List<ModListCandidate> Candidates(IEnumerable<SptServerMod> host, CatalogLookup catalog)
    {
        var candidates = new List<ModListCandidate>();

        foreach (var mod in host.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var guid = mod.ModGuid.Trim();
            var found = guid.Length > 0 ? catalog(guid) : null;

            var name = found?.Name is { Length: > 0 } listed ? listed : mod.Name.Trim();
            if (name.Length == 0) name = guid;
            if (name.Length == 0) continue;

            candidates.Add(new ModListCandidate
            {
                Name = name,
                ModId = found?.ModId,
                Version = mod.Version.Trim() is { Length: > 0 } version ? version : null,
                Guid = guid.Length > 0 ? guid : null,
            });
        }

        return candidates;
    }

    /// <summary>What a server's mod is known by between one look and the next: its GUID, or its name.</summary>
    public static string Key(SptServerMod mod) => mod.ModGuid.Trim() is { Length: > 0 } guid ? guid : mod.Name.Trim();

    //
    // The server's mods the player took out of the collection: the ones it was made from last time
    // (seen) that it no longer holds. They are not put back by the next look - a mod the host
    // has added since is not among them, and is offered.
    //
    public static HashSet<string> LeftOut(ModList? existing, IEnumerable<string>? seen)
    {
        var leftOut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existing is null || seen is null) return leftOut;

        var held = existing.Entries.SelectMany(e => new[] { e.Guid, e.Name }).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in seen)
            if (!held.Contains(key)) leftOut.Add(key);

        return leftOut;
    }

    /// <summary>The collection. <paramref name="existing"/>: the one made for this server before,
    /// which this replaces under the same id. <paramref name="leftOut"/>: see <see cref="LeftOut"/>.</summary>
    public static ModList BuildList(
        string name,
        string address,
        IEnumerable<SptServerMod> host,
        CatalogLookup catalog,
        ModListCapture.VersionLookup? versions,
        string? sptVersion,
        ModList? existing,
        DateTimeOffset now,
        IReadOnlySet<string>? leftOut = null)
    {
        if (leftOut is { Count: > 0 }) host = host.Where(mod => !leftOut.Contains(Key(mod)));

        var list = new ModList
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = name,
            Origin = ModListOrigin.Local,
            Policy = ModListPolicy.Additive,
            Source = address,
            SptVersion = sptVersion,
            Revision = existing?.Revision ?? 1,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        list.Entries.AddRange(ModListCapture.BuildEntries(Candidates(host, catalog), versions));
        return list;
    }

    /// <summary>The collection made for this server before, among these.</summary>
    public static ModList? Existing(IEnumerable<ModList> lists, string address) =>
        lists.FirstOrDefault(l => l.Origin == ModListOrigin.Local && l.Policy == ModListPolicy.Additive
            && string.Equals(l.Source, address, StringComparison.OrdinalIgnoreCase));
}
