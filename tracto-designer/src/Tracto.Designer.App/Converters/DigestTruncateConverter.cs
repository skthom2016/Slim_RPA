namespace Tracto.Designer.App.Converters;

using System.Globalization;
using System.Windows.Data;

[ValueConversion(typeof(string), typeof(string))]
public sealed class DigestTruncateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && s.Length > 16 ? s[..16] : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
