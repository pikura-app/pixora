using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pixora.Core.Http;
using Pixora.Core.Models;
using Pixora.Core.Settings;

namespace Pixora.Core.Services;

/// <summary>
/// Pixiv ugoira pipeline:
///   1. fetch <c>/ajax/illust/{id}/ugoira_meta</c> via <see cref="PixivClient"/>
///   2. download the frame zip (with pixiv referer)
///   3. extract frames into a temp dir
///   4. write an ffconcat input listing each frame + its delay
///   5. invoke ffmpeg to produce one or more output formats
/// All outputs are cached under <c>%APPDATA%/Pixora/cache/ugoira/{id}/</c>.
/// </summary>
public sealed class UgoiraService
{
    private const string PixivReferer = "https://www.pixiv.net/";

    private readonly PixivClient _client;
    private readonly PixivHttpClientFactory _httpFactory;
    private readonly FfmpegService _ffmpeg;
    private readonly SettingsService _settings;
    private readonly ILogger<UgoiraService> _logger;

    public UgoiraService(
        PixivClient client,
        PixivHttpClientFactory httpFactory,
        FfmpegService ffmpeg,
        SettingsService settings,
        ILogger<UgoiraService> logger)
    {
        _client = client;
        _httpFactory = httpFactory;
        _ffmpeg = ffmpeg;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Cache root used for both the in-app preview WebP and any user-requested exports.</summary>
    public static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pixora", "cache", "ugoira");

    /// <summary>Path of the cached preview file for an artwork (may not exist yet).</summary>
    public static string GetPreviewPath(string artworkId, UgoiraFormat fmt = UgoiraFormat.WebP)
        => Path.Combine(CacheRoot, artworkId, $"preview{ExtensionFor(fmt)}");

    /// <summary>
    /// Returns a path to a cached animated file the in-app player can decode.
    /// Builds it on first call: downloads zip → extracts → ffmpeg encodes WebP.
    /// </summary>
    public async Task<string?> GetOrCreatePreviewAsync(string artworkId, CancellationToken ct = default)
    {
        var previewPath = GetPreviewPath(artworkId, UgoiraFormat.WebP);
        if (File.Exists(previewPath) && new FileInfo(previewPath).Length > 0)
            return previewPath;

        if (!_ffmpeg.IsAvailable())
        {
            _logger.LogInformation("Ugoira preview requested but ffmpeg is unavailable. Artwork {Id}.", artworkId);
            return null;
        }

        try
        {
            var meta = await _client.GetUgoiraMetaAsync(artworkId, ct).ConfigureAwait(false);
            if (meta == null || meta.Frames.Count == 0)
            {
                _logger.LogWarning("ugoira_meta returned no frames for {Id}.", artworkId);
                return null;
            }

            var workDir = Path.Combine(CacheRoot, artworkId);
            Directory.CreateDirectory(workDir);

            var framesDir = Path.Combine(workDir, "frames");
            await EnsureFramesAsync(meta, framesDir, ct).ConfigureAwait(false);

            var concatPath = WriteConcatFile(meta, framesDir);
            var output = await EncodeAsync(UgoiraFormat.WebP, concatPath, previewPath, ct).ConfigureAwait(false);
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build ugoira preview for {Id}", artworkId);
            return null;
        }
    }

    /// <summary>
    /// Downloads the frame zip (if not already cached) plus exports every requested
    /// format. The original zip lives at <c>{cache}/{id}/frames.zip</c>; outputs at
    /// <c>{outputDir}/{id}.{ext}</c>. When <paramref name="outputDir"/> is null,
    /// outputs go to the cache.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExportAsync(
        string artworkId,
        IEnumerable<UgoiraFormat> formats,
        string? outputDir = null,
        CancellationToken ct = default)
    {
        var meta = await _client.GetUgoiraMetaAsync(artworkId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ugoira_meta unavailable for {artworkId}");
        if (meta.Frames.Count == 0)
            return [];

        var workDir = Path.Combine(CacheRoot, artworkId);
        Directory.CreateDirectory(workDir);
        var framesDir = Path.Combine(workDir, "frames");
        await EnsureFramesAsync(meta, framesDir, ct).ConfigureAwait(false);
        var concatPath = WriteConcatFile(meta, framesDir);

        var dir = outputDir ?? workDir;
        Directory.CreateDirectory(dir);
        var produced = new List<string>();
        foreach (var fmt in formats.Distinct())
        {
            var dest = Path.Combine(dir, artworkId + ExtensionFor(fmt));
            try
            {
                var path = await EncodeAsync(fmt, concatPath, dest, ct).ConfigureAwait(false);
                if (path != null) produced.Add(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ffmpeg failed for {Id} format {Fmt}", artworkId, fmt);
            }
        }
        return produced;
    }

    /// <summary>Returns the cached frame-zip path (download if missing).</summary>
    public async Task<string> GetOrDownloadFrameZipAsync(string artworkId, CancellationToken ct = default)
    {
        var meta = await _client.GetUgoiraMetaAsync(artworkId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ugoira_meta unavailable for {artworkId}");
        var workDir = Path.Combine(CacheRoot, artworkId);
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, "frames.zip");
        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
            await DownloadZipAsync(meta, zipPath, ct).ConfigureAwait(false);
        return zipPath;
    }

    private async Task EnsureFramesAsync(UgoiraMeta meta, string framesDir, CancellationToken ct)
    {
        if (Directory.Exists(framesDir) &&
            meta.Frames.All(f => File.Exists(Path.Combine(framesDir, f.File))))
        {
            return;
        }

        Directory.CreateDirectory(framesDir);
        var zipPath = Path.Combine(Path.GetDirectoryName(framesDir)!, "frames.zip");
        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length == 0)
            await DownloadZipAsync(meta, zipPath, ct).ConfigureAwait(false);

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var dest = Path.Combine(framesDir, entry.Name);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private async Task DownloadZipAsync(UgoiraMeta meta, string zipPath, CancellationToken ct)
    {
        var url = !string.IsNullOrEmpty(meta.OriginalSrc) ? meta.OriginalSrc : meta.Src;
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("ugoira_meta returned no zip URL.");

        var client = _httpFactory.GetClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", PixivReferer);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(zipPath);
        await src.CopyToAsync(dst, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an <a href="https://ffmpeg.org/ffmpeg-formats.html#concat-1">ffconcat</a>
    /// listing of every frame with its delay, ready to feed into <c>ffmpeg -f concat</c>.
    /// </summary>
    private static string WriteConcatFile(UgoiraMeta meta, string framesDir)
    {
        var concatPath = Path.Combine(Path.GetDirectoryName(framesDir)!, "concat.txt");
        var sb = new StringBuilder();
        sb.AppendLine("ffconcat version 1.0");
        foreach (var f in meta.Frames)
        {
            // ffconcat expects forward slashes and quoted paths.
            var rel = Path.Combine(framesDir, f.File).Replace('\\', '/');
            sb.AppendLine($"file '{rel}'");
            // delay is in milliseconds; ffmpeg wants seconds.
            sb.AppendLine($"duration {f.DelayMs / 1000.0:0.###}");
        }
        // The last entry's duration is ignored unless we restate the final file.
        var last = meta.Frames[^1];
        sb.AppendLine($"file '{Path.Combine(framesDir, last.File).Replace('\\', '/')}'");
        File.WriteAllText(concatPath, sb.ToString());
        return concatPath;
    }

    private async Task<string?> EncodeAsync(UgoiraFormat fmt, string concatPath, string outputPath, CancellationToken ct)
    {
        var exe = _ffmpeg.GetExecutablePath();
        if (exe == null)
        {
            _logger.LogWarning("ffmpeg not available — cannot encode {Fmt}.", fmt);
            return null;
        }

        var args = BuildFfmpegArgs(fmt, concatPath, outputPath, _settings.Current);
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg process.");
        var stderr = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (p.ExitCode != 0)
        {
            _logger.LogWarning("ffmpeg exited {Code} for {Fmt}: {Err}", p.ExitCode, fmt, stderr);
            return null;
        }
        return File.Exists(outputPath) ? outputPath : null;
    }

    private static string BuildFfmpegArgs(UgoiraFormat fmt, string concatPath, string outputPath, AppSettings s)
    {
        // -y to overwrite, -f concat -safe 0 to read our explicit listing.
        var common = $"-y -f concat -safe 0 -i \"{concatPath}\"";
        var crf = s.FFmpegCRF;
        return fmt switch
        {
            // Animated WebP — used for in-app preview.
            UgoiraFormat.WebP =>
                $"{common} -vcodec libwebp -lossless 0 -compression_level 4 -q:v 75 -loop 0 \"{outputPath}\"",
            // MP4 (h264 + yuv420p) — universally playable.
            UgoiraFormat.Mp4 =>
                $"{common} -vsync vfr -pix_fmt yuv420p -c:v libx264 -movflags +faststart -vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" \"{outputPath}\"",
            // WebM (VP9) — small, modern.
            UgoiraFormat.WebM =>
                $"{common} -c:v {Quote(s.FFmpegCodec)} -crf {crf} -b:v 0 \"{outputPath}\"",
            // Animated GIF with palette gen for size/quality.
            UgoiraFormat.Gif =>
                $"{common} -vf \"split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -loop 0 \"{outputPath}\"",
            // APNG.
            UgoiraFormat.Apng =>
                $"{common} -plays 0 -f apng \"{outputPath}\"",
            _ => throw new ArgumentOutOfRangeException(nameof(fmt), fmt, null),
        };
    }

    private static string Quote(string s) => string.IsNullOrWhiteSpace(s) ? "libvpx-vp9" : s;

    private static string ExtensionFor(UgoiraFormat fmt) => fmt switch
    {
        UgoiraFormat.WebP => ".webp",
        UgoiraFormat.Mp4 => ".mp4",
        UgoiraFormat.WebM => ".webm",
        UgoiraFormat.Gif => ".gif",
        UgoiraFormat.Apng => ".apng",
        _ => ".bin",
    };

    // ── Editor Support: Frame Extraction ─────────────────────────────────────

    /// <summary>
    /// Extracts a single frame from the ugoira as a static PNG image.
    /// Useful for editing a single frame in the Image Editor.
    /// </summary>
    /// <param name="artworkId">The artwork ID</param>
    /// <param name="frameIndex">0-based frame index (default: 0 = first frame)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Path to the extracted PNG file, or null if failed</returns>
    public async Task<string?> ExtractSingleFrameAsync(string artworkId, int frameIndex = 0, CancellationToken ct = default)
    {
        try
        {
            var meta = await _client.GetUgoiraMetaAsync(artworkId, ct).ConfigureAwait(false);
            if (meta == null || meta.Frames.Count == 0) return null;
            if (frameIndex < 0 || frameIndex >= meta.Frames.Count) frameIndex = 0;

            var workDir = Path.Combine(CacheRoot, artworkId);
            var framesDir = Path.Combine(workDir, "frames");
            await EnsureFramesAsync(meta, framesDir, ct).ConfigureAwait(false);

            var targetFrame = meta.Frames[frameIndex];
            var framePath = Path.Combine(framesDir, targetFrame.File);
            if (!File.Exists(framePath)) return null;

            // Copy to a dedicated "single frame" location with PNG format
            var outputPath = Path.Combine(workDir, $"frame_{frameIndex}.png");
            if (File.Exists(outputPath)) return outputPath;

            // Convert to PNG using ffmpeg if needed (frames are usually JPG)
            if (Path.GetExtension(framePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(framePath, outputPath, overwrite: true);
                return outputPath;
            }

            // Use ffmpeg to convert to PNG
            var exe = _ffmpeg.GetExecutablePath();
            if (exe == null) return null;

            var args = $"-y -i \"{framePath}\" -f image2 -vcodec png \"{outputPath}\"";
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);

            return File.Exists(outputPath) ? outputPath : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract single frame for {Id}", artworkId);
            return null;
        }
    }

    /// <summary>
    /// Gets the cover/first frame image for ugoira.
    /// This is the same as ExtractSingleFrameAsync with frameIndex=0.
    /// </summary>
    public async Task<string?> GetCoverImageAsync(string artworkId, CancellationToken ct = default)
        => await ExtractSingleFrameAsync(artworkId, frameIndex: 0, ct).ConfigureAwait(false);

    /// <summary>
    /// Extracts ALL frames from the ugoira as individual PNG files.
    /// Useful for batch processing every frame with presets.
    /// </summary>
    /// <param name="artworkId">The artwork ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of paths to all extracted PNG files, or empty list if failed</returns>
    public async Task<IReadOnlyList<string>> ExtractAllFramesAsync(string artworkId, CancellationToken ct = default)
    {
        var results = new List<string>();
        try
        {
            var meta = await _client.GetUgoiraMetaAsync(artworkId, ct).ConfigureAwait(false);
            if (meta == null || meta.Frames.Count == 0) return results;

            var workDir = Path.Combine(CacheRoot, artworkId);
            var framesDir = Path.Combine(workDir, "frames");
            await EnsureFramesAsync(meta, framesDir, ct).ConfigureAwait(false);

            var exe = _ffmpeg.GetExecutablePath();
            if (exe == null) return results;

            for (int i = 0; i < meta.Frames.Count; i++)
            {
                var frame = meta.Frames[i];
                var framePath = Path.Combine(framesDir, frame.File);
                if (!File.Exists(framePath)) continue;

                var outputPath = Path.Combine(workDir, $"frame_{i:D4}.png");
                if (File.Exists(outputPath))
                {
                    results.Add(outputPath);
                    continue;
                }

                // Convert to PNG using ffmpeg
                var args = $"-y -i \"{framePath}\" -f image2 -vcodec png \"{outputPath}\"";
                var psi = new ProcessStartInfo(exe, args)
                {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) continue;
                await p.WaitForExitAsync(ct).ConfigureAwait(false);

                if (File.Exists(outputPath))
                    results.Add(outputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract all frames for {Id}", artworkId);
        }
        return results;
    }
}
