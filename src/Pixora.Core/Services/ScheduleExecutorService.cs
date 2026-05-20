using Microsoft.Extensions.Logging;
using Pixora.Core.Data;
using Pixora.Core.Models;
using Pixora.Core.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pixora.Core.Services;

/// <summary>
/// Executes scheduled download tasks based on their configured triggers.
/// </summary>
public sealed class ScheduleExecutorService : IDisposable
{
    private readonly DownloadScheduleRepository _repository;
    private readonly DownloadCoordinator _coordinator;
    private readonly PixivClient _client;
    private readonly SettingsService _settingsService;
    private readonly ILogger<ScheduleExecutorService> _logger;
    private readonly Timer _checkTimer;

    private TimeSpan _checkInterval = TimeSpan.FromMinutes(1); // Check every minute
    private bool _isRunning;

    /// <summary>
    /// Event raised when a schedule starts executing.
    /// </summary>
    public event EventHandler<ScheduleExecutingEventArgs>? ScheduleExecuting;

    /// <summary>
    /// Event raised when a schedule completes.
    /// </summary>
    public event EventHandler<ScheduleCompletedEventArgs>? ScheduleCompleted;

    public ScheduleExecutorService(
        DownloadScheduleRepository repository,
        DownloadCoordinator coordinator,
        PixivClient client,
        SettingsService settingsService,
        ILogger<ScheduleExecutorService> logger)
    {
        _repository = repository;
        _coordinator = coordinator;
        _client = client;
        _settingsService = settingsService;
        _logger = logger;
        _checkTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Starts the schedule executor.
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        _isRunning = true;
        _checkTimer.Change(TimeSpan.Zero, _checkInterval);
        _logger.LogInformation("Schedule executor started");
    }

    /// <summary>
    /// Stops the schedule executor.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _checkTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _logger.LogInformation("Schedule executor stopped");
    }

    /// <summary>
    /// Executes all startup schedules immediately.
    /// </summary>
    public async Task ExecuteStartupSchedulesAsync(CancellationToken ct = default)
    {
        var schedules = await _repository.GetStartupSchedulesAsync(ct);
        if (schedules.Count == 0)
        {
            _logger.LogInformation("No startup schedules to execute");
            return;
        }

        _logger.LogInformation("Executing {Count} startup schedules", schedules.Count);

        foreach (var schedule in schedules)
        {
            try
            {
                await ExecuteScheduleAsync(schedule, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute startup schedule {ScheduleName}", schedule.Name);
            }
        }
    }

    private void OnTimerTick(object? state)
    {
        if (!_isRunning) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await CheckAndExecuteSchedulesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking schedules");
            }
        });
    }

    private async Task CheckAndExecuteSchedulesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var dueSchedules = await _repository.GetDueSchedulesAsync(now, ct);

        if (dueSchedules.Count == 0) return;

        _logger.LogInformation("Found {Count} schedules due for execution", dueSchedules.Count);

        foreach (var schedule in dueSchedules)
        {
            try
            {
                await ExecuteScheduleAsync(schedule, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute schedule {ScheduleName}", schedule.Name);
                await _repository.RecordRunAsync(schedule.Id, false, ex.Message, ct);
            }

            // Small delay between schedules
            await Task.Delay(1000, ct);
        }
    }

    private async Task ExecuteScheduleAsync(DownloadSchedule schedule, CancellationToken ct)
    {
        _logger.LogInformation("Executing schedule: {ScheduleName} (Type: {Type})",
            schedule.Name, schedule.Type);

        ScheduleExecuting?.Invoke(this, new ScheduleExecutingEventArgs(schedule));

        try
        {
            switch (schedule.Type)
            {
                case ScheduleType.FollowedArtists:
                    await ExecuteFollowedArtistsScheduleAsync(schedule, ct);
                    break;

                case ScheduleType.DailyRankings:
                    await ExecuteDailyRankingsScheduleAsync(schedule, ct);
                    break;

                case ScheduleType.SpecificArtists:
                    await ExecuteSpecificArtistsScheduleAsync(schedule, ct);
                    break;

                case ScheduleType.Bookmarks:
                    await ExecuteBookmarksScheduleAsync(schedule, ct);
                    break;

                default:
                    throw new NotSupportedException($"Schedule type {schedule.Type} not supported");
            }

            await _repository.RecordRunAsync(schedule.Id, true, null, ct);
            ScheduleCompleted?.Invoke(this, new ScheduleCompletedEventArgs(schedule, true, null));

            _logger.LogInformation("Schedule {ScheduleName} completed successfully", schedule.Name);
        }
        catch (Exception ex)
        {
            await _repository.RecordRunAsync(schedule.Id, false, ex.Message, ct);
            ScheduleCompleted?.Invoke(this, new ScheduleCompletedEventArgs(schedule, false, ex.Message));
            throw;
        }
    }

    /// <summary>
    /// Executes a single schedule immediately (used by RunNow in the UI).
    /// </summary>
    public async Task ExecuteOneAsync(DownloadSchedule schedule, CancellationToken ct = default)
        => await ExecuteScheduleAsync(schedule, ct);

    private async Task ExecuteFollowedArtistsScheduleAsync(DownloadSchedule schedule, CancellationToken ct)
    {
        var self = await _client.ResolveSelfAsync();
        if (self == null)
        {
            throw new InvalidOperationException("Not logged in");
        }

        // Get followed artists
        var response = await _client.GetFollowedArtistsAsync(self.Value.UserId, limit: schedule.ArtistLimit ?? 100);
        var artists = response.Users.ToList();

        if (artists.Count == 0)
        {
            _logger.LogWarning("No followed artists found");
            return;
        }

        // Create targets
        var targets = artists.Select(user => new DownloadTarget
        {
            TargetId = user.UserId.ToString(),
            Name = user.UserName,
            Type = TargetType.Artist,
            PageRange = schedule.PageRange
        }).ToList();

        // Get settings — apply OnlyNewSinceLastRun by setting DateFrom to the schedule's last run time
        var settings = ApplyOnlyNewFilter(schedule);

        // Create job
        await _coordinator.CreateJobAsync(
            DownloadJobType.BookmarkArtist,
            $"Scheduled: {schedule.Name}",
            targets,
            settings,
            startImmediately: true,
            ct);
    }

    private async Task ExecuteDailyRankingsScheduleAsync(DownloadSchedule schedule, CancellationToken ct)
    {
        var mode = schedule.RankingMode ?? "daily";
        var content = schedule.RankingContent ?? "all";
        var date = schedule.RankingDate?.ToString("yyyyMMdd") ?? "";

        // Get rankings
        var rankings = await _client.GetRankingAsync(mode, content, 1, ct, date);

        if (rankings.Contents.Count == 0)
        {
            _logger.LogWarning("No rankings found");
            return;
        }

        // Create targets from ranking entries
        var targets = rankings.Contents.Select(entry => new DownloadTarget
        {
            TargetId = entry.IllustId.ToString(),
            Name = entry.Title,
            Type = TargetType.Artwork
        }).ToList();

        var settings = schedule.Settings ?? new SettingsOverride { UseGlobalSettings = true };

        await _coordinator.CreateJobAsync(
            DownloadJobType.BookmarkImage, // Using bookmark image type for single artworks
            $"Scheduled Rankings: {schedule.Name}",
            targets,
            settings,
            startImmediately: true,
            ct);
    }

    private async Task ExecuteSpecificArtistsScheduleAsync(DownloadSchedule schedule, CancellationToken ct)
    {
        if (schedule.Artists.Count == 0)
        {
            throw new InvalidOperationException("No artists configured for schedule");
        }

        var targets = schedule.Artists.Select(artist => new DownloadTarget
        {
            TargetId = artist.UserId,
            Name = artist.UserName,
            Type = TargetType.Artist,
            PageRange = artist.PageRange ?? schedule.PageRange
        }).ToList();

        // Apply OnlyNewSinceLastRun by injecting DateFrom = last run time
        var settings = ApplyOnlyNewFilter(schedule);

        await _coordinator.CreateJobAsync(
            DownloadJobType.Artist,
            $"Scheduled: {schedule.Name}",
            targets,
            settings,
            startImmediately: true,
            ct);
    }

    private async Task ExecuteBookmarksScheduleAsync(DownloadSchedule schedule, CancellationToken ct)
    {
        var self = await _client.ResolveSelfAsync();
        if (self == null)
        {
            throw new InvalidOperationException("Not logged in");
        }

        // Get bookmarked artworks
        var bookmarks = await _client.GetBookmarkedArtworksAsync(self.Value.UserId, null, false);

        if (bookmarks.Works.Count == 0)
        {
            _logger.LogWarning("No bookmarks found");
            return;
        }

        var targets = bookmarks.Works.Select(artwork => new DownloadTarget
        {
            TargetId = artwork.Id,
            Name = artwork.Title,
            Type = TargetType.Artwork
        }).ToList();

        var settings = schedule.Settings ?? new SettingsOverride { UseGlobalSettings = true };

        await _coordinator.CreateJobAsync(
            DownloadJobType.BookmarkImage,
            $"Scheduled Bookmarks: {schedule.Name}",
            targets,
            settings,
            startImmediately: true,
            ct);
    }

    /// <summary>
    /// Returns effective settings with DateFrom resolved from the schedule's DateLimitMode.
    /// Produces a fresh copy so the stored schedule.Settings is never mutated.
    /// </summary>
    private static SettingsOverride ApplyOnlyNewFilter(DownloadSchedule schedule)
    {
        var src = schedule.Settings;
        var settings = src != null
            ? new SettingsOverride
            {
                UseGlobalSettings = src.UseGlobalSettings,
                DownloadRoot      = src.DownloadRoot,
                FolderTemplate    = src.FolderTemplate,
                FilenameTemplate  = src.FilenameTemplate,
                FilterAiGenerated = src.FilterAiGenerated,
                SkipManga         = src.SkipManga,
                SkipUgoira        = src.SkipUgoira,
                SkipR18           = src.SkipR18,
                SkipR18G          = src.SkipR18G,
                DateLimitMode        = src.DateLimitMode,
                MaxArtworksPerArtist = src.MaxArtworksPerArtist,
                IncludeTags          = src.IncludeTags,
                ExcludeTagsFilter    = src.ExcludeTagsFilter,
                SeparateR18Folder    = src.SeparateR18Folder,
                DateFrom             = src.DateFrom,
                DateTo               = src.DateTo,
                AllowRedownload      = src.AllowRedownload,
            }
            : new SettingsOverride { UseGlobalSettings = true };

        var now = DateTime.UtcNow;
        settings.DateFrom = settings.DateLimitMode switch
        {
            ScheduleDateLimitMode.SinceLastRun =>
                schedule.LastRunAt.HasValue ? schedule.LastRunAt.Value.ToLocalTime().Date : null,
            ScheduleDateLimitMode.Last1Day   => now.ToLocalTime().Date.AddDays(-1),
            ScheduleDateLimitMode.Last7Days  => now.ToLocalTime().Date.AddDays(-7),
            ScheduleDateLimitMode.Last30Days => now.ToLocalTime().Date.AddDays(-30),
            _                                => settings.DateFrom, // None — keep explicit DateFrom or null
        };

        return settings;
    }

    public void Dispose()
    {
        _checkTimer.Dispose();
    }
}

/// <summary>
/// Event args for schedule execution events.
/// </summary>
public class ScheduleExecutingEventArgs : EventArgs
{
    public DownloadSchedule Schedule { get; }

    public ScheduleExecutingEventArgs(DownloadSchedule schedule)
    {
        Schedule = schedule;
    }
}

/// <summary>
/// Event args for schedule completion events.
/// </summary>
public class ScheduleCompletedEventArgs : EventArgs
{
    public DownloadSchedule Schedule { get; }
    public bool Success { get; }
    public string? Error { get; }

    public ScheduleCompletedEventArgs(DownloadSchedule schedule, bool success, string? error)
    {
        Schedule = schedule;
        Success = success;
        Error = error;
    }
}
