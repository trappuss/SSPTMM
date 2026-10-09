using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TCFModManager.App.Behaviors;

//
// Fork (round 47): the shadow under a subscribed mod's card, and the white one it turns to under
// the pointer - Steam's box-shadow: 2px 2px 10px rgba(0,0,0,0.3) on a row with 4px corners.
//
// Not a DropShadowEffect. Round 46 used two per card (one hidden), each on a layer kept as a
// bitmap, which is what Browse's cards do - but that page adds thirty cards at a time and this one
// holds every mod there is, half of them inside a faded (disabled) card where an effect is worked
// out again on every frame. Scrolling suffered badly.
//
// Here the shadow of one small rounded rectangle is worked out once, in arithmetic, and cut into
// its four corners and four edges; a card of any size is those eight pieces, the edges stretched.
// Drawing one is eight pictures and no effect.
//
public sealed class CardShadow : FrameworkElement
{
    // How far the shadow reaches past the card, and how much of the card each corner piece covers.
    private const int Reach = 18;
    private const int Corner = 18;
    private const int Tile = (Reach + Corner) * 2;
    private const double Radius = 4;

    // CSS's 10px blur is a deviation of 5px: three passes of a box 9 wide (4.5).
    private const int BoxRadius = 4;

    private static readonly Lazy<ImageSource[]> Dark = new(() => Pieces(Colors.Black, 0.3, 2, 2));

    // Under the pointer, as a Browse card's: white at 25%, set off by 1px.
    private static readonly Lazy<ImageSource[]> Lit = new(() => Pieces(Colors.White, 0.25, 1, 1));

    public static readonly DependencyProperty IsLitProperty = DependencyProperty.Register(
        nameof(IsLit), typeof(bool), typeof(CardShadow),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The white shadow, for the card under the pointer.</summary>
    public bool IsLit
    {
        get => (bool)GetValue(IsLitProperty);
        set => SetValue(IsLitProperty, value);
    }

    public CardShadow()
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < Corner * 2 || height < Corner * 2) return;

        var pieces = (IsLit ? Lit : Dark).Value;
        const double Piece = Reach + Corner;

        // Columns and rows of the nine-patch: the corner pieces at their own size, the middle
        // taking whatever the card has left.
        double[] xs = [-Reach, Corner, width - Corner, width + Reach];
        double[] ys = [-Reach, Corner, height - Corner, height + Reach];

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                // The middle is under the card and is never seen.
                if (row == 1 && column == 1) continue;

                var w = xs[column + 1] - xs[column];
                var h = ys[row + 1] - ys[row];
                if (w <= 0 || h <= 0) continue;

                drawing.DrawImage(pieces[row * 3 + column], new Rect(xs[column], ys[row], column == 1 ? w : Piece, row == 1 ? h : Piece));
            }
        }
    }

    // The nine pieces of the shadow, row by row; the middle one is null.
    private static ImageSource[] Pieces(Color color, double opacity, int offsetX, int offsetY)
    {
        // The rounded rectangle the shadow is of, shifted by the offset, as coverage 0..1.
        var mask = new float[Tile * Tile];
        for (var y = 0; y < Tile; y++)
        {
            for (var x = 0; x < Tile; x++)
            {
                mask[y * Tile + x] = Inside(x + 0.5 - offsetX, y + 0.5 - offsetY) ? 1f : 0f;
            }
        }

        var scratch = new float[mask.Length];
        for (var pass = 0; pass < 3; pass++)
        {
            Box(mask, scratch, horizontal: true);
            Box(scratch, mask, horizontal: false);
        }

        var pixels = new byte[Tile * Tile * 4];
        for (var i = 0; i < mask.Length; i++)
        {
            var alpha = mask[i] * opacity;
            pixels[i * 4] = (byte)(color.B * alpha + 0.5);
            pixels[i * 4 + 1] = (byte)(color.G * alpha + 0.5);
            pixels[i * 4 + 2] = (byte)(color.R * alpha + 0.5);
            pixels[i * 4 + 3] = (byte)(255 * alpha + 0.5);
        }

        var whole = BitmapSource.Create(Tile, Tile, 96, 96, PixelFormats.Pbgra32, null, pixels, Tile * 4);
        whole.Freeze();

        const int Piece = Reach + Corner;
        var pieces = new ImageSource[9];

        // Corners are the tile's quarters. An edge is the one line of pixels where the quarters
        // meet, which a card stretches to its own length.
        int[] starts = [0, Piece - 1, Piece];
        int[] sizes = [Piece, 1, Piece];

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if (row == 1 && column == 1) continue;

                var piece = new CroppedBitmap(whole, new Int32Rect(starts[column], starts[row], sizes[column], sizes[row]));
                piece.Freeze();
                pieces[row * 3 + column] = piece;
            }
        }

        return pieces;
    }

    private static bool Inside(double x, double y)
    {
        const double Left = Reach, Top = Reach, Right = Tile - Reach, Bottom = Tile - Reach;
        if (x < Left || x > Right || y < Top || y > Bottom) return false;

        // Only the corners are not simply inside.
        var cx = x < Left + Radius ? Left + Radius : x > Right - Radius ? Right - Radius : x;
        var cy = y < Top + Radius ? Top + Radius : y > Bottom - Radius ? Bottom - Radius : y;
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= Radius * Radius;
    }

    private static void Box(float[] from, float[] to, bool horizontal)
    {
        const int Width = BoxRadius * 2 + 1;

        for (var line = 0; line < Tile; line++)
        {
            for (var i = 0; i < Tile; i++)
            {
                var sum = 0f;
                for (var k = -BoxRadius; k <= BoxRadius; k++)
                {
                    var along = i + k;
                    if (along < 0 || along >= Tile) continue; // nothing past the tile: the shadow has faded out by there
                    sum += from[horizontal ? line * Tile + along : along * Tile + line];
                }

                to[horizontal ? line * Tile + i : i * Tile + line] = sum / Width;
            }
        }
    }
}
