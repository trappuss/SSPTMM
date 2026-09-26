using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TCFModManager.App.Localization;
using TCFModManager.Core.Markup;
using TCFModManager.Core.Models;
using TCFModManager.Core.Services;
using TCFModManager.Core.SpModApi;
using TCFModManager.Core.SpModLists;

namespace TCFModManager.App.Services;

/// <summary>The version of one item for one SPT version: found, not published for it, or not
/// found out (sp-mod.com could not be asked).</summary>
public sealed record CollectionVersionPick(string? Version, int? VersionId, bool CouldNotCheck = false)
{
    public bool Found => Version is not null;

    public static readonly CollectionVersionPick None = new(null, null);

    public static readonly CollectionVersionPick Unknown = new(null, null, CouldNotCheck: true);
}

/// <summary>An item a public collection's subscription leaves out, and why.</summary>
public sealed record CollectionSkip(string Name, string Reason);

/// <summary>A public collection turned into mod list entries for one SPT version.</summary>
public sealed record CollectionResolution(
    IReadOnlyList<ModListEntry> Entries,
    IReadOnlyList<CollectionSkip> Skipped,
    string SptVersion);

//
// sp-mod.com's public lists, as this app's collections.
//
// A list on sp-mod.com names MODS: the version its page shows beside each is the newest one for
// the list's SPT version, worked out when the page is drawn, not a build the author picked. So
// subscribing to one here pins each item to a version the way a list of this app's own does, and
// the question is which. The answer is the newest version for the SPT version the items will run
// on - this install's, unless the user chose the list's own - and an item with no version for it is
// left out and named, rather than installed at a version that will not load. That is the whole of
// "subscribe only matching versions": nothing is installed for an SPT version it was not made for.
//
// The stored copy is an ordinary imported mod list, applied through the Collections page like any
// other, with one id per sp-mod.com list so subscribing again brings the same copy up to date
// instead of making another.
//
public static class PublicCollections
{
    /// <summary>The id the copy of sp-mod.com list <paramref name="listId"/> is stored under:
    /// the same every time, on every machine.</summary>
    public static Guid IdFor(int listId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"sp-mod.com/list/{listId}"));
        return new Guid(hash);
    }

    /// <summary>This install's copy of an sp-mod.com list, if it has subscribed to it.</summary>
    public static ModList? Stored(int listId) => AppServices.ModLists.Find(IdFor(listId));

    /// <summary>The sp-mod.com list a stored list was taken from, if it was.</summary>
    public static bool TryGetSource(ModList list, out int id, out string slug) =>
        SpModListAddress.TryParse(list.Link, out id, out slug);

    // ------------------------------------------------------------------ one item

    //
    // The newest version of a mod for an SPT version.
    //
    // The catalog first: it carries each mod's latest six releases, and for a current mod the one
    // wanted is among them - no request at all. Only when none of those is for this SPT version is
    // sp-mod.com asked (filter[spt_version], newest first), which is what finds a release further
    // back for an older SPT, or says there is none.
    //
    public static async Task<CollectionVersionPick> PickModAsync(int modId, Mod? mod, string spt, CancellationToken ct = default)
    {
        if (mod?.Versions is { Count: > 0 } versions)
        {
            var cached = versions
                .Where(v => v.Version is not null && SptVersionMatcher.IsSatisfiedBy(v.SptVersionConstraint, spt) == true)
                .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            if (cached is not null) return new CollectionVersionPick(cached.Version, cached.Id > 0 ? cached.Id : null);
        }

        try
        {
            var found = await AppServices.SpModApi.GetModVersionsAsync(
                modId.ToString(),
                new ModVersionsQuery { FilterSptVersion = spt, Sort = "-published_at", PerPage = 1 },
                ct);

            return found.Data.FirstOrDefault() is { Version: not null } version
                ? new CollectionVersionPick(version.Version, version.Id > 0 ? version.Id : null)
                : CollectionVersionPick.None;
        }
        catch (Exception ex) when (ex is SpModApiException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            AppLog.Info("Collections", $"versions of mod {modId} for SPT {spt} could not be read: {ex.Message}");
            return CollectionVersionPick.Unknown;
        }
    }

    //
    // An addon's version follows its MOD's, not SPT's: each addon release says which versions of
    // its mod it works with. So the newest release that fits the mod version being installed with
    // it (or the one installed already). With neither known - or none fitting in the releases the
    // cache carries - it is left unpinned, and applying takes the newest, as a hand-added addon does.
    //
    public static CollectionVersionPick PickAddon(int addonId, string? parentVersion)
    {
        var addon = AppServices.Addons.ById(addonId);
        if (addon?.Versions is not { Count: > 0 } versions || parentVersion is null) return new CollectionVersionPick(null, null);

        var fitting = versions
            .Where(v => v.Version is not null && ModVersionFits(v.ModVersionConstraint, parentVersion))
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        return fitting is null ? new CollectionVersionPick(null, null) : new CollectionVersionPick(fitting.Version, fitting.Id > 0 ? fitting.Id : null);
    }

    // No constraint means any version of the mod.
    private static bool ModVersionFits(string? constraint, string version) =>
        string.IsNullOrWhiteSpace(constraint) || SptVersionMatcher.IsSatisfiedBy(constraint, version) != false;

    // ------------------------------------------------------------------ the whole list

    //
    // Every item's version for <paramref name="spt"/>, a few requests at a time. Picks already made
    // (the page works them out as it opens) are used rather than asked for again.
    //
    public static async Task<CollectionResolution> ResolveAsync(
        SpModListDetails details,
        string spt,
        IReadOnlyDictionary<int, CollectionVersionPick>? known = null,
        CancellationToken ct = default)
    {
        var catalog = CatalogById();
        var picks = await PickAllAsync(details.Items.Select(i => i.ModId), catalog, spt, known, ct);

        var entries = new List<ModListEntry>();
        var skipped = new List<CollectionSkip>();

        foreach (var item in details.Items)
        {
            var mod = catalog.GetValueOrDefault(item.ModId);
            var pick = picks[item.ModId];
            var name = mod?.Name ?? item.Name;

            if (!pick.Found)
            {
                skipped.Add(new CollectionSkip(name, pick.CouldNotCheck
                    ? Strings.Collection_SkipNotChecked
                    : LocalizationTextFor(spt)));
                continue;
            }

            entries.Add(new ModListEntry
            {
                Name = name,
                ModId = item.ModId,
                VersionId = pick.VersionId,
                Version = pick.Version,
                Guid = string.IsNullOrWhiteSpace(mod?.Guid) ? null : mod.Guid,
            });

            // The addons go with their mod, fitted to the version it is getting - and are left out
            // with it, since an addon without its mod installs files nothing loads.
            foreach (var addon in item.Addons)
            {
                var addonPick = PickAddon(addon.AddonId, pick.Version);
                entries.Add(new ModListEntry
                {
                    Name = AppServices.Addons.ById(addon.AddonId)?.Name ?? addon.Name,
                    ModId = addon.AddonId,
                    IsAddon = true,
                    VersionId = addonPick.VersionId,
                    Version = addonPick.Version,
                });
            }
        }

        return new CollectionResolution(ModListEntries.Sorted(entries), skipped, spt);
    }

    private static string LocalizationTextFor(string spt) =>
        LocalizationService.Text(Strings.Collection_SkipNoVersionFormat, spt);

    /// <summary>Picks for many mods at once: the catalog's answers at once, the rest four at a time.</summary>
    public static async Task<Dictionary<int, CollectionVersionPick>> PickAllAsync(
        IEnumerable<int> modIds,
        IReadOnlyDictionary<int, Mod> catalog,
        string spt,
        IReadOnlyDictionary<int, CollectionVersionPick>? known = null,
        CancellationToken ct = default)
    {
        var picks = new Dictionary<int, CollectionVersionPick>();
        using var gate = new SemaphoreSlim(4, 4);

        var work = modIds.Distinct().Select(async id =>
        {
            if (known?.GetValueOrDefault(id) is { CouldNotCheck: false } already)
            {
                lock (picks) picks[id] = already;
                return;
            }

            await gate.WaitAsync(ct);
            try
            {
                var pick = await PickModAsync(id, catalog.GetValueOrDefault(id), spt, ct);
                lock (picks) picks[id] = pick;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(work);
        return picks;
    }

    public static Dictionary<int, Mod> CatalogById() =>
        AppServices.ModCache.AllMods.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

    //
    // The copy stored for a subscription: imported (so the Collections page shows it as somebody
    // else's and edits fork it), from the list's author, linking back to its page, for the SPT
    // version its entries were picked for.
    //
    public static ModList Store(SpModListDetails details, CollectionResolution resolution)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = Stored(details.Id);

        var list = new ModList
        {
            Id = IdFor(details.Id),
            Name = details.Title,
            Description = Describe(details),
            Origin = ModListOrigin.Imported,
            Policy = existing?.Policy ?? ModListPolicy.Additive,
            Source = details.Author,
            SptVersion = resolution.SptVersion,
            Link = details.Url,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };
        list.Entries.AddRange(resolution.Entries);

        return AppServices.ModLists.Add(list);
    }

    /// <summary>A local collection of the user's own, holding a public collection's items.</summary>
    public static ModList SaveCopy(SpModListDetails details, CollectionResolution resolution, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new ModList
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = Describe(details),
            Origin = ModListOrigin.Local,
            Policy = ModListPolicy.Additive,
            DerivedFrom = IdFor(details.Id),
            SptVersion = resolution.SptVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };
        list.Entries.AddRange(resolution.Entries);

        return AppServices.ModLists.Add(list);
    }

    // The Collections page shows a description as plain text.
    private static string? Describe(SpModListDetails details) =>
        details.DescriptionHtml is { } html ? SpModMarkup.PlainText(SpModMarkup.Parse(html)) : null;
}
