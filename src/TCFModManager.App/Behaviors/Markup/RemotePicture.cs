using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TCFModManager.Core.Markup;
using TCFModManager.Core.Services;

namespace TCFModManager.App.Behaviors.Markup;

//
// A picture from a description, loaded off the network: animated GIFs play (XamlAnimatedGif draws
// them a frame at a time), everything else WPF can read is decoded once at no more than the size
// it can be shown at. Shown at its own size, the way sp-mod.com shows it, and scaled down to fit
// when that is wider than the space it is in - never scaled up.
//
public sealed class RemotePicture : Image
{
    // The widest a description picture is decoded at, in pixels. Wider than any column it can sit in
    // on a 4K screen at 150%, and it keeps a 4000px screenshot from holding 60 MB of pixels.
    private const int MaxDecodeWidth = 1600;

    // Same limit and the same Referer rule as the thumbnails: files.sp-mod.com refuses requests
    // that don't come from sp-mod.com, and the image hosts descriptions link to don't mind one.
    private static readonly SemaphoreSlim Gate = new(4, 4);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"TCFModManager/{AppVersion.Current}");
        http.DefaultRequestHeaders.Referrer = new Uri("https://sp-mod.com/");
        return http;
    }

    public RemotePicture()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        // Until the picture arrives it takes no room, rather than a guessed box the text jumps past.
    }

    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
        nameof(Url), typeof(string), typeof(RemotePicture), new PropertyMetadata(null, OnUrlChanged));

    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    /// <summary>False for a picture that fills a box of its own (a video's thumbnail) rather than
    /// being shown at its own size.</summary>
    public bool LimitToNaturalSize
    {
        get => _limitToNaturalSize;
        set
        {
            _limitToNaturalSize = value;
            ApplyNaturalWidth();
        }
    }

    private bool _limitToNaturalSize = true;

    // The shown picture's own width, once it is known.
    private double? _naturalWidth;

    private void ApplyNaturalWidth()
    {
        // Only ever set, as before: a MaxWidth the page gave the picture itself is left alone.
        if (_limitToNaturalSize && _naturalWidth is { } width) MaxWidth = width;
    }

    /// <summary>Raised when the picture could not be fetched or read, so the host can show its
    /// alternative text instead.</summary>
    public event EventHandler? Failed;

    private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picture = (RemotePicture)d;
        GifPlayback.Stop(picture);
        picture.Source = null;

        if (e.NewValue is string url && !string.IsNullOrWhiteSpace(url)) _ = picture.LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        // Seen before this session: shown at once, without fetching or decoding it again.
        if (PictureCache.Find(url) is { } cached)
        {
            Show(cached);
            return;
        }

        // A download that fails for a reason that may pass (the network, a timeout, a busy server)
        // is tried twice more, 2 and then 6 seconds later; the slot is given up while it waits.
        // A picture no longer wanted - the page moved on - is not fetched at all.
        byte[]? bytes = null;
        for (var attempt = 0; bytes is null; attempt++)
        {
            await Gate.WaitAsync();
            try
            {
                if (Url != url) return;
                bytes = await Http.GetByteArrayAsync(url);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is { } status && (int)status is >= 400 and < 500 && (int)status is not (408 or 429))
            {
                AppLog.Debug("Markup", $"picture {url} refused ({(int)status})");
                if (Url == url) Failed?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException && attempt < 2)
            {
                AppLog.Debug("Markup", $"picture {url} failed, trying again: {ex.Message}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UriFormatException or InvalidOperationException)
            {
                AppLog.Info("Markup", $"picture {url} did not load: {ex.Message}");
                if (Url == url) Failed?.Invoke(this, EventArgs.Empty);
                return;
            }
            finally
            {
                Gate.Release();
            }

            if (bytes is null) await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 2 : 6));
        }

        if (Url != url) return;

        if (IsGif(bytes) && GifSize(bytes) is { } gifSize)
        {
            // A GIF of one frame is still handed over this way: the library shows it as a still.
            var gif = new CachedPicture(null, bytes, gifSize.Width, bytes.Length);
            PictureCache.Add(url, gif);
            Show(gif);
            return;
        }

        var decoded = await DecodeThread.Run(() => Decode(bytes));

        if (decoded is null)
        {
            if (Url != url) return;
            AppLog.Debug("Markup", $"picture {url} could not be read");
            Failed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var still = new CachedPicture(
            decoded.Value.Bitmap,
            null,
            decoded.Value.NaturalWidth,
            (long)decoded.Value.Bitmap.PixelHeight * ((decoded.Value.Bitmap.PixelWidth * decoded.Value.Bitmap.Format.BitsPerPixel + 7) / 8));
        PictureCache.Add(url, still);

        if (Url != url) return;
        Show(still);
    }

    private void Show(CachedPicture picture)
    {
        _naturalWidth = picture.NaturalWidth;
        ApplyNaturalWidth();

        if (picture.Gif is { } gif)
        {
            // Played while any of it is on screen - see GifPlayback.
            GifPlayback.Play(this, gif);
        }
        else
        {
            Source = picture.Bitmap;
        }
    }

    private static bool IsGif(byte[] bytes) =>
        bytes.Length > 10 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8';

    // The logical screen size in the GIF header - bytes 6 to 9, little-endian.
    private static Size? GifSize(byte[] bytes)
    {
        var width = bytes[6] | (bytes[7] << 8);
        var height = bytes[8] | (bytes[9] << 8);
        return width > 0 && height > 0 ? new Size(width, height) : null;
    }

    // The picture's own width in pixels is kept: shown in device-independent units at that
    // number, the way a browser shows an <img> in CSS pixels. Read from the file's header rather
    // than a decoder built for the purpose - see ImageHeader.
    private static (BitmapSource Bitmap, double NaturalWidth)? Decode(byte[] bytes)
    {
        if (!ImageSupport.CanDecode(bytes)) return null;

        try
        {
            var natural = ImageHeader.Width(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (natural is null or > MaxDecodeWidth) bitmap.DecodePixelWidth = MaxDecodeWidth;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();

            // A format the header reader does not know is shown at the width it decoded to.
            return (bitmap, natural ?? bitmap.PixelWidth);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException or OverflowException or FileFormatException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}

// A picture as it is shown: a decoded (frozen) still, or a GIF's file for the animator to play.
internal sealed record CachedPicture(BitmapSource? Bitmap, byte[]? Gif, double NaturalWidth, long Cost);

//
// The pictures shown this session, most recently used kept, up to a budget: an item page opened
// again - from Browse, a collection, Quick View's See More, the picture viewer - shows its pictures
// at once instead of fetching and decoding every one again, and its description is laid out once
// rather than again as each arrives. Only pictures that loaded are kept; one that failed is tried
// again next time.
//
internal static class PictureCache
{
    // About twenty full-width screenshots' worth of decoded pixels.
    private const long Budget = 96L * 1024 * 1024;

    private static readonly object Lock = new();
    private static readonly Dictionary<string, LinkedListNode<(string Url, CachedPicture Picture)>> ByUrl = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Url, CachedPicture Picture)> ByUse = new();
    private static long _held;

    public static CachedPicture? Find(string url)
    {
        lock (Lock)
        {
            if (!ByUrl.TryGetValue(url, out var node)) return null;

            ByUse.Remove(node);
            ByUse.AddFirst(node);
            return node.Value.Picture;
        }
    }

    public static void Add(string url, CachedPicture picture)
    {
        // One picture bigger than the whole budget is shown but not kept.
        if (picture.Cost > Budget) return;

        lock (Lock)
        {
            if (ByUrl.TryGetValue(url, out var old))
            {
                ByUse.Remove(old);
                _held -= old.Value.Picture.Cost;
            }

            var node = ByUse.AddFirst((url, picture));
            ByUrl[url] = node;
            _held += picture.Cost;

            while (_held > Budget && ByUse.Last is { } last)
            {
                ByUse.RemoveLast();
                ByUrl.Remove(last.Value.Url);
                _held -= last.Value.Picture.Cost;
            }
        }
    }
}
