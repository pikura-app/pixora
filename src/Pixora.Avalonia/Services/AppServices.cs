using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pixora.Avalonia.ViewModels;
using Pixora.Core.Data;
using Pixora.Core.DependencyInjection;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Pixora.Avalonia.Services;

/// <summary>
/// Service provider for the Avalonia application
/// </summary>
public static class AppServices
{
    private static IServiceProvider? _serviceProvider;
    private static IHost? _host;

    public static void Initialize()
    {
        // Load settings early to determine log level
        var earlySettings = new SettingsService();
        var logLevel = earlySettings.Current.VerboseLogging ? LogLevel.Debug : LogLevel.Information;
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pixora", "pixora.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(logLevel);
                logging.AddProvider(new FileLoggerProvider(logPath, logLevel));
            })
            .ConfigureServices((context, services) =>
            {
                // Core services (SettingsService, PixivHttpClientFactory, PixivClient, PixivDownloadService, PixivImageLoader)
                services.AddPixivCore();

                // Local favorites (app-side persistence)
                services.AddSingleton<Pixora.Core.Services.LocalFavoritesService>();

                // Download job repository (SQLite)
                var dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Pixora", "downloads.db");
                services.AddSingleton<DownloadJobRepository>(provider =>
                    new DownloadJobRepository(dbPath, provider.GetRequiredService<ILogger<DownloadJobRepository>>()));

                // Download preset repository (same database)
                services.AddSingleton<DownloadPresetRepository>(provider =>
                    new DownloadPresetRepository(dbPath, provider.GetRequiredService<ILogger<DownloadPresetRepository>>()));

                // Artist settings repository (same database)
                services.AddSingleton<ArtistSettingsRepository>(provider =>
                    new ArtistSettingsRepository(dbPath, provider.GetRequiredService<ILogger<ArtistSettingsRepository>>()));

                // Artist monitoring repository (same database)
                services.AddSingleton<ArtistMonitorRepository>(provider =>
                    new ArtistMonitorRepository(dbPath, provider.GetRequiredService<ILogger<ArtistMonitorRepository>>()));

                // Schedule repository (same database)
                services.AddSingleton<DownloadScheduleRepository>(provider =>
                    new DownloadScheduleRepository(dbPath, provider.GetRequiredService<ILogger<DownloadScheduleRepository>>()));

                // Download coordinator
                services.AddSingleton<DownloadCoordinator>(provider =>
                    new DownloadCoordinator(
                        provider.GetRequiredService<PixivClient>(),
                        provider.GetRequiredService<PixivDownloadService>(),
                        provider.GetRequiredService<SettingsService>(),
                        provider.GetRequiredService<DownloadJobRepository>(),
                        provider.GetRequiredService<FanboxClient>(),
                        provider.GetRequiredService<ILogger<DownloadCoordinator>>()));

                // Artist monitor service
                services.AddSingleton<ArtistMonitorService>();

                // Schedule executor service
                services.AddSingleton<ScheduleExecutorService>();

                // FANBOX client
                services.AddSingleton<FanboxClient>();

                // AI assistant (Ollama)
                services.AddSingleton<OllamaService>();
                services.AddSingleton<HoshiSessionService>();
                services.AddSingleton<ImageLookupService>();
                services.AddSingleton<Pixora.Avalonia.ViewModels.AiViewModel>(provider =>
                    new Pixora.Avalonia.ViewModels.AiViewModel(
                        provider.GetRequiredService<OllamaService>(),
                        provider.GetRequiredService<Pixora.Core.Services.LocalFavoritesService>(),
                        provider.GetRequiredService<PixivDownloadService>(),
                        provider.GetRequiredService<HoshiSessionService>(),
                        provider.GetRequiredService<PixivClient>(),
                        provider.GetRequiredService<PixivImageLoader>(),
                        provider.GetRequiredService<SettingsService>(),
                        provider.GetRequiredService<ImageLookupService>()));

                // Update check
                services.AddSingleton<UpdateCheckService>();

                // Multi-account
                services.AddSingleton<AccountService>();

                // Avalonia-specific services
                services.AddSingleton<NavigationService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<AccessibilityService>();
                services.AddSingleton<FilePickerService>();
                services.AddSingleton<NotificationService>();

                // ViewModels
                services.AddTransient<MainWindowViewModel>();
                services.AddSingleton<GalleryViewModel>();
                services.AddTransient<ArtworkDetailViewModel>();
                services.AddTransient<RankingsViewModel>();
                services.AddSingleton<EnhancedRankingsViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<AnalyticsViewModel>();
                services.AddTransient<DownloadsViewModel>();
                services.AddSingleton<DownloadByArtistViewModel>();
                services.AddTransient<DownloadByImageIdViewModel>();
                services.AddTransient<DownloadBookmarksViewModel>(provider =>
                    new DownloadBookmarksViewModel(
                        provider.GetRequiredService<PixivClient>(),
                        provider.GetRequiredService<DownloadCoordinator>(),
                        provider.GetRequiredService<DialogService>(),
                        provider.GetRequiredService<Pixora.Core.Services.LocalFavoritesService>(),
                        provider.GetRequiredService<SettingsService>()));
                services.AddTransient<DownloadFromListViewModel>(provider =>
                    new DownloadFromListViewModel(
                        provider.GetRequiredService<PixivClient>(),
                        provider.GetRequiredService<DownloadCoordinator>(),
                        provider.GetRequiredService<DialogService>()));
                services.AddTransient<DownloadBySearchViewModel>(provider =>
                    new DownloadBySearchViewModel(
                        provider.GetRequiredService<PixivClient>(),
                        provider.GetRequiredService<DownloadCoordinator>(),
                        provider.GetRequiredService<DialogService>(),
                        provider.GetRequiredService<PixivImageLoader>()));
                services.AddSingleton<SchedulesViewModel>(provider =>
                    new SchedulesViewModel(
                        provider.GetRequiredService<DownloadScheduleRepository>(),
                        provider.GetRequiredService<PixivClient>(),
                        provider.GetRequiredService<SettingsService>(),
                        provider.GetRequiredService<DialogService>(),
                        provider.GetRequiredService<ScheduleExecutorService>()));
                services.AddTransient<DownloadByFanboxViewModel>(provider =>
                    new DownloadByFanboxViewModel(
                        provider.GetRequiredService<FanboxClient>(),
                        provider.GetRequiredService<DownloadCoordinator>(),
                        provider.GetRequiredService<DialogService>(),
                        provider.GetRequiredService<SettingsService>()));
                services.AddTransient<BatchDownloadViewModel>(provider =>
                    new BatchDownloadViewModel(
                        provider.GetRequiredService<DownloadByArtistViewModel>(),
                        provider.GetRequiredService<DownloadByImageIdViewModel>(),
                        provider.GetRequiredService<DownloadBookmarksViewModel>(),
                        provider.GetRequiredService<DownloadFromListViewModel>(),
                        provider.GetRequiredService<DownloadBySearchViewModel>(),
                        provider.GetRequiredService<DownloadByFanboxViewModel>(),
                        provider.GetRequiredService<SchedulesViewModel>()));
                services.AddTransient<HistoryViewModel>(provider =>
                    new HistoryViewModel(
                        provider.GetRequiredService<DownloadJobRepository>(),
                        provider.GetRequiredService<DownloadCoordinator>(),
                        provider.GetRequiredService<DialogService>(),
                        provider.GetRequiredService<PixivImageLoader>()));
                services.AddSingleton<DiscoverViewModel>();
                services.AddSingleton<BookmarksViewModel>(provider =>
                    new BookmarksViewModel(
                        provider.GetRequiredService<PixivClient>(),
                        provider.GetRequiredService<PixivImageLoader>(),
                        provider.GetRequiredService<SettingsService>(),
                        provider.GetRequiredService<Pixora.Core.Services.LocalFavoritesService>(),
                        provider.GetRequiredService<GalleryViewModel>(),
                        provider.GetRequiredService<DownloadCoordinator>(),
                        provider.GetRequiredService<DialogService>()));

                // Logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                });
            });

        _host = hostBuilder.Build();
        _serviceProvider = _host.Services;

        // Wire up notifications
        WireUpNotifications();
    }

    private static void WireUpNotifications()
    {
        try
        {
            var coordinator = Get<DownloadCoordinator>();
            var notificationService = Get<NotificationService>();
            var monitorService = Get<ArtistMonitorService>();

            // Download completion notifications
            coordinator.JobCompleted += (sender, e) =>
            {
                var succeeded = e.Job.CompletedItems;
                var failed = e.Job.FailedItems;
                var firstArtworkId = e.Job.Targets.FirstOrDefault()?.TargetId;
                notificationService.ShowJobCompletedNotification(e.Job.Name, succeeded, failed, firstArtworkId);
            };

            // New submission notifications
            monitorService.NewSubmissionsDetected += (sender, e) =>
            {
                var count = e.NewSubmissions.Count;
                var firstSubmission = e.NewSubmissions.FirstOrDefault();
                var firstTitle = firstSubmission?.Title;
                var firstArtworkId = firstSubmission?.ArtworkId;
                notificationService.ShowNewSubmissionNotification(e.Artist.UserName, count, firstTitle, firstArtworkId);
            };

            // Handle notification clicks
            notificationService.NotificationClicked += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Url))
                {
                    // Open the URL in browser
                    try
                    {
                        Process.Start(new ProcessStartInfo(e.Url) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to open URL: {ex.Message}");
                    }
                }
            };

            notificationService.Initialize();

            // Start monitoring (if user is logged in)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5000); // Wait 5 seconds for app to fully load
                    monitorService.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to start monitor service: {ex.Message}");
                }
            });

            // Start schedule executor
            _ = Task.Run(async () =>
            {
                try
                {
                    var executor = Get<ScheduleExecutorService>();

                    // Execute startup schedules first
                    await executor.ExecuteStartupSchedulesAsync();

                    // Start periodic checking
                    executor.Start();

                    Debug.WriteLine("Schedule executor started");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to start schedule executor: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            // Log but don't crash if notification setup fails
            Debug.WriteLine($"Failed to wire up notifications: {ex.Message}");
        }
    }

    public static T Get<T>() where T : class
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("AppServices not initialized. Call Initialize() first.");
        
        return _serviceProvider.GetRequiredService<T>();
    }

    public static async Task ShutdownAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
