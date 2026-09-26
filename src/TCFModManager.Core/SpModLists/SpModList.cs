namespace TCFModManager.Core.SpModLists;

//
// sp-mod.com's public mod lists (sp-mod.com/lists), as the site's own pages show them.
//
// The API has no list endpoints (sp-mod.com/docs lists none, 2026-09-25), so these are read from the
// pages - see SpModListParser. Every field is what the page prints; nothing here is worked out.
//

/// <summary>One card on sp-mod.com/lists.</summary>
public sealed record SpModListSummary
{
    public required int Id { get; init; }

    // The address needs it: sp-mod.com/list/{id} without the slug is a 404.
    public required string Slug { get; init; }

    public required string Title { get; init; }

    // The SPT version the list targets, "4.1.6" - null when the author gave none.
    public string? SptVersion { get; init; }

    public string? Author { get; init; }

    // The start of the description, as the card shows it.
    public string? Teaser { get; init; }

    public int ItemCount { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    // The list's own picture, when its author gave it one.
    public string? Cover { get; init; }

    public string Url => SpModListAddress.For(Id, Slug);
}

/// <summary>An SPT version sp-mod.com's lists page can filter by: its id there, and the version.</summary>
public sealed record SpModListSptOption(int Id, string Version);

/// <summary>One page of sp-mod.com/lists.</summary>
public sealed record SpModListPage
{
    public IReadOnlyList<SpModListSummary> Lists { get; init; } = [];

    // How many lists match, over every page.
    public int Total { get; init; }

    public int Page { get; init; } = 1;

    public int LastPage { get; init; } = 1;

    // The SPT versions the page offers to filter by, newest first as the page lists them.
    public IReadOnlyList<SpModListSptOption> SptOptions { get; init; } = [];
}

/// <summary>What sp-mod.com shows for one dependency of a list item: "On list: BigBrain".</summary>
public sealed record SpModListDependency(string State, string Name);

/// <summary>An addon on a list, shown under the mod it is for.</summary>
public sealed record SpModListAddon
{
    public required int AddonId { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    public string? Version { get; init; }

    public string? Author { get; init; }

    public string? Thumbnail { get; init; }

    public string? Note { get; init; }
}

/// <summary>One mod on a list, as its page shows it.</summary>
public sealed record SpModListItem
{
    public required int ModId { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    //
    // The version the page shows: the newest one for the list's SPT version, or - when there is
    // none (IsIncompatible) - the closest one there is. Not a version the list's author chose: a
    // list on sp-mod.com names mods, not versions.
    //
    public string? Version { get; init; }

    // The SPT version that version is for.
    public string? SptVersion { get; init; }

    public string? Author { get; init; }

    public long? Downloads { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public string? Thumbnail { get; init; }

    // Another mod on the list needs this one.
    public bool IsDependency { get; init; }

    // No version of it is for the list's SPT version.
    public bool IsIncompatible { get; init; }

    // The list author's note on it - "OPTIONAL", "[DISABLED]", anything.
    public string? Note { get; init; }

    public IReadOnlyList<SpModListDependency> Dependencies { get; init; } = [];

    public IReadOnlyList<SpModListAddon> Addons { get; init; } = [];
}

/// <summary>A list's own page, sp-mod.com/list/{id}/{slug}.</summary>
public sealed record SpModListDetails
{
    public required int Id { get; init; }

    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string? SptVersion { get; init; }

    public string? Author { get; init; }

    public string? AuthorUrl { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public string? Cover { get; init; }

    // The description as the site renders it, HTML - shown exactly as the author wrote it.
    public string? DescriptionHtml { get; init; }

    public IReadOnlyList<SpModListItem> Items { get; init; } = [];

    // Entries the page shows as "This mod is no longer available": taken down since the list was
    // made, with nothing left to name them by.
    public int Unavailable { get; init; }

    public string Url => SpModListAddress.For(Id, Slug);

    public int AddonCount => Items.Sum(i => i.Addons.Count);
}

/// <summary>Reading and writing a list's address on sp-mod.com.</summary>
public static class SpModListAddress
{
    public const string Site = "https://sp-mod.com";

    public static string For(int id, string slug) => $"{Site}/list/{id}/{slug}";

    // A mod's or an addon's page. The slug is needed there too: without it, a 404.
    public static string ForMod(int id, string slug) => $"{Site}/mod/{id}/{slug}";

    public static string ForAddon(int id, string slug) => $"{Site}/addon/{id}/{slug}";

    private static readonly System.Text.RegularExpressions.Regex Pattern = new(
        @"(?:^|/)list/(\d+)/([A-Za-z0-9\-_]+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>A list's id and slug out of its address - pasted with or without https://, a
    /// trailing slash or a query.</summary>
    public static bool TryParse(string? text, out int id, out string slug)
    {
        id = 0;
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        var match = Pattern.Match(trimmed);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out id) || id <= 0) return false;

        // Something else's address with /list/ in it is not a list on sp-mod.com.
        if (trimmed.Contains("://", StringComparison.Ordinal)
            && !trimmed.StartsWith(Site, StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://www.sp-mod.com", StringComparison.OrdinalIgnoreCase))
        {
            id = 0;
            return false;
        }

        slug = match.Groups[2].Value;
        return true;
    }
}
