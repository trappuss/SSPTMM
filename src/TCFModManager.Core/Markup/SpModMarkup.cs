using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TCFModManager.Core.Markup;

//
// What sp-mod.com's markdown renders to, read into a small tree the app can lay out.
//
// The API hands descriptions and changelogs over as the site's own HTML, not as markdown. Surveyed
// across the 200 most-downloaded and most recently updated mods (2026-09-24), that HTML uses:
// paragraphs, headings h1-h6, bold/italic/strikethrough, inline code and <pre><code> blocks,
// links, images (png/jpg/gif/webp, a couple of svg), ordered and unordered lists nested to any
// depth, tables, horizontal rules, blockquotes (plain, and "is-warning"), YouTube embeds
// (<div class="youtube-lite" data-video-id="...">) and tab sets:
//
//   <div class="tabset">
//     <div class="tab-panel"><div class="tab-title">Features</div><div class="tab-content">...</div></div>
//     ...
//   </div>
//
// which the site turns into a row of tab buttons with script. Tab sets can nest. Anything else is
// unwrapped: its children are kept and the element itself ignored, so an unknown tag never loses text.
//
public static class SpModMarkup
{
    public static MarkupDocument Parse(string? html)
    {
        var document = new MarkupDocument();
        if (string.IsNullOrWhiteSpace(html)) return document;

        var htmlDocument = new HtmlDocument { OptionFixNestedTags = true };
        htmlDocument.LoadHtml(html);

        new Reader().ReadBlocks(htmlDocument.DocumentNode.ChildNodes, document.Blocks);
        return document;
    }

    //
    // The document as one run of plain text, the way Steam's Quick View shows an item's
    // description: every block's words in reading order, separated by single spaces, list items
    // led by "- ", pictures, videos and rules left out. Tab sets give their first tab only (the one
    // the page opens on). Cut at maxLength characters, on a word where one is near, with "..." -
    // null when there are no words at all.
    //
    public static string? PlainText(MarkupDocument document, int maxLength = 800)
    {
        var text = new System.Text.StringBuilder();

        void Space()
        {
            if (text.Length > 0 && text[^1] != ' ') text.Append(' ');
        }

        void Inlines(IEnumerable<MarkupInline> inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case MarkupText t:
                        foreach (var ch in t.Text)
                        {
                            if (char.IsWhiteSpace(ch)) Space();
                            else text.Append(ch);
                        }
                        break;
                    case MarkupInlineCode code:
                        text.Append(code.Text);
                        break;
                    case MarkupBreak:
                        Space();
                        break;
                    case MarkupSpan span:
                        Inlines(span.Children);
                        break;
                }
            }
        }

        void Blocks(IEnumerable<MarkupBlock> blocks)
        {
            foreach (var block in blocks)
            {
                if (text.Length > maxLength) return;

                switch (block)
                {
                    case MarkupParagraph p:
                        Space(); Inlines(p.Inlines);
                        break;
                    case MarkupHeading h:
                        Space(); Inlines(h.Inlines);
                        break;
                    case MarkupList list:
                        foreach (var item in list.Items)
                        {
                            Space(); text.Append("- ");
                            Blocks(item);
                        }
                        break;
                    case MarkupQuote quote:
                        Blocks(quote.Blocks);
                        break;
                    case MarkupCode code:
                        Space(); Inlines([new MarkupText(code.Text)]);
                        break;
                    case MarkupTable table:
                        foreach (var cell in table.Rows.SelectMany(r => r.Cells)) Blocks(cell.Blocks);
                        break;
                    case MarkupTabSet tabs when tabs.Tabs.Count > 0:
                        Blocks(tabs.Tabs[0].Blocks);
                        break;
                }
            }
        }

        Blocks(document.Blocks);

        var result = text.ToString().Trim();
        if (result.Length == 0) return null;
        if (result.Length <= maxLength) return result;

        var cut = result.LastIndexOf(' ', maxLength);
        if (cut < maxLength * 3 / 4) cut = maxLength;
        return result[..cut].TrimEnd() + "...";
    }

    //
    // Every picture and video in the document, in reading order and without repeats - the
    // screenshot strip under the item page's preview. Tab sets are walked through all their tabs.
    //
    public static IReadOnlyList<MarkupMedia> Media(MarkupDocument document)
    {
        var found = new List<MarkupMedia>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(MarkupMedia media)
        {
            if (seen.Add(media.Key)) found.Add(media);
        }

        void Inlines(IEnumerable<MarkupInline> inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case MarkupImage image:
                        Add(new MarkupMedia(MarkupMediaKind.Image, image.Source, image.Source, image.Alt, null));
                        break;
                    case MarkupSpan span:
                        Inlines(span.Children);
                        break;
                }
            }
        }

        void Blocks(IEnumerable<MarkupBlock> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case MarkupParagraph paragraph:
                        Inlines(paragraph.Inlines);
                        break;
                    case MarkupHeading heading:
                        Inlines(heading.Inlines);
                        break;
                    case MarkupList list:
                        foreach (var item in list.Items) Blocks(item);
                        break;
                    case MarkupQuote quote:
                        Blocks(quote.Blocks);
                        break;
                    case MarkupTable table:
                        foreach (var row in table.Rows)
                        foreach (var cell in row.Cells)
                            Blocks(cell.Blocks);
                        break;
                    case MarkupTabSet tabs:
                        foreach (var tab in tabs.Tabs) Blocks(tab.Blocks);
                        break;
                    case MarkupVideo video:
                        Add(new MarkupMedia(MarkupMediaKind.Video, video.WatchUrl, video.Thumbnail, null, video.EmbedUrl));
                        break;
                }
            }
        }

        Blocks(document.Blocks);
        return found;
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // YouTube's own id alphabet; anything else in data-video-id is not trusted into a URL.
    private static readonly Regex VideoId = new("^[A-Za-z0-9_-]{6,20}$", RegexOptions.Compiled);

    private sealed class Reader
    {
        // ---------------------------------------------------------------- blocks

        public void ReadBlocks(IEnumerable<HtmlNode> nodes, List<MarkupBlock> into)
        {
            // Text and inline elements sitting directly among blocks (a bare line inside a <div> or
            // an <li>) are gathered into one paragraph until the next block starts.
            var loose = new List<MarkupInline>();

            void FlushLoose()
            {
                Trim(loose);
                if (loose.Count > 0) into.Add(new MarkupParagraph([.. loose]));
                loose.Clear();
            }

            foreach (var node in nodes)
            {
                if (node.NodeType == HtmlNodeType.Comment) continue;

                if (node.NodeType == HtmlNodeType.Text || !IsBlock(node))
                {
                    ReadInline(node, loose);
                    continue;
                }

                FlushLoose();
                ReadBlock(node, into);
            }

            FlushLoose();
        }

        private void ReadBlock(HtmlNode node, List<MarkupBlock> into)
        {
            switch (node.Name)
            {
                case "p":
                {
                    var inlines = Inlines(node.ChildNodes);
                    if (inlines.Count > 0) into.Add(new MarkupParagraph(inlines));
                    break;
                }

                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                {
                    var inlines = Inlines(node.ChildNodes);
                    if (inlines.Count > 0) into.Add(new MarkupHeading(node.Name[1] - '0', inlines));
                    break;
                }

                case "ul" or "ol":
                    into.Add(ReadList(node));
                    break;

                case "blockquote":
                {
                    var blocks = new List<MarkupBlock>();
                    ReadBlocks(node.ChildNodes, blocks);
                    var kind = HasClass(node, "is-warning") ? MarkupQuoteKind.Warning : MarkupQuoteKind.Plain;
                    if (blocks.Count > 0) into.Add(new MarkupQuote(kind, blocks));
                    break;
                }

                case "pre":
                {
                    var code = node.SelectSingleNode(".//code");
                    var text = WebUtility.HtmlDecode((code ?? node).InnerText).TrimEnd('\n', '\r');
                    var language = code is null ? null : Language(code);
                    into.Add(new MarkupCode(text, language));
                    break;
                }

                case "hr":
                    into.Add(new MarkupRule());
                    break;

                case "table":
                {
                    var table = ReadTable(node);
                    if (table.Rows.Count > 0) into.Add(table);
                    break;
                }

                case "div" when HasClass(node, "tabset"):
                {
                    var tabs = ReadTabSet(node);
                    if (tabs.Tabs.Count > 0) into.Add(tabs);
                    break;
                }

                case "div" when HasClass(node, "youtube-lite"):
                {
                    var id = node.GetAttributeValue("data-video-id", string.Empty);
                    if (VideoId.IsMatch(id)) into.Add(new MarkupVideo(id));
                    break;
                }

                // div, section, article, details and the rest: a container, read through.
                default:
                    ReadBlocks(node.ChildNodes, into);
                    break;
            }
        }

        private MarkupList ReadList(HtmlNode node)
        {
            var items = new List<IReadOnlyList<MarkupBlock>>();
            foreach (var child in node.ChildNodes.Where(c => c.Name == "li"))
            {
                var blocks = new List<MarkupBlock>();
                ReadBlocks(child.ChildNodes, blocks);
                items.Add(blocks);
            }

            var start = node.Name == "ol" && int.TryParse(node.GetAttributeValue("start", "1"), out var n) ? n : 1;
            return new MarkupList(node.Name == "ol", start, items);
        }

        private MarkupTable ReadTable(HtmlNode node)
        {
            var rows = new List<MarkupTableRow>();

            foreach (var tr in node.Descendants("tr"))
            {
                // A nested table's rows belong to that table, not this one.
                if (!ReferenceEquals(tr.Ancestors("table").FirstOrDefault(), node)) continue;

                var cells = new List<MarkupTableCell>();
                foreach (var cell in tr.ChildNodes.Where(c => c.Name is "td" or "th"))
                {
                    var blocks = new List<MarkupBlock>();
                    ReadBlocks(cell.ChildNodes, blocks);
                    cells.Add(new MarkupTableCell(cell.Name == "th", Align(cell), blocks));
                }

                if (cells.Count > 0) rows.Add(new MarkupTableRow(cells));
            }

            return new MarkupTable(rows);
        }

        private MarkupTabSet ReadTabSet(HtmlNode node)
        {
            var tabs = new List<MarkupTab>();

            foreach (var panel in node.ChildNodes.Where(c => HasClass(c, "tab-panel")))
            {
                var titleNode = panel.ChildNodes.FirstOrDefault(c => HasClass(c, "tab-title"));
                var contentNode = panel.ChildNodes.FirstOrDefault(c => HasClass(c, "tab-content"));

                var title = titleNode is null ? string.Empty : Collapse(WebUtility.HtmlDecode(titleNode.InnerText)).Trim();
                var blocks = new List<MarkupBlock>();
                if (contentNode is not null) ReadBlocks(contentNode.ChildNodes, blocks);

                tabs.Add(new MarkupTab(title, blocks));
            }

            return new MarkupTabSet(tabs);
        }

        // ---------------------------------------------------------------- inlines

        private List<MarkupInline> Inlines(IEnumerable<HtmlNode> nodes)
        {
            var inlines = new List<MarkupInline>();
            foreach (var node in nodes) ReadInline(node, inlines);
            Trim(inlines);
            return inlines;
        }

        private void ReadInline(HtmlNode node, List<MarkupInline> into)
        {
            switch (node.NodeType)
            {
                case HtmlNodeType.Comment:
                    return;

                case HtmlNodeType.Text:
                {
                    var text = Collapse(WebUtility.HtmlDecode(node.InnerText));
                    if (text.Length == 0) return;

                    // One space between two runs is enough; HTML collapses the rest.
                    if (text == " " && (into.Count == 0 || EndsWithSpace(into[^1]))) return;
                    into.Add(new MarkupText(text));
                    return;
                }
            }

            switch (node.Name)
            {
                case "br":
                    into.Add(new MarkupBreak());
                    return;

                case "img":
                {
                    var source = WebUtility.HtmlDecode(node.GetAttributeValue("src", string.Empty));
                    if (AbsoluteHttp(source) is { } url)
                        into.Add(new MarkupImage(url, WebUtility.HtmlDecode(node.GetAttributeValue("alt", string.Empty))));
                    return;
                }

                case "code":
                    into.Add(new MarkupInlineCode(WebUtility.HtmlDecode(node.InnerText)));
                    return;
            }

            var style = node.Name switch
            {
                "strong" or "b" => MarkupStyle.Bold,
                "em" or "i" => MarkupStyle.Italic,
                "del" or "s" or "strike" => MarkupStyle.Strike,
                "u" or "ins" => MarkupStyle.Underline,
                "a" => MarkupStyle.Link,
                "sup" => MarkupStyle.Superscript,
                "sub" => MarkupStyle.Subscript,
                _ => MarkupStyle.None,
            };

            var children = new List<MarkupInline>();
            foreach (var child in node.ChildNodes) ReadInline(child, children);

            if (style == MarkupStyle.None)
            {
                // span, font, mark and anything unknown: keep what is inside.
                into.AddRange(children);
                return;
            }

            string? href = null;
            if (style == MarkupStyle.Link)
            {
                href = AbsoluteHttp(WebUtility.HtmlDecode(node.GetAttributeValue("href", string.Empty)));

                // A link to nowhere the app can open is just its text.
                if (href is null)
                {
                    into.AddRange(children);
                    return;
                }
            }

            if (children.Count > 0) into.Add(new MarkupSpan(style, href, children));
        }

        // ---------------------------------------------------------------- helpers

        private static readonly HashSet<string> BlockTags =
        [
            "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "hr",
            "table", "thead", "tbody", "tfoot", "tr", "section", "article", "details", "summary", "figure",
            "figcaption", "header", "footer", "aside", "nav", "dl", "dt", "dd", "center",
        ];

        private static bool IsBlock(HtmlNode node) =>
            node.NodeType == HtmlNodeType.Element && BlockTags.Contains(node.Name);

        private static bool HasClass(HtmlNode node, string name) =>
            node.NodeType == HtmlNodeType.Element &&
            node.GetAttributeValue("class", string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(name, StringComparer.OrdinalIgnoreCase);

        private static string? Language(HtmlNode code) =>
            code.GetAttributeValue("class", string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(c => c.StartsWith("language-", StringComparison.Ordinal))?["language-".Length..];

        private static MarkupAlign Align(HtmlNode cell)
        {
            var value = (cell.GetAttributeValue("align", null) ?? cell.GetAttributeValue("style", string.Empty)).ToLowerInvariant();
            if (value.Contains("center")) return MarkupAlign.Center;
            if (value.Contains("right")) return MarkupAlign.Right;
            return MarkupAlign.Left;
        }

        private static string Collapse(string text) => Whitespace.Replace(text, " ");

        // Only web links: a relative or javascript: href has nothing the app should open.
        private static string? AbsoluteHttp(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) trimmed = "https:" + trimmed;

            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri.AbsoluteUri
                : null;
        }

        private static bool EndsWithSpace(MarkupInline inline) => inline switch
        {
            MarkupText text => text.Text.EndsWith(' '),
            MarkupBreak => true,
            MarkupSpan span => span.Children.Count > 0 && EndsWithSpace(span.Children[^1]),
            _ => false,
        };

        // Leading and trailing spaces at a block's edges are layout noise from the HTML source.
        private static void Trim(List<MarkupInline> inlines)
        {
            while (inlines.Count > 0 && inlines[0] is MarkupText first && first.Text.TrimStart().Length == 0) inlines.RemoveAt(0);
            while (inlines.Count > 0 && inlines[^1] is MarkupText last && last.Text.TrimEnd().Length == 0) inlines.RemoveAt(inlines.Count - 1);
            while (inlines.Count > 0 && inlines[^1] is MarkupBreak) inlines.RemoveAt(inlines.Count - 1);

            if (inlines.Count > 0 && inlines[0] is MarkupText head) inlines[0] = head with { Text = head.Text.TrimStart() };
            if (inlines.Count > 0 && inlines[^1] is MarkupText tail) inlines[^1] = tail with { Text = tail.Text.TrimEnd() };
        }
    }
}

// ------------------------------------------------------------------------ the tree

public sealed class MarkupDocument
{
    public List<MarkupBlock> Blocks { get; } = [];

    public bool IsEmpty => Blocks.Count == 0;

    // The document's words with no markup - what a test or a search compares against.
    public string PlainText()
    {
        var text = new StringBuilder();

        void Inlines(IEnumerable<MarkupInline> inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case MarkupText t: text.Append(t.Text); break;
                    case MarkupInlineCode c: text.Append(c.Text); break;
                    case MarkupBreak: text.Append('\n'); break;
                    case MarkupSpan s: Inlines(s.Children); break;
                }
            }
        }

        void Blocks(IEnumerable<MarkupBlock> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case MarkupParagraph p: Inlines(p.Inlines); text.Append('\n'); break;
                    case MarkupHeading h: Inlines(h.Inlines); text.Append('\n'); break;
                    case MarkupList l: foreach (var item in l.Items) Blocks(item); break;
                    case MarkupQuote q: Blocks(q.Blocks); break;
                    case MarkupCode c: text.Append(c.Text).Append('\n'); break;
                    case MarkupTable t:
                        foreach (var row in t.Rows)
                        foreach (var cell in row.Cells)
                            Blocks(cell.Blocks);
                        break;
                    case MarkupTabSet s:
                        foreach (var tab in s.Tabs)
                        {
                            text.Append(tab.Title).Append('\n');
                            Blocks(tab.Blocks);
                        }
                        break;
                }
            }
        }

        Blocks(this.Blocks);
        return text.ToString();
    }
}

public abstract record MarkupBlock;

public sealed record MarkupParagraph(IReadOnlyList<MarkupInline> Inlines) : MarkupBlock;

public sealed record MarkupHeading(int Level, IReadOnlyList<MarkupInline> Inlines) : MarkupBlock;

// Each item is a list of blocks: a plain item holds one paragraph, a nested one also holds a list.
public sealed record MarkupList(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<MarkupBlock>> Items) : MarkupBlock;

public enum MarkupQuoteKind { Plain, Warning }

public sealed record MarkupQuote(MarkupQuoteKind Kind, IReadOnlyList<MarkupBlock> Blocks) : MarkupBlock;

public sealed record MarkupCode(string Text, string? Language) : MarkupBlock;

public sealed record MarkupRule : MarkupBlock;

public enum MarkupAlign { Left, Center, Right }

public sealed record MarkupTableCell(bool IsHeader, MarkupAlign Align, IReadOnlyList<MarkupBlock> Blocks);

public sealed record MarkupTableRow(IReadOnlyList<MarkupTableCell> Cells);

public sealed record MarkupTable(IReadOnlyList<MarkupTableRow> Rows) : MarkupBlock
{
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells.Count);
}

public sealed record MarkupTab(string Title, IReadOnlyList<MarkupBlock> Blocks);

public sealed record MarkupTabSet(IReadOnlyList<MarkupTab> Tabs) : MarkupBlock;

public sealed record MarkupVideo(string VideoId) : MarkupBlock
{
    // The same thumbnail and privacy-enhanced player sp-mod.com's own embed uses.
    public string Thumbnail => $"https://i.ytimg.com/vi/{VideoId}/hqdefault.jpg";
    public string EmbedUrl => $"https://www.youtube-nocookie.com/embed/{VideoId}?autoplay=1";
    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";
}

public abstract record MarkupInline;

public sealed record MarkupText(string Text) : MarkupInline;

public sealed record MarkupBreak : MarkupInline;

public sealed record MarkupInlineCode(string Text) : MarkupInline;

public sealed record MarkupImage(string Source, string Alt) : MarkupInline;

public enum MarkupStyle { None, Bold, Italic, Strike, Underline, Link, Superscript, Subscript }

public sealed record MarkupSpan(MarkupStyle Style, string? Href, IReadOnlyList<MarkupInline> Children) : MarkupInline;

public enum MarkupMediaKind { Image, Video }

// One entry in the screenshot strip. Key is what makes two entries the same picture. EmbedUrl is
// the player a video plays in; a picture has none.
public sealed record MarkupMedia(MarkupMediaKind Kind, string Url, string Thumbnail, string? Alt, string? EmbedUrl)
{
    public string Key => Url;
}
