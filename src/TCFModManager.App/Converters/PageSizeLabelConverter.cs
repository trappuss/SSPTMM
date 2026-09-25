using System.Globalization;
using System.Windows.Data;
using TCFModManager.App.Localization;
using TCFModManager.App.ViewModels;

namespace TCFModManager.App.Converters;

//
// The Per page dropdown's entries: the page sizes as numbers, and the infinite list (stored as 0)
// by name.
//
public sealed class PageSizeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BrowseViewModel.InfinitePageSize ? Strings.Workshop_PerPageInfinite : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
