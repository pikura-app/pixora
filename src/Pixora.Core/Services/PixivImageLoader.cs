using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Pixora.Core.Http;

namespace Pixora.Core.Services;

/// <summary>
/// Downloads Pixiv CDN image bytes with the mandatory
/// <c>Referer: https://www.pixiv.net/</c> header. Uses a 3-tier cache:
/// (1) in-memory deduplicated tasks (instant for in-flight + recent fetches),
/// (2) on-disk byte cache under <c>%LOCALAPPDATA%\PixivUtil\imgcache</c>
/// so thumbnails survive app restarts, and (3) a network fallback when
/// neither cache hits. A semaphore caps concurrent network fetches so
/// scrolling a large gallery doesn't open hundreds of sockets at once.
/// </summary>
public sealed class PixivImageLoader
{
    private const string Referer = "https://www.pixiv.net/";
    private const int MaxMemoryEntries = 1024;
    private const int MaxConcurrentFetches = 24;
    // 14 days on disk before re-fetching (Pixiv URLs are content-hashed so
    // anything we cached under a given URL is still valid as long as the
    // URL is).
    private static readonly TimeSpan DiskCacheTtl = TimeSpan.FromDays(14);

    private static readonly string DiskCacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pixora", "imgcache");

    private readonly PixivHttpClientFactory _factory;
    private readonly ILogger<PixivImageLoader> _logger;
    private readonly ConcurrentDictionary<string, Task<byte[]?>> _memoryCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(MaxConcurrentFetches, MaxConcurrentFetches);

    public PixivImageLoader(PixivHttpClientFactory factory, ILogger<PixivImageLoader> logger)
    {
        _factory = factory;
        _logger = logger;
        try { Directory.CreateDirectory(DiskCacheRoot); } catch { /* best-effort */ }
    }

    public Task<byte[]?> FetchBytesAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return Task.FromResult<byte[]?>(null);

        // Hard cap on the in-memory map so it can't grow without bound.
        if (_memoryCache.Count > MaxMemoryEntries)
        {
            foreach (var kv in _memoryCache.Take(_memoryCache.Count - MaxMemoryEntries / 2))
                _memoryCache.TryRemove(kv.Key, out _);
        }

        // GetOrAdd is not atomic: if the existing task is faulted or cancelled,
        // replace it so the next caller gets a fresh attempt.
        if (_memoryCache.TryGetValue(url, out var existing))
        {
            if (existing.IsCompletedSuccessfully) return existing;
            if (existing.IsFaulted || existing.IsCanceled)
                _memoryCache.TryRemove(new KeyValuePair<string, Task<byte[]?>>(url, existing));
        }

        // Use CancellationToken.None for the cached task so one caller
        // cancelling doesn't poison the shared result for others.
        return _memoryCache.GetOrAdd(url, u => FetchInternalAsync(u, CancellationToken.None));
    }

    private async Task<byte[]?> FetchInternalAsync(string url, CancellationToken ct)
    {
        // Try the on-disk cache first — avoids hitting Pixiv's CDN at all
        // for thumbnails the user has seen before.
        var diskPath = GetDiskPath(url);
        try
        {
            if (File.Exists(diskPath))
            {
                var fi = new FileInfo(diskPath);
                if (fi.Length > 0 && DateTime.UtcNow - fi.LastWriteTimeUtc <= DiskCacheTtl)
                {
                    return await File.ReadAllBytesAsync(diskPath, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disk-cache read failed for {Url}", url);
        }

        // Throttle network fetches so we don't open dozens of sockets at once.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        byte[]? bytes;
        try
        {
            bytes = await DownloadAsync(url, ct).ConfigureAwait(false);
        }
        finally
        {
            // Release immediately after download so the slot is free for the next fetch
            // while we do the (potentially slow) disk write below.
            _gate.Release();
        }
        if (bytes is not null && bytes.Length > 0)
        {
            try
            {
                var dir = Path.GetDirectoryName(diskPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(diskPath, bytes, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Disk-cache write failed for {Url}", url); }
        }
        return bytes;
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = _factory.GetClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Referer", Referer);
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Thumbnail {Url} -> {Code}", url, resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail fetch failed for {Url}", url);
            return null;
        }
    }

    private static string GetDiskPath(string url)
    {
        // SHA-1 over the URL gives us a deterministic, filesystem-safe key
        // and avoids issues with URLs containing chars Windows rejects.
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(Encoding.UTF8.GetBytes(url), hash);
        var name = Convert.ToHexString(hash);
        // Two-char shard prevents any single dir from exploding to 100k+ files.
        return Path.Combine(DiskCacheRoot, name[..2], name);
    }
}
