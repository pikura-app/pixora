using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixora.Avalonia.Services;
using Pixora.Core.Models;
using Pixora.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Pixora.Avalonia.ViewModels;

public partial class ArtworkViewerViewModel : ObservableObject
{
    private readonly ArtworkPreview _artwork;
    private readonly GalleryViewModel _gallery;
    private readonly Window _owner;
    private readonly PixivClient _pixivClient;
    private readonly PixivImageLoader _imageLoader;
    private readonly PixivDownloadService _downloader;

    private IReadOnlyList<ArtworkPage> _pages = [];

    [ObservableProperty] private Bitmap? _currentPageBitmap;
    [ObservableProperty] private int _currentPageIndex;
    [ObservableProperty] private bool _isLoadingPage;
    [ObservableProperty] private bool _isDownloading;

    public string Title => _artwork.Title;
    public string ArtistName => _artwork.UserName;
    public string TypeLabel => _artwork.TypeLabel;
    public int PageCount => _pages.Count > 0 ? _pages.Count : _artwork.PageCount;
    public bool HasMultiplePages => PageCount > 1;
    public bool IsR18 => _artwork.IsR18;
    public string PageIndicator => $"{CurrentPageIndex + 1} / {PageCount}";
    public string TagsText => _artwork.Tags.Count > 0 ? string.Join("  ·  ", _artwork.Tags.Take(8)) : string.Empty;

    public ArtworkViewerViewModel(ArtworkPreview artwork, GalleryViewModel gallery, Window owner)
    {
        _artwork = artwork;
        _gallery = gallery;
        _owner = owner;
        _pixivClient = AppServices.Get<PixivClient>();
        _imageLoader = AppServices.Get<PixivImageLoader>();
        _downloader = AppServices.Get<PixivDownloadService>();
    }

    public async Task LoadFirstPageAsync()
    {
        IsLoadingPage = true;
        try
        {
            _pages = await _pixivClient.GetArtworkPagesAsync(_artwork.Id);
            OnPropertyChanged(nameof(PageCount));
            OnPropertyChanged(nameof(HasMultiplePages));
            OnPropertyChanged(nameof(PageIndicator));
            await LoadPageAsync(0);
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

        var url = _pages[index].Urls.Regular
               ?? _pages[index].Urls.Small
               ?? _pages[index].Urls.Original
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
    {
        IsDownloading = true;
        try
        {
            var pages = new[] { CurrentPageIndex };
            await _downloader.DownloadArtworkPagesAsync(_artwork, pages);
        }
        catch (Exception ex)
        {
            _ = ex;
        }
        finally { IsDownloading = false; }
    }

    [RelayCommand]
    private async Task DownloadAllPages()
    {
        IsDownloading = true;
        try { await _downloader.DownloadArtworkAsync(_artwork); }
        catch (Exception ex) { _ = ex; }
        finally { IsDownloading = false; }
    }

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

        IsDownloading = true;
        try { await _downloader.DownloadArtworkPagesAsync(_artwork, dialog.SelectedIndexes); }
        catch (Exception ex) { _ = ex; }
        finally { IsDownloading = false; }
    }

    [RelayCommand]
    private void Close() => _owner.Close();
}
