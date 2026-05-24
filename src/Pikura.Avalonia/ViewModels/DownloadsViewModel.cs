using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pikura.Core.Services;
using Pikura.Avalonia.Services;
using System.Threading.Tasks;
using System;

namespace Pikura.Avalonia.ViewModels;

public partial class DownloadsViewModel : ViewModelBase
{
    private readonly PixivDownloadService _downloadService;
    private readonly DialogService _dialogService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public DownloadsViewModel(PixivDownloadService downloadService, DialogService dialogService)
    {
        _downloadService = downloadService;
        _dialogService = dialogService;
    }

    [RelayCommand]
    private async Task RefreshDownloadsAsync()
    {
        IsLoading = true;
        StatusMessage = "Refreshing downloads...";

        try
        {
            await Task.Delay(500); // Simulate loading
            StatusMessage = "Downloads refreshed";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to refresh downloads");
            StatusMessage = "Failed to refresh downloads";
            await _dialogService.ShowMessageAsync("Error", "Failed to refresh downloads.");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
