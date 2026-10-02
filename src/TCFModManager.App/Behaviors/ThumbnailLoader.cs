using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using TCFModManager.Core.Services;

namespace TCFModManager.App.Behaviors;

// 
// Attached behavior for loading remote thumbnail images onto an &lt;Image&gt; with bounded
// concurrency and an in-memory cache.
//
// Pictures sometimes never showed after a scroll. Two reasons, both in how downloads waited:
//  - They waited in the order they were asked for, and every card a scroll passed near asked. Six
//    at a time, each a full-size picture (about 380 KB on average), so after a long scroll the
//    cards on screen waited behind dozens that had already left the view - over three seconds on
//    the build machine's connection, far longer on a slower one.
//  - A download that failed once (a timeout, a busy server) was never tried again: that picture
//    stayed blank until the page was built again.
// Now a picture no longer wanted - its card scrolled away and let it go - is dropped before it is
// fetched; the most recent burst of requests goes first, in its own order, so what is on screen
// now comes before what was on screen a moment ago; one download serves every Image asking for the
// same picture; a download that fails for a reason that may pass is tried twice more, a few
// seconds apart; and a picture still missing when its Image is shown again is asked for again.
// 
public static class ThumbnailLoader
{
    // How many downloads run at once.
    private const int MaxRunning = 6;

    // Decode width when an Image does not set DecodeWidth - the small list thumbnails.
    private const int DefaultDecodeWidth = 128;

    // In-memory cache of decoded thumbnails, keyed by decode width and URL, least recently used
    // first out once it holds more than CacheBudget bytes of pixels. Only touched from the UI thread.
    //
    // It had no bound, and Browse's infinite list can reach every mod in the catalog: a 245px card
    // is about a quarter of a megabyte decoded (more on a scaled screen), so a long scroll kept
    // hundreds of megabytes for the rest of the session. The budget is several screens of cards
    // at any scale, so what is near the view is always a hit. HUNCH: the figure is a judgement,
    // not measured against real use.
    private const long CacheBudget = 160L * 1024 * 1024;

    private static readonly PictureCache Cache = new(CacheBudget);

    //
    // Animated GIFs, kept as their files: XamlAnimatedGif decodes a frame at a time as it plays, so
    // there is no one decoded picture to keep. No mod cover on sp-mod.com was one when this was
    // written (every thumbnail in the catalog checked, 2026-09-25), so this stays empty until one
    // is; a cover that is one plays wherever it is shown, while it is on screen - see GifPlayback.
    //
    private const long GifBudget = 32L * 1024 * 1024;

    private static readonly GifCache Gifs = new(GifBudget);

    //
    // The width the image is shown at, in device-independent pixels. Decoding at the shown size
    // (times the screen's scale) keeps a large preview sharp without holding every small one at
    // full resolution: a 245px Workshop card decoded at 128 was being stretched to twice its size.
    //
    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.RegisterAttached(
        "DecodeWidth", typeof(int), typeof(ThumbnailLoader), new PropertyMetadata(DefaultDecodeWidth));

    public static void SetDecodeWidth(DependencyObject element, int value) => element.SetValue(DecodeWidthProperty, value);

    public static int GetDecodeWidth(DependencyObject element) => (int)element.GetValue(DecodeWidthProperty);

    private static int PixelWidth(Image image)
    {
        var scale = 1.0;
        try
        {
            scale = VisualTreeHelper.GetDpi(image).DpiScaleX;
        }
        catch (InvalidOperationException)
        {
            // Not in a visual tree yet: decode at 1:1.
        }

        return (int)Math.Ceiling(GetDecodeWidth(image) * Math.Max(1.0, scale));
    }

    private static string KeyFor(string url, int width) => width + "|" + url;

    // files.sp-mod.com serves images only to requests carrying a Referer from sp-mod.com itself;
    // anything else gets a 403.
    private static readonly Uri Referrer = new("https://sp-mod.com/");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{SelfMod.ShortName}/{AppVersion.Current}");
        http.DefaultRequestHeaders.Referrer = Referrer;
        return http;
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(ThumbnailLoader), new PropertyMetadata(null, OnSourceChanged));

    public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

    public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;

        var url = e.NewValue as string;
        GifPlayback.Stop(image);
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;

        WatchLoaded(image);
        Show(image, url);
    }

    // From the cache, or queued.
    private static void Show(Image image, string url)
    {
        if (Gifs.TryGetValue(url, out var gif))
        {
            GifPlayback.Play(image, gif, RestoreStill);
            return;
        }

        var width = PixelWidth(image);
        if (Cache.TryGetValue(KeyFor(url, width), out var cached))
        {
            image.Source = cached;
            return;
        }

        AppLog.Debug("Thumbnails", $"ThumbnailLoader: queuing {url}");
        _ = Enqueue(image, url, width);
    }

    // What an Image that has played a GIF shows again once it is not playing - see GifPlayback.Play.
    private static void RestoreStill(Image image)
    {
        if (GetSource(image) is { Length: > 0 } url) Show(image, url);
    }

    private static readonly DependencyProperty WatchedProperty = DependencyProperty.RegisterAttached(
        "Watched", typeof(bool), typeof(ThumbnailLoader), new PropertyMetadata(false));

    // A picture still missing when its Image is shown again - a page gone back to, a card
    // regenerated - is asked for again, rather than left blank for the rest of the session.
    private static void WatchLoaded(Image image)
    {
        if ((bool)image.GetValue(WatchedProperty)) return;
        image.SetValue(WatchedProperty, true);

        image.Loaded += (_, _) =>
        {
            if (image.Source is null && !GifPlayback.IsPlaying(image)
                && GetSource(image) is { Length: > 0 } url && !IsQueued(KeyFor(url, PixelWidth(image))))
                Show(image, url);
        };
    }

    /// <summary>Loads <paramref name="url"/> into the cache at <paramref name="width"/> device-independent
    /// pixels (times the main window's scale), without an Image to show it in - so it is there, already
    /// decoded, when one asks. True once it is; false when it cannot be fetched or decoded here.</summary>
    public static Task<bool> PrefetchAsync(string url, int width)
    {
        var scale = 1.0;
        if (Application.Current?.MainWindow is { } window)
        {
            try
            {
                scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
            }
            catch (InvalidOperationException)
            {
                // Not shown yet: 1:1.
            }
        }

        var pixels = (int)Math.Ceiling(width * Math.Max(1.0, scale));
        return Cache.TryGetValue(KeyFor(url, pixels), out _) || Gifs.TryGetValue(url, out _)
            ? Task.FromResult(true)
            : Enqueue(null, url, pixels);
    }

    // ------------------------------------------------------------------ the queue
    //
    // All of it runs on the UI thread: requests arrive from bindings there, and every download's
    // continuation comes back to it.
    //

    // One picture at one width, and everything waiting for it.
    private sealed class Request(string url, int width, string key)
    {
        public string Url { get; } = url;
        public int Width { get; } = width;
        public string Key { get; } = key;

        // Images to show it in - each only while it still asks for this picture.
        public List<WeakReference<Image>> Images { get; } = [];

        // Prefetches (the hover slideshow), told whether it loaded.
        public List<TaskCompletionSource<bool>> Waiters { get; } = [];

        // When it was last asked for, and in what order, for picking the next one.
        public long AskedAt { get; set; }
        public long Order { get; set; }

        public int Attempt { get; set; }

        // Stops its download once nothing wants it any more.
        public CancellationTokenSource Cancel { get; set; } = new();
    }

    // Not yet started, or waiting to be tried again.
    private static readonly List<Request> Pending = [];

    // Every request not finished yet, running or not, by key.
    private static readonly Dictionary<string, Request> Open = new(StringComparer.Ordinal);

    // Downloading now.
    private static readonly List<Request> Running = [];

    private static int _running;
    private static long _order;

    // Requests asked for within this long of the newest one are one burst - a page of cards, a
    // scroll's stop - and go in the order they were asked for.
    private const long BurstMs = 150;

    // A failed download is tried this many more times, this long apart and then three times that.
    private const int Retries = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private static bool IsQueued(string key) => Open.ContainsKey(key);

    private static Task<bool> Enqueue(Image? image, string url, int width)
    {
        var key = KeyFor(url, width);
        if (!Open.TryGetValue(key, out var request))
        {
            request = new Request(url, width, key);
            Open[key] = request;
            Pending.Add(request);
        }

        if (image is not null && !request.Images.Any(w => w.TryGetTarget(out var i) && ReferenceEquals(i, image)))
            request.Images.Add(new WeakReference<Image>(image));

        // Asked for again: to the front of the line.
        request.AskedAt = Environment.TickCount64;
        request.Order = ++_order;

        Task<bool> result = Task.FromResult(true);
        if (image is null)
        {
            var waiter = new TaskCompletionSource<bool>();
            request.Waiters.Add(waiter);
            result = waiter.Task;
        }

        Pump();
        return result;
    }

    // An Image still asking for this picture, or a prefetch still waiting on it.
    private static bool Wanted(Request request) =>
        request.Waiters.Count > 0 ||
        request.Images.Any(w => w.TryGetTarget(out var image) && GetSource(image) as string == request.Url);

    private static void Pump()
    {
        // A download whose card has scrolled away stops, and its slot goes to one that is wanted.
        foreach (var running in Running)
        {
            if (!running.Cancel.IsCancellationRequested && !Wanted(running)) running.Cancel.Cancel();
        }

        while (_running < MaxRunning && TakeNext() is { } request)
        {
            _running++;
            Running.Add(request);
            _ = RunAsync(request);
        }
    }

    // The earliest-asked of the newest burst; anything no longer wanted is dropped on the way.
    private static Request? TakeNext()
    {
        Pending.RemoveAll(r =>
        {
            if (Wanted(r)) return false;
            Open.Remove(r.Key);
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: dropped {r.Url}, no longer on its card");
            return true;
        });

        if (Pending.Count == 0) return null;

        // What is on screen now first, in the order it was asked for: a card grid keeps two
        // screens either side of the view asking too, and those can wait.
        var next = Pending.Where(OnScreen).MinBy(r => r.Order);
        if (next is null)
        {
            var newest = Pending.Max(r => r.AskedAt);
            next = Pending.Where(r => r.AskedAt >= newest - BurstMs).MinBy(r => r.Order)!;
        }

        Pending.Remove(next);
        return next;
    }

    // Any Image of it shown and inside its window.
    private static bool OnScreen(Request request)
    {
        foreach (var weak in request.Images)
        {
            if (!weak.TryGetTarget(out var image) || !image.IsVisible || GetSource(image) as string != request.Url) continue;

            // Not laid out yet: where it will be is not known, so it does not jump the line.
            if (!image.IsArrangeValid || image.RenderSize.Width <= 0 || image.RenderSize.Height <= 0) continue;
            if (Window.GetWindow(image) is not { } window) continue;

            try
            {
                var bounds = image.TransformToAncestor(window).TransformBounds(new Rect(image.RenderSize));
                if (bounds.IntersectsWith(new Rect(0, 0, window.ActualWidth, window.ActualHeight))) return true;
            }
            catch (InvalidOperationException)
            {
                // Not in that window's tree after all.
            }
        }

        return false;
    }

    private static async Task RunAsync(Request request)
    {
        Outcome outcome;
        try
        {
            outcome = await LoadAsync(request);
        }
        catch (Exception ex)
        {
            // Whatever it was, the slot comes back and the request is closed: one picture that
            // could not load must not stop the rest.
            AppLog.Warn("Thumbnails", $"picture {request.Url} could not be loaded: {ex.Message}");
            outcome = Outcome.Failed;
        }

        _running--;
        Running.Remove(request);

        if (outcome is Outcome.Dropped && Wanted(request))
        {
            // Stopped, then asked for again before it had stopped: back in line.
            request.Cancel = new CancellationTokenSource();
            request.AskedAt = Environment.TickCount64;
            request.Order = ++_order;
            Pending.Add(request);
            Pump();
            return;
        }

        if (outcome is Outcome.Dropped)
        {
            // Nothing wanted it any more. Asked for again later, it starts afresh.
            Open.Remove(request.Key);
            foreach (var waiter in request.Waiters) waiter.TrySetResult(false);
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: stopped {request.Url}, no longer on its card");
            Pump();
            return;
        }

        if (outcome is Outcome.Retry && request.Attempt < Retries && Wanted(request))
        {
            // Stays open, so an Image asking meanwhile joins this request rather than starting one.
            request.Attempt++;
            var delay = RetryDelay * Math.Pow(3, request.Attempt - 1);
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: trying {request.Url} again in {delay.TotalSeconds:F0}s");
            _ = RetryLaterAsync(request, delay);
        }
        else
        {
            Open.Remove(request.Key);
            foreach (var waiter in request.Waiters) waiter.TrySetResult(outcome is Outcome.Loaded);
            if (outcome is Outcome.Retry)
                AppLog.Info("Thumbnails", $"picture {request.Url} did not load after {request.Attempt + 1} tries");
        }

        Pump();
    }

    private static async Task RetryLaterAsync(Request request, TimeSpan delay)
    {
        await Task.Delay(delay);
        request.Cancel = new CancellationTokenSource();
        request.AskedAt = Environment.TickCount64;
        request.Order = ++_order;
        Pending.Add(request);
        Pump();
    }

    private enum Outcome
    {
        Loaded,

        // Might load another time: the network, a timeout, a busy or failing server.
        Retry,

        // Will not: not found, refused, or not a picture this machine can read.
        Failed,

        // Stopped part way: nothing wanted it any more.
        Dropped,
    }

    //
    // sp-mod.com keeps each newer mod picture (a 40-character name) at 192 and 384 pixels wide as
    // well, in WebP, and its own pages show those: 9 KB and 28 KB for a picture of 150 KB. When the
    // size an Image needs fits one of them, that one is fetched instead - far less to download, the
    // same picture. The full one is fetched when there is no smaller copy (older pictures, named by
    // number, have none), when the Image needs more pixels than 384, or when this machine cannot
    // read WebP - found out once, by decoding a one-pixel WebP picture kept here.
    //
    private static readonly Lazy<bool> WebpReadable = new(() =>
    {
        var probe = Convert.FromBase64String("UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==");
        var readable = Decode(probe, 0) is not null;
        AppLog.Info("Thumbnails", readable
            ? "WebP pictures can be read here: mod pictures come in sp-mod.com's smaller copies where they fit"
            : "WebP pictures cannot be read here: mod pictures are fetched full size");
        return readable;
    });

    private static readonly System.Text.RegularExpressions.Regex SizedCopyName = new(
        @"^https://files\.sp-mod\.com/mods/([A-Za-z0-9]{40})\.(png|jpe?g|webp|gif)$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string? SizedCopy(string url, int pixels)
    {
        // A GIF's smaller copies are single WebP stills: taking one would stop it playing.
        if (url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) return null;
        if (pixels <= 0 || pixels > 384 || !WebpReadable.Value) return null;
        if (SizedCopyName.Match(url) is not { Success: true } match) return null;

        var copyWidth = pixels <= 192 ? 192 : 384;
        return $"https://files.sp-mod.com/mods/{match.Groups[1].Value}_{copyWidth}w.webp";
    }

    // Whether a smaller copy has been shown, and whether one has had to give way to the full
    // picture, this session: each logged once at Info, so a normal log (not only a verbose one)
    // says whether the smaller copies work on this machine.
    private static bool _smallerShownLogged;
    private static bool _smallerFailedLogged;

    // error: why the download failed, or null when it arrived but could not be decoded.
    private static void SmallerFailed(string smaller, string? error)
    {
        if (_smallerFailedLogged) return;
        _smallerFailedLogged = true;
        AppLog.Info("Thumbnails", $"a smaller copy gave way to the full picture ({error ?? "not decoded"}), first time this session: {smaller}");
    }

    private static async Task<Outcome> LoadAsync(Request request)
    {
        var url = request.Url;
        var sw = Stopwatch.StartNew();

        if (SizedCopy(url, request.Width) is { } smaller)
        {
            try
            {
                var small = await Http.GetByteArrayAsync(smaller, request.Cancel.Token);
                var fromSmall = await DecodeThread.Run(() => Decode(small, request.Width));
                if (fromSmall is not null)
                {
                    AppLog.Debug("Thumbnails", $"ThumbnailLoader: loaded {small.Length} bytes after {sw.ElapsedMilliseconds}ms for {smaller}");
                    if (!_smallerShownLogged)
                    {
                        _smallerShownLogged = true;
                        AppLog.Info("Thumbnails", $"smaller copies in use - the first: {small.Length:N0} bytes for {smaller}");
                    }

                    return Shown(request, fromSmall);
                }

                // Fetched but unreadable: the full one below.
                AppLog.Debug("Thumbnails", $"ThumbnailLoader: {smaller} could not be decoded; fetching the full picture");
                SmallerFailed(smaller, null);
            }
            catch (OperationCanceledException) when (request.Cancel.IsCancellationRequested)
            {
                return Outcome.Dropped;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                // No smaller copy after all, or it failed: the full one below decides.
                AppLog.Debug("Thumbnails", $"ThumbnailLoader: {smaller} - {ex.Message}; fetching the full picture");
                SmallerFailed(smaller, ex.Message);
            }
        }

        byte[] bytes;
        try
        {
            bytes = await Http.GetByteArrayAsync(url, request.Cancel.Token);
        }
        catch (OperationCanceledException) when (request.Cancel.IsCancellationRequested)
        {
            return Outcome.Dropped;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } status && (int)status is >= 400 and < 500 && (int)status is not (408 or 429))
        {
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} refused ({(int)status})");
            return Outcome.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} failed after {sw.ElapsedMilliseconds}ms - {ex.Message}");
            return Outcome.Retry;
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} is not a picture address - {ex.Message}");
            return Outcome.Failed;
        }

        // An animation plays rather than showing its first frame.
        if (TCFModManager.Core.Markup.ImageHeader.IsAnimatedGif(bytes))
        {
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} is an animated GIF ({bytes.Length} bytes)");
            return ShownGif(request, bytes);
        }

        // Decoded off the UI thread: thirty cards arriving at once used to decode one after
        // another on it, which is the stutter a fast scroll through the grid ran into.
        var bitmap = await DecodeThread.Run(() => Decode(bytes, request.Width));
        if (bitmap is null)
        {
            AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} could not be decoded");
            return Outcome.Failed;
        }

        AppLog.Debug("Thumbnails", $"ThumbnailLoader: loaded {bytes.Length} bytes after {sw.ElapsedMilliseconds}ms for {url}");
        return Shown(request, bitmap);
    }

    // Kept, under the picture's own address, and shown in every Image still asking for it.
    private static Outcome Shown(Request request, BitmapSource bitmap)
    {
        Cache.Add(request.Key, bitmap);
        foreach (var weak in request.Images)
        {
            if (weak.TryGetTarget(out var image) && GetSource(image) as string == request.Url) image.Source = bitmap;
        }

        return Outcome.Loaded;
    }

    // Kept by its address, whatever width it was asked at - it plays at the size it is shown - and
    // played in every Image still asking for it.
    private static Outcome ShownGif(Request request, byte[] gif)
    {
        Gifs.Add(request.Url, gif);
        foreach (var weak in request.Images)
        {
            if (weak.TryGetTarget(out var image) && GetSource(image) as string == request.Url) GifPlayback.Play(image, gif, RestoreStill);
        }

        return Outcome.Loaded;
    }

    // Null when the bytes are not an image WPF can read.
    internal static BitmapSource? Decode(byte[] bytes, int width)
    {
        if (!ImageSupport.CanDecode(bytes)) return null;

        try
        {
            // Never decoded larger than the picture itself: that only spends memory on a blur.
            // (Read from the header - see ImageHeader - when it can be; otherwise at the width asked.)
            var natural = TCFModManager.Core.Markup.ImageHeader.Width(bytes) ?? int.MaxValue;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            if (width > 0 && width < natural) bitmap.DecodePixelWidth = width;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();

            // Frozen so it can cross back to the UI thread and be shared between Images.
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException or OverflowException or FileFormatException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // A byte-budgeted least-recently-used map. What an Image is showing stays alive through that
    // Image whatever happens here; eviction only means the next card to ask decodes it again.
    private sealed class PictureCache(long budget)
    {
        private readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Bitmap, long Bytes)>> _index = new();

        // Most recently used at the front.
        private readonly LinkedList<(string Key, BitmapSource Bitmap, long Bytes)> _order = new();

        private long _bytes;

        public bool TryGetValue(string key, out BitmapSource bitmap)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }

            bitmap = null!;
            return false;
        }

        public void Add(string key, BitmapSource bitmap)
        {
            if (_index.Remove(key, out var old))
            {
                _order.Remove(old);
                _bytes -= old.Value.Bytes;
            }

            var bytes = (long)bitmap.PixelWidth * bitmap.PixelHeight * Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            _index[key] = _order.AddFirst((key, bitmap, bytes));
            _bytes += bytes;

            // Always keeps the newest, even alone over budget.
            while (_bytes > budget && _order.Count > 1)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _index.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
            }
        }
    }

    // The animated GIFs' files, by address, least recently used first out past the budget. UI thread only.
    private sealed class GifCache(long budget)
    {
        private readonly Dictionary<string, LinkedListNode<(string Url, byte[] Gif)>> _index = new(StringComparer.Ordinal);
        private readonly LinkedList<(string Url, byte[] Gif)> _order = new();
        private long _bytes;

        public bool TryGetValue(string url, out byte[] gif)
        {
            if (_index.TryGetValue(url, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                gif = node.Value.Gif;
                return true;
            }

            gif = null!;
            return false;
        }

        public void Add(string url, byte[] gif)
        {
            if (_index.Remove(url, out var old))
            {
                _order.Remove(old);
                _bytes -= old.Value.Gif.Length;
            }

            _index[url] = _order.AddFirst((url, gif));
            _bytes += gif.Length;

            while (_bytes > budget && _order.Count > 1)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _index.Remove(last.Value.Url);
                _bytes -= last.Value.Gif.Length;
            }
        }
    }
}
