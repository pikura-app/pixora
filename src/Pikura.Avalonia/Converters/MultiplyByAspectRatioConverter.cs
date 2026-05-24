using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pikura.Avalonia.Converters;

/// <summary>
/// Multi-binding converter that multiplies CardSize × AspectRatio.
/// Used for calculating natural image height: width × aspectRatio = height.
/// </summary>
public class MultiplyByAspectRatioConverter : IMultiValueConverter
{
    public static readonly MultiplyByAspectRatioConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return 200.0; // default fallback

        // values[0] = CardSize (width)
        var width = values[0] switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 0.0
        };

        // values[1] = AspectRatio
        var ratio = values[1] switch
        {
            double d => d,
            int i => i,
            float f => f,
            _ => 1.0
        };

        // Natural height = width × aspectRatio (no cap — preserves full image proportions)
        return width * ratio;
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
