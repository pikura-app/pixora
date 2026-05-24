using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Pikura.Avalonia.Converters;

/// <summary>
/// Converts an image URL to a Bitmap for display.
/// Uses a static cache and async loading with retry.
/// </summary>
public class ImageUrlConverter : IValueConverter
{
    private static readonly HttpClient _httpClient = new();
    private static readonly ConcurrentDictionary<string, Bitmap> _cache = new();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> _loadingTasks = new();

    public static ImageUrlConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrEmpty(url))
            return null;

        // Check cache first
        if (_cache.TryGetValue(url, out var cachedBitmap))
        {
            return cachedBitmap;
        }

        // Start loading if not already loading
        var loadingTask = _loadingTasks.GetOrAdd(url, LoadImageAsync);

        // If already completed, return result
        if (loadingTask.IsCompleted)
        {
            return loadingTask.Result;
        }

        // Return null while loading - binding will update when task completes
        return null;
    }

    private static async Task<Bitmap?> LoadImageAsync(string url)
    {
        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            _cache[url] = bitmap;
            return bitmap;
        }
        catch
        {
            // Remove from loading tasks on error
            _loadingTasks.TryRemove(url, out _);
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
