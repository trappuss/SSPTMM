using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TCFModManager.Core.SpModLists;

//
// Reads sp-mod.com's list pages.
//
// Found by the markers the site's own templates put there, never by the styling classes, which
// change with any restyle: Livewire's wire:key on each card and each item ("list-index-card-126918",
// "list-group-152387", "list-addon-156645", "note-display-156619"), the links' own addresses, <time
// datetime>, and the badge-version class the site uses for its SPT badges. Measured against
// sp-mod.com on 2026-09-25 (the fixtures in the tests are those pages as fetched).
//
// A page that has changed shape reads as fewer fields rather than as an error: a card without a
// teaser is a card without a teaser, and only a page with no lists or items at all says nothing.
//
public static partial class SpModListParser
{
    // ------------------------------------------------------------------ sp-mod.com/lists

    public static SpModListPage ParseBrowse(string html)
    {
        var doc = Load(html);
        var root = doc.DocumentNode;

        var lists = new List<SpModListSummary>();
        foreach (var card in WithKey(root, "list-index-card-"))
        {
            if (ReadCard(card) is { } summary) lists.Add(summary);
        }

        var (page, lastPage) = ReadPaging(root);
        var total = ReadTotal(root) ?? lists.Count;

        return new SpModListPage
        {
            Lists = lists,
            Total = total,
            Page = page,
            LastPage = Math.Max(page, lastPage),
            SptOptions = ReadSptOptions(root),
        };
    }

    //
    // A member's lists, as the Lists tab of their page shows them: the same cards, keyed
    // "user-lists-card-", without a "by" line - they are all the member's own.
    //
    public static IReadOnlyList<SpModListSummary> ParseUserLists(string html, string author)
    {
        var root = Load(html).DocumentNode;
        var lists = new List<SpModListSummary>();
        foreach (var card in WithKey(root, "user-lists-card-"))
        {
            if (ReadCard(card) is { } summary) lists.Add(summary with { Author = summary.Author ?? author });
        }

        return lists;
    }

    // ------------------------------------------------------------------ sp-mod.com/user/{id}/{slug}

    /// <summary>A member's page, or null when it is not one.</summary>
    public static SpModUserProfile? ParseUser(string html, int id)
    {
        var root = Load(html).DocumentNode.Descendants()
            .FirstOrDefault(n => Attribute(n, "wire:name") == "pages::user.show");
        if (root is null) return null;

        // The name is the heading's innermost text; a "Staff" tooltip may sit beside it.
        var heading = root.Descendants("h1").FirstOrDefault();
        var name = heading?.Descendants("span").FirstOrDefault(s => !s.Elements("span").Any() && Text(s).Length > 0) is { } span
            ? Text(span)
            : heading is null ? null : Text(heading);
        if (string.IsNullOrWhiteSpace(name)) return null;

        var since = root.Descendants("div")
            .FirstOrDefault(d => d.ChildNodes.OfType<HtmlTextNode>().Any(t => Clean(t.Text) == "Member since"));

        return new SpModUserProfile
        {
            Id = id,
            Name = name,
            IsStaff = heading!.Descendants().Any(n => n.Attributes.Contains("data-flux-tooltip-content") && Text(n) == "Staff"),
            Avatar = root.Descendants("img").Select(i => Attribute(i, "src")).FirstOrDefault(src => src?.Contains("/profile-photos/", StringComparison.Ordinal) == true),
            Cover = root.Descendants("img").Select(i => Attribute(i, "src")).FirstOrDefault(src => src?.Contains("/cover-photos/", StringComparison.Ordinal) == true),
            MemberSince = Time(since?.Descendants("time").FirstOrDefault()),
            Followers = ReadFollowers(Load(html).DocumentNode),
        };
    }

    //
    // The Followers card: "204 total" once there are more than it shows, "No followers yet." for
    // none, and otherwise one link per follower.
    //
    private static int? ReadFollowers(HtmlNode document)
    {
        var card = document.Descendants()
            .FirstOrDefault(n => Attribute(n, "wire:name") == "user.follow-card"
                                 && (Attribute(n, "wire:snapshot") ?? string.Empty).Contains("\"relationship\":\"followers\"", StringComparison.Ordinal));
        if (card is null) return null;

        // Only what the card shows, not the full list in its dialog.
        foreach (var dialog in card.Descendants("dialog").ToList()) dialog.Remove();

        var text = Text(card);
        var total = FollowersTotal().Match(text);
        if (total.Success) return Number(total.Groups[1].Value);
        if (text.Contains("No followers yet", StringComparison.Ordinal)) return 0;

        return card.Descendants("a")
            .Select(Href)
            .Where(h => h.Contains("/user/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    private static SpModListSummary? ReadCard(HtmlNode card)
    {
        var link = card.Descendants("a").FirstOrDefault(a => ListLink().IsMatch(Href(a)));
        if (link is null) return null;

        var match = ListLink().Match(Href(link));
        var title = Attribute(link, "title") ?? Text(link);
        if (string.IsNullOrWhiteSpace(title)) return null;

        var author = card.Descendants("span")
            .Select(Text)
            .FirstOrDefault(t => t.StartsWith("by ", StringComparison.Ordinal));

        var count = card.Descendants("span")
            .Select(s => ItemCount().Match(Text(s)))
            .FirstOrDefault(m => m.Success);

        return new SpModListSummary
        {
            Id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            Slug = match.Groups[2].Value,
            Title = title,
            SptVersion = SptBadge(card),
            Author = author?[3..].Trim(),
            Teaser = card.Descendants("p").FirstOrDefault(p => HasClass(p, "line-clamp-2")) is { } teaser
                ? NullIfEmpty(Text(teaser))
                : null,
            ItemCount = count is null ? 0 : Number(count.Groups[1].Value),
            UpdatedAt = Time(card.Descendants("time").FirstOrDefault()),
            Cover = card.Descendants("img").Select(i => Attribute(i, "src")).FirstOrDefault(IsListCover),
        };
    }

    // "Showing 1 to 12 of 317 results": the last number. Absent when everything fits on one page.
    private static int? ReadTotal(HtmlNode root)
    {
        var showing = root.Descendants("p").FirstOrDefault(p => Text(p).StartsWith("Showing", StringComparison.Ordinal));
        if (showing is null) return null;

        var numbers = showing.Descendants("span")
            .Select(Text)
            .Where(t => t.Length > 0 && t.All(c => char.IsDigit(c) || c == ','))
            .ToList();

        return numbers.Count > 0 ? Number(numbers[^1]) : null;
    }

    // The current page is the one marked aria-current; the last is the highest page a button goes to.
    private static (int Page, int LastPage) ReadPaging(HtmlNode root)
    {
        var current = root.Descendants()
            .FirstOrDefault(n => Attribute(n, "aria-current") == "page") is { } marked
            && int.TryParse(Text(marked), NumberStyles.Integer, CultureInfo.InvariantCulture, out var shown)
            ? shown
            : 1;

        var last = current;
        foreach (var node in root.Descendants())
        {
            if (Attribute(node, "wire:click") is not { } click) continue;
            var go = GotoPage().Match(click);
            if (go.Success) last = Math.Max(last, int.Parse(go.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        return (current, last);
    }

    // <ui-option value="55">4.1.6</ui-option>: the filter's choices, by the id the site filters on.
    private static IReadOnlyList<SpModListSptOption> ReadSptOptions(HtmlNode root)
    {
        var options = new List<SpModListSptOption>();
        foreach (var option in root.Descendants("ui-option"))
        {
            if (!int.TryParse(Attribute(option, "value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;

            var version = Text(option);
            if (version.Length == 0 || options.Any(o => o.Id == id)) continue;
            options.Add(new SpModListSptOption(id, version));
        }

        return options;
    }

    // ------------------------------------------------------------------ sp-mod.com/list/{id}/{slug}

    /// <summary>A list's page, or null when it has no list on it (removed, private, not a list).</summary>
    public static SpModListDetails? ParseList(string html, int id, string slug)
    {
        var doc = Load(html);

        // The page's own component; the site's header and search sit outside it. A page without it
        // is not a list's page - a sign-in page, a notice - and reading it anyway took the site's
        // own "Forge" heading for the list's title.
        var root = doc.DocumentNode.Descendants()
            .FirstOrDefault(n => Attribute(n, "wire:name") == "pages::list.show");
        if (root is null) return null;

        var title = root.Descendants("h1").FirstOrDefault() is { } heading ? Text(heading) : null;
        if (string.IsNullOrWhiteSpace(title)) return null;

        // The line under the title: "by WUVGAWORE · Thursday at 11:42 PM · 96 mods · 1 addon".
        var byline = root.Descendants("p").FirstOrDefault(p => Text(p).StartsWith("by", StringComparison.Ordinal));
        var author = byline?.Descendants("a").FirstOrDefault(a => Href(a).Contains("/user/", StringComparison.Ordinal));

        var groups = WithKey(root, "list-group-").ToList();
        var items = groups.Select(ReadItem).OfType<SpModListItem>().ToList();

        return new SpModListDetails
        {
            Id = id,
            Slug = slug,
            Title = title,
            // The header's badge comes before any item's.
            SptVersion = SptBadge(root, before: groups.FirstOrDefault()),
            Author = author is null ? null : NullIfEmpty(Text(author)),
            AuthorUrl = author is null ? null : NullIfEmpty(Href(author)),
            UpdatedAt = Time(byline?.Descendants("time").FirstOrDefault()),
            Cover = root.Descendants("img").Select(i => Attribute(i, "src")).FirstOrDefault(IsListCover),
            DescriptionHtml = root.Descendants("div").FirstOrDefault(d => HasClass(d, "user-markdown")) is { } description
                ? NullIfEmpty(description.InnerHtml.Trim())
                : null,
            Items = items,
            // "This mod is no longer available.": a group with no mod in it.
            Unavailable = groups.Count - items.Count,
        };
    }

    private static SpModListItem? ReadItem(HtmlNode group)
    {
        // The addons first, then out of the way, so nothing below reads an addon's name, picture,
        // version or note as the mod's.
        var addons = new List<SpModListAddon>();
        foreach (var node in WithKey(group, "list-addon-").ToList())
        {
            if (ReadAddon(node) is { } addon) addons.Add(addon);
        }

        foreach (var container in group.Descendants("ul").ToList()) container.Remove();

        var links = group.Descendants("a").Where(a => ModLink().IsMatch(Href(a))).ToList();
        var nameLink = links.FirstOrDefault(a => Text(a).Length > 0);
        if (nameLink is null) return null;

        var match = ModLink().Match(Href(nameLink));
        var badges = group.Descendants()
            .Where(n => n.Attributes.Contains("data-flux-badge"))
            .Select(Text)
            .ToList();

        return new SpModListItem
        {
            ModId = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            Slug = match.Groups[2].Value,
            Name = Text(nameLink),
            Version = VersionBeside(nameLink),
            SptVersion = SptBadge(group),
            Author = Byline(group),
            Downloads = group.Descendants("span")
                .Select(s => DownloadsTitle().Match(Attribute(s, "title") ?? string.Empty))
                .FirstOrDefault(m => m.Success) is { } downloads
                ? long.Parse(downloads.Groups[1].Value.Replace(",", string.Empty), CultureInfo.InvariantCulture)
                : null,
            PublishedAt = Time(group.Descendants("time").FirstOrDefault()),
            Thumbnail = links.SelectMany(a => a.Descendants("img")).Select(i => Attribute(i, "src")).FirstOrDefault(s => s is not null),
            IsDependency = badges.Any(b => b == "Dependency"),
            IsIncompatible = badges.Any(b => b.StartsWith("Not compatible", StringComparison.Ordinal)),
            Note = Note(group),
            Dependencies = ReadDependencies(group),
            Addons = addons,
        };
    }

    private static SpModListAddon? ReadAddon(HtmlNode node)
    {
        var links = node.Descendants("a").Where(a => AddonLink().IsMatch(Href(a))).ToList();
        var nameLink = links.FirstOrDefault(a => Text(a).Length > 0);
        if (nameLink is null) return null;

        var match = AddonLink().Match(Href(nameLink));
        return new SpModListAddon
        {
            AddonId = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            Slug = match.Groups[2].Value,
            Name = Text(nameLink),
            Version = VersionBeside(nameLink),
            Author = Byline(node),
            Thumbnail = links.SelectMany(a => a.Descendants("img")).Select(i => Attribute(i, "src")).FirstOrDefault(s => s is not null),
            Note = Note(node),
        };
    }

    // The version sits in a span right beside the name's link.
    private static string? VersionBeside(HtmlNode nameLink) =>
        nameLink.ParentNode?.Elements("span").FirstOrDefault() is { } span ? NullIfEmpty(Text(span)) : null;

    // "by DrakiaXYZ · 1.35M downloads · Aug 3": the first line of the div it starts, up to the dot.
    private static string? Byline(HtmlNode node)
    {
        foreach (var div in node.Descendants("div"))
        {
            var first = div.ChildNodes.OfType<HtmlTextNode>()
                .Select(t => Clean(t.Text))
                .FirstOrDefault(t => t.Length > 0);

            if (first is not null && first.StartsWith("by ", StringComparison.Ordinal))
                return NullIfEmpty(first[3..].Trim());
        }

        return null;
    }

    // The note the list's author wrote on an item, in its own wire:key block.
    private static string? Note(HtmlNode node)
    {
        var note = WithKey(node, "note-display-").FirstOrDefault();
        if (note is null) return null;

        // Line by line, as written: a note of several lines keeps them.
        var lines = WebUtility.HtmlDecode(note.InnerText)
            .Split('\n')
            .Select(l => Clean(l))
            .Where(l => l.Length > 0);

        return NullIfEmpty(string.Join("\n", lines));
    }

    //
    // "2 dependencies satisfied", and in its tooltip one line per dependency: a state ("On list:")
    // and the mod's name.
    //
    private static IReadOnlyList<SpModListDependency> ReadDependencies(HtmlNode group)
    {
        var found = new List<SpModListDependency>();

        foreach (var tip in group.Descendants().Where(n => n.Attributes.Contains("data-flux-tooltip-content")))
        {
            foreach (var line in tip.Descendants("div"))
            {
                var spans = line.Elements("span").Select(Text).Where(t => t.Length > 0).ToList();
                if (spans.Count < 2 || !spans[0].EndsWith(':')) continue;
                found.Add(new SpModListDependency(spans[0].TrimEnd(':'), spans[1]));
            }
        }

        return found;
    }

    // ------------------------------------------------------------------ small readers

    private static HtmlDocument Load(string html)
    {
        var doc = new HtmlDocument { OptionFixNestedTags = true };
        doc.LoadHtml(html);
        return doc;
    }

    private static IEnumerable<HtmlNode> WithKey(HtmlNode root, string prefix) =>
        root.Descendants().Where(n => Attribute(n, "wire:key")?.StartsWith(prefix, StringComparison.Ordinal) == true);

    //
    // The site's SPT badge: "SPT version 4.1.6" on a card, "SPT 4.0.13" in a list's header and on
    // its items, the first word of each a screen reader's. Only the number is kept.
    //
    private static string? SptBadge(HtmlNode root, HtmlNode? before = null)
    {
        foreach (var node in root.Descendants("span"))
        {
            if (before is not null && node.StreamPosition >= before.StreamPosition) break;
            if (!HasClass(node, "badge-version")) continue;

            var version = SptNumber().Match(Text(node));
            if (version.Success) return version.Value;
        }

        return null;
    }

    private static bool IsListCover(string? src) =>
        src is not null && src.Contains("/mod-lists/", StringComparison.Ordinal);

    private static string Href(HtmlNode a) => Attribute(a, "href") ?? string.Empty;

    private static string? Attribute(HtmlNode node, string name) =>
        node.Attributes[name]?.DeEntitizeValue;

    private static bool HasClass(HtmlNode node, string name) =>
        (Attribute(node, "class") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name);

    // The text as read: entities decoded, runs of white space made one, no-break spaces plain.
    private static string Text(HtmlNode node) => Clean(WebUtility.HtmlDecode(node.InnerText));

    private static string Clean(string text) =>
        Spaces().Replace(text.Replace(' ', ' '), " ").Trim();

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static int Number(string text) =>
        int.TryParse(text.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static DateTimeOffset? Time(HtmlNode? node) =>
        node is not null
        && DateTimeOffset.TryParse(Attribute(node, "datetime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when)
            ? when
            : null;

    [GeneratedRegex(@"^https?://(?:www\.)?sp-mod\.com/list/(\d+)/([^/?#""]+)")]
    private static partial Regex ListLink();

    [GeneratedRegex(@"^https?://(?:www\.)?sp-mod\.com/mod/(\d+)/([^/?#""]+)$")]
    private static partial Regex ModLink();

    [GeneratedRegex(@"^https?://(?:www\.)?sp-mod\.com/addon/(\d+)/([^/?#""]+)$")]
    private static partial Regex AddonLink();

    [GeneratedRegex(@"^([\d,]+) items?$")]
    private static partial Regex ItemCount();

    [GeneratedRegex(@"^([\d,]+) Downloads$")]
    private static partial Regex DownloadsTitle();

    [GeneratedRegex(@"gotoPage\((\d+)")]
    private static partial Regex GotoPage();

    [GeneratedRegex(@"\d+(?:\.\d+)+")]
    private static partial Regex SptNumber();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"([\d,]+) total")]
    private static partial Regex FollowersTotal();
}
