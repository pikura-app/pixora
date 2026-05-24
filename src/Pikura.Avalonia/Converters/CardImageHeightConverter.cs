using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pikura.Avalonia.Converters;

/// <summary>
/// Multi-binding converter that returns the correct image height for a card based on the height mode.
/// Inputs (in order):
///   0: CardSize (double) — the card's column width
///   1: IsNaturalHeight (bool) — true = natural (width × aspect), false = fixed (= CardSize)
///   2: AspectRatio (double) — image aspect ratio (h / w), only used when IsNaturalHeight is true
/// Output: double height value for the Image. Capped at 700 in natural mode for visual uniformity.
/// </summary>
public class CardImageHeightConverter : IMultiValueConverter
{
    public static readonly CardImageHeightConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return 200.0;

        var width = ToDouble(values[0], 200.0);
        var isNatural = values[1] is bool b && b;

        if (!isNatural) return width;

        var ratio = values.Count > 2 ? ToDouble(values[2], 1.0) : 1.0;
        if (ratio <= 0) ratio = 1.0;
        var height = width * ratio;
        return Math.Min(height, 700.0);
    }

    private static double ToDouble(object? v, double fallback) => v switch
    {
        double d => d,
        int i => i,
        float f => f,
        _ => fallback
    };

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
