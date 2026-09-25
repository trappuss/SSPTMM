using TCFModManager.Core.Markup;
using Xunit;

namespace TCFModManager.Core.Tests;

//
// The fragments below are cut from real descriptions returned by the API (mods 791, 644, 2788,
// 1860, 861), trimmed to the part under test.
//
public class SpModMarkupTests
{
    [Fact]
    public void A_tab_set_becomes_tabs_with_their_titles_and_content()
    {
        var doc = SpModMarkup.Parse(
            """
            <p>Intro</p>
            <div class="tabset"><div id="tabset-1-panel-1" class="tab-panel"><div class="tab-title">Features</div>
            <div class="tab-content"><p><strong>SAIN 3.0: New Features:</strong></p>
            <ul><li><p>New Combat AI</p><ul><li>Fully replaced combat logic.</li></ul></li></ul></div></div>
            <div id="tabset-1-panel-2" class="tab-panel"><div class="tab-title">Install</div>
            <div class="tab-content"><p>Drop it in.</p></div></div></div>
            """);

        Assert.IsType<MarkupParagraph>(doc.Blocks[0]);
        var tabs = Assert.IsType<MarkupTabSet>(doc.Blocks[1]);
        Assert.Equal(["Features", "Install"], tabs.Tabs.Select(t => t.Title));

        var list = Assert.IsType<MarkupList>(tabs.Tabs[0].Blocks[1]);
        var item = list.Items[0];
        Assert.IsType<MarkupParagraph>(item[0]);
        var nested = Assert.IsType<MarkupList>(item[1]);
        Assert.Single(nested.Items);
    }

    [Fact]
    public void Tab_sets_can_sit_inside_tabs()
    {
        var doc = SpModMarkup.Parse(
            """
            <div class="tabset"><div class="tab-panel"><div class="tab-title">Outer</div><div class="tab-content">
            <div class="tabset"><div class="tab-panel"><div class="tab-title">Inner</div><div class="tab-content"><p>x</p></div></div></div>
            </div></div></div>
            """);

        var outer = Assert.IsType<MarkupTabSet>(Assert.Single(doc.Blocks));
        var inner = Assert.IsType<MarkupTabSet>(Assert.Single(outer.Tabs[0].Blocks));
        Assert.Equal("Inner", inner.Tabs[0].Title);
    }

    [Fact]
    public void A_youtube_embed_becomes_a_video_with_the_sites_own_thumbnail_and_player()
    {
        var doc = SpModMarkup.Parse(
            """<div class="youtube-lite" data-video-id="yRSzwaKDyIY" data-embed-url="https://www.youtube-nocookie.com/embed/yRSzwaKDyIY?autoplay=1"><img src="https://i.ytimg.com/vi/yRSzwaKDyIY/hqdefault.jpg" alt="YouTube video thumbnail" loading="lazy" decoding="async" /></div>""");

        var video = Assert.IsType<MarkupVideo>(Assert.Single(doc.Blocks));
        Assert.Equal("yRSzwaKDyIY", video.VideoId);
        Assert.Equal("https://i.ytimg.com/vi/yRSzwaKDyIY/hqdefault.jpg", video.Thumbnail);
        Assert.Equal("https://www.youtube-nocookie.com/embed/yRSzwaKDyIY?autoplay=1", video.EmbedUrl);
    }

    [Fact]
    public void A_video_id_that_is_not_youtubes_shape_is_dropped()
    {
        var doc = SpModMarkup.Parse("""<div class="youtube-lite" data-video-id="x&quot;onerror"></div>""");
        Assert.True(doc.IsEmpty);
    }

    [Fact]
    public void A_warning_quote_keeps_its_kind()
    {
        var doc = SpModMarkup.Parse(
            """
            <blockquote class="is-warning">
            <p>⚠️ 1.1 STORYLINE AND QUEST NOT INCLUDED.</p>
            </blockquote>
            <blockquote><p>Plain</p></blockquote>
            """);

        Assert.Equal(MarkupQuoteKind.Warning, Assert.IsType<MarkupQuote>(doc.Blocks[0]).Kind);
        Assert.Equal(MarkupQuoteKind.Plain, Assert.IsType<MarkupQuote>(doc.Blocks[1]).Kind);
    }

    [Fact]
    public void A_code_block_keeps_its_line_breaks_and_language()
    {
        var doc = SpModMarkup.Parse("<pre><code class=\"hljs language-json\">{\n  \"a\": 1,\n  \"b\": &quot;x&quot;\n}\n</code></pre>");

        var code = Assert.IsType<MarkupCode>(Assert.Single(doc.Blocks));
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": \"x\"\n}", code.Text);
        Assert.Equal("json", code.Language);
    }

    [Fact]
    public void Inline_formatting_links_and_code_are_kept()
    {
        var doc = SpModMarkup.Parse(
            """<p>Write a <code>.DLG</code> script, <em>then</em> <del>not</del> <a target="_blank" class="external-link" href="https://github.com/ArchangelWTF/SAIN/issues" rel="noreferrer noopener">report it</a>.</p>""");

        var paragraph = Assert.IsType<MarkupParagraph>(Assert.Single(doc.Blocks));
        Assert.Contains(paragraph.Inlines, i => i is MarkupInlineCode { Text: ".DLG" });
        Assert.Contains(paragraph.Inlines, i => i is MarkupSpan { Style: MarkupStyle.Italic });
        Assert.Contains(paragraph.Inlines, i => i is MarkupSpan { Style: MarkupStyle.Strike });
        var link = Assert.Single(paragraph.Inlines.OfType<MarkupSpan>(), s => s.Style == MarkupStyle.Link);
        Assert.Equal("https://github.com/ArchangelWTF/SAIN/issues", link.Href);
        Assert.Equal("Write a .DLG script, then not report it.\n", doc.PlainText());
    }

    [Fact]
    public void A_link_that_is_not_a_web_address_is_only_its_text()
    {
        var doc = SpModMarkup.Parse("""<p><a href="javascript:alert(1)">click</a> <a href="/relative">here</a></p>""");

        var paragraph = Assert.IsType<MarkupParagraph>(Assert.Single(doc.Blocks));
        Assert.DoesNotContain(paragraph.Inlines, i => i is MarkupSpan);
        Assert.Equal("click here\n", doc.PlainText());
    }

    [Fact]
    public void Images_are_kept_inline_with_their_link()
    {
        var doc = SpModMarkup.Parse(
            """<p><strong><a href="https://i.imgur.com/YIDLOwM_d.png" rel="noreferrer noopener"><img loading="lazy" decoding="async" src="https://i.imgur.com/YIDLOwM_d.png?maxwidth=520&amp;shape=thumb&amp;fidelity=high" alt="Image" /></a></strong></p>""");

        var paragraph = Assert.IsType<MarkupParagraph>(Assert.Single(doc.Blocks));
        var bold = Assert.IsType<MarkupSpan>(Assert.Single(paragraph.Inlines));
        var link = Assert.IsType<MarkupSpan>(Assert.Single(bold.Children));
        var image = Assert.IsType<MarkupImage>(Assert.Single(link.Children));
        Assert.Equal("https://i.imgur.com/YIDLOwM_d.png?maxwidth=520&shape=thumb&fidelity=high", image.Source);
    }

    [Fact]
    public void A_table_keeps_its_header_cells_and_rows()
    {
        var doc = SpModMarkup.Parse(
            """
            <table><thead><tr><th>Setting</th><th align="right">Default</th></tr></thead>
            <tbody><tr><td>Speed</td><td align="right">1.0</td></tr><tr><td>Size</td><td>2</td></tr></tbody></table>
            """);

        var table = Assert.IsType<MarkupTable>(Assert.Single(doc.Blocks));
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(2, table.ColumnCount);
        Assert.True(table.Rows[0].Cells[0].IsHeader);
        Assert.Equal(MarkupAlign.Right, table.Rows[1].Cells[1].Align);
    }

    [Fact]
    public void Headings_rules_and_ordered_list_starts_are_read()
    {
        var doc = SpModMarkup.Parse("<h2>Install</h2><hr /><ol start=\"3\"><li>Three</li><li>Four</li></ol>");

        Assert.Equal(2, Assert.IsType<MarkupHeading>(doc.Blocks[0]).Level);
        Assert.IsType<MarkupRule>(doc.Blocks[1]);
        var list = Assert.IsType<MarkupList>(doc.Blocks[2]);
        Assert.True(list.Ordered);
        Assert.Equal(3, list.Start);
    }

    [Fact]
    public void Whitespace_between_tags_is_collapsed_and_unknown_tags_keep_their_text()
    {
        var doc = SpModMarkup.Parse("<div>\n  <span>one</span>\n  <font color=\"red\">two</font>\n</div>\n<p>  three   four </p>");

        Assert.Equal("one two\nthree four\n", doc.PlainText());
    }

    [Fact]
    public void Media_lists_every_picture_and_video_once_in_reading_order_through_every_tab()
    {
        var doc = SpModMarkup.Parse(
            """
            <p><img src="https://i.imgur.com/a.png" alt="A" /></p>
            <div class="tabset"><div class="tab-panel"><div class="tab-title">One</div><div class="tab-content">
            <div class="youtube-lite" data-video-id="H52R3wgOzqw"></div></div></div>
            <div class="tab-panel"><div class="tab-title">Two</div><div class="tab-content"><p><img src="https://i.imgur.com/b.gif" alt="B" /><img src="https://i.imgur.com/a.png" /></p></div></div></div>
            """);

        var media = SpModMarkup.Media(doc);
        Assert.Equal(
            ["https://i.imgur.com/a.png", "https://www.youtube.com/watch?v=H52R3wgOzqw", "https://i.imgur.com/b.gif"],
            media.Select(m => m.Url));
        Assert.Equal(MarkupMediaKind.Video, media[1].Kind);
    }

    [Fact]
    public void Nothing_in_is_nothing_out()
    {
        Assert.True(SpModMarkup.Parse(null).IsEmpty);
        Assert.True(SpModMarkup.Parse("   ").IsEmpty);
    }
}
