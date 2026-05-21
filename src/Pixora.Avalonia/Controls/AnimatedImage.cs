using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Pixora.Avalonia.Controls;

/// <summary>
/// An <see cref="Image"/> that plays an animated WebP / GIF / APNG file. Frames
/// are decoded once via <see cref="SKCodec"/> and cycled by a single
/// <see cref="DispatcherTimer"/>. Designed for short ugoira animations
/// (typically &lt;200 frames) — large GIFs will use proportional memory.
/// </summary>
public sealed class AnimatedImage : Image
{
    /// <summary>Path to the animated image file. Setting it to null/empty stops playback.</summary>
    public static readonly StyledProperty<string?> SourcePathProperty =
        AvaloniaProperty.Register<AnimatedImage, string?>(nameof(SourcePath));

    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    /// <summary>Whether playback is currently looping.</summary>
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<AnimatedImage, bool>(nameof(IsPlaying), defaultValue: true);

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    private Bitmap[]? _frames;
    private int[]? _frameDurationsMs;
    private int _frameIndex;
    private DispatcherTimer? _timer;
    private readonly object _lock = new();

    static AnimatedImage()
    {
        SourcePathProperty.Changed.AddClassHandler<AnimatedImage>((c, _) =>
        {
#pragma warning disable CS4014
            c.ReloadAsync();
#pragma warning restore CS4014
        });
        IsPlayingProperty.Changed.AddClassHandler<AnimatedImage>((c, e) =>
        {
            if (e.NewValue is true) c.StartTimer();
            else c.StopTimer();
        });
    }

    public AnimatedImage()
    {
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    /// <summary>Stops playback and disposes pre-decoded frames.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            StopTimer();
            // Clear source first to prevent rendering disposed bitmaps
            Source = null;
            if (_frames != null)
            {
                foreach (var f in _frames)
                {
                    try { f.Dispose(); } catch { /* best-effort */ }
                }
                _frames = null;
                _frameDurationsMs = null;
                _frameIndex = 0;
            }
        }
    }

    private async Task ReloadAsync()
    {
        Dispose();
        var path = SourcePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            var (frames, delays) = await Task.Run(() => DecodeAllFrames(path));
            if (frames.Length == 0) return;

            _frames = frames;
            _frameDurationsMs = delays;
            _frameIndex = 0;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (_frames == null || _frames.Length == 0) return;
                    Source = _frames[0];
                    if (IsPlaying && _frames.Length > 1) StartTimer();
                }
            });
        }
        catch
        {
            // Swallow — caller will see no animation; logging is the consumer's job.
        }
    }

    private void StartTimer()
    {
        if (_frames == null || _frames.Length <= 1 || _frameDurationsMs == null) return;
        StopTimer();
        var first = Math.Max(20, _frameDurationsMs[_frameIndex]);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(first), DispatcherPriority.Render, OnTick);
        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_frames == null || _frameDurationsMs == null || _frames.Length == 0)
            {
                StopTimer();
                return;
            }
            _frameIndex = (_frameIndex + 1) % _frames.Length;
            var frame = _frames[_frameIndex];
            if (frame != null)
                Source = frame;

            // Some animated formats use variable per-frame delays — recompute the
            // interval each tick so timing stays accurate.
            if (_timer != null)
                _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, _frameDurationsMs[_frameIndex]));
        }
    }

    /// <summary>
    /// Decodes every frame of an animated image into Avalonia <see cref="Bitmap"/>s
    /// using <see cref="SKCodec"/>. Returns the per-frame display delay in ms
    /// (defaults to 80 ms when the codec doesn't supply one).
    /// </summary>
    private static (Bitmap[] Frames, int[] DelaysMs) DecodeAllFrames(string path)
    {
        using var stream = File.OpenRead(path);
        using var data = SKData.Create(stream);
        using var codec = SKCodec.Create(data);
        if (codec == null) return (Array.Empty<Bitmap>(), Array.Empty<int>());

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var frameInfos = codec.FrameInfo;
        var count = Math.Max(1, frameInfos?.Length ?? 1);

        var frames = new Bitmap[count];
        var delays = new int[count];

        for (int i = 0; i < count; i++)
        {
            using var bmp = new SKBitmap(info);
            var opts = new SKCodecOptions(i);
            var result = codec.GetPixels(info, bmp.GetPixels(), bmp.RowBytes, opts);
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                continue;

            frames[i] = SkBitmapToAvalonia(bmp);
            delays[i] = frameInfos != null && i < frameInfos.Length && frameInfos[i].Duration > 0
                ? frameInfos[i].Duration
                : 80;
        }
        return (frames, delays);
    }

    private static unsafe Bitmap SkBitmapToAvalonia(SKBitmap source)
    {
        var size = new PixelSize(source.Width, source.Height);
        var dpi = new global::Avalonia.Vector(96, 96);
        var writeable = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = writeable.Lock();
        int rowBytes = source.Width * 4;
        byte* src = (byte*)source.GetPixels();
        byte* dst = (byte*)fb.Address;
        for (int y = 0; y < source.Height; y++)
        {
            Buffer.MemoryCopy(src + y * source.RowBytes, dst + y * fb.RowBytes, rowBytes, rowBytes);
        }
        return writeable;
    }
}
