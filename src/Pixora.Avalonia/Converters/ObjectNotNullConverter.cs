using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Pixora.Avalonia.Converters;

/// <summary>
/// Converts an object to boolean based on whether it's not null. Returns true if not null, false if null.
/// </summary>
public class ObjectNotNullConverter : IValueConverter
{
    public static readonly ObjectNotNullConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
