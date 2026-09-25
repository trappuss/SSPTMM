using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TCFModManager.App.Behaviors.Markup;
using TCFModManager.Core.Markup;

namespace TCFModManager.App.Behaviors;

//
// Attached property that renders sp-mod.com HTML (a description or a changelog) into a
// RichTextBox, in the site's markdown style - see Markup/MarkupRenderer.cs for what it lays out and
// Core/Markup/SpModMarkup.cs for what it reads.
//
public static class HtmlText
{
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.RegisterAttached(
        "Html", typeof(string), typeof(HtmlText), new PropertyMetadata(null, OnHtmlChanged));

    public static void SetHtml(DependencyObject element, string? value) => element.SetValue(HtmlProperty, value);

    public static string? GetHtml(DependencyObject element) => (string?)element.GetValue(HtmlProperty);

    private static void OnHtmlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox richTextBox) return;

        ForwardWheel(richTextBox);

        var timer = Services.PerfProbe.IsOn ? System.Diagnostics.Stopwatch.StartNew() : null;

        var document = MarkupRenderer.Render(SpModMarkup.Parse(e.NewValue as string), richTextBox.FontSize);
        Show(richTextBox, document);

        if (timer is not null && e.NewValue is string { Length: > 0 } markup) LogTimings(richTextBox, document, timer, markup.Length);
    }

    //
    // A long document is laid out a part at a time instead of all at once. Laid out whole, a
    // 12,000-character description with no tabs held the window still for 1.1 seconds on the build
    // machine as its item page opened. Now the first part (a screen or two) is laid out with the
    // page - 0.77 seconds there - and the rest follows in parts of about the same size, each after
    // the window has drawn and answered input, so the page shows and scrolls while the end is still
    // going in. It is the same document the whole time, its blocks only held back and added
    // in their order, so once it is all in, what shows is exactly what laying it out at once showed:
    // the same text, pictures and spacing, and one selection across all of it. Tabs in a
    // description are documents of their own and go in the same way.
    //
    // A part is measured in characters of text, a picture or other control counting as a few
    // lines' worth. A list or section longer than a part is itself handed in a part at a time -
    // its items or blocks added to it in order - since a single list can be most of a tab. Adding
    // to the end of a document lays out only what was added.
    //
    private const int PartSize = 3000;

    private const int ControlWeight = 400;

    // What is still to go into a document, in order: each piece adds one block or list item to
    // where it was taken from.
    private sealed record Piece(int Weight, Action Add);

    // The pieces of a document still to go in; null once it is whole.
    private static readonly DependencyProperty PendingProperty = DependencyProperty.RegisterAttached(
        "Pending", typeof(List<Piece>), typeof(HtmlText), new PropertyMetadata(null));

    // True while a part of this document is waiting to go in.
    private static readonly DependencyProperty HandingInProperty = DependencyProperty.RegisterAttached(
        "HandingIn", typeof(bool), typeof(HtmlText), new PropertyMetadata(false));

    // True once a document has been cut into parts (or found not to need it).
    private static readonly DependencyProperty SplitProperty = DependencyProperty.RegisterAttached(
        "Split", typeof(bool), typeof(HtmlText), new PropertyMetadata(false));

    /// <summary>Shows a document in a box: its first part now, the rest a part at a time while it
    /// is the box's document. A document shown again (a tab chosen again) carries on where it
    /// stopped.</summary>
    internal static void Show(RichTextBox host, FlowDocument document)
    {
        if (!(bool)document.GetValue(SplitProperty))
        {
            document.SetValue(SplitProperty, true);

            var pending = new List<Piece>();
            var size = 0;
            try
            {
                HoldBack(document.Blocks, pending, ref size);
            }
            catch (Exception ex)
            {
                // Whatever went wrong, nothing is lost: everything taken out goes straight back.
                Core.Services.AppLog.Warn("Markup", $"could not cut a document into parts, showing it whole: {ex.Message}");
                foreach (var piece in pending) piece.Add();
                pending.Clear();
            }

            if (pending.Count > 0) document.SetValue(PendingProperty, pending);
        }

        Attach(host, document);
        HandInLater(host, document);
    }

    // Leaves what fits in the first part where it is and takes the rest out into pieces, in order.
    private static void HoldBack(BlockCollection blocks, List<Piece> pending, ref int size)
    {
        foreach (var block in blocks.ToList())
        {
            if (size >= PartSize)
            {
                var held = Weight(block);
                blocks.Remove(block);
                pending.Add(new Piece(held, () => blocks.Add(block)));
                continue;
            }

            var weight = Weight(block);
            if (size + weight <= 2 * PartSize)
            {
                size += weight;
                continue;
            }

            // Too long to go in whole: its own contents a part at a time, if it has any.
            switch (block)
            {
                case List list:
                    foreach (var item in list.ListItems.ToList())
                    {
                        if (size >= PartSize)
                        {
                            var held = Weight(item);
                            list.ListItems.Remove(item);
                            pending.Add(new Piece(held, () => list.ListItems.Add(item)));
                        }
                        else
                        {
                            size += Weight(item);
                        }
                    }

                    break;

                case Section section:
                    HoldBack(section.Blocks, pending, ref size);
                    break;

                default:
                    size += weight;
                    break;
            }
        }
    }

    // Characters of text, a picture or other control counting as ControlWeight. Counted from the
    // elements themselves rather than through a TextRange, which cannot be taken over a list item
    // on its own.
    private static int Weight(DependencyObject element) => element switch
    {
        Run run => run.Text.Length,
        InlineUIContainer or BlockUIContainer => ControlWeight,
        LineBreak => 1,
        _ => LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().Sum(Weight),
    };

    private static void HandInLater(RichTextBox host, FlowDocument document)
    {
        if (document.GetValue(PendingProperty) is not List<Piece> || (bool)document.GetValue(HandingInProperty)) return;

        document.SetValue(HandingInProperty, true);
        host.Dispatcher.BeginInvoke(() => HandIn(host, document), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void HandIn(RichTextBox host, FlowDocument document)
    {
        document.SetValue(HandingInProperty, false);

        // The box shows something else now: the rest waits until this document is shown again.
        if (!ReferenceEquals(host.Document, document) || document.GetValue(PendingProperty) is not List<Piece> pending) return;

        var size = 0;
        var taken = 0;
        while (taken < pending.Count && size < PartSize)
        {
            size += pending[taken].Weight;
            pending[taken].Add();
            taken++;
        }

        pending.RemoveRange(0, taken);
        if (pending.Count == 0) document.ClearValue(PendingProperty);

        HandInLater(host, document);
    }

    // PerfProbe: how long a description took to read, render, lay out and first draw - its first
    // part, and all of it (not counting tabs, which go in when shown).
    private static void LogTimings(RichTextBox host, FlowDocument document, System.Diagnostics.Stopwatch timer, int length)
    {
        double? first = null;

        void Check()
        {
            first ??= timer.Elapsed.TotalMilliseconds;

            if (ReferenceEquals(host.Document, document) && document.GetValue(PendingProperty) is List<Piece>)
            {
                host.Dispatcher.BeginInvoke(Check, System.Windows.Threading.DispatcherPriority.ContextIdle);
                return;
            }

            Core.Services.AppLog.Info(
                "Perf",
                FormattableString.Invariant($"description {length} chars: first part on screen {first:F0}ms, all {timer.Elapsed.TotalMilliseconds:F0}ms, {host.ActualHeight:F0}px tall"));
        }

        // Loaded comes after the frame that drew the first part, before the next part goes in.
        host.Dispatcher.BeginInvoke(Check, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Sets a RichTextBox up the way the XAML hosts are: read-only, frameless, no scroll
    /// bars of its own, links clickable. Used for the boxes a tab set creates.</summary>
    internal static void PrepareHost(RichTextBox host, double fontSize)
    {
        host.Padding = new Thickness(0);
        host.Background = Brushes.Transparent;
        host.BorderThickness = new Thickness(0);
        host.FontSize = fontSize;
        host.Focusable = false;
        host.IsReadOnly = true;
        host.IsDocumentEnabled = true;
        host.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        host.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ForwardWheel(host);
    }

    internal static void Attach(RichTextBox host, FlowDocument document)
    {
        //
        // A FlowDocument does not take its text properties from the RichTextBox hosting it - it
        // applies its own defaults, which are a serif face and justified text. Left alone, every
        // changelog in the app rendered in Times New Roman with stretched word spacing, in the
        // middle of a Segoe UI dialog.
        //
        // Font comes from the host so the changelog matches whatever it is sitting in, rather than
        // being pinned to a font of its own here.
        //
        // The app's Steam face, set on the box itself: WPF UI's RichTextBox style pins its own font,
        // and that font had no bold, so every <strong> and heading came out regular.
        if (host.TryFindResource("SteamFont") is FontFamily steamFont) host.FontFamily = steamFont;

        document.FontFamily = host.FontFamily;
        document.FontSize = host.FontSize;
        document.TextAlignment = TextAlignment.Left;

        // SetResourceReference rather than copying the host's brush: this is the code equivalent of
        // a DynamicResource, so the text re-colours on a live theme switch instead of freezing at
        // whatever the theme was when the changelog was parsed.
        document.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");

        // A document is only ever in one box; a tab shown again is handed back to the same one.
        if (!ReferenceEquals(host.Document, document)) host.Document = document;
    }

    private static readonly DependencyProperty WheelForwardedProperty = DependencyProperty.RegisterAttached(
        "WheelForwarded", typeof(bool), typeof(HtmlText), new PropertyMetadata(false));

    //
    // A RichTextBox takes every wheel turn over it for its own scroll viewer, even with its scroll
    // bars off - so the page stopped scrolling whenever the pointer crossed a description. The turn
    // is passed on to whatever the box sits in instead.
    //
    private static void ForwardWheel(RichTextBox host)
    {
        if ((bool)host.GetValue(WheelForwardedProperty)) return;
        host.SetValue(WheelForwardedProperty, true);

        host.PreviewMouseWheel += (sender, e) =>
        {
            if (e.Handled || sender is not DependencyObject source) return;

            var parent = VisualTreeHelper.GetParent(source) as UIElement;
            if (parent is null) return;

            e.Handled = true;
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = source,
            });
        };
    }
}
