using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pikura.Core.Services;
using Pikura.Avalonia.Services;
using System;
using System.Threading.Tasks;

namespace Pikura.Avalonia.ViewModels;

public partial class RankingsViewModel : ViewModelBase
{
    private readonly PixivClient _pixivClient;
    private readonly DialogService _dialogService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private int _cardSize = 200;

    [ObservableProperty]
    private bool _fitWholeImage;

    [ObservableProperty]
    private bool _showR18;

    public RankingsViewModel(PixivClient pixivClient, DialogService dialogService)
    {
        _pixivClient = pixivClient;
        _dialogService = dialogService;
    }

    [RelayCommand]
    private async Task LoadRankingsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading rankings...";

        try
        {
            // This would load actual rankings from Pixiv
            await Task.Delay(1000); // Simulate loading
            StatusMessage = "Rankings loaded successfully";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load rankings");
            StatusMessage = "Failed to load rankings";
            await _dialogService.ShowMessageAsync("Error", "Failed to load rankings. Please try again.");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
