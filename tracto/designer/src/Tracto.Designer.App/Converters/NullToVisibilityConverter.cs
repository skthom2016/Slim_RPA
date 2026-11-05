namespace Tracto.Designer.App.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>Shows an element only while something is selected, and hides it otherwise.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
