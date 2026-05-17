using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixora.Avalonia.Services;
using Pixora.Core.Settings;
using Avalonia.Controls;
using System;

namespace Pixora.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly NavigationService _navigationService;
    private readonly SettingsService _settingsService;
    private ContentControl? _mainContentControl;

    [ObservableProperty] private string _sidebarUserName = "Guest User";
    [ObservableProperty] private string _sidebarUserStatus = "Not signed in";

    public MainWindowViewModel(NavigationService navigationService, SettingsService settingsService)
    {
        _navigationService = navigationService;
        _settingsService = settingsService;
        Title = "Pixora";
        RefreshUserChip();
        _settingsService.Changed += (_, _) => RefreshUserChip();
    }

    private void RefreshUserChip()
    {
        var s = _settingsService.Current;
        SidebarUserName = s.IsConfigured ? (s.UserName ?? s.UserId ?? "Pixiv User") : "Guest User";
        SidebarUserStatus = s.IsConfigured ? $"ID: {s.UserId}" : "Not signed in";
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
            System.Diagnostics.Debug.WriteLine("NavigateToGallery called");
            if (_mainContentControl != null)
            {
                var galleryView = new Pixora.Avalonia.Views.Gallery.GalleryView();
                _mainContentControl.Content = galleryView;
                System.Diagnostics.Debug.WriteLine("GalleryView loaded successfully");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateToGallery failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void NavigateToRankings()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("NavigateToRankings called");
            if (_mainContentControl != null)
            {
                var rankingsView = new Pixora.Avalonia.Views.RankingsView();
                _mainContentControl.Content = rankingsView;
                System.Diagnostics.Debug.WriteLine("RankingsView loaded successfully");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateToRankings failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void NavigateToDownloads()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("NavigateToDownloads called");
            if (_mainContentControl != null)
            {
                var downloadsView = new Pixora.Avalonia.Views.DownloadsView();
                _mainContentControl.Content = downloadsView;
                System.Diagnostics.Debug.WriteLine("DownloadsView loaded successfully");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateToDownloads failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void NavigateToHistory()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("NavigateToHistory called");
            if (_mainContentControl != null)
            {
                var historyView = new Pixora.Avalonia.Views.History.HistoryView();
                _mainContentControl.Content = historyView;
                System.Diagnostics.Debug.WriteLine("HistoryView loaded successfully");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateToHistory failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("NavigateToSettings called");
            if (_mainContentControl != null)
            {
                var settingsView = new Pixora.Avalonia.Views.Settings.SettingsView();
                _mainContentControl.Content = settingsView;
                System.Diagnostics.Debug.WriteLine("SettingsView loaded successfully");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NavigateToSettings failed: {ex.Message}");
        }
    }

    public bool IsConfigured => _settingsService.Current.IsConfigured;
}
