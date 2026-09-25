using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TCFModManager.App.Services;
using TCFModManager.Core.Markup;

namespace TCFModManager.App.Behaviors.Markup;

//
// Lays a parsed sp-mod.com description out as a FlowDocument, drawn the way a Steam Workshop item
// description is: Steam's BBCode styles (shared_global.css and the live item page, measured
// 2026-09-25 - see docs/steam-workshop-ui.md) at the Workshop page's 14px, scaled to whatever size
// the host sets.
//
// sp-mod.com's descriptions can hold things Steam's BBCode has no form for - tab sets, inline code,
// warning callouts, YouTube embeds. Those keep their shape and take Steam's palette.
//
// Tab sets, pictures, GIFs and videos are real controls inside the document. Plain text stays text,
// so it wraps, reflows and reads the same as it did.
//
public sealed class MarkupRenderer
{
    // ---------------------------------------------------------------- Steam's colours

    private static readonly Brush White = Frozen(0xFF, 0xFF, 0xFF);

    // div.bb_h1 / bb_h2 / bb_h3: #5AA9D6.
    private static readonly Brush HeadingBlue = Frozen(0x5A, 0xA9, 0xD6);

    // a.bb_link on the item page: #EBEBEB, not underlined (measured, hover included).
    private static readonly Brush LinkText = Frozen(0xEB, 0xEB, 0xEB);

    // blockquote.bb_blockquote: 1px #56707F.
    private static readonly Brush QuoteBorder = Frozen(0x56, 0x70, 0x7F);

    // div.bb_code: 1px #535354.
    private static readonly Brush CodeBorder = Frozen(0x53, 0x53, 0x54);

    // div.bb_table cells: 1px #4D4D4D.
    private static readonly Brush TableBorder = Frozen(0x4D, 0x4D, 0x4D);

    // The Workshop page's own rule (.rightDetailsBlock hr): 1px #1F2C41.
    private static readonly Brush RuleColour = Frozen(0x1F, 0x2C, 0x41);

    //
    // Not Steam's - it has no form for these - drawn in its palette:
    //  - a tab set's buttons as the item page's own tabs (#ACB2B8, white and a #1A9FFF bar when
    //    chosen), the content in the rgba(0,0,0,.2) box Steam puts change notes in;
    //  - a warning callout as a Steam quote with an amber edge and a faint amber wash, so it still
    //    reads as a warning;
    //  - inline code in the code face on the same faint shade, in Steam's light #C6D4DF.
    //
    private static readonly Brush TabText = Frozen(0xAC, 0xB2, 0xB8);
    private static readonly Brush TabChosenBar = Frozen(0x1A, 0x9F, 0xFF);
    private static readonly Brush TabContentBackground = Frozen(0x33, 0x00, 0x00, 0x00);
    private static readonly Brush WarningBorder = Frozen(0xD0, 0x87, 0x00);
    private static readonly Brush WarningBackground = Frozen(0x1F, 0xD0, 0x87, 0x00);
    private static readonly Brush InlineCodeBackground = Frozen(0x33, 0x00, 0x00, 0x00);
    private static readonly Brush InlineCodeText = Frozen(0xC6, 0xD4, 0xDF);

    // Themes/SteamStyles.xaml's code face (Cascadia Mono, then Consolas, then Courier New).
    private static FontFamily Monospace =>
        Application.Current?.TryFindResource("SteamMonospace") as FontFamily ?? new FontFamily("Consolas");

    // Themes/SteamStyles.xaml's light face, for bb_h3's weight 300.
    private static FontFamily? LightFace =>
        Application.Current?.TryFindResource("SteamFontLight") as FontFamily;

    private readonly double _scale;
    private readonly double _base;
    private readonly IReadOnlyList<MarkupMedia> _gallery;

    private MarkupRenderer(double baseSize, IReadOnlyList<MarkupMedia> gallery)
    {
        _base = baseSize;

        // Steam's description is 14px; every measurement below is at that size.
        _scale = baseSize / 14.0;
        _gallery = gallery;
    }

    /// <summary>The whole document, sized from <paramref name="baseSize"/> (the host's font size).</summary>
    public static FlowDocument Render(MarkupDocument document, double baseSize)
    {
        var renderer = new MarkupRenderer(baseSize, SpModMarkup.Media(document));
        return renderer.Document(document.Blocks);
    }

    private double Px(double cssPixels) => Math.Round(cssPixels * _scale, 1);

    private FlowDocument Document(IEnumerable<MarkupBlock> blocks)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), TextAlignment = TextAlignment.Left };
        AddBlocks(document.Blocks, blocks);
        TrimOuterMargins(document.Blocks);
        return document;
    }

    // ---------------------------------------------------------------- blocks

    private void AddBlocks(BlockCollection into, IEnumerable<MarkupBlock> blocks)
    {
        foreach (var block in blocks)
        {
            var built = Build(block);
            if (built is not null) into.Add(built);
        }
    }

    private Block? Build(MarkupBlock block) => block switch
    {
        MarkupParagraph p => Paragraph(p),
        MarkupHeading h => Heading(h),
        MarkupList l => List(l),
        MarkupQuote q => Quote(q),
        MarkupCode c => CodeBlock(c),
        MarkupRule => Rule(),
        MarkupTable t => Table(t),
        MarkupTabSet s => TabSet(s),
        MarkupVideo v => Video(v),
        _ => null,
    };

    private Block Paragraph(MarkupParagraph paragraph)
    {
        // A paragraph of nothing but pictures (a screenshot, a row of badges) is laid out as a row
        // of controls, which is measured against the column's width and so can shrink a wide
        // picture to fit. Pictures inside a sentence stay in the sentence.
        if (IsPicturesOnly(paragraph.Inlines))
        {
            var row = new WrapPanel();
            foreach (var (image, href) in Pictures(paragraph.Inlines, null)) row.Children.Add(Picture(image, href));
            return new BlockUIContainer(row) { Margin = new Thickness(0, Px(7), 0, Px(7)) };
        }

        // Steam's descriptions separate their lines with breaks rather than paragraphs; a paragraph
        // here keeps half a line either side, which is how a blank line between them reads there.
        var built = new Paragraph { Margin = new Thickness(0, Px(7), 0, Px(7)) };
        AddInlines(built.Inlines, paragraph.Inlines);
        return built;
    }

    private Block Heading(MarkupHeading heading)
    {
        // Steam's three: bb_h1 20/23 normal, 10px below; bb_h2 18/21 normal, 8 above and 6 below;
        // bb_h3 16/19 light, 8 above and 6 below - all #5AA9D6. BBCode stops at h3, so h4-h6 take
        // its style.
        var built = new Paragraph { Foreground = HeadingBlue, FontWeight = FontWeights.Normal };

        switch (heading.Level)
        {
            case 1:
                built.FontSize = Px(20);
                built.LineHeight = Px(23);
                built.Margin = new Thickness(0, Px(7), 0, Px(10));
                break;
            case 2:
                built.FontSize = Px(18);
                built.LineHeight = Px(21);
                built.Margin = new Thickness(0, Px(8), 0, Px(6));
                break;
            default:
                built.FontSize = Px(16);
                built.LineHeight = Px(19);
                built.Margin = new Thickness(0, Px(8), 0, Px(6));
                built.FontWeight = FontWeights.Light;
                if (LightFace is { } light) built.FontFamily = light;
                break;
        }

        AddInlines(built.Inlines, heading.Inlines);
        return built;
    }

    private Block List(MarkupList list)
    {
        // .bb_ul: disc markers outside, the browser's 40px indent, items line after line.
        var built = new List
        {
            MarkerStyle = list.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            StartIndex = Math.Max(1, list.Start),
            Margin = new Thickness(0, 0, 0, Px(7)),
            Padding = new Thickness(Px(40), 0, 0, 0),
        };

        foreach (var item in list.Items)
        {
            var listItem = new ListItem { Margin = new Thickness(0) };
            AddBlocks(listItem.Blocks, item);

            // An item's first paragraph sits on the marker's line; its own margins would push it off.
            foreach (var block in listItem.Blocks.OfType<Paragraph>()) block.Margin = new Thickness(0);
            if (listItem.Blocks.Count == 0) listItem.Blocks.Add(new Paragraph());

            built.ListItems.Add(listItem);
        }

        return built;
    }

    private Block Quote(MarkupQuote quote)
    {
        var warning = quote.Kind == MarkupQuoteKind.Warning;

        // blockquote.bb_blockquote: 1px #56707F all round, 12px inside, 8px outside, text at 92%.
        // (Its 3px corner radius is left out: a FlowDocument section cannot round its corners.)
        var section = new Section
        {
            BorderBrush = warning ? WarningBorder : QuoteBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(Px(12)),
            Margin = new Thickness(Px(8)),
            FontSize = Math.Round(_base * 0.92, 1),
        };

        if (warning) section.Background = WarningBackground;

        AddBlocks(section.Blocks, quote.Blocks);
        TrimOuterMargins(section.Blocks);
        return section;
    }

    private Block CodeBlock(MarkupCode code)
    {
        // div.bb_code: 11px in the code face, 1px #535354 all round, 12px inside, 8px outside,
        // text as it was written. (Its 3px corners are left out, as a quote's are.)
        var built = new Paragraph
        {
            FontFamily = Monospace,
            FontSize = Px(11),
            BorderBrush = CodeBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(Px(12)),
            Margin = new Thickness(Px(8)),
        };

        var lines = code.Text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) built.Inlines.Add(new LineBreak());
            built.Inlines.Add(new Run(lines[i]));
        }

        return built;
    }

    private Block Rule() =>
        // The Workshop page's rule: 1px #1F2C41, 10px either side.
        new BlockUIContainer(new Rectangle { Height = 1, Fill = RuleColour, SnapsToDevicePixels = true })
        {
            Margin = new Thickness(0, Px(10), 0, Px(10)),
        };

    private Block Table(MarkupTable table)
    {
        // div.bb_table: 12px text; every cell 4px inside a 1px #4D4D4D border, headers bold.
        var built = new Table { CellSpacing = 0, FontSize = Px(12), Margin = new Thickness(0, Px(7), 0, Px(7)) };

        // sp-mod.com sizes columns to their content. A FlowDocument table cannot, so each column
        // gets a share of the width in proportion to how much text it holds, within limits.
        var columns = table.ColumnCount;
        for (var c = 0; c < columns; c++)
        {
            var column = c;
            var weight = table.Rows
                .Where(r => column < r.Cells.Count)
                .Select(r => (double)CellLength(r.Cells[column]))
                .DefaultIfEmpty(1)
                .Average();

            built.Columns.Add(new TableColumn { Width = new GridLength(Math.Clamp(weight, 6, 60), GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        foreach (var row in table.Rows)
        {
            var builtRow = new TableRow();
            foreach (var cell in row.Cells)
            {
                var builtCell = new TableCell
                {
                    Padding = new Thickness(Px(4)),
                    BorderBrush = TableBorder,
                    BorderThickness = new Thickness(1),
                };
                AddBlocks(builtCell.Blocks, cell.Blocks);
                TrimOuterMargins(builtCell.Blocks);

                if (cell.IsHeader) builtCell.FontWeight = FontWeights.Bold;

                var alignment = cell.Align switch
                {
                    MarkupAlign.Center => TextAlignment.Center,
                    MarkupAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                builtCell.TextAlignment = alignment;

                builtRow.Cells.Add(builtCell);
            }

            group.Rows.Add(builtRow);
        }

        built.RowGroups.Add(group);
        return built;
    }

    private static int CellLength(MarkupTableCell cell)
    {
        var holder = new MarkupDocument();
        holder.Blocks.AddRange(cell.Blocks);
        return holder.PlainText().Length;
    }

    // ---------------------------------------------------------------- tab sets

    //
    // sp-mod.com's tab sets, which Steam has no form for, drawn as the Workshop item page's own tabs:
    // the names in #ACB2B8, the chosen one white over a 3px #1A9FFF bar; its content 4px below in
    // the rgba(0,0,0,.2) box Steam puts change notes in, 16px inside.
    //
    private Block TabSet(MarkupTabSet set)
    {
        var strip = new WrapPanel();
        var content = new RichTextBox();
        HtmlText.PrepareHost(content, _base);

        var box = new Border
        {
            Background = TabContentBackground,
            Padding = new Thickness(Px(16)),
            Margin = new Thickness(0, 4, 0, 0),
            Child = content,
        };

        // Each tab's document is built the first time it is shown and kept.
        var documents = new FlowDocument?[set.Tabs.Count];
        var buttons = new List<ToggleButton>();

        void Select(int index)
        {
            for (var i = 0; i < buttons.Count; i++) buttons[i].IsChecked = i == index;

            documents[index] ??= Document(set.Tabs[index].Blocks);
            HtmlText.Attach(content, documents[index]!);
        }

        for (var i = 0; i < set.Tabs.Count; i++)
        {
            var index = i;
            var button = new ToggleButton
            {
                Content = set.Tabs[i].Title,
                Style = TabStyle(),
                Margin = new Thickness(0, 4, Px(16), 0),
            };
            button.Click += (_, _) => Select(index);
            buttons.Add(button);
            strip.Children.Add(button);
        }

        var panel = new StackPanel();
        panel.Children.Add(strip);
        panel.Children.Add(box);

        Select(0);
        return new BlockUIContainer(panel) { Margin = new Thickness(0, Px(8), 0, Px(8)) };
    }

    private Style? _tabStyle;

    private Style TabStyle()
    {
        if (_tabStyle is not null) return _tabStyle;

        var template = new ControlTemplate(typeof(ToggleButton));
        var border = new FrameworkElementFactory(typeof(Border), "Chrome");
        border.SetValue(Border.PaddingProperty, new Thickness(0, 4, 0, 5));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 3));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(text);
        template.VisualTree = border;

        var chosen = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        chosen.Setters.Add(new Setter(Border.BorderBrushProperty, TabChosenBar, "Chrome"));
        template.Triggers.Add(chosen);

        var style = new Style(typeof(ToggleButton));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.FontSizeProperty, _base));
        style.Setters.Add(new Setter(Control.ForegroundProperty, TabText));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.ForegroundProperty, White));
        style.Triggers.Add(hover);

        var styleChosen = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        styleChosen.Setters.Add(new Setter(Control.ForegroundProperty, White));
        style.Triggers.Add(styleChosen);

        return _tabStyle = style;
    }

    // ---------------------------------------------------------------- videos

    //
    // sp-mod.com's lite embed: the video's thumbnail at 16:9 across the full width on black, with
    // a play button. Clicking plays it in the viewer rather than loading YouTube into the page.
    //
    private Block Video(MarkupVideo video)
    {
        var thumbnail = new RemotePicture
        {
            LimitToNaturalSize = false,
            Stretch = Stretch.UniformToFill,
            StretchDirection = StretchDirection.Both,
            Url = video.Thumbnail,
        };

        var play = new Grid { Width = 68, Height = 48, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        play.Children.Add(new Border { Background = Frozen(0xE6, 0x21, 0x21, 0x21), CornerRadius = new CornerRadius(12) });
        play.Children.Add(new Path
        {
            Data = Geometry.Parse("M0,0L18,11L0,22Z"),
            Fill = White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        });

        var frame = new AspectBox { Ratio = 9.0 / 16.0, Background = Brushes.Black, Cursor = Cursors.Hand, ClipToBounds = true };
        var layers = new Grid();
        layers.Children.Add(thumbnail);
        layers.Children.Add(play);
        frame.Child = layers;
        frame.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            MarkupActions.PlayVideo(_gallery, video);
        };

        return new BlockUIContainer(frame) { Margin = new Thickness(0, Px(4), 0, Px(4)) };
    }

    // ---------------------------------------------------------------- inlines

    private void AddInlines(InlineCollection into, IEnumerable<MarkupInline> inlines, string? href = null)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkupText text:
                    into.Add(new Run(text.Text));
                    break;

                case MarkupBreak:
                    into.Add(new LineBreak());
                    break;

                case MarkupInlineCode code:
                    // Steam has no inline code (its [code] is a block): the code face at 12px on a
                    // faint shade, light enough to tell from the text around it.
                    into.Add(new Run(code.Text)
                    {
                        FontFamily = Monospace,
                        FontSize = Px(12),
                        Foreground = InlineCodeText,
                        Background = InlineCodeBackground,
                    });
                    break;

                case MarkupImage image:
                    into.Add(new InlineUIContainer(Picture(image, href)) { BaselineAlignment = BaselineAlignment.Bottom });
                    break;

                case MarkupSpan span:
                    into.Add(Span(span, href));
                    break;
            }
        }
    }

    private Inline Span(MarkupSpan span, string? outerHref)
    {
        Span built = span.Style switch
        {
            MarkupStyle.Bold => new Bold(),
            MarkupStyle.Italic => new Italic(),
            MarkupStyle.Underline => new Underline(),
            MarkupStyle.Link => Link(span.Href!),
            _ => new Span(),
        };

        switch (span.Style)
        {
            case MarkupStyle.Strike:
                built.TextDecorations = TextDecorations.Strikethrough;
                break;
            case MarkupStyle.Superscript:
                built.BaselineAlignment = BaselineAlignment.Superscript;
                built.FontSize = Px(10.5);
                break;
            case MarkupStyle.Subscript:
                built.BaselineAlignment = BaselineAlignment.Subscript;
                built.FontSize = Px(10.5);
                break;
        }

        AddInlines(built.Inlines, span.Children, span.Style == MarkupStyle.Link ? span.Href : outerHref);
        return built;
    }

    // a.bb_link: #EBEBEB and not underlined, pointed at or not (measured on the item page).
    private static Hyperlink Link(string href)
    {
        var link = new Hyperlink
        {
            Foreground = LinkText,
            TextDecorations = null,
            Cursor = Cursors.Hand,
            ToolTip = href,
        };
        link.Click += (_, e) =>
        {
            e.Handled = true;
            MarkupActions.OpenLink(href);
        };
        return link;
    }

    // ---------------------------------------------------------------- pictures

    private FrameworkElement Picture(MarkupImage image, string? href)
    {
        var picture = new RemotePicture { Url = image.Source, Cursor = Cursors.Hand };
        if (!string.IsNullOrWhiteSpace(image.Alt)) picture.ToolTip = image.Alt;

        var host = new ContentControl { Content = picture, Focusable = false };

        // A picture that will not load is shown as its alternative text, as a browser shows it.
        picture.Failed += (_, _) =>
        {
            var label = string.IsNullOrWhiteSpace(image.Alt) ? image.Source : image.Alt;
            host.Content = new TextBlock
            {
                Text = label,
                Foreground = LinkText,
                Cursor = Cursors.Hand,
                TextWrapping = TextWrapping.Wrap,
            };
        };

        host.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (MarkupActions.IsPictureLink(href, image.Source)) MarkupActions.ShowPicture(_gallery, image.Source);
            else MarkupActions.OpenLink(href!);
        };

        return host;
    }

    private static bool IsPicturesOnly(IReadOnlyList<MarkupInline> inlines)
    {
        var any = false;
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkupImage:
                    any = true;
                    break;
                case MarkupBreak:
                    break;
                case MarkupText text when text.Text.Trim().Length == 0:
                    break;
                case MarkupSpan span when span.Style is MarkupStyle.Link or MarkupStyle.Bold or MarkupStyle.Italic or MarkupStyle.None:
                    if (!IsPicturesOnly(span.Children)) return false;
                    any = true;
                    break;
                default:
                    return false;
            }
        }

        return any;
    }

    private static IEnumerable<(MarkupImage Image, string? Href)> Pictures(IEnumerable<MarkupInline> inlines, string? href)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkupImage image:
                    yield return (image, href);
                    break;
                case MarkupSpan span:
                    foreach (var inner in Pictures(span.Children, span.Style == MarkupStyle.Link ? span.Href : href)) yield return inner;
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    // The first block's top margin and the last one's bottom margin would pad the host's own edges.
    private static void TrimOuterMargins(BlockCollection blocks)
    {
        if (blocks.FirstBlock is { } first) first.Margin = first.Margin with { Top = 0 };
        if (blocks.LastBlock is { } last) last.Margin = last.Margin with { Bottom = 0 };
    }

    private static Brush Frozen(byte r, byte g, byte b) => Frozen(0xFF, r, g, b);

    private static Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

//
// A box whose height follows its width by a fixed ratio - a 16:9 video frame that is as wide as
// the column it is in.
//
public sealed class AspectBox : Border
{
    public double Ratio { get; set; } = 9.0 / 16.0;

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsInfinity(constraint.Width) ? 640 : constraint.Width;
        var size = new Size(width, width * Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(new Size(arrangeSize.Width, arrangeSize.Width * Ratio)));
        return new Size(arrangeSize.Width, arrangeSize.Width * Ratio);
    }
}
