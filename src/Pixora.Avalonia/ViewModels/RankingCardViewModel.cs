using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Pixora.Core.Models;
using Pixora.Core.Services;

namespace Pixora.Avalonia.ViewModels;

public partial class RankingCardViewModel : ObservableObject
{
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isBlurred;

    public int Rank { get; }
    public string Id { get; }
    public string Title { get; }
    public string UserName { get; }
    public string UserId { get; }
    public int PageCount { get; }
    public bool IsMultiPage => PageCount > 1;
    public bool IsR18 { get; }
    public bool IsR18G { get; }
    public int YesterdayRank { get; }
    public int RatingCount { get; }
    public int ViewCount { get; }
    public string? ThumbnailUrl { get; }
    public string Date { get; }
    public double ClampedAspectRatio { get; }
    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<string> TopTags => Tags.Count > 3 ? Tags.Take(3).ToList() : Tags;

    public string RankChangeDisplay => (YesterdayRank == 0) ? "New"
        : (YesterdayRank - Rank) switch
        {
            > 0 => $"▲{YesterdayRank - Rank}",
            < 0 => $"▼{Rank - YesterdayRank}",
            _ => "—"
        };

    public string RankChangeForeground => (YesterdayRank == 0) ? "#818CF8"
        : (YesterdayRank - Rank) switch
        {
            > 0 => "#4ADE80",
            < 0 => "#F87171",
            _ => "#6B7280"
        };

    public RankingEntry Entry { get; }
    public ArtworkPreview ToPreview() => Entry.ToPreview();

    public RankingCardViewModel(RankingEntry entry)
    {
        Entry = entry;
        Rank = entry.Rank;
        Id = entry.IllustId.ToString();
        Title = entry.Title;
        UserName = entry.UserName;
        UserId = entry.UserId.ToString();
        PageCount = int.TryParse(entry.IllustPageCount, out var p) ? p : 1;
        // Check both API flag and tags for R-18 detection (some AI content may lack the flag)
        var hasR18Tag = entry.Tags.Any(t => t.Contains("R-18", StringComparison.OrdinalIgnoreCase));
        var hasR18GTag = entry.Tags.Any(t => t.Contains("R-18G", StringComparison.OrdinalIgnoreCase));
        IsR18 = entry.ContentType.Sexual || hasR18Tag || hasR18GTag;
        IsR18G = entry.ContentType.Grotesque || entry.ContentType.Violent || hasR18GTag;
        YesterdayRank = entry.YesRank;
        RatingCount = entry.RatingCount;
        ViewCount = entry.ViewCount;
        ThumbnailUrl = UpgradeThumbnailUrl(entry.ThumbnailUrl);
        Date = entry.Date.Length == 8
            ? $"{entry.Date[..4]}-{entry.Date[4..6]}-{entry.Date[6..8]}"
            : entry.Date;
        var rawAspect = entry.Height > 0 && entry.Width > 0
            ? (double)entry.Height / entry.Width : 1.0;
        ClampedAspectRatio = Math.Min(Math.Max(rawAspect, 0.5), 2.5);
        Tags = entry.Tags;
    }

    private static string? UpgradeThumbnailUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        return url.Replace("_square1200", "_master1200")
                  .Replace("/250x250_80_a2/", "/540x540_70/");
    }

    public async Task LoadThumbnailAsync(PixivImageLoader loader, ThumbnailSize size = ThumbnailSize.Medium, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ThumbnailUrl)) return;
        try
        {
            // Use the decoded bitmap cache with size hint
            var skBitmap = await loader.FetchBitmapAsync(ThumbnailUrl, size, ct);
            if (skBitmap is null || ct.IsCancellationRequested) return;

            // Convert SKBitmap to Avalonia Bitmap off the UI thread
            var bmp = await Task.Run(() =>
            {
                using var data = skBitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                if (data is null) return null;
                using var ms = new MemoryStream(data.ToArray());
                return new Bitmap(ms);
            }, ct);

            skBitmap.Dispose(); // Dispose the copy we received

            if (bmp is not null && !ct.IsCancellationRequested)
                await Dispatcher.UIThread.InvokeAsync(() => Thumbnail = bmp);
        }
        catch (OperationCanceledException) { }
        catch { }
    }
}
