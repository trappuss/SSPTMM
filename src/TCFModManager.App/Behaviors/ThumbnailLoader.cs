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
public static class ThumbnailLoader
{
    // Caps concurrent thumbnail downloads.
    private static readonly SemaphoreSlim Gate = new(6, 6);

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
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"TCFModManager/{AppVersion.Current}");
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
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url)) return;

        var width = PixelWidth(image);
        if (Cache.TryGetValue(KeyFor(url, width), out var cached))
        {
            image.Source = cached;
            return;
        }

        AppLog.Debug("Thumbnails", $"ThumbnailLoader: queuing {url}");
        _ = LoadAsync(image, url, width);
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
        return Cache.TryGetValue(KeyFor(url, pixels), out _) ? Task.FromResult(true) : LoadAsync(null, url, pixels);
    }

    private static async Task<bool> LoadAsync(Image? image, string url, int width)
    {
        var key = KeyFor(url, width);
        var sw = Stopwatch.StartNew();
        await Gate.WaitAsync();
        AppLog.Debug("Thumbnails", $"ThumbnailLoader: gate acquired after {sw.ElapsedMilliseconds}ms for {url}");
        try
        {
            // Re-check the cache in case another card loaded this URL while waiting on the gate.
            if (Cache.TryGetValue(key, out var cached))
            {
                if (image is not null && GetSource(image) as string == url) image.Source = cached;
                return true;
            }

            byte[] bytes;
            try
            {
                bytes = await Http.GetByteArrayAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
            {
                AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} failed after {sw.ElapsedMilliseconds}ms - {ex.Message}");
                return false;
            }

            // Decoded off the UI thread: thirty cards arriving at once used to decode one after
            // another on it, which is the stutter a fast scroll through the grid ran into.
            var bitmap = await DecodeThread.Run(() => Decode(bytes, width));
            if (bitmap is null)
            {
                AppLog.Debug("Thumbnails", $"ThumbnailLoader: {url} could not be decoded");
                return false;
            }

            AppLog.Debug("Thumbnails", $"ThumbnailLoader: loaded {bytes.Length} bytes after {sw.ElapsedMilliseconds}ms total for {url}");

            Cache.Add(key, bitmap);
            if (image is not null && GetSource(image) as string == url) image.Source = bitmap;
            return true;
        }
        finally
        {
            Gate.Release();
        }
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
}
