using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TCFModManager.Core.Markup;
using TCFModManager.Core.Services;
using XamlAnimatedGif;

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
    public bool LimitToNaturalSize { get; set; } = true;

    /// <summary>Raised when the picture could not be fetched or read, so the host can show its
    /// alternative text instead.</summary>
    public event EventHandler? Failed;

    private static void OnUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picture = (RemotePicture)d;
        picture.Source = null;
        AnimationBehavior.SetSourceStream(picture, null);

        if (e.NewValue is string url && !string.IsNullOrWhiteSpace(url)) _ = picture.LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        byte[] bytes;
        await Gate.WaitAsync();
        try
        {
            bytes = await Http.GetByteArrayAsync(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            AppLog.Debug("Markup", $"picture {url} failed: {ex.Message}");
            if (Url == url) Failed?.Invoke(this, EventArgs.Empty);
            return;
        }
        finally
        {
            Gate.Release();
        }

        if (Url != url) return;

        if (IsGif(bytes) && GifSize(bytes) is { } gifSize)
        {
            // A GIF of one frame is still handed over this way: the library shows it as a still.
            if (LimitToNaturalSize) MaxWidth = gifSize.Width;
            AnimationBehavior.SetSourceStream(this, new MemoryStream(bytes));
            return;
        }

        var decoded = await DecodeThread.Run(() => Decode(bytes));
        if (Url != url) return;

        if (decoded is null)
        {
            AppLog.Debug("Markup", $"picture {url} could not be read");
            Failed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (LimitToNaturalSize) MaxWidth = decoded.Value.NaturalWidth;
        Source = decoded.Value.Bitmap;
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
