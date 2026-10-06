using System.Windows;
using TCFModManager.App.Localization;

namespace TCFModManager.App.Views;

//
// Fork (SSPTMM 1.3.0): the app's questions and notices as Steam's modal (SteamDialog) instead of
// Windows' own message box - the same arguments and answer as MessageBox.Show, so a call site
// changes only its name.
//
// The caption is the dialog's title and the text its body; the icon is not drawn (Steam's modal has
// none). Yes / OK are green, No / Cancel grey. Enter picks defaultResult - or, as Windows does, the
// first button. Closing the dialog (the X, or Esc) answers Cancel where there is one, otherwise No
// for a Yes/No question and OK for a notice.
//
// Not for the crash report (App.OnDispatcherUnhandledException): that stays Windows' own box, which
// still shows when the app's own resources are what failed.
//
public static class SteamMessageBox
{
    public static MessageBoxResult Show(
        string text,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        _ = icon;

        // Windows' own box could be raised from any thread; a WPF window only from the UI thread.
        if (Application.Current?.Dispatcher is { } ui && !ui.CheckAccess())
            return ui.Invoke(() => Show(text, caption, button, icon, defaultResult));

        var answers = button switch
        {
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
            _ => new[] { MessageBoxResult.OK },
        };

        var preferred = Array.IndexOf(answers, defaultResult) is var at and >= 0 ? at : 0;

        var choices = answers
            .Select((answer, i) => new SteamDialogChoice(
                Label(answer),
                answer is MessageBoxResult.Yes or MessageBoxResult.OK ? SteamDialogButton.Green : SteamDialogButton.Grey,
                IsDefault: i == preferred))
            .ToArray();

        var chosen = SteamDialog.Show(caption, text, choices);
        if (chosen >= 0) return answers[chosen];

        return answers.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
            : answers.Contains(MessageBoxResult.No) ? MessageBoxResult.No
            : MessageBoxResult.OK;
    }

    private static string Label(MessageBoxResult answer) => answer switch
    {
        MessageBoxResult.Yes => Strings.Common_Yes,
        MessageBoxResult.No => Strings.Common_No,
        MessageBoxResult.Cancel => Strings.Common_Cancel,
        _ => Strings.Common_OK,
    };
}
