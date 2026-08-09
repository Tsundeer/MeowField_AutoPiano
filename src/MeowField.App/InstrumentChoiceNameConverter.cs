using System.Globalization;
using System.Windows.Data;
using MeowField.Domain;

namespace MeowField.App;

public sealed class InstrumentChoiceNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not InstrumentChoice choice)
        {
            return string.Empty;
        }
        var english = values.Length > 1 && values[1] is true;
        if (choice.Profile is { } profile)
        {
            return profile.Name;
        }
        return english
            ? choice.Kind switch
            {
                InstrumentKind.Piano => "Piano (Generic)",
                InstrumentKind.Drums => "Drums (Generic)",
                InstrumentKind.Microphone => "Microphone (Generic)",
                _ => choice.Kind.ToString(),
            }
            : choice.Kind switch
            {
                InstrumentKind.Piano => "钢琴（通用）",
                InstrumentKind.Drums => "架子鼓（通用）",
                InstrumentKind.Microphone => "麦克风（通用）",
                _ => choice.Kind.ToString(),
            };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
