using Avalonia.Data.Converters;
using Pixora.Avalonia.Services;
using System;
using System.Globalization;

namespace Pixora.Avalonia.Converters;

/// <summary>
/// Converts a boolean to a blur radius value using the user's BlurIntensity setting.
/// True = blurred (using BlurIntensity from settings), False = no blur (0).
/// </summary>
public class BoolToBlurRadiusConverter : IValueConverter
{
    public static readonly BoolToBlurRadiusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBlurred && isBlurred)
        {
            // Get blur intensity from settings (default 15)
            var intensity = AppServices.Get<Core.Settings.SettingsService>()?.Current.BlurIntensity ?? 15;
            return (double)intensity;
        }
        return 0.0; // No blur
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
