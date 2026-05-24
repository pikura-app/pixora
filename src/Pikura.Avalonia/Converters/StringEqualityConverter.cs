using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Pikura.Avalonia.Converters;

/// <summary>
/// Returns true when the bound string value equals the converter parameter.
/// Used to highlight active mode/content buttons in rankings.
/// </summary>
public sealed class StringEqualityConverter : IValueConverter
{
    public static readonly StringEqualityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && s == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
