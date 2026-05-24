using SkiaSharp;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Pikura.Avalonia.Services;

/// <summary>
/// Fast SKBitmap → Avalonia Bitmap conversion via direct pixel copy.
/// Avoids the PNG encode/decode roundtrip which is ~10× slower and
/// allocates large temporary buffers per thumbnail.
/// </summary>
public static class BitmapInterop
{
    /// <summary>
    /// Convert an <see cref="SKBitmap"/> to an Avalonia <see cref="Bitmap"/>
    /// using a direct BGRA pixel buffer copy. Safe to call from a background thread.
    /// </summary>
    public static Bitmap SkiaToAvalonia(SKBitmap skBitmap)
    {
        SKBitmap source = skBitmap;
        bool ownsSource = false;
        if (source.ColorType != SKColorType.Bgra8888 || source.AlphaType != SKAlphaType.Premul)
        {
            var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var converted = new SKBitmap(info);
            using (var canvas = new SKCanvas(converted))
            {
                canvas.DrawBitmap(source, 0, 0);
            }
            source = converted;
            ownsSource = true;
        }

        var size = new PixelSize(source.Width, source.Height);
        var dpi = new Vector(96, 96);
        var writeable = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);

        using (var fb = writeable.Lock())
        {
            int rowBytes = source.Width * 4;
            unsafe
            {
                byte* src = (byte*)source.GetPixels();
                byte* dst = (byte*)fb.Address;
                for (int y = 0; y < source.Height; y++)
                {
                    System.Buffer.MemoryCopy(
                        src + y * source.RowBytes,
                        dst + y * fb.RowBytes,
                        rowBytes, rowBytes);
                }
            }
        }

        if (ownsSource) source.Dispose();
        return writeable;
    }
}
