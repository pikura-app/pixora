using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixora.Core.Models;
using Pixora.Core.Services;
using Pixora.Avalonia.Services;

namespace Pixora.Avalonia.ViewModels;

public partial class DownloadBySearchViewModel : ViewModelBase
{
    private readonly PixivClient _client;
    private readonly DownloadCoordinator _coordinator;
    private readonly DialogService _dialogService;
    private readonly PixivImageLoader _imageLoader;

    [ObservableProperty] private string _searchKeyword = string.Empty;
    [ObservableProperty] private int _maxResults = 100;
    [ObservableProperty] private string _searchMode = "safe"; // safe, r18, all
    [ObservableProperty] private string _sortOrder = "date_d"; // date_d, popular_d
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private int _resultCount = 0;
    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();
    public bool HasResults => SearchResults.Count > 0;

    public DownloadBySearchViewModel(
        PixivClient client,
        DownloadCoordinator coordinator,
        DialogService dialogService,
        PixivImageLoader imageLoader)
    {
        _client = client;
        _coordinator = coordinator;
        _dialogService = dialogService;
        _imageLoader = imageLoader;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            await _dialogService.ShowMessageAsync("Error", "Please enter a search keyword.");
            return;
        }

        try
        {
            IsSearching = true;
            SearchResults.Clear();

            var result = await _client.SearchArtworksAsync(
                SearchKeyword,
                SortOrder,
                SearchMode,
                page: 1);

            if (result?.IllustManga?.Data != null)
            {
                var artworks = result.IllustManga.Data.Take(MaxResults).ToList();
                foreach (var artwork in artworks)
                {
                    var item = new SearchResultItem
                    {
                        Artwork = artwork,
                        Title = artwork.Title,
                        ArtistName = artwork.UserName,
                        PageCount = artwork.PageCount,
                        IsR18 = artwork.IsR18,
                        IsAiGenerated = artwork.IsAiGenerated
                    };

                    // Load thumbnail asynchronously
                    if (!string.IsNullOrEmpty(artwork.ThumbnailUrl))
                    {
                        _ = LoadThumbnailAsync(item, artwork.ThumbnailUrl);
                    }

                    SearchResults.Add(item);
                }
                ResultCount = SearchResults.Count;
            }
            else
            {
                await _dialogService.ShowMessageAsync("No Results", "No artworks found for this search.");
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Search failed: {ex.Message}");
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    private async Task LoadThumbnailAsync(SearchResultItem item, string thumbnailUrl)
    {
        try
        {
            var bytes = await _imageLoader.FetchBytesAsync(thumbnailUrl);
            if (bytes != null)
            {
                item.Thumbnail = new Bitmap(new System.IO.MemoryStream(bytes));
            }
        }
        catch
        {
            // Thumbnail load failed - continue without it
        }
    }

    [RelayCommand]
    private void ClearResults()
    {
        SearchResults.Clear();
        SearchKeyword = string.Empty;
        ResultCount = 0;
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (!HasResults)
        {
            await _dialogService.ShowMessageAsync("No Items", "No search results to download. Please search first.");
            return;
        }

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Start Download",
            $"Download {ResultCount} artworks from search results?");
        
        if (!confirmed) return;

        try
        {
            // Create download targets
            var targets = new List<DownloadTarget>();
            foreach (var item in SearchResults)
            {
                targets.Add(new DownloadTarget
                {
                    TargetId = item.Artwork.Id,
                    Name = $"{item.Artwork.Title} by {item.Artwork.UserName}",
                    Type = TargetType.Artwork
                });
            }

            // Create download job
            var job = await _coordinator.CreateJobAsync(
                DownloadJobType.Search,
                $"Search: {SearchKeyword}",
                targets);

            // Start the job
            await _coordinator.StartJobAsync(job.Id);

            await _dialogService.ShowMessageAsync("Download Started", 
                $"Queued {ResultCount} artworks for download.\n\nCheck the History tab for progress.");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to start download: {ex.Message}");
        }
    }
}

public class SearchResultItem : ObservableObject
{
    public ArtworkPreview Artwork { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string ArtistName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public bool IsR18 { get; set; }
    public bool IsAiGenerated { get; set; }
    
    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }
}
