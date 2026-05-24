using System;
using System.Collections.Generic;

namespace Pikura.Core.Models;

/// <summary>
/// Types of scheduled download tasks.
/// </summary>
public enum ScheduleType
{
    /// <summary>Download from followed artists.</summary>
    FollowedArtists,

    /// <summary>Download top rankings for the day.</summary>
    DailyRankings,

    /// <summary>Download specific artists.</summary>
    SpecificArtists,

    /// <summary>Download from bookmarks.</summary>
    Bookmarks
}

/// <summary>
/// When/how a schedule should trigger.
/// </summary>
public enum ScheduleTrigger
{
    /// <summary>Trigger at a specific time daily.</summary>
    DailyAtTime,

    /// <summary>Trigger on application startup.</summary>
    OnStartup,

    /// <summary>Trigger after a specific interval.</summary>
    Interval,

    /// <summary>Trigger once at a specific date/time.</summary>
    Once
}

/// <summary>
/// A scheduled download task.
/// </summary>
public sealed class DownloadSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-friendly name for this schedule.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this schedule is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Type of download to perform.</summary>
    public ScheduleType Type { get; set; }

    /// <summary>When/how to trigger this schedule.</summary>
    public ScheduleTrigger Trigger { get; set; }

    #region Time-based Trigger Settings

    /// <summary>For DailyAtTime: Hour to trigger (0-23).</summary>
    public int? TriggerHour { get; set; }

    /// <summary>For DailyAtTime: Minute to trigger (0-59).</summary>
    public int? TriggerMinute { get; set; }

    /// <summary>For Once: The specific date/time to trigger.</summary>
    public DateTime? TriggerDateTime { get; set; }

    /// <summary>For Interval: Interval between runs.</summary>
    public TimeSpan? Interval { get; set; }

    #endregion

    #region Download Configuration

    /// <summary>For SpecificArtists: The artist IDs to download from.</summary>
    public List<ScheduledArtist> Artists { get; set; } = new();

    /// <summary>For FollowedArtists: Limit to top N artists (null = all).</summary>
    public int? ArtistLimit { get; set; }

    /// <summary>Page range to download (e.g., "0" for all, "1-5" for range).</summary>
    public string PageRange { get; set; } = "0";

    /// <summary>For Rankings: Ranking mode (daily, weekly, monthly, etc.).</summary>
    public string? RankingMode { get; set; }

    /// <summary>For Rankings: Content type (all, illust, ugoira, manga).</summary>
    public string? RankingContent { get; set; }

    /// <summary>For Rankings: Date for rankings (null = today).</summary>
    public DateTime? RankingDate { get; set; }

    /// <summary>Settings override to use (null = use global settings).</summary>
    public SettingsOverride? Settings { get; set; }

    #endregion

    #region Metadata

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int RunCount { get; set; }
    public string? LastError { get; set; }

    #endregion

    /// <summary>
    /// Calculates when this schedule should next run.
    /// </summary>
    public DateTime? CalculateNextRun()
    {
        if (!IsEnabled) return null;

        var now = DateTime.UtcNow;

        return Trigger switch
        {
            ScheduleTrigger.DailyAtTime => CalculateDailyNextRun(now),
            ScheduleTrigger.OnStartup => null, // Special: only on startup
            ScheduleTrigger.Interval => CalculateIntervalNextRun(now),
            ScheduleTrigger.Once => TriggerDateTime > now ? TriggerDateTime : null,
            _ => null
        };
    }

    private DateTime? CalculateDailyNextRun(DateTime now)
    {
        if (TriggerHour == null || TriggerMinute == null) return null;

        var nextRun = new DateTime(now.Year, now.Month, now.Day, TriggerHour.Value, TriggerMinute.Value, 0, DateTimeKind.Utc);
        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }
        return nextRun;
    }

    private DateTime? CalculateIntervalNextRun(DateTime now)
    {
        if (Interval == null || Interval.Value <= TimeSpan.Zero) return null;

        var lastRun = LastRunAt ?? CreatedAt;
        var nextRun = lastRun + Interval.Value;

        // If we've missed multiple intervals, run from now
        while (nextRun <= now)
        {
            nextRun = nextRun.Add(Interval.Value);
        }

        return nextRun;
    }
}

/// <summary>
/// An artist configured in a schedule.
/// </summary>
public class ScheduledArtist
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? PageRange { get; set; }
}
