using System.Globalization;
using System.Windows.Data;

namespace MouseConfig.App;

/// <summary>true → false and false → true, e.g. to turn a slider off while a checkbox is on.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
