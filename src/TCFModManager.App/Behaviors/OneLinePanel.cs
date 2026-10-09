using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TCFModManager.App.Behaviors;

//
// Fork (round 49): a row of tags that stays one row. What does not fit is left out and counted in
// a "+2" drawn at the end, with every tag's text in the tooltip.
//
// For the collections a subscribed mod is in. They wrapped onto a second line whenever a card was
// too narrow for them, which made that card taller than the ones beside it and the grid ragged; in
// one line every closed card is the same height.
//
public sealed class OneLinePanel : Panel
{
    private const double CounterGap = 4;
    private const double CounterPadding = 6;
    private const double CounterFontSize = 11;

    private static readonly Brush CounterFill = Frozen(Color.FromArgb(0x33, 0x67, 0xC1, 0xF5));
    private static readonly Brush CounterText = Frozen(Color.FromRgb(0x67, 0xC1, 0xF5));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // How many children the last arrange left out, and where their count is drawn.
    private int _hidden;
    private Rect _counter;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        var width = 0.0;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        var shown = Fit(finalSize.Width);

        var x = 0.0;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            if (i < shown)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, child.DesiredSize.Height));
                x += child.DesiredSize.Width;
            }
            else
            {
                // Nowhere: left in the tree (it is an item's container) but given no room.
                child.Arrange(new Rect(0, 0, 0, 0));
            }
        }

        var hidden = children.Count - shown;
        var counter = hidden > 0
            ? new Rect(x, CounterTop(finalSize.Height), CounterWidth(hidden), CounterHeight())
            : Rect.Empty;

        if (hidden != _hidden || counter != _counter)
        {
            _hidden = hidden;
            _counter = counter;
            InvalidateVisual();

            // What the hidden ones say, since they cannot be pointed at.
            ToolTip = hidden > 0
                ? string.Join(Environment.NewLine, children.Cast<UIElement>().Select(TextOf).Where(t => t.Length > 0))
                : null;
        }

        return finalSize;
    }

    // How many children fit, leaving room for the counter when any do not.
    private int Fit(double width)
    {
        var children = InternalChildren;

        var total = 0.0;
        foreach (UIElement child in children) total += child.DesiredSize.Width;
        if (total <= width + 0.5) return children.Count;

        var used = 0.0;
        for (var i = 0; i < children.Count; i++)
        {
            var next = used + children[i].DesiredSize.Width;
            if (next + CounterWidth(children.Count - i - 1) > width + 0.5) return i;
            used = next;
        }

        return children.Count;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (_hidden <= 0 || _counter.IsEmpty) return;

        drawing.DrawRoundedRectangle(CounterFill, null, _counter, 2, 2);

        var text = Text(_hidden);
        drawing.DrawText(text, new Point(
            _counter.X + CounterPadding,
            _counter.Y + (_counter.Height - text.Height) / 2));
    }

    private FormattedText Text(int hidden) => new(
        "+" + hidden.ToString(CultureInfo.CurrentCulture),
        CultureInfo.CurrentUICulture,
        FlowDirection,
        new Typeface(System.Windows.Documents.TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        CounterFontSize,
        CounterText,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private double CounterWidth(int hidden) => hidden <= 0 ? 0 : Text(hidden).Width + CounterPadding * 2 + CounterGap;

    private double CounterHeight() => Text(1).Height + 2;

    // In line with the tags, which carry a 3px margin above themselves.
    private double CounterTop(double height) => Math.Max(0, height - CounterHeight());

    private static string TextOf(UIElement element) =>
        element is FrameworkElement { DataContext: string text } ? text : string.Empty;
}
