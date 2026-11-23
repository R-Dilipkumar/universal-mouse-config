using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MouseConfig.App.ViewModels;

/// <summary>Visible when a count is 0, for "nothing here yet" messages.</summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
