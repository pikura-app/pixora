using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Pixora.Core.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pixora.Core.Data;

/// <summary>
/// SQLite-backed repository for download schedules.
/// </summary>
public sealed class DownloadScheduleRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<DownloadScheduleRepository> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DownloadScheduleRepository(string dbPath, ILogger<DownloadScheduleRepository> logger)
    {
        _logger = logger;
        _connectionString = $"Data Source={dbPath};Foreign Keys=True;";
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        EnsureDatabaseCreated();
    }

    private void EnsureDatabaseCreated()
    {
        using var connection = CreateConnection();
        connection.Open();

        var createTable = @"
            CREATE TABLE IF NOT EXISTS download_schedules (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                is_enabled INTEGER DEFAULT 1,
                type INTEGER NOT NULL,
                trigger_type INTEGER NOT NULL,
                trigger_hour INTEGER,
                trigger_minute INTEGER,
                trigger_datetime TEXT,
                interval_minutes INTEGER,
                page_range TEXT DEFAULT '0',
                ranking_mode TEXT,
                ranking_content TEXT,
                ranking_date TEXT,
                artist_limit INTEGER,
                artists_json TEXT,
                settings_json TEXT,
                created_at TEXT NOT NULL,
                last_run_at TEXT,
                next_run_at TEXT,
                run_count INTEGER DEFAULT 0,
                last_error TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_schedules_enabled ON download_schedules(is_enabled);
            CREATE INDEX IF NOT EXISTS idx_schedules_next_run ON download_schedules(next_run_at);
        ";

        using var cmd = new SqliteCommand(createTable, connection);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// Gets all schedules.
    /// </summary>
    public async Task<List<DownloadSchedule>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var schedules = new List<DownloadSchedule>();
        var cmd = new SqliteCommand(@"
            SELECT id, name, is_enabled, type, trigger_type, trigger_hour, trigger_minute,
                   trigger_datetime, interval_minutes, page_range, ranking_mode, ranking_content,
                   ranking_date, artist_limit, artists_json, settings_json, created_at,
                   last_run_at, next_run_at, run_count, last_error
            FROM download_schedules
            ORDER BY created_at DESC", connection);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schedules.Add(ReadScheduleFromReader(reader));
        }

        return schedules;
    }

    /// <summary>
    /// Gets enabled schedules that are due to run.
    /// </summary>
    public async Task<List<DownloadSchedule>> GetDueSchedulesAsync(DateTime before, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var schedules = new List<DownloadSchedule>();
        var cmd = new SqliteCommand(@"
            SELECT id, name, is_enabled, type, trigger_type, trigger_hour, trigger_minute,
                   trigger_datetime, interval_minutes, page_range, ranking_mode, ranking_content,
                   ranking_date, artist_limit, artists_json, settings_json, created_at,
                   last_run_at, next_run_at, run_count, last_error
            FROM download_schedules
            WHERE is_enabled = 1 AND next_run_at IS NOT NULL AND next_run_at <= @before
            ORDER BY next_run_at ASC", connection);

        cmd.Parameters.AddWithValue("@before", before);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schedules.Add(ReadScheduleFromReader(reader));
        }

        return schedules;
    }

    /// <summary>
    /// Gets schedules that should run on startup.
    /// </summary>
    public async Task<List<DownloadSchedule>> GetStartupSchedulesAsync(CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var schedules = new List<DownloadSchedule>();
        var cmd = new SqliteCommand(@"
            SELECT id, name, is_enabled, type, trigger_type, trigger_hour, trigger_minute,
                   trigger_datetime, interval_minutes, page_range, ranking_mode, ranking_content,
                   ranking_date, artist_limit, artists_json, settings_json, created_at,
                   last_run_at, next_run_at, run_count, last_error
            FROM download_schedules
            WHERE is_enabled = 1 AND trigger_type = @triggerType
            ORDER BY created_at ASC", connection);

        cmd.Parameters.AddWithValue("@triggerType", (int)ScheduleTrigger.OnStartup);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schedules.Add(ReadScheduleFromReader(reader));
        }

        return schedules;
    }

    /// <summary>
    /// Gets a schedule by ID.
    /// </summary>
    public async Task<DownloadSchedule?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand(@"
            SELECT id, name, is_enabled, type, trigger_type, trigger_hour, trigger_minute,
                   trigger_datetime, interval_minutes, page_range, ranking_mode, ranking_content,
                   ranking_date, artist_limit, artists_json, settings_json, created_at,
                   last_run_at, next_run_at, run_count, last_error
            FROM download_schedules
            WHERE id = @id", connection);

        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadScheduleFromReader(reader);
        }

        return null;
    }

    /// <summary>
    /// Saves a schedule (insert or update).
    /// </summary>
    public async Task SaveAsync(DownloadSchedule schedule, CancellationToken ct = default)
    {
        // Calculate next run time
        schedule.NextRunAt = schedule.CalculateNextRun();

        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand(@"
            INSERT INTO download_schedules (
                id, name, is_enabled, type, trigger_type, trigger_hour, trigger_minute,
                trigger_datetime, interval_minutes, page_range, ranking_mode, ranking_content,
                ranking_date, artist_limit, artists_json, settings_json, created_at,
                last_run_at, next_run_at, run_count, last_error
            ) VALUES (
                @id, @name, @isEnabled, @type, @triggerType, @triggerHour, @triggerMinute,
                @triggerDateTime, @intervalMinutes, @pageRange, @rankingMode, @rankingContent,
                @rankingDate, @artistLimit, @artistsJson, @settingsJson, @createdAt,
                @lastRunAt, @nextRunAt, @runCount, @lastError
            )
            ON CONFLICT(id) DO UPDATE SET
                name = @name,
                is_enabled = @isEnabled,
                type = @type,
                trigger_type = @triggerType,
                trigger_hour = @triggerHour,
                trigger_minute = @triggerMinute,
                trigger_datetime = @triggerDateTime,
                interval_minutes = @intervalMinutes,
                page_range = @pageRange,
                ranking_mode = @rankingMode,
                ranking_content = @rankingContent,
                ranking_date = @rankingDate,
                artist_limit = @artistLimit,
                artists_json = @artistsJson,
                settings_json = @settingsJson,
                last_run_at = @lastRunAt,
                next_run_at = @nextRunAt,
                run_count = @runCount,
                last_error = @lastError", connection);

        cmd.Parameters.AddWithValue("@id", schedule.Id.ToString());
        cmd.Parameters.AddWithValue("@name", schedule.Name);
        cmd.Parameters.AddWithValue("@isEnabled", schedule.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@type", (int)schedule.Type);
        cmd.Parameters.AddWithValue("@triggerType", (int)schedule.Trigger);
        cmd.Parameters.AddWithValue("@triggerHour", (object?)schedule.TriggerHour ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@triggerMinute", (object?)schedule.TriggerMinute ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@triggerDateTime", (object?)schedule.TriggerDateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@intervalMinutes", (object?)schedule.Interval?.TotalMinutes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pageRange", schedule.PageRange);
        cmd.Parameters.AddWithValue("@rankingMode", (object?)schedule.RankingMode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rankingContent", (object?)schedule.RankingContent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rankingDate", (object?)schedule.RankingDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@artistLimit", (object?)schedule.ArtistLimit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@artistsJson", JsonSerializer.Serialize(schedule.Artists, _jsonOptions));
        cmd.Parameters.AddWithValue("@settingsJson", schedule.Settings != null
            ? JsonSerializer.Serialize(schedule.Settings, _jsonOptions)
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", schedule.CreatedAt);
        cmd.Parameters.AddWithValue("@lastRunAt", (object?)schedule.LastRunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nextRunAt", (object?)schedule.NextRunAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@runCount", schedule.RunCount);
        cmd.Parameters.AddWithValue("@lastError", (object?)schedule.LastError ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Saved schedule {ScheduleId}: {ScheduleName}", schedule.Id, schedule.Name);
    }

    /// <summary>
    /// Deletes a schedule.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand("DELETE FROM download_schedules WHERE id = @id", connection);
        cmd.Parameters.AddWithValue("@id", id.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Deleted schedule {ScheduleId}", id);
    }

    /// <summary>
    /// Updates a schedule's last run information.
    /// </summary>
    public async Task RecordRunAsync(Guid id, bool success, string? error = null, CancellationToken ct = default)
    {
        var schedule = await GetByIdAsync(id, ct);
        if (schedule == null) return;

        schedule.LastRunAt = DateTime.UtcNow;
        schedule.RunCount++;
        if (!success)
        {
            schedule.LastError = error;
        }
        else
        {
            schedule.LastError = null;
        }

        // Recalculate next run
        schedule.NextRunAt = schedule.CalculateNextRun();

        await SaveAsync(schedule, ct);
    }

    private DownloadSchedule ReadScheduleFromReader(SqliteDataReader reader)
    {
        var idOrdinal = reader.GetOrdinal("id");
        var artistsJsonOrdinal = reader.GetOrdinal("artists_json");
        var settingsJsonOrdinal = reader.GetOrdinal("settings_json");

        var schedule = new DownloadSchedule
        {
            Id = Guid.Parse(reader.GetString(idOrdinal)),
            Name = reader.GetString(reader.GetOrdinal("name")),
            IsEnabled = reader.GetInt32(reader.GetOrdinal("is_enabled")) == 1,
            Type = (ScheduleType)reader.GetInt32(reader.GetOrdinal("type")),
            Trigger = (ScheduleTrigger)reader.GetInt32(reader.GetOrdinal("trigger_type")),
            TriggerHour = reader.IsDBNull(reader.GetOrdinal("trigger_hour")) ? null : reader.GetInt32(reader.GetOrdinal("trigger_hour")),
            TriggerMinute = reader.IsDBNull(reader.GetOrdinal("trigger_minute")) ? null : reader.GetInt32(reader.GetOrdinal("trigger_minute")),
            TriggerDateTime = reader.IsDBNull(reader.GetOrdinal("trigger_datetime")) ? null : reader.GetDateTime(reader.GetOrdinal("trigger_datetime")),
            Interval = reader.IsDBNull(reader.GetOrdinal("interval_minutes")) ? null : TimeSpan.FromMinutes(reader.GetInt32(reader.GetOrdinal("interval_minutes"))),
            PageRange = reader.GetString(reader.GetOrdinal("page_range")),
            RankingMode = reader.IsDBNull(reader.GetOrdinal("ranking_mode")) ? null : reader.GetString(reader.GetOrdinal("ranking_mode")),
            RankingContent = reader.IsDBNull(reader.GetOrdinal("ranking_content")) ? null : reader.GetString(reader.GetOrdinal("ranking_content")),
            RankingDate = reader.IsDBNull(reader.GetOrdinal("ranking_date")) ? null : reader.GetDateTime(reader.GetOrdinal("ranking_date")),
            ArtistLimit = reader.IsDBNull(reader.GetOrdinal("artist_limit")) ? null : reader.GetInt32(reader.GetOrdinal("artist_limit")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            LastRunAt = reader.IsDBNull(reader.GetOrdinal("last_run_at")) ? null : reader.GetDateTime(reader.GetOrdinal("last_run_at")),
            NextRunAt = reader.IsDBNull(reader.GetOrdinal("next_run_at")) ? null : reader.GetDateTime(reader.GetOrdinal("next_run_at")),
            RunCount = reader.GetInt32(reader.GetOrdinal("run_count")),
            LastError = reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error"))
        };

        // Deserialize JSON
        var artistsJson = reader.GetString(artistsJsonOrdinal);
        if (!string.IsNullOrEmpty(artistsJson))
        {
            schedule.Artists = JsonSerializer.Deserialize<List<ScheduledArtist>>(artistsJson, _jsonOptions) ?? new();
        }

        var settingsJson = reader.IsDBNull(settingsJsonOrdinal) ? null : reader.GetString(settingsJsonOrdinal);
        if (!string.IsNullOrEmpty(settingsJson))
        {
            schedule.Settings = JsonSerializer.Deserialize<SettingsOverride>(settingsJson, _jsonOptions);
        }

        return schedule;
    }

    public void Dispose()
    {
        // Connection is disposed per-operation
    }
}
