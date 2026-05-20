using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pixora.Agent;
using Pixora.Core.Data;
using Pixora.Core.DependencyInjection;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using System.IO;
using System;

var builder = Host.CreateApplicationBuilder(args);

// Logging — file + console
var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Pixora", "agent.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Core Pixiv services (SettingsService, PixivClient, PixivDownloadService, PixivImageLoader)
builder.Services.AddPixivCore();

// Repositories — same database as the main app (%APPDATA%\Pixora\downloads.db)
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Pixora", "downloads.db");

builder.Services.AddSingleton<DownloadJobRepository>(provider =>
    new DownloadJobRepository(dbPath, provider.GetRequiredService<ILogger<DownloadJobRepository>>()));

builder.Services.AddSingleton<DownloadPresetRepository>(provider =>
    new DownloadPresetRepository(dbPath, provider.GetRequiredService<ILogger<DownloadPresetRepository>>()));

builder.Services.AddSingleton<ArtistSettingsRepository>(provider =>
    new ArtistSettingsRepository(dbPath, provider.GetRequiredService<ILogger<ArtistSettingsRepository>>()));

builder.Services.AddSingleton<DownloadScheduleRepository>(provider =>
    new DownloadScheduleRepository(dbPath, provider.GetRequiredService<ILogger<DownloadScheduleRepository>>()));

// FanboxClient (required by DownloadCoordinator)
builder.Services.AddSingleton<FanboxClient>();

// AccountService (required by DownloadCoordinator)
builder.Services.AddSingleton<Pixora.Core.Settings.AccountService>();

// DownloadCoordinator
builder.Services.AddSingleton<DownloadCoordinator>(provider =>
    new DownloadCoordinator(
        provider.GetRequiredService<PixivClient>(),
        provider.GetRequiredService<PixivDownloadService>(),
        provider.GetRequiredService<SettingsService>(),
        provider.GetRequiredService<DownloadJobRepository>(),
        provider.GetRequiredService<FanboxClient>(),
        provider.GetRequiredService<ILogger<DownloadCoordinator>>(),
        provider.GetRequiredService<Pixora.Core.Settings.AccountService>()));

// Schedule executor
builder.Services.AddSingleton<ScheduleExecutorService>();

// IPC server
builder.Services.AddSingleton<IpcServer>();

// Background worker
builder.Services.AddHostedService<AgentWorker>();

// Windows Service support (no-op on Linux/macOS)
if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService(options => options.ServiceName = "Pixora Agent");

var host = builder.Build();
await host.RunAsync();
