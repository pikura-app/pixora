using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pixora.Avalonia.Services;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace Pixora.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigationService;
    private readonly SettingsService _settingsService;
    private readonly UpdateCheckService _updateCheck;
    private readonly ILogger<MainWindowViewModel> _logger;
    private ContentControl? _mainContentControl;

    [ObservableProperty] private string _sidebarUserName    = "Guest User";
    [ObservableProperty] private string _sidebarUserStatus  = "Not signed in";
    [ObservableProperty] private string _sidebarUserInitial = "G";
    [ObservableProperty] private bool   _updateAvailable;
    [ObservableProperty] private string _updateVersion      = string.Empty;
    [ObservableProperty] private string _updateUrl          = string.Empty;
    [ObservableProperty] private string? _updateDownloadUrl;
    [ObservableProperty] private bool   _updateDownloading;
    [ObservableProperty] private int    _updateDownloadProgress;
    [ObservableProperty] private bool   _updateReadyToInstall;
    [ObservableProperty] private string _updateStatusText   = string.Empty;

    private UpdateInfo? _pendingUpdate;
    private string?     _downloadedPath;
    private System.Threading.CancellationTokenSource? _downloadCts;

    [ObservableProperty] private bool   _changelogAvailable;
    [ObservableProperty] private string _changelogVersion     = string.Empty;
    [ObservableProperty] private string _changelogNotes       = string.Empty;
    [ObservableProperty] private string _changelogReleaseUrl  = string.Empty;

    public MainWindowViewModel(NavigationService navigationService, SettingsService settingsService, UpdateCheckService updateCheck, ILogger<MainWindowViewModel> logger)
    {
        _navigationService = navigationService;
        _settingsService   = settingsService;
        _updateCheck       = updateCheck;
        _logger            = logger;
        Title = "Pixora";
        RefreshUserChip();
        _settingsService.Changed += (_, _) => RefreshUserChip();
        _ = Task.Run(CheckForUpdateAsync);
        _ = Task.Run(CheckChangelogAsync);
    }

    /// <summary>
    /// If the app version is newer than LastSeenVersion, fetch that release's notes
    /// from the GitHub API and signal the UI to show the changelog popup.
    /// </summary>
    public async Task CheckChangelogAsync()
    {
        try
        {
            var current = UpdateCheckService.CurrentVersion;
            var lastSeen = _settingsService.Current.LastSeenVersion;

            // Mark seen immediately so we don't show it again on next launch
            _settingsService.Update(s => s.LastSeenVersion = current);

            // First-ever launch or same version — nothing to show
            if (string.IsNullOrEmpty(lastSeen)) return;
            if (Version.TryParse(current, out var cv) &&
                Version.TryParse(lastSeen,  out var lv) &&
                cv <= lv) return;

            // Fetch release notes for the current version tag
            var notes = await _updateCheck.FetchReleaseNotesAsync(current).ConfigureAwait(false);
            if (notes is null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ChangelogVersion    = notes.Version;
                ChangelogNotes      = notes.ReleaseNotes;
                ChangelogReleaseUrl = notes.ReleasePageUrl;
                ChangelogAvailable  = true;
            });
        }
        catch { /* non-fatal */ }
    }

    [RelayCommand]
    private void DismissChangelog() => ChangelogAvailable = false;

    public async Task CheckForUpdateAsync()
    {
        var info = await _updateCheck.CheckAsync().ConfigureAwait(false);
        if (info is null) return;

        var settings = AppServices.Get<SettingsService>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pendingUpdate       = info;
            UpdateVersion        = info.Version;
            UpdateUrl            = info.ReleasePageUrl;
            UpdateDownloadUrl    = info.DownloadUrl;
            UpdateReadyToInstall = false;
            UpdateDownloading    = false;
            UpdateStatusText     = string.Empty;

            if (settings.Current.NotifyOnUpdate)
                UpdateAvailable = true;

            if (settings.Current.AutoDownloadUpdates && !string.IsNullOrEmpty(info.DownloadUrl))
                _ = Task.Run(StartDownloadAsync);
        });
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        if (!string.IsNullOrEmpty(UpdateUrl))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateUrl) { UseShellExecute = true });
        UpdateAvailable = false;
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        UpdateAvailable      = false;
        UpdateReadyToInstall = false;
        UpdateDownloading    = false;
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (_pendingUpdate is null || string.IsNullOrWhiteSpace(_pendingUpdate.DownloadUrl)) return;
        UpdateAvailable = false;
        await StartDownloadAsync();
    }

    private async Task StartDownloadAsync()
    {
        if (_pendingUpdate is null) return;
        _downloadCts = new System.Threading.CancellationTokenSource();
        var progress = new Progress<int>(p => Dispatcher.UIThread.Post(() =>
        {
            UpdateDownloadProgress = p;
            UpdateStatusText       = $"Downloading update... {p}%";
        }));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateDownloading    = true;
            UpdateReadyToInstall = false;
            UpdateStatusText     = "Starting download...";
        });

        try
        {
            _downloadedPath = await _updateCheck
                .DownloadUpdateAsync(_pendingUpdate, progress, _downloadCts.Token)
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateDownloading    = false;
                UpdateReadyToInstall = true;
                UpdateStatusText     = $"v{_pendingUpdate.Version} ready to install";
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateDownloading = false;
                UpdateStatusText  = "Download cancelled.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateDownloading = false;
                UpdateStatusText  = $"Download failed: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private void InstallAndRestartUpdate()
    {
        if (_downloadedPath is null) return;
        _updateCheck.InstallAndRestart(_downloadedPath);
    }

    private void RefreshUserChip()
    {
        var s = _settingsService.Current;
        SidebarUserName    = s.IsConfigured ? (s.UserName ?? s.UserId ?? "Pixiv User") : "Guest User";
        SidebarUserStatus  = s.IsConfigured ? $"ID: {s.UserId}" : "Not signed in";
        SidebarUserInitial = string.IsNullOrEmpty(SidebarUserName) ? "G" : SidebarUserName[0].ToString().ToUpper();
    }

    public string Title { get; }

    public void SetMainContentControl(ContentControl contentControl)
    {
        _mainContentControl = contentControl;
    }

    [RelayCommand]
    private void NavigateToGallery()
    {
        try
        {
            if (_mainContentControl != null)
            {
                var galleryView = new Pixora.Avalonia.Views.Gallery.GalleryView();
                _mainContentControl.Content = galleryView;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateToGallery failed");
        }
    }

    [RelayCommand]
    private void NavigateToRankings()
    {
        try
        {
            if (_mainContentControl != null)
            {
                var rankingsView = new Pixora.Avalonia.Views.RankingsView();
                _mainContentControl.Content = rankingsView;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateToRankings failed");
        }
    }

    [RelayCommand]
    private void NavigateToDownloads()
    {
        try
        {
            if (_mainContentControl != null)
            {
                var downloadsView = new Pixora.Avalonia.Views.DownloadsView();
                _mainContentControl.Content = downloadsView;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateToDownloads failed");
        }
    }

    [RelayCommand]
    private void NavigateToHistory()
    {
        try
        {
            if (_mainContentControl != null)
            {
                var historyView = new Pixora.Avalonia.Views.History.HistoryView();
                _mainContentControl.Content = historyView;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateToHistory failed");
        }
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        try
        {
            if (_mainContentControl != null)
            {
                var settingsView = new Pixora.Avalonia.Views.Settings.SettingsView();
                _mainContentControl.Content = settingsView;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NavigateToSettings failed");
        }
    }

    public bool IsConfigured => _settingsService.Current.IsConfigured;
}
