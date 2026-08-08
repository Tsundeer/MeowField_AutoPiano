using System.Globalization;
using System.Windows.Data;

namespace MeowField.App;

public sealed class UiTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var value = values.Length > 0 ? values[0] : null;
        var english = values.Length > 1 && values[1] is true;
        if (values.Length > 2 && Equals(values[2], "PageNumber"))
            return english ? $"Page {value}" : $"第 {value} 页";
        return LocalizationService.TranslateDynamic(value, english);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
