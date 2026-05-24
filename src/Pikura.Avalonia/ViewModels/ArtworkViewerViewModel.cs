using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pikura.Avalonia.Services;
using Pikura.Core.Data;
using Pikura.Core.Models;
using Pikura.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Pikura.Avalonia.ViewModels;

public partial class ArtworkViewerViewModel : ObservableObject
{
    private readonly ArtworkPreview _artwork;
    private readonly GalleryViewModel _gallery;
    private readonly Window _owner;
    private readonly PixivClient _pixivClient;
    private readonly PixivImageLoader _imageLoader;
    private readonly PixivDownloadService _downloader;
    private readonly UgoiraService _ugoiraService;
    private readonly DownloadCoordinator _coordinator;
    private readonly DownloadJobRepository _jobRepository;

    private IReadOnlyList<ArtworkPage> _pages = [];

    [ObservableProperty] private Bitmap? _currentPageBitmap;
    [ObservableProperty] private int _currentPageIndex;
    [ObservableProperty] private bool _isLoadingPage;
    [ObservableProperty] private bool _isDownloading;

    // Ugoira (animated) playback
    [ObservableProperty] private string? _ugoiraPreviewPath;
    [ObservableProperty] private bool _isUgoira;
    [ObservableProperty] private bool _isPlayingUgoira = true;

    public string Title => _artwork.Title;
    public string ArtistName => _artwork.UserName;
    public string TypeLabel => _artwork.TypeLabel;
    public int PageCount => _pages.Count > 0 ? _pages.Count : _artwork.PageCount;
    public bool HasMultiplePages => PageCount > 1 && !IsUgoira;
    public bool IsR18 => _artwork.IsR18;
    public string PageIndicator => $"{CurrentPageIndex + 1} / {PageCount}";
    public string TagsText => _artwork.Tags.Count > 0 ? string.Join("  ·  ", _artwork.Tags.Take(8)) : string.Empty;

    public ArtworkViewerViewModel(ArtworkPreview artwork, GalleryViewModel gallery, Window owner)
    {
        _artwork = artwork;
        _gallery = gallery;
        _owner = owner;
        _pixivClient   = AppServices.Get<PixivClient>();
        _imageLoader   = AppServices.Get<PixivImageLoader>();
        _downloader    = AppServices.Get<PixivDownloadService>();
        _ugoiraService = AppServices.Get<UgoiraService>();
        _coordinator   = AppServices.Get<DownloadCoordinator>();
        _jobRepository = AppServices.Get<DownloadJobRepository>();
    }

    public async Task LoadFirstPageAsync()
    {
        IsLoadingPage = true;
        try
        {
            // Ugoira — animated, fetch preview via ffmpeg
            if (_artwork.IllustType == 2)
            {
                IsUgoira = true;
                OnPropertyChanged(nameof(HasMultiplePages));
                var path = await _ugoiraService.GetOrCreatePreviewAsync(_artwork.Id);
                UgoiraPreviewPath = path;
                IsPlayingUgoira = true;
            }
            else
            {
                IsUgoira = false;
                _pages = await _pixivClient.GetArtworkPagesAsync(_artwork.Id);
                OnPropertyChanged(nameof(PageCount));
                OnPropertyChanged(nameof(HasMultiplePages));
                OnPropertyChanged(nameof(PageIndicator));
                await LoadPageAsync(0);
            }
        }
        catch (Exception ex)
        {
            _ = ex;
        }
        finally
        {
            IsLoadingPage = false;
        }
    }

    private async Task LoadPageAsync(int index)
    {
        if (_pages.Count == 0 || index < 0 || index >= _pages.Count) return;
        IsLoadingPage = true;

        var url = _pages[index].Urls.Original
               ?? _pages[index].Urls.Regular
               ?? _pages[index].Urls.Small
               ?? _pages[index].Urls.ThumbMini;

        if (!string.IsNullOrEmpty(url))
        {
            var bytes = await _imageLoader.FetchBytesAsync(url);
            if (bytes != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var ms = new MemoryStream(bytes);
                    CurrentPageBitmap = new Bitmap(ms);
                });
            }
        }

        CurrentPageIndex = index;
        OnPropertyChanged(nameof(PageIndicator));
        IsLoadingPage = false;
    }

    [RelayCommand]
    private async Task PrevPage()
    {
        if (CurrentPageIndex > 0)
            await LoadPageAsync(CurrentPageIndex - 1);
    }

    [RelayCommand]
    private async Task NextPage()
    {
        if (CurrentPageIndex < PageCount - 1)
            await LoadPageAsync(CurrentPageIndex + 1);
    }

    [RelayCommand]
    private async Task DownloadCurrentPage()
        => await DownloadWithJobAsync([CurrentPageIndex], $"{_artwork.Title} (p{CurrentPageIndex + 1})");

    [RelayCommand]
    private async Task DownloadAllPages()
        => await DownloadWithJobAsync(null, _artwork.Title);

    [RelayCommand]
    private async Task DownloadRange()
    {
        if (PageCount <= 1) { await DownloadAllPages(); return; }
        var dialog = new Views.Dialogs.RangePickerDialog(
            title: $"Page range — {_artwork.Title}",
            description: $"This artwork has {PageCount} pages (0-based indexes). " +
                         "Examples: \"0\" (first), \"0-2\" (first 3), \"0,2,4\".",
            maxInclusive: PageCount - 1,
            placeholder: $"0-{PageCount - 1}");
        dialog.ShowInTaskbar = false;
        await dialog.ShowDialog(_owner);
        if (dialog.SelectedIndexes.Count == 0) return;
        await DownloadWithJobAsync(dialog.SelectedIndexes, $"{_artwork.Title} (range)");
    }

    private async Task DownloadWithJobAsync(IReadOnlyCollection<int>? pages, string jobName)
    {
        IsDownloading = true;
        var target = new DownloadTarget
        {
            TargetId = _artwork.Id, Name = _artwork.Title,
            ThumbnailUrl = _artwork.ThumbnailUrl, UserName = _artwork.UserName,
            UserId = _artwork.UserId, Type = TargetType.Artwork, Status = TargetStatus.Running
        };
        var job = new DownloadJob
        {
            Name = jobName, Type = DownloadJobType.ImageId,
            Status = JobStatus.Running, StartedAt = DateTime.UtcNow,
            Targets = [target]
        };
        await _jobRepository.SaveJobAsync(job);
        _coordinator.NotifyJobStarted(job);
        try
        {
            var files = await _downloader.DownloadArtworkPagesAsync(_artwork, pages);
            target.Status = TargetStatus.Completed;
            target.DownloadedItems = files.Count;
            job.Status = JobStatus.Completed;
            job.OutputFolder = files.Count > 0 ? Path.GetDirectoryName(files[0]) : null;
        }
        catch (Exception ex)
        {
            target.Status = TargetStatus.Failed;
            target.ErrorMessage = ex.Message;
            job.Status = JobStatus.Failed;
        }
        finally
        {
            job.CompletedAt = DateTime.UtcNow;
            IsDownloading = false;
            _ = Task.Run(async () => { await _jobRepository.SaveJobAsync(job); _coordinator.NotifyJobSaved(job); });
        }
    }

    [RelayCommand]
    private void Close() => _owner.Close();
}
