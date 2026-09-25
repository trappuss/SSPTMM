using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TCFModManager.App.Behaviors;

//
// Wheel scrolling that glides, the way a browser and the Steam client scroll, instead of WPF's
// three-line jumps.
//
// Registered once for every ScrollViewer in the app. A wheel turn goes to the innermost scroll
// viewer under the pointer that can still move that way - a list inside a page scrolls before the
// page does, and hands over once it reaches its end - and eases there over a quarter of a second.
// Turns that arrive while it is still moving add to where it is heading, so a fast spin covers
// ground rather than restarting each time.
//
// Viewers that scroll by item rather than by pixel (virtualised lists) keep WPF's own behaviour:
// their offsets count rows, and gliding between rows would only look like a stutter.
//
public static class SmoothScrolling
{
    // Pixels per wheel notch. WPF's own is three 16px lines; browsers move about 100. HUNCH: chosen
    // to feel like a browser, not measured from one; so is the glide time below.
    private const double NotchDistance = 100;

    private static readonly Duration GlideTime = new(TimeSpan.FromMilliseconds(250));

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    // Where a viewer is gliding to, so turns during a glide build on it.
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(double), typeof(SmoothScrolling), new PropertyMetadata(double.NaN));

    // Animated from the current offset to the target; each step scrolls the viewer.
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScrolling), new PropertyMetadata(0.0, OnOffsetChanged));

    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer viewer) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        // The preview arrives at the outermost viewer first. Leave it to a viewer nearer the
        // pointer if one of those can take it.
        if (InnerViewerCanScroll(viewer, e.OriginalSource as DependencyObject, e.Delta)) return;

        if (!CanScroll(viewer, e.Delta) || viewer.CanContentScroll) return;

        e.Handled = true;
        Glide(viewer, e.Delta);
    }

    /// <summary>Scrolls <paramref name="viewer"/> by one wheel turn of <paramref name="delta"/>, gliding.
    /// For the pages that route the wheel to a list of their own from anywhere on the page.</summary>
    public static void Glide(ScrollViewer viewer, int delta)
    {
        if (viewer.CanContentScroll)
        {
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset - delta);
            return;
        }

        var from = viewer.VerticalOffset;
        var heading = viewer.GetValue(TargetProperty) is double t && !double.IsNaN(t) ? t : from;
        var target = Math.Clamp(heading - delta / 120.0 * NotchDistance, 0, viewer.ScrollableHeight);

        viewer.SetValue(TargetProperty, target);

        var glide = new DoubleAnimation(from, target, GlideTime) { EasingFunction = Ease };
        glide.Completed += (_, _) =>
        {
            // Only the glide still running clears the target; a newer one has replaced it.
            if (viewer.GetValue(TargetProperty) is double current && current.Equals(target))
                viewer.SetValue(TargetProperty, double.NaN);
        };

        viewer.BeginAnimation(OffsetProperty, glide, HandoffBehavior.SnapshotAndReplace);

        HoldStill(viewer);
    }

    //
    // While a viewer glides, what it shows stops reacting to the pointer. The pointer stands still
    // while the page moves under it, so otherwise every card that passes beneath it would lift, open
    // its hover popup (a window of its own) and start its slideshow, one after another, for the
    // whole scroll. (HUNCH, not measured on Windows: that churn is a large part of the stutter.)
    // It comes back as the glide ends, under the pointer where the scroll left it; a click in the
    // glide's last quarter-second is not taken.
    //
    private static readonly TimeSpan HoverRestDelay = TimeSpan.FromMilliseconds(80);

    private static readonly DependencyProperty RestTimerProperty = DependencyProperty.RegisterAttached(
        "RestTimer", typeof(System.Windows.Threading.DispatcherTimer), typeof(SmoothScrolling));

    private static void HoldStill(ScrollViewer viewer)
    {
        if (viewer.Content is not UIElement content) return;

        if (viewer.GetValue(RestTimerProperty) is not System.Windows.Threading.DispatcherTimer timer)
        {
            timer = new System.Windows.Threading.DispatcherTimer { Interval = GlideTime.TimeSpan + HoverRestDelay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (viewer.Content is UIElement shown) shown.IsHitTestVisible = true;
                Mouse.Synchronize();
            };
            viewer.SetValue(RestTimerProperty, timer);

            // Something to hit while the content is not: the wheel must still reach this viewer.
            viewer.Background ??= Brushes.Transparent;
        }

        // Only content that was taking the pointer to begin with.
        if (!content.IsHitTestVisible && !timer.IsEnabled) return;

        content.IsHitTestVisible = false;
        timer.Stop();
        timer.Start();
    }

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer viewer) viewer.ScrollToVerticalOffset((double)e.NewValue);
    }

    private static bool CanScroll(ScrollViewer viewer, int delta) =>
        viewer.ScrollableHeight > 0 &&
        viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled &&
        (delta > 0 ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight);

    private static bool InnerViewerCanScroll(ScrollViewer outer, DependencyObject? source, int delta)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, outer); node = Parent(node))
        {
            if (node is ScrollViewer inner && CanScroll(inner, delta)) return true;
        }

        return false;
    }

    // Visual parent where there is one; content elements (a Run in a document) have only a logical one.
    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
}
