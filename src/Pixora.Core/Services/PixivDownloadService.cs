using Microsoft.Extensions.Logging;
using Pixora.Core.Http;
using Pixora.Core.Models;
using Pixora.Core.Settings;

namespace Pixora.Core.Services;

/// <summary>Per-file/per-artwork download progress callbacks.</summary>
public sealed record DownloadProgress(string ArtworkId, int PageIndex, int TotalPages, long BytesSoFar, long? TotalBytes);

/// <summary>
/// Streams original-size images from i.pximg.net into a local folder.
/// Honors the mandatory <c>Referer: https://www.pixiv.net/</c> header.
/// </summary>
public sealed class PixivDownloadService
{
    private const string PixivReferer = "https://www.pixiv.net/";
    private const int BufferSize = 81920;

    private readonly PixivClient _client;
    private readonly PixivHttpClientFactory _httpFactory;
    private readonly SettingsService _settings;
    private readonly ImageResizeService _resizeService;
    private readonly ILogger<PixivDownloadService> _logger;

    private static readonly string DiagLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Pixora", "download.log");

    private static void Diag(string s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagLog)!);
            File.AppendAllText(DiagLog, $"[{DateTime.Now:HH:mm:ss.fff}] {s}\n");
        }
        catch { }
    }

    public PixivDownloadService(
        PixivClient client,
        PixivHttpClientFactory httpFactory,
        SettingsService settings,
        ImageResizeService resizeService,
        ILogger<PixivDownloadService> logger)
    {
        _client = client;
        _httpFactory = httpFactory;
        _settings = settings;
        _resizeService = resizeService;
        _logger = logger;
    }

    /// <summary>
    /// Downloads every page of <paramref name="artwork"/> to
    /// <c>{DownloadRoot}/{UserName}/{ArtworkId}_pN.ext</c>.
    /// </summary>
    public Task<IReadOnlyList<string>> DownloadArtworkAsync(
        ArtworkPreview artwork,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default,
        SettingsOverride? overrideSettings = null)
    {
        // null = "all pages"
        return DownloadArtworkPagesAsync(artwork, pageIndexes: null, progress, ct, overrideSettings);
    }

    /// <summary>
    /// Downloads the specified 0-based page indexes of <paramref name="artwork"/>.
    /// Passing <c>null</c> downloads every page.
    /// When <paramref name="overrideSettings"/> is supplied (and not UseGlobalSettings),
    /// its values take precedence over the corresponding global AppSettings values.
    /// </summary>
    public async Task<IReadOnlyList<string>> DownloadArtworkPagesAsync(
        ArtworkPreview artwork,
        IReadOnlyCollection<int>? pageIndexes,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default,
        SettingsOverride? overrideSettings = null)
    {
        Diag($"=== START artwork {artwork.Id} title='{artwork.Title}' previewPageCount={artwork.PageCount} requestedPages={(pageIndexes is null ? "ALL" : string.Join(",", pageIndexes))}");
        var pages = await _client.GetArtworkPagesAsync(artwork.Id, ct).ConfigureAwait(false);
        Diag($"GetArtworkPagesAsync returned {pages.Count} pages");
        for (var pi = 0; pi < pages.Count; pi++)
        {
            Diag($"  page[{pi}] Original={pages[pi].Urls.Original}");
        }
        if (pages.Count == 0)
        {
            _logger.LogWarning("Artwork {Id} returned no pages", artwork.Id);
            Diag("ABORT: no pages");
            return [];
        }

        var s = _settings.Current;
        var ovr = overrideSettings != null && !overrideSettings.UseGlobalSettings ? overrideSettings : null;

        // Resolve effective values: override wins over global when set
        var effectiveDownloadRoot = !string.IsNullOrWhiteSpace(ovr?.DownloadRoot) ? ovr!.DownloadRoot! : s.DownloadRoot;
        var effectiveFolderTemplate = ovr?.FolderTemplate ?? s.FolderTemplate;
        var effectiveFilenameTemplate = ovr?.FilenameTemplate ?? s.FilenameTemplate;
        var effectiveDateFormat = ovr?.DateFormat ?? s.DateFormat;
        var effectiveCreateSubfolder = ovr?.CreateSubfolderPerSubmission ?? s.CreateSubfolderPerSubmission;
        var effectiveSeparateR18 = ovr?.SeparateR18Folder ?? s.SeparateR18Folder;
        var allowRedownload = ovr?.AllowRedownload ?? false;

        var template = new FilenameTemplate(effectiveDateFormat);
        var ctx0 = new FilenameContext
        {
            Artwork = artwork,
            PageIndex = 0,
            PageCount = pages.Count,
            OriginalUrl = string.Empty,
        };

        var targetDir = effectiveDownloadRoot;

        // Resolve folder template
        var folderPath = template.Resolve(effectiveFolderTemplate, ctx0);
        targetDir = Path.Combine(targetDir, folderPath);

        // Optionally separate R-18 into its own subfolder
        if (effectiveSeparateR18 && artwork.IsR18)
            targetDir = Path.Combine(targetDir, "R-18");

        // Optionally put multi-page artworks into their own subfolder
        if (effectiveCreateSubfolder && pages.Count > 1)
        {
            var subfolderName = SafeName($"{artwork.Id}_{artwork.Title}");
            Diag($"SafeName('{artwork.Id}_{artwork.Title}') => '{subfolderName}'");
            targetDir = Path.Combine(targetDir, subfolderName);
        }

        // Override targetDir if CustomOutputFolder is set (from preset)
        if (!string.IsNullOrWhiteSpace(ovr?.CustomOutputFolder))
        {
            targetDir = ovr!.CustomOutputFolder!;
            Diag($"Using CustomOutputFolder: {targetDir}");
        }

        Diag($"Creating directory: {targetDir} (override={(ovr != null ? "yes" : "no")})");
        try
        {
            Directory.CreateDirectory(targetDir);
            Diag($"Directory created/verified: {targetDir}");
        }
        catch (Exception ex)
        {
            Diag($"FAILED to create directory: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        var targetIndexes = pageIndexes is null
            ? Enumerable.Range(0, pages.Count).ToList()
            : pageIndexes.Where(i => i >= 0 && i < pages.Count).Distinct().OrderBy(i => i).ToList();

        // If the user's filename template doesn't include any page-differentiating
        // token but the artwork has multiple pages, auto-append _p{N} so pages
        // don't collide and overwrite each other. Matches Nandaka PixivUtil2 behavior.
        var filenameTemplate = effectiveFilenameTemplate;
        var differentiates = filenameTemplate.Contains("%page_index%", StringComparison.OrdinalIgnoreCase)
            || filenameTemplate.Contains("%page_number%", StringComparison.OrdinalIgnoreCase)
            || filenameTemplate.Contains("%urlFilename%", StringComparison.OrdinalIgnoreCase);
        if (pages.Count > 1 && !differentiates)
        {
            filenameTemplate += "_p%page_index%";
            Diag($"Multi-page artwork without page token in template — auto-appending _p%page_index%");
        }

        Diag($"targetDir={targetDir} targetIndexes=[{string.Join(",", targetIndexes)}] folderTemplate='{effectiveFolderTemplate}' filenameTemplate='{filenameTemplate}'");
        var savedFiles = new List<string>(targetIndexes.Count);
        var batchTotal = targetIndexes.Count;
        for (var batchIdx = 0; batchIdx < targetIndexes.Count; batchIdx++)
        {
            var i = targetIndexes[batchIdx];
            ct.ThrowIfCancellationRequested();
            var pageUrl = pages[i].Urls.Original;
            if (string.IsNullOrWhiteSpace(pageUrl))
            {
                Diag($"  page[{i}] SKIP: no original URL");
                continue;
            }

            var ext = Path.GetExtension(new Uri(pageUrl).AbsolutePath);
            var rawName = template.Resolve(filenameTemplate, new FilenameContext
            {
                Artwork = artwork,
                PageIndex = i,
                PageCount = pages.Count,
                OriginalUrl = pageUrl,
            });
            var fileName = SafeName(rawName) + ext;
            var destPath = Path.Combine(targetDir, fileName);
            Diag($"  page[{i}] destPath={destPath}");

            if (!allowRedownload && File.Exists(destPath) && new FileInfo(destPath).Length > 0)
            {
                Diag($"  page[{i}] EXISTS, skipping (size={new FileInfo(destPath).Length})");
                savedFiles.Add(destPath);
                progress?.Report(new DownloadProgress(artwork.Id, batchIdx, batchTotal, new FileInfo(destPath).Length, null));
                continue;
            }

            try
            {
                await DownloadFileAsync(pageUrl, destPath, batchIdx, batchTotal, artwork.Id, progress, ct)
                    .ConfigureAwait(false);
                Diag($"  page[{i}] DOWNLOADED size={(File.Exists(destPath) ? new FileInfo(destPath).Length : -1)}");

                // Apply preset post-processing if specified (resize, color adjustments, format conversion)
                var preset = ovr?.ImagePreset;
                bool needsResize = preset != null
                    && preset.DevicePreset != DevicePreset.Original
                    && preset.DevicePreset != DevicePreset.None;
                bool needsAdjustments = preset?.Adjustments?.HasAdjustments == true;
                bool needsCrop = preset?.CropRegion != null;
                bool needsFormatConvert = preset != null
                    && preset.ResizeSettings.OutputFormat != ResizeOutputFormat.KeepOriginal;
                if (preset != null && (needsResize || needsAdjustments || needsCrop || needsFormatConvert))
                {
                    Diag($"  page[{i}] POST-PROCESS with preset '{preset.Name}' (SaveAsNew={preset.SaveAsNew}, CustomFolder={preset.CustomOutputFolder ?? "<null>"})");
                    try
                    {
                        var processedPath = await _resizeService.ProcessAsync(destPath, preset, ct).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(processedPath) && File.Exists(processedPath))
                        {
                            Diag($"  page[{i}] PROCESSED -> {processedPath}");
                            // If processed file is in a different location, return that path so the user sees it
                            savedFiles.Add(processedPath);

                            // When SaveAsNew=true, original (unprocessed) at destPath is preserved by ProcessAsync.
                            // Delete it unless user explicitly opted in via AlsoDownloadUnprocessed.
                            if (preset.SaveAsNew
                                && !preset.AlsoDownloadUnprocessed
                                && !string.Equals(processedPath, destPath, StringComparison.OrdinalIgnoreCase)
                                && File.Exists(destPath))
                            {
                                try
                                {
                                    File.Delete(destPath);
                                    Diag($"  page[{i}] removed unprocessed copy at {destPath} (AlsoDownloadUnprocessed=false)");
                                }
                                catch (Exception delEx)
                                {
                                    Diag($"  page[{i}] failed to remove unprocessed copy: {delEx.Message}");
                                }
                            }
                        }
                        else
                        {
                            Diag($"  page[{i}] PROCESS returned no file, keeping original");
                            savedFiles.Add(destPath);
                        }
                    }
                    catch (Exception procEx)
                    {
                        Diag($"  page[{i}] PROCESS FAILED: {procEx.GetType().Name}: {procEx.Message}");
                        _logger.LogError(procEx, "Failed to apply preset to {Path}", destPath);
                        savedFiles.Add(destPath); // keep original even if processing fails
                    }
                }
                else
                {
                    savedFiles.Add(destPath);
                }
            }
            catch (Exception ex)
            {
                Diag($"  page[{i}] FAILED: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
        Diag($"=== DONE artwork {artwork.Id}: saved {savedFiles.Count}/{pages.Count}");
        return savedFiles;
    }

    private async Task DownloadFileAsync(
        string url, string destPath,
        int pageIndex, int totalPages, string artworkId,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var client = _httpFactory.GetClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Referer", PixivReferer);

        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var expectedTotal = resp.Content.Headers.ContentLength;
        var tmp = destPath + ".part";
        long readTotal = 0;
        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await src.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    readTotal += read;
                    progress?.Report(new DownloadProgress(artworkId, pageIndex, totalPages, readTotal, expectedTotal));
                }
                await dst.FlushAsync(ct).ConfigureAwait(false);
            }

            // ---- Integrity checks (fail-fast: deletes the .part file on any mismatch) ----
            // 1) Empty file is always corrupt.
            if (readTotal == 0)
                throw new IOException($"Empty download for {url}");

            // 2) Byte count must match Content-Length (when the server sent one).
            //    Servers may legitimately omit it for chunked responses.
            if (expectedTotal.HasValue && readTotal != expectedTotal.Value)
                throw new IOException($"Truncated download: got {readTotal} bytes, expected {expectedTotal.Value} for {url}");

            // 3) On-disk size must equal what we counted.
            var fi = new FileInfo(tmp);
            if (!fi.Exists || fi.Length != readTotal)
                throw new IOException($"On-disk size mismatch for {tmp}: file={(fi.Exists ? fi.Length : -1)} counter={readTotal}");

            // 4) Image magic bytes — catches HTML error pages saved as .jpg, etc.
            if (!await HasValidImageHeaderAsync(tmp, ct).ConfigureAwait(false))
                throw new IOException($"Downloaded file is not a recognized image (HTML error page?): {url}");

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmp, destPath);
        }
        catch
        {
            // Don't leave a half-written .part file around — the next attempt
            // would skip it because the existence check uses Length>0 only on
            // the final path, but a stale .part can confuse retries.
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            throw;
        }
    }

    /// <summary>
    /// Verifies the file starts with magic bytes for one of the formats Pixiv
    /// serves (JPEG / PNG / GIF / WebP). Returns false on truncated headers
    /// or anything else (e.g. a JSON/HTML error response saved as bytes).
    /// </summary>
    private static async Task<bool> HasValidImageHeaderAsync(string path, CancellationToken ct)
    {
        var head = new byte[12];
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var n = await fs.ReadAsync(head.AsMemory(0, 12), ct).ConfigureAwait(false);
        if (n < 4) return false;

        // JPEG: FF D8 FF
        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return true;
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (n >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                   && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A) return true;
        // GIF: "GIF87a" or "GIF89a"
        if (n >= 6 && head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x38
                   && (head[4] == 0x37 || head[4] == 0x39) && head[5] == 0x61) return true;
        // WebP: "RIFF" .... "WEBP"
        if (n >= 12 && head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
                    && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) return true;
        // BMP: "BM"
        if (head[0] == 0x42 && head[1] == 0x4D) return true;
        return false;
    }

    /// <summary>
    /// Returns true if any file whose name starts with <paramref name="artworkId"/> exists
    /// inside the download directory (recursive). Checks both DownloadRoot and CustomOutputFolder.
    /// Used to detect already-downloaded artworks before showing a re-download confirmation dialog.
    /// When <paramref name="fileTypeFilter"/> is "processed", only checks for files with "_processed" suffix.
    /// When "unprocessed", only checks for files without "_processed" suffix.
    /// When "all" (default), checks for any file starting with the artwork ID.
    /// </summary>
    public bool HasExistingFiles(string artworkId, SettingsOverride? overrideSettings = null, string fileTypeFilter = "all")
    {
        var s = _settings.Current;
        var ovr = overrideSettings != null && !overrideSettings.UseGlobalSettings ? overrideSettings : null;
        
        // Helper to check if filename matches the criteria
        bool MatchesPattern(string filename)
        {
            var name = Path.GetFileNameWithoutExtension(filename);
            // Must start with artwork ID
            if (!name.StartsWith(artworkId, StringComparison.OrdinalIgnoreCase))
                return false;
            
            // Apply file type filter
            return fileTypeFilter switch
            {
                "processed" => name.Contains("_processed", StringComparison.OrdinalIgnoreCase),
                "unprocessed" => !name.Contains("_processed", StringComparison.OrdinalIgnoreCase),
                _ => true // "all" - match any file starting with artwork ID
            };
        }
        
        // Check CustomOutputFolder first if set
        if (!string.IsNullOrWhiteSpace(ovr?.CustomOutputFolder))
        {
            var customFolder = ovr!.CustomOutputFolder!;
            if (Directory.Exists(customFolder))
            {
                try
                {
                    var found = Directory
                        .EnumerateFiles(customFolder, "*", SearchOption.AllDirectories)
                        .Any(MatchesPattern);
                    if (found) return true;
                }
                catch { /* continue to check DownloadRoot */ }
            }
        }
        
        // Check DownloadRoot (either from override or global)
        var root = !string.IsNullOrWhiteSpace(ovr?.DownloadRoot) ? ovr!.DownloadRoot! : s.DownloadRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return false;
        try
        {
            return Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Any(MatchesPattern);
        }
        catch { return false; }
    }

    private static readonly HashSet<string> WindowsReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Sanitizes a string for use as a Windows path segment. Preserves Unicode
    /// characters (Japanese kanji/kana, emoji, etc.) since NTFS supports them,
    /// but replaces characters that Windows actually rejects, trims trailing
    /// dots/spaces (which Windows silently strips), and avoids reserved names.
    /// </summary>
    private static string SafeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            // Strip control chars and Windows-invalid chars; keep all printable Unicode.
            if (ch < 0x20 || Array.IndexOf(invalid, ch) >= 0)
                sb.Append('_');
            else
                sb.Append(ch);
        }

        // Windows strips trailing dots/spaces from path segments at create time,
        // which then breaks subsequent file writes. Trim both ends.
        var cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        if (string.IsNullOrEmpty(cleaned)) return "unknown";

        // Avoid Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
        // even when followed by an extension (CON.txt is also rejected).
        var rootName = cleaned;
        var dot = rootName.IndexOf('.');
        if (dot > 0) rootName = rootName[..dot];
        if (WindowsReserved.Contains(rootName)) cleaned = "_" + cleaned;

        // Cap individual path-segment length so we don't blow Windows MAX_PATH.
        // Pixiv titles can be very long; 120 chars per segment is plenty.
        const int MaxSegment = 120;
        if (cleaned.Length > MaxSegment)
            cleaned = cleaned[..MaxSegment].TrimEnd('.', ' ');

        return cleaned;
    }
}
