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
// Lays a parsed sp-mod.com description out as a FlowDocument, in the site's own markdown style:
// sizes, spacing and colours measured off sp-mod.com's user-markdown styles (2026-09-24, see
// docs/steam-workshop-ui.md), scaled from the site's 16px body to whatever size the host sets, so
// the Workshop page's 14px description keeps its proportions.
//
// Tab sets, pictures, GIFs and videos are real controls inside the document. Plain text stays text,
// so it wraps, reflows and reads the same as it did.
//
public sealed class MarkupRenderer
{
    // ---------------------------------------------------------------- sp-mod.com's colours

    private static readonly Brush White = Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush InlineCodeBackground = Frozen(0x36, 0x41, 0x53);   // gray-700
    private static readonly Brush InlineCodeText = Frozen(0xD1, 0xD5, 0xDC);         // gray-300
    private static readonly Brush TabContentBackground = Frozen(0x10, 0x18, 0x28);   // gray-900
    private static readonly Brush TabBackground = Frozen(0x0F, 0x17, 0x2B);          // slate-900
    private static readonly Brush TabActiveBackground = Frozen(0x1D, 0x29, 0x3D);    // slate-800
    private static readonly Brush TabActiveText = Frozen(0x53, 0xEA, 0xFD);          // cyan-300
    private static readonly Brush Cyan = Frozen(0x00, 0x92, 0xB8);                   // cyan-600
    private static readonly Brush QuoteBackground = TabContentBackground;
    private static readonly Brush WarningBackground = Frozen(0x43, 0x20, 0x04);      // amber-950
    private static readonly Brush WarningBorder = Frozen(0xD0, 0x87, 0x00);          // amber-600
    private static readonly Brush WarningText = Frozen(0xFE, 0xF9, 0xC2);            // yellow-100
    private static readonly Brush RuleColour = Frozen(0x33, 0x33, 0x33);

    // Not measured: sp-mod.com's code block sits on the page's own colour with no box. On the
    // Workshop page's blue that read as ordinary text in another font, so it gets a faint shade.
    private static readonly Brush CodeBlockBackground = Frozen(0x33, 0x00, 0x00, 0x00);

    // Themes/SteamStyles.xaml's code face (Cascadia Mono, then Consolas, then Courier New).
    private static FontFamily Monospace =>
        Application.Current?.TryFindResource("SteamMonospace") as FontFamily ?? new FontFamily("Consolas");

    private readonly double _scale;
    private readonly double _base;
    private readonly IReadOnlyList<MarkupMedia> _gallery;

    private MarkupRenderer(double baseSize, IReadOnlyList<MarkupMedia> gallery)
    {
        _base = baseSize;
        _scale = baseSize / 16.0;
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
            return new BlockUIContainer(row) { Margin = new Thickness(0, Px(8), 0, Px(8)) };
        }

        var built = new Paragraph { Margin = new Thickness(0, Px(8), 0, Px(8)) };
        AddInlines(built.Inlines, paragraph.Inlines);
        return built;
    }

    private Block Heading(MarkupHeading heading)
    {
        // h1 30/36, h2 24/32, h3 20/28, h4 18/28, all bold white with 16px above and 8px below.
        var (size, line) = heading.Level switch
        {
            1 => (30.0, 36.0),
            2 => (24.0, 32.0),
            3 => (20.0, 28.0),
            4 => (18.0, 28.0),
            _ => (16.0, 24.0),
        };

        var built = new Paragraph
        {
            FontSize = Px(size),
            LineHeight = Px(line),
            FontWeight = FontWeights.Bold,
            Foreground = White,
            Margin = new Thickness(0, Px(16), 0, Px(8)),
        };
        AddInlines(built.Inlines, heading.Inlines);
        return built;
    }

    private Block List(MarkupList list)
    {
        var built = new List
        {
            MarkerStyle = list.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            StartIndex = Math.Max(1, list.Start),
            Margin = new Thickness(0, 0, 0, Px(8)),
            Padding = new Thickness(Px(28), 0, 0, 0),
        };

        foreach (var item in list.Items)
        {
            var listItem = new ListItem { Margin = new Thickness(0, Px(4), 0, Px(4)) };
            AddBlocks(listItem.Blocks, item);

            // An item's first paragraph sits on the marker's line; its own margins would push it off.
            foreach (var block in listItem.Blocks.OfType<Paragraph>()) block.Margin = new Thickness(0, 0, 0, Px(4));
            if (listItem.Blocks.Count == 0) listItem.Blocks.Add(new Paragraph());

            built.ListItems.Add(listItem);
        }

        return built;
    }

    private Block Quote(MarkupQuote quote)
    {
        var warning = quote.Kind == MarkupQuoteKind.Warning;

        // A 4px bar down the left, 16px inside, 16px above and below.
        var section = new Section
        {
            Background = warning ? WarningBackground : QuoteBackground,
            BorderBrush = warning ? WarningBorder : Cyan,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(Px(16)),
            Margin = new Thickness(0, Px(16), 0, Px(16)),
        };

        if (warning) section.Foreground = WarningText;

        AddBlocks(section.Blocks, quote.Blocks);
        TrimOuterMargins(section.Blocks);
        return section;
    }

    private Block CodeBlock(MarkupCode code)
    {
        // 14px on 20px lines, 14px inside.
        var built = new Paragraph
        {
            FontFamily = Monospace,
            FontSize = Px(14),
            LineHeight = Px(20),
            Foreground = White,
            Background = CodeBlockBackground,
            Padding = new Thickness(Px(14)),
            Margin = new Thickness(0, Px(8), 0, Px(8)),
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
        // 2px of #333 with 16px either side.
        new BlockUIContainer(new Rectangle { Height = 2, Fill = RuleColour, SnapsToDevicePixels = true })
        {
            Margin = new Thickness(0, Px(16), 0, Px(16)),
        };

    private Block Table(MarkupTable table)
    {
        var built = new Table { CellSpacing = 0, Margin = new Thickness(0, Px(8), 0, Px(8)) };

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
                var builtCell = new TableCell { Padding = new Thickness(0, Px(2), Px(12), Px(2)) };
                AddBlocks(builtCell.Blocks, cell.Blocks);
                TrimOuterMargins(builtCell.Blocks);

                if (cell.IsHeader)
                {
                    builtCell.FontWeight = FontWeights.Bold;
                    builtCell.Foreground = White;
                }

                var alignment = cell.Align switch
                {
                    MarkupAlign.Center => TextAlignment.Center,
                    MarkupAlign.Right => TextAlignment.Right,
                    _ => cell.IsHeader ? TextAlignment.Center : TextAlignment.Left,
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
    // sp-mod.com's tab row: upper-case buttons 4px 12px inside, rounded 4px at the top, 4px apart,
    // slate on slate; the chosen one bold cyan on a lighter slate with a 2px cyan line under it.
    // The chosen tab's content sits 4px below in a gray-900 box, 16px inside, rounded 4/4/16/16.
    //
    private Block TabSet(MarkupTabSet set)
    {
        var strip = new WrapPanel();
        var content = new RichTextBox();
        HtmlText.PrepareHost(content, _base);

        var box = new Border
        {
            Background = TabContentBackground,
            CornerRadius = new CornerRadius(4, 4, 16, 16),
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
                Content = set.Tabs[i].Title.ToUpperInvariant(),
                Style = TabStyle(),
                Margin = new Thickness(0, 4, 4, 0),
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
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4, 4, 0, 0));
        border.SetValue(Border.PaddingProperty, new Thickness(12, 4, 12, 4));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
        border.SetValue(Border.BackgroundProperty, TabBackground);
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        border.AppendChild(text);
        template.VisualTree = border;

        var chosen = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        chosen.Setters.Add(new Setter(Border.BackgroundProperty, TabActiveBackground, "Chrome"));
        chosen.Setters.Add(new Setter(Border.BorderBrushProperty, Cyan, "Chrome"));
        template.Triggers.Add(chosen);

        var style = new Style(typeof(ToggleButton));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.FontSizeProperty, _base));
        style.Setters.Add(new Setter(Control.ForegroundProperty, White));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));

        var styleChosen = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        styleChosen.Setters.Add(new Setter(Control.ForegroundProperty, TabActiveText));
        styleChosen.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
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
                    // 12.8px gray-300 on gray-700. The site's 2px 6px padding and rounded corners
                    // need a box that would stop long paths from wrapping, so the shade is kept alone.
                    into.Add(new Run(code.Text)
                    {
                        FontFamily = Monospace,
                        FontSize = Px(12.8),
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
                built.FontSize = Px(12);
                break;
            case MarkupStyle.Subscript:
                built.BaselineAlignment = BaselineAlignment.Subscript;
                built.FontSize = Px(12);
                break;
        }

        AddInlines(built.Inlines, span.Children, span.Style == MarkupStyle.Link ? span.Href : outerHref);
        return built;
    }

    // White and underlined, as sp-mod.com's external links are.
    private static Hyperlink Link(string href)
    {
        var link = new Hyperlink
        {
            Foreground = White,
            TextDecorations = TextDecorations.Underline,
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
                TextDecorations = TextDecorations.Underline,
                Foreground = White,
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
