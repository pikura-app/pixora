using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Pikura.Avalonia.Converters;

/// <summary>
/// Converts an enum value to boolean for RadioButton binding.
/// Returns true when the bound enum value matches the converter parameter.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        // Convert both to strings for comparison to handle different enum types
        var valueString = value.ToString();
        var parameterString = parameter.ToString();

        return string.Equals(valueString, parameterString, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // When RadioButton is checked (value = true), return the parameter as the enum value
        if (value is bool isChecked && isChecked && parameter != null)
        {
            // If target type is an enum, try to parse it
            if (targetType.IsEnum)
            {
                var parameterString = parameter.ToString();
                if (Enum.TryParse(targetType, parameterString, true, out var result))
                {
                    return result;
                }
            }
            // For non-enum types, just return the parameter
            return parameter;
        }

        // If unchecked or no parameter, return UnsetValue to indicate no change
        return global::Avalonia.AvaloniaProperty.UnsetValue;
    }
}
