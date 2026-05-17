using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Pixora.Avalonia.Converters;

public class BoolToConfiguredTextConverter : IValueConverter
{
    public static readonly BoolToConfiguredTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConfigured)
        {
            return isConfigured ? "✓ Configured" : "⚠ Not configured";
        }
        return "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
