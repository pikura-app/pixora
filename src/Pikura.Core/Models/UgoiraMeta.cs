using System.Text.Json.Serialization;

namespace Pikura.Core.Models;

/// <summary>
/// Body of <c>GET /ajax/illust/{id}/ugoira_meta</c>. Pixiv returns the URL of a
/// ZIP that contains every frame as a numbered image (jpg or png), plus the
/// per-frame display delay (in milliseconds).
/// </summary>
public sealed record UgoiraMeta
{
    /// <summary>URL of the original-size frame zip (1080p+ when available).</summary>
    [JsonPropertyName("originalSrc")]
    public string OriginalSrc { get; init; } = string.Empty;

    /// <summary>URL of the 600px frame zip (always present).</summary>
    [JsonPropertyName("src")]
    public string Src { get; init; } = string.Empty;

    /// <summary>"image/jpeg" or "image/png" — frame format inside the zip.</summary>
    [JsonPropertyName("mime_type")]
    public string MimeType { get; init; } = "image/jpeg";

    /// <summary>Per-frame timing entries.</summary>
    [JsonPropertyName("frames")]
    public IReadOnlyList<UgoiraFrame> Frames { get; init; } = [];
}

/// <summary>A single ugoira frame: filename inside the zip + display delay (ms).</summary>
public sealed record UgoiraFrame
{
    [JsonPropertyName("file")] public string File { get; init; } = string.Empty;
    [JsonPropertyName("delay")] public int DelayMs { get; init; }
}

/// <summary>Output formats the ugoira pipeline can produce via ffmpeg.</summary>
public enum UgoiraFormat
{
    /// <summary>Animated WebP — used for in-app preview (small, decoded by SkiaSharp).</summary>
    WebP,
    /// <summary>MP4 (h264 + yuv420p) — universally playable in OS players.</summary>
    Mp4,
    /// <summary>WebM (VP9).</summary>
    WebM,
    /// <summary>Animated GIF (palette-optimized).</summary>
    Gif,
    /// <summary>APNG.</summary>
    Apng,
}
