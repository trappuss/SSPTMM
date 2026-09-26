using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Services;

//
// Measurements for the log, on request only: started with the environment variable TCFMM_PERF=1
// (steam-ui-measure.bat sets it), off otherwise and costing nothing.
//
// For every second in which something scrolled it logs how many frames were drawn and the longest
// gap between two of them - "[Perf] scrolling: fps 58.9, worst 31ms". Seconds with no scrolling are
// left out: while this listens for frames WPF draws every frame whether or not anything changed, so
// an idle second would only report the screen's refresh rate. Descriptions log how long they took
// to read, lay out and first draw, and each page change is logged so the numbers can be placed.
//
// The build machine can only run the app under a software renderer whose own ceiling hides what
// these would show on a real PC, so they are how a change meant to make scrolling smoother is
// checked on one.
//
public static class PerfProbe
{
    public static bool IsOn { get; } = Environment.GetEnvironmentVariable("TCFMM_PERF") == "1";

    /// <summary>Logs the item page opening or closing, when measuring, so the numbers can be placed.</summary>
    public static void ItemPage(bool opened)
    {
        if (IsOn) AppLog.Info("Perf", opened ? "item page opened" : "item page closed");
    }

    /// <summary>When measuring, hands <paramref name="log"/> how long something took, in ms: to here
    /// (made), and to the end of the layout and drawing that followed (on screen).</summary>
    public static void Timed(Stopwatch timer, System.Windows.Threading.Dispatcher dispatcher, Action<double, double> log)
    {
        if (!IsOn) return;

        var made = timer.Elapsed.TotalMilliseconds;
        dispatcher.BeginInvoke(() => log(made, timer.Elapsed.TotalMilliseconds), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Whether anything scrolled since the last line.
    private static bool _scrolled;

    public static void Start()
    {
        if (!IsOn) return;

        // Tier 2 is drawn by the graphics card, 0 by the processor alone.
        AppLog.Info("Perf", $"measuring - render tier {RenderCapability.Tier >> 16}");

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, e) =>
            {
                if (e.VerticalChange != 0 || e.HorizontalChange != 0) _scrolled = true;
            }));

        AppNavigation.Navigated += (_, page) => AppLog.Info("Perf", $"page {page.Name}");

        // Anything that held the UI thread for 50ms or more, by what it was - so a stall in the
        // numbers above can be put down to its cause rather than guessed at. (A stall with no line
        // here was spent drawing, not in the app's own work.)
        var started = new Dictionary<System.Windows.Threading.DispatcherOperation, long>();
        var hooks = System.Windows.Threading.Dispatcher.CurrentDispatcher.Hooks;
        var methodField = typeof(System.Windows.Threading.DispatcherOperation).GetField(
            "_method", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        hooks.OperationStarted += (_, e) => started[e.Operation] = Stopwatch.GetTimestamp();
        hooks.OperationAborted += (_, e) => started.Remove(e.Operation);
        hooks.OperationCompleted += (_, e) =>
        {
            if (!started.Remove(e.Operation, out var began)) return;

            var ms = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
            if (ms < 50) return;

            var method = (methodField?.GetValue(e.Operation) as Delegate)?.Method;
            AppLog.Info("Perf", FormattableString.Invariant($"busy {ms:F0}ms: {method?.DeclaringType?.FullName}.{method?.Name} ({e.Operation.Priority})"));
        };

        var frames = 0;
        var worst = 0.0;
        var last = TimeSpan.Zero;
        var window = Stopwatch.StartNew();
        var previous = Stopwatch.GetTimestamp();

        CompositionTarget.Rendering += (_, e) =>
        {
            // Rendering can be raised more than once for the same frame.
            var time = ((RenderingEventArgs)e).RenderingTime;
            if (time == last) return;
            last = time;

            var now = Stopwatch.GetTimestamp();
            var gap = (now - previous) * 1000.0 / Stopwatch.Frequency;
            previous = now;

            frames++;
            worst = Math.Max(worst, gap);

            if (window.ElapsedMilliseconds < 1000) return;

            if (_scrolled)
            {
                // Invariant, so steam-ui-measure.bat can read the numbers whatever the language.
                AppLog.Info("Perf", FormattableString.Invariant($"scrolling: fps {frames * 1000.0 / window.ElapsedMilliseconds:F1}, worst {worst:F0}ms"));
            }

            _scrolled = false;
            frames = 0;
            worst = 0;
            window.Restart();
        };
    }
}
