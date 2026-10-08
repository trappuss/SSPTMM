using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TCFModManager.App.Views;

//
// Fork (SSPTMM 1.3.0): the base for every dialog with more in it than SteamDialog's text and
// buttons, so they all look like Steam's modal (SteamModalWindowStyle in SteamStyles.xaml) rather
// than a Windows window: no title bar of Windows', the close X, Esc closes, the title drags it, and
// the main window behind is dimmed while it is open - whichever way the caller shows it.
//
public class SteamModalWindow : Window
{
    private MainWindow? _dimmed;

    /// <summary>Dims the main window behind while open - for a dialog (ShowDialog). A window left
    /// open beside the app (Show) sets this false.</summary>
    public bool DimsOwner { get; set; } = true;

    public SteamModalWindow()
    {
        SetResourceReference(StyleProperty, "SteamModalWindowStyle");

        // The theme's text brushes, as Steam's modal colours its text, for everything inside that
        // reads them (the controls' own styles do): body #ACB2B8, quieter lines #8F98A0.
        Resources["TextFillColorPrimaryBrush"] = Frozen("#ACB2B8");
        Resources["TextFillColorSecondaryBrush"] = Frozen("#8F98A0");
        Resources["TextFillColorTertiaryBrush"] = Frozen("#7A8189");

        // Headings and names (SemiBold, Bold) in white, as Steam's modal sets them off from its body
        // text. A TextBlock with a colour of its own keeps it.
        var heading = new Style(typeof(TextBlock));
        foreach (var weight in new[] { FontWeights.SemiBold, FontWeights.Bold })
        {
            var trigger = new Trigger { Property = TextBlock.FontWeightProperty, Value = weight };
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, Frozen("#FFFFFF")));
            heading.Triggers.Add(trigger);
        }

        Resources[typeof(TextBlock)] = heading;

        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Never taller than the screen, so a dialog with a long list keeps its buttons in view (its
        // rows scroll instead). A MaxHeight of the dialog's own, set in its XAML, replaces this.
        MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 40);

        KeyDown += OnKeyDown; // bubbling: an open dropdown takes its own Esc first
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("PART_Close") is Button close) close.Click += (_, _) => Close();

        if (GetTemplateChild("PART_Title") is FrameworkElement title)
        {
            title.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
        }
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // With no owner it opens in the middle of the screen, as CenterOwner can't.
        if (Owner is null && WindowStartupLocation == WindowStartupLocation.CenterOwner)
        {
            Left = (SystemParameters.WorkArea.Width - ActualWidth) / 2 + SystemParameters.WorkArea.Left;
            Top = (SystemParameters.WorkArea.Height - ActualHeight) / 2 + SystemParameters.WorkArea.Top;
        }

        if (DimsOwner && Owner is MainWindow main && _dimmed is null)
        {
            _dimmed = main;
            main.SetModalDim(true);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _dimmed?.SetModalDim(false);
        _dimmed = null;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled) return;

        e.Handled = true;
        Close();
    }
}
