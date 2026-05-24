using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Pikura.Avalonia.Converters;

/// <summary>
/// MultiValueConverter — returns true when values[0] == values[1] (string equality).
/// Used to compare a profile's Id against the active profile Id in the account list.
/// </summary>
public sealed class StringsEqualMultiConverter : IMultiValueConverter
{
    public static readonly StringsEqualMultiConverter Instance = new();

    public object Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return false;
        var equal = values[0]?.ToString() == values[1]?.ToString();
        return parameter?.ToString() == "negate" ? !equal : equal;
    }
}
