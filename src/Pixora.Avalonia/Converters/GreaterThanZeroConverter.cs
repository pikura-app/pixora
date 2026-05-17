using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Pixora.Avalonia.Converters;

/// <summary>Returns true when an integer value is greater than zero.</summary>
public sealed class GreaterThanZeroConverter : IValueConverter
{
    public static readonly GreaterThanZeroConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
