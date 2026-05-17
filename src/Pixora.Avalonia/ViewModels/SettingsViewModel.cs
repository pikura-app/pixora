using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using Pixora.Avalonia.Services;
using Pixora.Avalonia.Views.Login;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Pixora.Avalonia.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly DialogService _dialogService;
    private readonly PixivClient _pixivClient;
    private readonly FilePickerService _filePickerService;

    [ObservableProperty]
    private string _accountStatus = "Not signed in";

    [ObservableProperty]
    private string _accountDetail = "Paste your PHPSESSID cookie to sign in.";

    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private string _downloadRoot = string.Empty;

    [ObservableProperty]
    private string _settingsPathHint = string.Empty;

    [ObservableProperty]
    private bool _isLightTheme;

    // Accessibility
    [ObservableProperty] private double _fontSizeScale = 1.0;
    [ObservableProperty] private bool _useHighContrast;
    [ObservableProperty] private bool _useLargeFonts;
    [ObservableProperty] private bool _increaseSpacing;
    [ObservableProperty] private bool _reduceMotion;

    // Download Configuration - Core
    [ObservableProperty] private string _filenameTemplate = "%image_id%_p%page_index%";
    [ObservableProperty] private string _filenameMangaFormat = "%artist% (%member_id%)\\%image_id% - %title%\\%page_number%";
    [ObservableProperty] private string _filenameInfoFormat = "%artist% (%member_id%)\\%image_id% - %title%.txt";
    [ObservableProperty] private string _folderTemplate = "%artist% (%member_id%)";
    [ObservableProperty] private string _dateFormat = "yyyy-MM-dd";
    [ObservableProperty] private string _tagsSeparator = ", ";
    [ObservableProperty] private int _maxConcurrentDownloads = 3;
    [ObservableProperty] private bool _createSubfolderPerSubmission;
    [ObservableProperty] private bool _separateR18Folder;

    // R-18 & Filtering
    [ObservableProperty] private string _r18Mode = "Show";
    [ObservableProperty] private string _r18Type = "Both";
    [ObservableProperty] private bool _blurR18Content;
    [ObservableProperty] private int _blurIntensity = 15;
    [ObservableProperty] private bool _filterAiGenerated;
    [ObservableProperty] private ObservableCollection<string> _excludedTags = new();
    [ObservableProperty] private string _newExcludedTag = "";

    // Metadata Export
    [ObservableProperty] private bool _writeImageJSON;
    [ObservableProperty] private bool _writeImageInfo;
    [ObservableProperty] private bool _writeRawJSON;
    [ObservableProperty] private bool _includeSeriesJSON;
    [ObservableProperty] private bool _writeImageXMP;
    [ObservableProperty] private bool _writeImageXMPPerImage;
    [ObservableProperty] private bool _verifyImage;
    [ObservableProperty] private bool _setLastModified = true;
    [ObservableProperty] private bool _useLocalTimezone;

    // Download Control
    [ObservableProperty] private int _overwriteMode; // 0=skip, 1=overwrite, 2=backup
    [ObservableProperty] private bool _backupOldFile;
    [ObservableProperty] private int _minFileSizeKB;
    [ObservableProperty] private int _maxFileSizeKB;
    [ObservableProperty] private int _downloadTimeout = 60;
    [ObservableProperty] private int _retryCount = 3;
    [ObservableProperty] private int _retryDelaySeconds = 5;
    [ObservableProperty] private int _downloadDelaySeconds;
    [ObservableProperty] private int _downloadBufferKB = 512;

    // Blacklist Filtering
    [ObservableProperty] private ObservableCollection<string> _blacklistTags = new();
    [ObservableProperty] private bool _useBlacklistTagsRegex;
    [ObservableProperty] private ObservableCollection<string> _blacklistTitles = new();
    [ObservableProperty] private bool _useBlacklistTitlesRegex;
    [ObservableProperty] private ObservableCollection<string> _blacklistMembers = new();
    [ObservableProperty] private string _newBlacklistTag = "";
    [ObservableProperty] private string _newBlacklistTitle = "";
    [ObservableProperty] private string _newBlacklistMember = "";

    // Network / Proxy
    [ObservableProperty] private bool _useProxy;
    [ObservableProperty] private string _proxyAddress = "";
    [ObservableProperty] private bool _enableSSLVerification = true;
    [ObservableProperty] private bool _useRobots = true;

    // Ugoira (Animated Images)
    [ObservableProperty] private bool _createUgoiraWebm;
    [ObservableProperty] private bool _createUgoiraGif;
    [ObservableProperty] private bool _createUgoiraWebp;
    [ObservableProperty] private bool _createUgoiraApng;
    [ObservableProperty] private bool _keepUgoiraZip = true;
    [ObservableProperty] private string _ffMpegCodec = "libvpx-vp9";
    [ObservableProperty] private int _ffMpegCRF = 15;

    // FANBOX
    [ObservableProperty] private string _filenameFanboxCover = "FANBOX %artist% (%member_id%)\\%urlFilename% - %title%";
    [ObservableProperty] private string _filenameFanboxContent = "FANBOX %artist% (%member_id%)\\%urlFilename% - %title%";
    [ObservableProperty] private string _filenameFanboxInfo = "FANBOX %artist% (%member_id%)\\%urlFilename% - %title%.txt";
    [ObservableProperty] private bool _downloadFanboxCoverWhenRestricted = true;
    [ObservableProperty] private bool _writeFanboxHtml;

    // Localization
    [ObservableProperty] private string _pixivLocale = "en";
    [ObservableProperty] private string _appLanguage = "English";

    // Startup & System Tray
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _startupWindowState = "Normal";
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startMinimizedToTray;

    /// <summary>0=Disabled, 1=Minimize to tray, 2=Close to tray</summary>
    public int TrayBehavior
    {
        get
        {
            if (CloseToTray) return 2;
            if (MinimizeToTray) return 1;
            return 0;
        }
        set
        {
            MinimizeToTray = value >= 1;
            CloseToTray = value == 2;
            StartMinimizedToTray = false;
            _settingsService.Update(s =>
            {
                s.MinimizeToTray = MinimizeToTray;
                s.CloseToTray = CloseToTray;
                s.StartMinimizedToTray = false;
            });
            OnPropertyChanged();
        }
    }

    // Available options
    public string[] StartupWindowStates { get; } = { "Normal", "Maximized", "Minimized", "System Tray" };
    public string[] AvailableLocales { get; } = { "en", "ja", "zh", "ko", "es", "fr", "de", "it", "pt", "ru" };
    public string[] AvailableAppLanguages { get; } = { "English", "日本語", "中文", "한국어" };
    public string[] AvailableR18Types { get; } = { "Both", "R18", "R18G" };

    // Token definitions with descriptions for UI
    public static readonly Dictionary<string, string> FilenameTokens = new()
    {
        ["%image_id%"] = "Image/post ID (12345678)",
        ["%title%"] = "Artwork title",
        ["%artist%"] = "Artist name",
        ["%member_id%"] = "Artist numeric ID",
        ["%member_token%"] = "Artist token (may change)",
        ["%page_index%"] = "Page number 0-indexed (0, 1, 2...)",
        ["%page_number%"] = "Page number 1-indexed (1, 2, 3...)",
        ["%page_big%"] = "Adds 'big' for manga mode",
        ["%date%"] = "Current date",
        ["%date_fmt{...}%"] = "Custom date format",
        ["%works_date%"] = "Artwork upload date",
        ["%works_date_only%"] = "Artwork date only (no time)",
        ["%works_date_fmt{...}%"] = "Custom works date format",
        ["%works_res%"] = "Image resolution",
        ["%works_tools%"] = "Tools used (e.g., Photoshop)",
        ["%R-18%"] = "R-18 tag if applicable",
        ["%AI%"] = "AI tag if AI-generated",
        ["%image_ext%"] = "File extension (jpg, png)",
        ["%urlFilename%"] = "Original server filename",
        ["%bookmark%"] = "'Bookmarks' string",
        ["%bookmark_count%"] = "Number of bookmarks",
        ["%image_response_count%"] = "Response count",
        ["%manga_series_id%"] = "Manga series ID",
        ["%manga_series_order%"] = "Order in manga series",
        ["%manga_series_title%"] = "Manga series title",
        ["%original_member_id%"] = "Original member ID (bookmarks)",
        ["%original_member_token%"] = "Original token (bookmarks)",
        ["%original_artist%"] = "Original artist (bookmarks)",
        ["%searchTags%"] = "Searched tags",
        ["%tags%"] = "All artwork tags",
    };

    public static readonly Dictionary<string, string> FolderTokens = new()
    {
        ["%artist%"] = "Artist name",
        ["%member_id%"] = "Artist numeric ID",
        ["%member_token%"] = "Artist token",
        ["%R-18%"] = "R-18 folder marker",
    };

    // Diagnostics
    [ObservableProperty] private bool _verboseLogging;

    public static string AppDataFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixora");

    public static string DownloadLogPath =>
        System.IO.Path.Combine(AppDataFolder, "download.log");

    public static string AppLogPath =>
        System.IO.Path.Combine(AppDataFolder, "pixora.log");

    public static string CrashLogPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pixora-crash.txt");

    // Settings that require restart
    [ObservableProperty] private bool _restartRequired;
    private string _pendingRestartChange = "";

    // ── Hoshi AI Model Management ────────────────────────────────────────────
    private readonly OllamaService _ollama;
    [ObservableProperty] private bool _useCustomHoshiModels;
    [ObservableProperty] private string _hoshiTextModel = string.Empty;
    [ObservableProperty] private string _hoshiVisionModel = string.Empty;
    [ObservableProperty] private string _modelToInstall = string.Empty;
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private bool _isPullingModel;
    [ObservableProperty] private string _modelPullStatus = string.Empty;
    [ObservableProperty] private double _modelPullPercent;
    [ObservableProperty] private string _modelManagementError = string.Empty;
    [ObservableProperty] private ObservableCollection<InstalledModelRow> _installedModels = new();
    private CancellationTokenSource? _modelPullCts;

    /// <summary>Default model name used when no custom text model is configured.</summary>
    public string DefaultTextModelName => OllamaService.DefaultTextModel;
    /// <summary>Default model name used when no custom vision model is configured.</summary>
    public string DefaultVisionModelName => OllamaService.DefaultVisionModel;
    /// <summary>Suggested popular models for quick install.</summary>
    public string[] SuggestedModels { get; } = new[]
    {
        "llama3.2", "llama3.1", "llava", "moondream", "qwen2.5", "phi3", "gemma2", "mistral"
    };

    public SettingsViewModel(
        SettingsService settingsService,
        DialogService dialogService,
        PixivClient pixivClient,
        FilePickerService filePickerService,
        OllamaService ollama)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _pixivClient = pixivClient;
        _filePickerService = filePickerService;
        _ollama = ollama;

        LoadSettings();
        _settingsService.Changed += (_, _) => LoadSettings();
        IsLightTheme = settingsService.Current.Theme == "Light";

        // Kick off an installed-models refresh in the background so the Hoshi AI
        // tab shows results without blocking startup or the UI thread.
        _ = global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try { await RefreshInstalledModelsAsync(); }
            catch { /* ignore — error already surfaces via ModelManagementError */ }
        });
    }

    private void LoadSettings()
    {
        var s = _settingsService.Current;
        IsSignedIn = s.IsConfigured;
        AccountStatus = s.IsConfigured
            ? $"Signed in as {s.UserName ?? s.UserId}"
            : "Not signed in";
        AccountDetail = s.IsConfigured
            ? $"User ID: {s.UserId}  ·  Session active"
            : "Sign in with your Pixiv username and password.";    
        DownloadRoot = s.DownloadRoot;

        // Naming templates
        FilenameTemplate = s.FilenameTemplate;
        FilenameMangaFormat = s.FilenameMangaFormat;
        FilenameInfoFormat = s.FilenameInfoFormat;
        FolderTemplate = s.FolderTemplate;
        DateFormat = s.DateFormat;
        TagsSeparator = s.TagsSeparator;

        // Core download settings
        MaxConcurrentDownloads = s.MaxConcurrentDownloads;
        CreateSubfolderPerSubmission = s.CreateSubfolderPerSubmission;
        SeparateR18Folder = s.SeparateR18Folder;

        // R-18 & Filtering
        R18Mode = s.R18Mode.ToString();
        R18Type = s.R18Type.ToString();
        BlurR18Content = s.BlurR18Content;
        BlurIntensity = s.BlurIntensity;
        FilterAiGenerated = s.FilterAiGenerated;
        ExcludedTags = new ObservableCollection<string>(s.ExcludedTags);

        // Metadata Export
        WriteImageJSON = s.WriteImageJSON;
        WriteImageInfo = s.WriteImageInfo;
        WriteRawJSON = s.WriteRawJSON;
        IncludeSeriesJSON = s.IncludeSeriesJSON;
        WriteImageXMP = s.WriteImageXMP;
        WriteImageXMPPerImage = s.WriteImageXMPPerImage;
        VerifyImage = s.VerifyImage;
        SetLastModified = s.SetLastModified;
        UseLocalTimezone = s.UseLocalTimezone;

        // Download Control
        OverwriteMode = s.OverwriteMode;
        BackupOldFile = s.BackupOldFile;
        MinFileSizeKB = s.MinFileSizeKB;
        MaxFileSizeKB = s.MaxFileSizeKB;
        DownloadTimeout = s.DownloadTimeout;
        RetryCount = s.RetryCount;
        RetryDelaySeconds = s.RetryDelaySeconds;
        DownloadDelaySeconds = s.DownloadDelaySeconds;
        DownloadBufferKB = s.DownloadBufferKB;

        // Blacklist
        BlacklistTags = new ObservableCollection<string>(s.BlacklistTags);
        UseBlacklistTagsRegex = s.UseBlacklistTagsRegex;
        BlacklistTitles = new ObservableCollection<string>(s.BlacklistTitles);
        UseBlacklistTitlesRegex = s.UseBlacklistTitlesRegex;
        BlacklistMembers = new ObservableCollection<string>(s.BlacklistMembers);

        // Network
        UseProxy = s.UseProxy;
        ProxyAddress = s.ProxyAddress ?? "";
        EnableSSLVerification = s.EnableSSLVerification;
        UseRobots = s.UseRobots;

        // Accessibility
        FontSizeScale = s.FontSizeScale;
        UseHighContrast = s.UseHighContrast;
        UseLargeFonts = s.UseLargeFonts;
        IncreaseSpacing = s.IncreaseSpacing;
        ReduceMotion = s.ReduceMotion;

        // Ugoira
        CreateUgoiraWebm = s.CreateUgoiraWebm;
        CreateUgoiraGif = s.CreateUgoiraGif;
        CreateUgoiraWebp = s.CreateUgoiraWebp;
        CreateUgoiraApng = s.CreateUgoiraApng;
        KeepUgoiraZip = s.KeepUgoiraZip;
        FfMpegCodec = s.FFmpegCodec;
        FfMpegCRF = s.FFmpegCRF;

        // FANBOX
        FilenameFanboxCover = s.FilenameFanboxCover;
        FilenameFanboxContent = s.FilenameFanboxContent;
        FilenameFanboxInfo = s.FilenameFanboxInfo;
        DownloadFanboxCoverWhenRestricted = s.DownloadFanboxCoverWhenRestricted;
        WriteFanboxHtml = s.WriteFanboxHtml;

        // Localization
        PixivLocale = s.Locale;

        // Startup & System Tray
        StartWithWindows = s.StartWithWindows;
        StartupWindowState = s.StartupWindowState;
        MinimizeToTray = s.MinimizeToTray;
        CloseToTray = s.CloseToTray;
        StartMinimizedToTray = s.StartMinimizedToTray;
        OnPropertyChanged(nameof(TrayBehavior));

        SettingsPathHint = $"Settings: {SettingsService.DefaultPath()}";

        // Diagnostics
        VerboseLogging = s.VerboseLogging;

        // Hoshi AI model preferences
        UseCustomHoshiModels = s.UseCustomHoshiModels;
        HoshiTextModel = s.HoshiTextModel;
        HoshiVisionModel = s.HoshiVisionModel;
    }

    [RelayCommand]
    private void SetTheme(string theme)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        IsLightTheme = theme == "Light";
        _settingsService.Update(s => s.Theme = theme);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        try
        {
            var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow is null) return;

            var loginWindow = new PixivLoginWindow();
            await loginWindow.ShowDialog(mainWindow);

            if (loginWindow.LoginSucceeded)
                LoadSettings();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Login failed");
            await _dialogService.ShowMessageAsync("Error", $"Login failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (!IsSignedIn) return;
        var confirmed = await _dialogService.ShowConfirmationAsync("Sign out", "Clear your session cookie and sign out?");
        if (!confirmed) return;
        _settingsService.Update(s => { s.PhpSessId = string.Empty; s.UserId = null; s.UserName = null; });
        LoadSettings();
    }

    [RelayCommand]
    private async Task ChangeDownloadFolderAsync()
    {
        try
        {
            var folder = await _filePickerService.PickFolderAsync("Select Download Folder");
            if (!string.IsNullOrEmpty(folder))
            {
                _settingsService.Update(s => s.DownloadRoot = folder);
                LoadSettings();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to change download folder");
            await _dialogService.ShowMessageAsync("Error", "Failed to change download folder.");
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        var confirmed = await _dialogService.ShowConfirmationAsync("Reset Settings", "Are you sure you want to reset all settings to defaults?");
        if (confirmed)
        {
            try
            {
                // Simplified for now - will implement actual reset later
                await Task.Delay(500);
                LoadSettings();
                await _dialogService.ShowMessageAsync("Success", "Settings have been reset to defaults (simulated).");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to reset settings");
                await _dialogService.ShowMessageAsync("Error", "Failed to reset settings.");
            }
        }
    }

    // Instant save handlers - triggered when property changes
    partial void OnFilenameTemplateChanged(string value)
    {
        _settingsService.Update(s => s.FilenameTemplate = value);
        OnPropertyChanged(nameof(FilenamePreview));
    }

    partial void OnFolderTemplateChanged(string value)
    {
        _settingsService.Update(s => s.FolderTemplate = value);
        OnPropertyChanged(nameof(FolderPreview));
    }

    partial void OnDateFormatChanged(string value)
    {
        _settingsService.Update(s => s.DateFormat = value);
        OnPropertyChanged(nameof(FilenamePreview));
        OnPropertyChanged(nameof(FolderPreview));
        OnPropertyChanged(nameof(MangaFilenamePreview));
        OnPropertyChanged(nameof(InfoFilenamePreview));
    }

    partial void OnMaxConcurrentDownloadsChanged(int value)
        => _settingsService.Update(s => s.MaxConcurrentDownloads = value);

    partial void OnCreateSubfolderPerSubmissionChanged(bool value)
        => _settingsService.Update(s => s.CreateSubfolderPerSubmission = value);

    partial void OnSeparateR18FolderChanged(bool value)
        => _settingsService.Update(s => s.SeparateR18Folder = value);

    partial void OnBlurR18ContentChanged(bool value)
        => _settingsService.Update(s => s.BlurR18Content = value);

    partial void OnBlurIntensityChanged(int value)
        => _settingsService.Update(s => s.BlurIntensity = value);

    partial void OnFilterAiGeneratedChanged(bool value)
        => _settingsService.Update(s => s.FilterAiGenerated = value);

    partial void OnR18ModeChanged(string value)
    {
        if (Enum.TryParse<R18Mode>(value, out var r18Mode))
            _settingsService.Update(s => s.R18Mode = r18Mode);
    }

    partial void OnR18TypeChanged(string value)
    {
        if (Enum.TryParse<R18TypeFilter>(value, out var r18Type))
            _settingsService.Update(s => s.R18Type = r18Type);
    }

    partial void OnPixivLocaleChanged(string value)
        => _settingsService.Update(s => s.Locale = value);

    partial void OnAppLanguageChanged(string value)
    {
        RestartRequired = true;
        _pendingRestartChange = $"Language changed to {value}. Application language requires restart to fully apply.";
    }

    // Manga & Templates
    partial void OnFilenameMangaFormatChanged(string value)
    {
        _settingsService.Update(s => s.FilenameMangaFormat = value);
        OnPropertyChanged(nameof(MangaFilenamePreview));
    }

    partial void OnFilenameInfoFormatChanged(string value)
    {
        _settingsService.Update(s => s.FilenameInfoFormat = value);
        OnPropertyChanged(nameof(InfoFilenamePreview));
    }

    partial void OnTagsSeparatorChanged(string value)
        => _settingsService.Update(s => s.TagsSeparator = value);

    // Metadata Export
    partial void OnWriteImageJSONChanged(bool value)
        => _settingsService.Update(s => s.WriteImageJSON = value);

    partial void OnWriteImageInfoChanged(bool value)
        => _settingsService.Update(s => s.WriteImageInfo = value);

    partial void OnWriteRawJSONChanged(bool value)
        => _settingsService.Update(s => s.WriteRawJSON = value);

    partial void OnIncludeSeriesJSONChanged(bool value)
        => _settingsService.Update(s => s.IncludeSeriesJSON = value);

    partial void OnWriteImageXMPChanged(bool value)
        => _settingsService.Update(s => s.WriteImageXMP = value);

    partial void OnWriteImageXMPPerImageChanged(bool value)
        => _settingsService.Update(s => s.WriteImageXMPPerImage = value);

    partial void OnVerifyImageChanged(bool value)
        => _settingsService.Update(s => s.VerifyImage = value);

    partial void OnSetLastModifiedChanged(bool value)
        => _settingsService.Update(s => s.SetLastModified = value);

    partial void OnUseLocalTimezoneChanged(bool value)
        => _settingsService.Update(s => s.UseLocalTimezone = value);

    // Download Control
    partial void OnOverwriteModeChanged(int value)
        => _settingsService.Update(s => s.OverwriteMode = value);

    partial void OnBackupOldFileChanged(bool value)
        => _settingsService.Update(s => s.BackupOldFile = value);

    partial void OnMinFileSizeKBChanged(int value)
        => _settingsService.Update(s => s.MinFileSizeKB = value);

    partial void OnMaxFileSizeKBChanged(int value)
        => _settingsService.Update(s => s.MaxFileSizeKB = value);

    partial void OnDownloadTimeoutChanged(int value)
        => _settingsService.Update(s => s.DownloadTimeout = value);

    partial void OnRetryCountChanged(int value)
        => _settingsService.Update(s => s.RetryCount = value);

    partial void OnRetryDelaySecondsChanged(int value)
        => _settingsService.Update(s => s.RetryDelaySeconds = value);

    partial void OnDownloadDelaySecondsChanged(int value)
        => _settingsService.Update(s => s.DownloadDelaySeconds = value);

    partial void OnDownloadBufferKBChanged(int value)
        => _settingsService.Update(s => s.DownloadBufferKB = value);

    // Blacklist
    partial void OnUseBlacklistTagsRegexChanged(bool value)
        => _settingsService.Update(s => s.UseBlacklistTagsRegex = value);

    partial void OnUseBlacklistTitlesRegexChanged(bool value)
        => _settingsService.Update(s => s.UseBlacklistTitlesRegex = value);

    // Network
    partial void OnUseProxyChanged(bool value)
        => _settingsService.Update(s => s.UseProxy = value);

    partial void OnProxyAddressChanged(string value)
        => _settingsService.Update(s => s.ProxyAddress = value);

    partial void OnEnableSSLVerificationChanged(bool value)
        => _settingsService.Update(s => s.EnableSSLVerification = value);

    partial void OnUseRobotsChanged(bool value)
        => _settingsService.Update(s => s.UseRobots = value);

    // Diagnostics
    partial void OnVerboseLoggingChanged(bool value)
        => _settingsService.Update(s => s.VerboseLogging = value);

    // Accessibility
    partial void OnFontSizeScaleChanged(double value)
        => _settingsService.Update(s => s.FontSizeScale = value);

    partial void OnUseHighContrastChanged(bool value)
        => _settingsService.Update(s => s.UseHighContrast = value);

    partial void OnUseLargeFontsChanged(bool value)
        => _settingsService.Update(s => s.UseLargeFonts = value);

    partial void OnIncreaseSpacingChanged(bool value)
        => _settingsService.Update(s => s.IncreaseSpacing = value);

    partial void OnReduceMotionChanged(bool value)
        => _settingsService.Update(s => s.ReduceMotion = value);

    // Ugoira
    partial void OnCreateUgoiraWebmChanged(bool value)
        => _settingsService.Update(s => s.CreateUgoiraWebm = value);

    partial void OnCreateUgoiraGifChanged(bool value)
        => _settingsService.Update(s => s.CreateUgoiraGif = value);

    partial void OnCreateUgoiraWebpChanged(bool value)
        => _settingsService.Update(s => s.CreateUgoiraWebp = value);

    partial void OnCreateUgoiraApngChanged(bool value)
        => _settingsService.Update(s => s.CreateUgoiraApng = value);

    partial void OnKeepUgoiraZipChanged(bool value)
        => _settingsService.Update(s => s.KeepUgoiraZip = value);

    partial void OnFfMpegCodecChanged(string value)
        => _settingsService.Update(s => s.FFmpegCodec = value);

    partial void OnFfMpegCRFChanged(int value)
        => _settingsService.Update(s => s.FFmpegCRF = value);

    // FANBOX
    partial void OnFilenameFanboxCoverChanged(string value)
        => _settingsService.Update(s => s.FilenameFanboxCover = value);

    partial void OnFilenameFanboxContentChanged(string value)
        => _settingsService.Update(s => s.FilenameFanboxContent = value);

    partial void OnFilenameFanboxInfoChanged(string value)
        => _settingsService.Update(s => s.FilenameFanboxInfo = value);

    partial void OnDownloadFanboxCoverWhenRestrictedChanged(bool value)
        => _settingsService.Update(s => s.DownloadFanboxCoverWhenRestricted = value);

    partial void OnWriteFanboxHtmlChanged(bool value)
        => _settingsService.Update(s => s.WriteFanboxHtml = value);

    [RelayCommand]
    private void RestartApplication()
    {
        // Save any pending changes
        _settingsService.Save();

        // Get the current executable path
        var exePath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        if (string.IsNullOrEmpty(exePath)) return;

        // Start new instance
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);

        // Shutdown current instance
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    [RelayCommand]
    private void DismissRestartNotification()
    {
        RestartRequired = false;
        _pendingRestartChange = "";
    }

    [RelayCommand]
    private void SetR18Type(string type)
    {
        if (Enum.TryParse<R18TypeFilter>(type, out var r18Type))
        {
            R18Type = type;
            _settingsService.Update(s => s.R18Type = r18Type);
        }
    }

    [RelayCommand]
    private void SetPixivLocale(string locale)
    {
        PixivLocale = locale;
        _settingsService.Update(s => s.Locale = locale);
    }

    [RelayCommand]
    private void SetAppLanguage(string language)
    {
        AppLanguage = language;
        // TODO: Apply localization change when translation system is implemented
    }

    [RelayCommand]
    private void AddExcludedTag()
    {
        if (string.IsNullOrWhiteSpace(NewExcludedTag)) return;
        var tag = NewExcludedTag.Trim();
        if (!ExcludedTags.Contains(tag))
        {
            ExcludedTags.Add(tag);
            _settingsService.Update(s => s.ExcludedTags = ExcludedTags.ToList());
        }
        NewExcludedTag = "";
    }

    [RelayCommand]
    private void RemoveExcludedTag(string tag)
    {
        if (ExcludedTags.Remove(tag))
            _settingsService.Update(s => s.ExcludedTags = ExcludedTags.ToList());
    }

    // Blacklist management commands
    [RelayCommand] private void AddBlacklistTag()
    {
        if (string.IsNullOrWhiteSpace(NewBlacklistTag)) return;
        var tag = NewBlacklistTag.Trim();
        if (!BlacklistTags.Contains(tag))
        {
            BlacklistTags.Add(tag);
            _settingsService.Update(s => s.BlacklistTags = BlacklistTags.ToList());
        }
        NewBlacklistTag = "";
    }

    [RelayCommand] private void RemoveBlacklistTag(string tag)
    {
        if (BlacklistTags.Remove(tag))
            _settingsService.Update(s => s.BlacklistTags = BlacklistTags.ToList());
    }

    [RelayCommand] private void AddBlacklistTitle()
    {
        if (string.IsNullOrWhiteSpace(NewBlacklistTitle)) return;
        var title = NewBlacklistTitle.Trim();
        if (!BlacklistTitles.Contains(title))
        {
            BlacklistTitles.Add(title);
            _settingsService.Update(s => s.BlacklistTitles = BlacklistTitles.ToList());
        }
        NewBlacklistTitle = "";
    }

    [RelayCommand] private void RemoveBlacklistTitle(string title)
    {
        if (BlacklistTitles.Remove(title))
            _settingsService.Update(s => s.BlacklistTitles = BlacklistTitles.ToList());
    }

    [RelayCommand] private void AddBlacklistMember()
    {
        if (string.IsNullOrWhiteSpace(NewBlacklistMember)) return;
        var member = NewBlacklistMember.Trim();
        if (!BlacklistMembers.Contains(member))
        {
            BlacklistMembers.Add(member);
            _settingsService.Update(s => s.BlacklistMembers = BlacklistMembers.ToList());
        }
        NewBlacklistMember = "";
    }

    [RelayCommand] private void RemoveBlacklistMember(string member)
    {
        if (BlacklistMembers.Remove(member))
            _settingsService.Update(s => s.BlacklistMembers = BlacklistMembers.ToList());
    }

    // Template preview - generates sample output
    public string FilenamePreview => GenerateFilenamePreview();
    public string FolderPreview => GenerateFolderPreview();
    public string MangaFilenamePreview => GenerateMangaFilenamePreview();
    public string InfoFilenamePreview => GenerateInfoFilenamePreview();

    private string GenerateFilenamePreview()
    {
        var now = DateTime.Now;
        var preview = FilenameTemplate;
        // Core identifiers
        preview = preview.Replace("%image_id%", "12345678");
        preview = preview.Replace("%title%", "Sample Artwork Title");
        preview = preview.Replace("%artist%", "ArtistName");
        preview = preview.Replace("%member_id%", "9876543");
        preview = preview.Replace("%member_token%", "artist_token");
        // Page numbers
        preview = preview.Replace("%page_index%", "0");
        preview = preview.Replace("%page_number%", "1");
        preview = preview.Replace("%page_big%", "big");
        // Dates
        preview = preview.Replace("%date%", now.ToString("yyyyMMdd"));
        preview = preview.Replace("%works_date%", now.ToString(DateFormat));
        preview = preview.Replace("%works_date_only%", now.ToString("yyyy-MM-dd"));
        // Metadata
        preview = preview.Replace("%works_res%", "1920x1080");
        preview = preview.Replace("%works_tools%", "Clip Studio Paint");
        preview = preview.Replace("%R-18%", "R-18");
        preview = preview.Replace("%AI%", "AI");
        preview = preview.Replace("%image_ext%", "jpg");
        preview = preview.Replace("%urlFilename%", "12345678_p0_master1200");
        // Social stats
        preview = preview.Replace("%bookmark%", "Bookmarks");
        preview = preview.Replace("%bookmark_count%", "1234");
        preview = preview.Replace("%image_response_count%", "56");
        // Manga series
        preview = preview.Replace("%manga_series_id%", "876543");
        preview = preview.Replace("%manga_series_order%", "5");
        preview = preview.Replace("%manga_series_title%", "My Manga Series");
        // Bookmark mode
        preview = preview.Replace("%original_member_id%", "111111");
        preview = preview.Replace("%original_member_token%", "original_artist");
        preview = preview.Replace("%original_artist%", "Original Artist");
        preview = preview.Replace("%searchTags%", "tag1 tag2");
        preview = preview.Replace("%tags%", "tag1, tag2, tag3");
        return preview + ".jpg";
    }

    private string GenerateFolderPreview()
    {
        var preview = FolderTemplate;
        preview = preview.Replace("%artist%", "ArtistName");
        preview = preview.Replace("%member_id%", "9876543");
        preview = preview.Replace("%member_token%", "artist_token");
        preview = preview.Replace("%R-18%", "R-18");
        return preview;
    }

    private string GenerateMangaFilenamePreview()
    {
        var now = DateTime.Now;
        var preview = FilenameMangaFormat;
        preview = preview.Replace("%image_id%", "12345678");
        preview = preview.Replace("%title%", "Sample Artwork Title");
        preview = preview.Replace("%artist%", "ArtistName");
        preview = preview.Replace("%member_id%", "9876543");
        preview = preview.Replace("%member_token%", "artist_token");
        preview = preview.Replace("%page_index%", "0");
        preview = preview.Replace("%page_number%", "1");
        preview = preview.Replace("%date%", now.ToString("yyyyMMdd"));
        preview = preview.Replace("%works_date%", now.ToString(DateFormat));
        preview = preview.Replace("%R-18%", "R-18");
        preview = preview.Replace("%image_ext%", "jpg");
        preview = preview.Replace("%urlFilename%", "12345678_p0");
        preview = preview.Replace("%tags%", "tag1, tag2");
        return preview + ".jpg";
    }

    private string GenerateInfoFilenamePreview()
    {
        var now = DateTime.Now;
        var preview = FilenameInfoFormat;
        preview = preview.Replace("%image_id%", "12345678");
        preview = preview.Replace("%title%", "Sample Artwork Title");
        preview = preview.Replace("%artist%", "ArtistName");
        preview = preview.Replace("%member_id%", "9876543");
        preview = preview.Replace("%member_token%", "artist_token");
        preview = preview.Replace("%date%", now.ToString("yyyyMMdd"));
        preview = preview.Replace("%works_date%", now.ToString(DateFormat));
        preview = preview.Replace("%R-18%", "R-18");
        preview = preview.Replace("%urlFilename%", "12345678_p0");
        return preview;
    }

    // Startup & System Tray - Instant save handlers
    partial void OnStartWithWindowsChanged(bool value)
    {
        _settingsService.Update(s => s.StartWithWindows = value);
        StartupHelper.SetStartupEnabled(value);

        // Update startup command if enabling
        if (value)
        {
            StartupHelper.UpdateStartupCommand(
                StartMinimizedToTray,
                MinimizeToTray && (CloseToTray || StartMinimizedToTray));
        }
    }

    partial void OnStartupWindowStateChanged(string value)
        => _settingsService.Update(s => s.StartupWindowState = value);

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settingsService.Update(s => s.MinimizeToTray = value);

        // Update registry if we're set to start with Windows
        if (StartWithWindows)
        {
            StartupHelper.UpdateStartupCommand(
                StartMinimizedToTray,
                value && (CloseToTray || StartMinimizedToTray));
        }

        // If disabling minimize to tray, also disable dependent options
        if (!value)
        {
            if (CloseToTray) CloseToTray = false;
            if (StartMinimizedToTray) StartMinimizedToTray = false;
        }
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        _settingsService.Update(s => s.CloseToTray = value);

        // Update registry if we're set to start with Windows
        if (StartWithWindows && MinimizeToTray)
        {
            StartupHelper.UpdateStartupCommand(
                StartMinimizedToTray,
                MinimizeToTray && (value || StartMinimizedToTray));
        }
    }

    partial void OnStartMinimizedToTrayChanged(bool value)
    {
        _settingsService.Update(s => s.StartMinimizedToTray = value);

        // Update registry if we're set to start with Windows
        if (StartWithWindows && MinimizeToTray)
        {
            StartupHelper.UpdateStartupCommand(
                value,
                MinimizeToTray && (CloseToTray || value));
        }
    }

    // ── Hoshi AI Model Management ────────────────────────────────────────────
    partial void OnUseCustomHoshiModelsChanged(bool value)
        => _settingsService.Update(s => s.UseCustomHoshiModels = value);

    partial void OnHoshiTextModelChanged(string value)
        => _settingsService.Update(s => s.HoshiTextModel = value ?? string.Empty);

    partial void OnHoshiVisionModelChanged(string value)
        => _settingsService.Update(s => s.HoshiVisionModel = value ?? string.Empty);

    /// <summary>Refresh the list of installed Ollama models. Non-blocking (runs on a background task).</summary>
    [RelayCommand]
    private async Task RefreshInstalledModelsAsync()
    {
        if (IsLoadingModels) return;
        IsLoadingModels = true;
        ModelManagementError = string.Empty;
        try
        {
            var list = await Task.Run(() => _ollama.ListInstalledModelsAsync());
            InstalledModels.Clear();
            foreach (var m in list)
            {
                InstalledModels.Add(new InstalledModelRow(m.Name, m.SizeBytes, m.ModifiedAt));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to list Ollama models");
            ModelManagementError = $"Could not list models: {ex.Message}";
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    /// <summary>Downloads a model in the background, updating progress without freezing the UI.</summary>
    [RelayCommand]
    private async Task PullModelAsync()
    {
        var name = (ModelToInstall ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (IsPullingModel) return;

        IsPullingModel = true;
        ModelPullPercent = 0;
        ModelPullStatus = $"Starting download of {name}…";
        ModelManagementError = string.Empty;

        _modelPullCts?.Cancel();
        _modelPullCts?.Dispose();
        _modelPullCts = new CancellationTokenSource();
        var ct = _modelPullCts.Token;

        try
        {
            // Run the pull on a background thread; marshal progress back via Dispatcher.
            await Task.Run(async () =>
            {
                await foreach (var p in _ollama.PullManualAsync(name, ct))
                {
                    var statusText = p.Total > 0
                        ? $"{p.Status} — {FormatBytes(p.Completed)} / {FormatBytes(p.Total)} ({p.Percent:0.0}%)"
                        : p.Status;
                    var percent = p.Percent;
                    await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ModelPullStatus = statusText;
                        ModelPullPercent = percent;
                    });
                }
            }, ct);

            ModelPullStatus = $"Installed {name}";
            ModelPullPercent = 100;
            await RefreshInstalledModelsAsync();
        }
        catch (OperationCanceledException)
        {
            ModelPullStatus = "Download cancelled.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to pull model {Name}", name);
            ModelManagementError = $"Download failed: {ex.Message}";
            ModelPullStatus = string.Empty;
        }
        finally
        {
            IsPullingModel = false;
        }
    }

    /// <summary>Cancels an in-progress model download.</summary>
    [RelayCommand]
    private void CancelModelPull()
    {
        try { _modelPullCts?.Cancel(); }
        catch { /* ignore */ }
    }

    /// <summary>Sets the ModelToInstall field to a suggested model name (used by chip buttons).</summary>
    [RelayCommand]
    private void SelectSuggestedModel(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) ModelToInstall = name.Trim();
    }

    /// <summary>Uninstalls (deletes) a model.</summary>
    [RelayCommand]
    private async Task DeleteModelAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Uninstall model?",
            $"Remove \"{name}\" from your machine? You can reinstall it any time.");
        if (!confirmed) return;

        try
        {
            await Task.Run(() => _ollama.DeleteModelAsync(name));
            await RefreshInstalledModelsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete model {Name}", name);
            ModelManagementError = $"Could not delete {name}: {ex.Message}";
        }
    }

    /// <summary>Sets the active text or vision model from an installed model row.</summary>
    [RelayCommand]
    private void UseModelForText(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        UseCustomHoshiModels = true;
        HoshiTextModel = name;
    }

    [RelayCommand]
    private void UseModelForVision(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        UseCustomHoshiModels = true;
        HoshiVisionModel = name;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Min(order, units.Length - 1);
        var value = bytes / Math.Pow(1024, order);
        return $"{value:0.##} {units[order]}";
    }
}

/// <summary>Row in the installed models list shown in Settings.</summary>
public sealed class InstalledModelRow
{
    public string Name { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedAt { get; }
    public string DisplaySize { get; }
    public string DisplayModified { get; }

    public InstalledModelRow(string name, long sizeBytes, DateTime modifiedAt)
    {
        Name = name;
        SizeBytes = sizeBytes;
        ModifiedAt = modifiedAt;
        DisplaySize = FormatBytes(sizeBytes);
        DisplayModified = modifiedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Min(order, units.Length - 1);
        var value = bytes / Math.Pow(1024, order);
        return $"{value:0.##} {units[order]}";
    }
}
