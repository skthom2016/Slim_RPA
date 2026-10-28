namespace Tracto.Designer.App.Converters;

using System.Globalization;
using System.Windows.Data;
using Tracto.Designer.Core.Validation;

public sealed class SeverityToLetterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ValidationSeverity s
            ? s switch
            {
                ValidationSeverity.Error   => "E",
                ValidationSeverity.Warning => "W",
                _                          => "I",
            }
            : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
