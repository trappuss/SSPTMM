using System.Globalization;
using System.Windows.Data;

namespace TCFModManager.App.Converters;

// Flips a bool both ways, for a switch whose On means the opposite of the setting it writes.
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}
