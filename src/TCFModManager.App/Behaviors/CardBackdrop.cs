using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using TCFModManager.Core.Services;

namespace TCFModManager.App.Behaviors;

//
// Fork (round 47): the light behind a subscribed mod's card - Steam's .backgroundImg, the item's own
// picture across the row, blur(30px), opacity 0.3, masked left to right from black (at -25%) to
// nothing (workshop_userfiles.css, read 2026-10-08).
//
// Round 46 drew that live: an Image under a BlurEffect, an opacity and an opacity mask, in every
// card, with a DropShadowEffect behind each. Scrolling a page of 132 of them became very slow -
// every one of those is drawn off to one side and put back, and a card inside a faded (disabled)
// card had it all done again on every frame.
//
// So the picture is made once here instead: shrunk to 64 pixels, blurred, faded and dimmed in plain
// arithmetic on a background thread, and kept as a frozen brush that any number of cards paint
// with. Nothing in the list is an effect any more, and what a card costs to draw is one more
// rectangle. One brush is 16 KB.
//
public static class CardBackdrop
{
    /// <summary>Options' "Picture behind each subscribed mod". Off: cards are the plain panel.
    /// Read when a card is made, so it takes hold the next time the page fills.</summary>
    public static bool Enabled { get; set; } = true;

    // The side of the picture kept, in pixels. Stretched over a card it is only ever a soft light.
    private const int Side = 64;

    // Steam blurs by a deviation of 30px on a row about 650px wide; the same share of 64px is 3px.
    // Three passes of a box 7 wide come to a deviation of 3.5.
    private const int BoxRadius = 3;

    private const double Opacity = 0.3;

    // By address. UI thread only. Null is kept too: a picture that would not load is not asked for
    // again by every card that shows it.
    private static readonly Dictionary<string, ImageBrush?> Made = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, Task<ImageBrush?>> Making = new(StringComparer.Ordinal);

    // Past this many the lot is dropped and made again as asked for: 16 KB each, so 8 MB.
    private const int MadeLimit = 512;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(CardBackdrop), new PropertyMetadata(null, OnSourceChanged));

    public static void SetSource(DependencyObject element, string? value) => element.SetValue(SourceProperty, value);

    public static string? GetSource(DependencyObject element) => (string?)element.GetValue(SourceProperty);

    private static async void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;

        border.Background = null;

        var url = e.NewValue as string;
        if (!Enabled || string.IsNullOrWhiteSpace(url)) return;

        if (Made.TryGetValue(url, out var made))
        {
            border.Background = made;
            return;
        }

        try
        {
            if (!Making.TryGetValue(url, out var making))
            {
                making = MakeAsync(url);
                Making[url] = making;
            }

            var brush = await making;

            // Still the picture this card asks for: cards are reused for other mods.
            if (GetSource(border) == url) border.Background = brush;
        }
        catch (Exception ex)
        {
            // A card without its light is a card; nothing here is worth more than a line.
            AppLog.Debug("Thumbnails", $"CardBackdrop: {url} - {ex.Message}");
        }
    }

    private static async Task<ImageBrush?> MakeAsync(string url)
    {
        ImageBrush? brush = null;

        try
        {
            // The card's own thumbnail asks for the same picture at the same width, so this is
            // that download and that decode, not another.
            var picture = await ThumbnailLoader.GetAsync(url, 128);
            if (picture is not null) brush = await Task.Run(() => Make(picture));
        }
        finally
        {
            Making.Remove(url);

            if (Made.Count >= MadeLimit) Made.Clear();
            Made[url] = brush;
        }

        return brush;
    }

    // Any thread: the picture is frozen, and everything made here is frozen before it leaves.
    private static ImageBrush? Make(BitmapSource picture)
    {
        if (picture.PixelWidth <= 0 || picture.PixelHeight <= 0) return null;

        // Fills the square, cropped about its middle, as Stretch="UniformToFill" would.
        var scale = Math.Max(Side / (double)picture.PixelWidth, Side / (double)picture.PixelHeight);
        BitmapSource scaled = new TransformedBitmap(picture, new ScaleTransform(scale, scale));
        scaled = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);

        var width = Math.Min(Side, scaled.PixelWidth);
        var height = Math.Min(Side, scaled.PixelHeight);
        if (width <= 0 || height <= 0) return null;

        var source = new byte[width * height * 4];
        scaled.CopyPixels(
            new Int32Rect((scaled.PixelWidth - width) / 2, (scaled.PixelHeight - height) / 2, width, height),
            source, width * 4, 0);

        // Premultiplied, so a logo's transparent surround blurs to nothing rather than to black.
        var pixels = new float[Side * Side * 4];
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                var from = (Math.Min(y, height - 1) * width + Math.Min(x, width - 1)) * 4;
                var to = (y * Side + x) * 4;
                for (var c = 0; c < 4; c++) pixels[to + c] = source[from + c];
            }
        }

        var scratch = new float[pixels.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            Box(pixels, scratch, horizontal: true);
            Box(scratch, pixels, horizontal: false);
        }

        // Steam's mask: black at -25% of the width, nothing at 100%. And its opacity.
        var result = new byte[pixels.Length];
        for (var x = 0; x < Side; x++)
        {
            var across = x / (double)(Side - 1);
            var strength = (float)(Opacity * Math.Clamp(1 - (across + 0.25) / 1.25, 0, 1));

            for (var y = 0; y < Side; y++)
            {
                var i = (y * Side + x) * 4;
                for (var c = 0; c < 4; c++) result[i + c] = (byte)Math.Clamp(pixels[i + c] * strength + 0.5f, 0, 255);
            }
        }

        var bitmap = BitmapSource.Create(Side, Side, 96, 96, PixelFormats.Pbgra32, null, result, Side * 4);
        bitmap.Freeze();

        // From the top, as Steam's sits (top: -30% of the row, of a picture far taller than it).
        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
        brush.Freeze();
        return brush;
    }

    // One pass of a box blur along one axis; the edge pixel stands in for what is past it.
    private static void Box(float[] from, float[] to, bool horizontal)
    {
        const int Width = BoxRadius * 2 + 1;

        for (var line = 0; line < Side; line++)
        {
            for (var c = 0; c < 4; c++)
            {
                var sum = 0f;
                for (var k = -BoxRadius; k <= BoxRadius; k++) sum += from[At(line, Math.Clamp(k, 0, Side - 1), horizontal) + c];

                for (var i = 0; i < Side; i++)
                {
                    to[At(line, i, horizontal) + c] = sum / Width;

                    var leaving = Math.Clamp(i - BoxRadius, 0, Side - 1);
                    var entering = Math.Clamp(i + BoxRadius + 1, 0, Side - 1);
                    sum += from[At(line, entering, horizontal) + c] - from[At(line, leaving, horizontal) + c];
                }
            }
        }
    }

    private static int At(int line, int along, bool horizontal) =>
        (horizontal ? line * Side + along : along * Side + line) * 4;
}
