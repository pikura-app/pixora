using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pixora.Core.Models;
using Pixora.Core.Services;
using Pixora.Avalonia.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Pixora.Avalonia.ViewModels;

public partial class ArtworkDetailViewModel : ViewModelBase
{
    private readonly PixivClient _pixivClient;
    private readonly PixivImageLoader _imageLoader;
    private readonly PixivDownloadService _downloadService;
    private readonly NavigationService _navigationService;
    private readonly DialogService _dialogService;

    [ObservableProperty]
    private ArtworkPreview? _artwork;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _currentPageIndex;

    [ObservableProperty]
    private int _totalPages;

    [ObservableProperty]
    private bool _canGoToPreviousPage;

    [ObservableProperty]
    private bool _canGoToNextPage;

    public IReadOnlyList<string> Tags => Artwork?.Tags ?? Array.Empty<string>();

    public string? ArtistName => Artwork?.UserName;
    public string? ArtistId => Artwork?.UserId;

    public ArtworkDetailViewModel(
        PixivClient pixivClient,
        PixivImageLoader imageLoader,
        PixivDownloadService downloadService,
        NavigationService navigationService,
        DialogService dialogService)
    {
        _pixivClient = pixivClient;
        _imageLoader = imageLoader;
        _downloadService = downloadService;
        _navigationService = navigationService;
        _dialogService = dialogService;
    }

    public void Initialize(ArtworkPreview artwork)
    {
        Artwork = artwork;
        CurrentPageIndex = 0;
        _ = LoadArtworkPagesAsync();
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.NavigateTo<GalleryViewModel>();
    }

    [RelayCommand]
    private async Task DownloadCurrentPageAsync()
    {
        if (Artwork == null) return;

        try
        {
            StatusMessage = "Downloading current page...";
            await Task.Delay(1000); // Simulate download
            StatusMessage = "Download completed (simulated)";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Download failed");
            StatusMessage = "Download failed";
            await _dialogService.ShowMessageAsync("Error", "Download failed. Please try again.");
        }
    }

    [RelayCommand]
    private async Task DownloadAllPagesAsync()
    {
        if (Artwork == null) return;

        try
        {
            StatusMessage = "Downloading all pages...";
            await Task.Delay(2000); // Simulate download
            StatusMessage = "Download completed (simulated)";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Download failed");
            StatusMessage = "Download failed";
            await _dialogService.ShowMessageAsync("Error", "Download failed. Please try again.");
        }
    }

    [RelayCommand]
    private void GoToPreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPageIndex--;
            UpdatePageNavigation();
        }
    }

    [RelayCommand]
    private void GoToNextPage()
    {
        if (CanGoToNextPage)
        {
            CurrentPageIndex++;
            UpdatePageNavigation();
        }
    }

    private async Task LoadArtworkPagesAsync()
    {
        if (Artwork == null) return;

        IsLoading = true;
        StatusMessage = "Loading artwork pages...";

        try
        {
            var pages = await _pixivClient.GetArtworkPagesAsync(Artwork.Id);
            TotalPages = pages.Count;
            UpdatePageNavigation();
            StatusMessage = $"Loaded {TotalPages} pages";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load artwork pages");
            StatusMessage = "Failed to load artwork pages";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdatePageNavigation()
    {
        CanGoToPreviousPage = CurrentPageIndex > 0;
        CanGoToNextPage = CurrentPageIndex < TotalPages - 1;
    }

    [RelayCommand]
    private async Task SearchByTagAsync(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        // Navigate back to gallery and trigger global search
        _navigationService.NavigateTo<GalleryViewModel>();

        // Wait for navigation
        await Task.Delay(100);

        // Get the GalleryViewModel and trigger search
        var galleryVm = AppServices.Get<GalleryViewModel>();
        if (galleryVm is not null)
        {
            await galleryVm.SearchByTagAsync(tag);
        }
    }

    [RelayCommand]
    private async Task SearchByArtistAsync()
    {
        if (Artwork?.UserId is null) return;

        _navigationService.NavigateTo<GalleryViewModel>();

        await Task.Delay(100);
        var galleryVm = AppServices.Get<GalleryViewModel>();
        if (galleryVm is not null)
        {
            await galleryVm.LoadArtistByIdAsync(Artwork.UserId);
        }
    }
}
