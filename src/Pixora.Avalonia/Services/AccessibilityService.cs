using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Pixora.Core.Settings;
using System;

namespace Pixora.Avalonia.Services;

/// <summary>
/// Service to apply accessibility settings across the application.
/// </summary>
public class AccessibilityService
{
    private readonly SettingsService _settingsService;
    private double _baseFontSize = 13.0;
    private bool _isHighContrast;

    public AccessibilityService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        
        // Apply initial settings
        ApplyAccessibilitySettings();
        
        // Listen for changes
        _settingsService.Changed += (_, _) => ApplyAccessibilitySettings();
    }

    private void ApplyAccessibilitySettings()
    {
        var settings = _settingsService.Current;
        
        ApplyFontScaling(settings.FontSizeScale, settings.UseLargeFonts);
        ApplyHighContrast(settings.UseHighContrast);
        ApplyReducedMotion(settings.ReduceMotion);
    }

    private void ApplyFontScaling(double scale, bool useLargeFonts)
    {
        if (Application.Current == null) return;
        
        // Calculate effective font size
        double effectiveScale = scale;
        if (useLargeFonts)
            effectiveScale *= 1.2;

        // Set global font size resource
        Application.Current.Resources["AccessibilityFontSize"] = _baseFontSize * effectiveScale;
    }

    private void ApplyHighContrast(bool enable)
    {
        if (Application.Current == null) return;
        
        _isHighContrast = enable;
        
        if (enable)
        {
            // Apply high contrast theme variant
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
            
            // Set high contrast application resources
            var currentResources = Application.Current.Resources;
            currentResources["AccessibilityForeground"] = new SolidColorBrush(Colors.Black);
            currentResources["AccessibilityBackground"] = new SolidColorBrush(Colors.White);
            currentResources["AccessibilityBorder"] = new SolidColorBrush(Colors.Black);
        }
        else
        {
            // Restore normal theme
            var theme = _settingsService.Current.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            Application.Current.RequestedThemeVariant = theme;
            
            // Remove accessibility color overrides
            var currentResources = Application.Current.Resources;
            currentResources.Remove("AccessibilityForeground");
            currentResources.Remove("AccessibilityBackground");
            currentResources.Remove("AccessibilityBorder");
        }
    }

    private void ApplyReducedMotion(bool reduce)
    {
        if (Application.Current == null) return;

        if (reduce)
        {
            // Set animation duration to 0 for reduced motion
            var currentResources = Application.Current.Resources;
            currentResources["AccessibilityAnimationDuration"] = TimeSpan.Zero;
        }
        else
        {
            // Restore default animation durations
            var currentResources = Application.Current.Resources;
            currentResources["AccessibilityAnimationDuration"] = TimeSpan.FromMilliseconds(200);
        }
    }
}
