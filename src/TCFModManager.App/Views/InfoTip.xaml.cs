using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TCFModManager.App.Views;

// Fork (SSPTMM): the "?" that holds the rest of a shortened explanation - see InfoTip.xaml.
public partial class InfoTip : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(InfoTip), new PropertyMetadata(null, OnTextChanged));

    private static readonly Brush Rest = Frozen(0x8F, 0x98, 0xA0);
    private static readonly Brush Lit = Frozen(0xDC, 0xDE, 0xDF);

    public InfoTip()
    {
        InitializeComponent();
        MouseEnter += (_, _) => Light(true);
        MouseLeave += (_, _) => Light(IsKeyboardFocused);
        GotKeyboardFocus += (_, _) => Light(true);
        LostKeyboardFocus += (_, _) => Light(IsMouseOver);
    }

    /// <summary>The full explanation, shown when the "?" is hovered or focused.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    // The tooltip is built here rather than bound in XAML: a ToolTip sits in a tree of its own,
    // where a binding back to this control has nothing to find. Wrapped, so a paragraph reads as
    // one rather than as a line across the screen.
    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tip = (InfoTip)d;
        tip.ToolTip = e.NewValue is string text && text.Length > 0
            ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 }
            : null;
        tip.Visibility = tip.ToolTip is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Light(bool on)
    {
        Ring.BorderBrush = on ? Lit : Rest;
        Mark.Foreground = on ? Lit : Rest;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
