using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MouseConfig.App.ViewModels;

/// <summary>Visible when a number isn't 0, e.g. to show a message box only when there's text in it.</summary>
public sealed class NonZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n != 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
