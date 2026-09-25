using System.Globalization;
using System.Windows.Data;

namespace TCFModManager.App.Converters;

//
// True when every bound value is the same object. Lets a radio row in a list know it is the list's
// current choice - bind the row's item and the list's selected item - without the item carrying an
// IsSelected of its own that would have to be kept in step by hand.
//
public sealed class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && values.Skip(1).All(v => Equals(v, values[0]));

    // One-way only: choosing a row is a command, not a write-back through this converter.
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
