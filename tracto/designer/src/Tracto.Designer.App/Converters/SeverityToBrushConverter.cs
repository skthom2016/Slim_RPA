namespace Tracto.Designer.App.Converters;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Tracto.Designer.Core.Validation;

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ValidationSeverity s
            ? s switch
            {
                ValidationSeverity.Error   => Brushes.Red,
                ValidationSeverity.Warning => Brushes.DarkOrange,
                _                          => Brushes.SteelBlue,
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
