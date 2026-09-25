using System.Globalization;
using System.Windows.Data;

namespace TCFModManager.App.Converters;

//
// A date and time from sp-mod.com, in this PC's time zone. The API sends UTC, and a binding's
// StringFormat prints a DateTimeOffset in its own offset - so a format with the time in it showed
// the time in UTC. Put before the StringFormat, which formats what this returns.
//
public sealed class LocalTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset offset => offset.ToLocalTime(),
        DateTime { Kind: DateTimeKind.Utc } utc => utc.ToLocalTime(),
        _ => value,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
