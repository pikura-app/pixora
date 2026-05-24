using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Pikura.Avalonia.Converters;

public class IntToBoolConverter : IValueConverter
{
    public static readonly IntToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramString)
        {
            if (int.TryParse(paramString, out var paramValue))
            {
                return intValue == paramValue;
            }
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
