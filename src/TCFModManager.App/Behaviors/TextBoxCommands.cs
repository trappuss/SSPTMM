using System.Windows.Controls;
using System.Windows.Input;

namespace TCFModManager.App.Behaviors;

//
// Commands a TextBox's own template can use, where there is no code-behind to handle a click - the
// search field's clear button (Steam's "Clear search" x).
//
public static class TextBoxCommands
{
    /// <summary>Empties the TextBox passed as the parameter and puts the caret back in it.</summary>
    public static ICommand Clear { get; } = new ClearCommand();

    private sealed class ClearCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => parameter is TextBox;

        public void Execute(object? parameter)
        {
            if (parameter is not TextBox box) return;

            box.Clear();
            box.Focus();
        }
    }
}
