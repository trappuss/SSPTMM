using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TCFModManager.App.Views;

/// <summary>The face of one of a <see cref="SteamDialog"/>'s buttons.</summary>
public enum SteamDialogButton
{
    Green,
    Blue,
    Grey,
}

/// <summary>One answer a <see cref="SteamDialog"/> offers. <paramref name="IsDefault"/> is the one
/// Enter chooses.</summary>
public sealed record SteamDialogChoice(string Text, SteamDialogButton Face, bool IsDefault = false);

//
// Steam's modal dialog - see the notes in SteamDialog.xaml. Shown with Show, which answers with the
// index of the button chosen, or -1 when it was closed (the X, or Esc).
//
public partial class SteamDialog : Window
{
    private int _choice = -1;

    private SteamDialog(string title, object body, IReadOnlyList<SteamDialogChoice> choices)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        Body.Content = body;

        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var index = i;
            var button = new Button
            {
                Content = choice.Text,
                IsDefault = choice.IsDefault,
                Style = (Style)FindResource(choice.Face switch
                {
                    SteamDialogButton.Green => "SteamModalGreenButton",
                    SteamDialogButton.Blue => "SteamModalBlueButton",
                    _ => "SteamModalGreyButton",
                }),
            };

            // The first button sits flush right of the body's edge; the rest keep their 12px gap.
            if (i == 0) button.Margin = new Thickness(0);

            button.Click += (_, _) =>
            {
                _choice = index;
                Close();
            };
            Buttons.Children.Add(button);
        }
    }

    /// <summary>Shows the dialog over the main window, dimming it as Steam dims the page, and
    /// returns the index of the answer chosen in <paramref name="choices"/>, or -1 if it was closed.</summary>
    public static int Show(string title, object body, params SteamDialogChoice[] choices)
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new SteamDialog(title, body, choices);

        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var dimmed = dialog.Owner as MainWindow;
        dimmed?.SetModalDim(true);
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            dimmed?.SetModalDim(false);
        }

        return dialog._choice;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        Close();
    }
}
