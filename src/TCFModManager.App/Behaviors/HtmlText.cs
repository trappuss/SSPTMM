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
        Attach(richTextBox, document);

        if (timer is not null && e.NewValue is string { Length: > 0 } markup) LogTimings(richTextBox, timer, markup.Length);
    }

    // PerfProbe: how long a description took to read and render, then to lay out and first draw.
    private static void LogTimings(RichTextBox host, System.Diagnostics.Stopwatch timer, int length)
    {
        var built = timer.Elapsed.TotalMilliseconds;
        host.Dispatcher.BeginInvoke(
            () => Core.Services.AppLog.Info(
                "Perf",
                FormattableString.Invariant($"description {length} chars: built {built:F0}ms, on screen {timer.Elapsed.TotalMilliseconds:F0}ms, {host.ActualHeight:F0}px tall")),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
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
