using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Pixora.Core.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Pixora.Core.Data;

/// <summary>
/// SQLite repository for artist monitoring state.
/// Tracks last checked timestamps and known submission IDs.
/// </summary>
public sealed class ArtistMonitorRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<ArtistMonitorRepository> _logger;

    public ArtistMonitorRepository(string dbPath, ILogger<ArtistMonitorRepository> logger)
    {
        _logger = logger;
        _connectionString = $"Data Source={dbPath};Foreign Keys=True;";
        EnsureDatabaseCreated();
    }

    private void EnsureDatabaseCreated()
    {
        using var connection = CreateConnection();
        connection.Open();

        var createTable = @"
            CREATE TABLE IF NOT EXISTS artist_monitor_state (
                user_id TEXT PRIMARY KEY,
                user_name TEXT NOT NULL,
                last_checked_at TEXT,
                last_submission_id TEXT,
                is_enabled INTEGER DEFAULT 1,
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS known_submissions (
                user_id TEXT NOT NULL,
                artwork_id TEXT NOT NULL,
                title TEXT,
                created_at TEXT NOT NULL,
                notified_at TEXT,
                PRIMARY KEY (user_id, artwork_id),
                FOREIGN KEY (user_id) REFERENCES artist_monitor_state(user_id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_known_submissions_user ON known_submissions(user_id);
            CREATE INDEX IF NOT EXISTS idx_monitor_enabled ON artist_monitor_state(is_enabled);
        ";

        using var cmd = new SqliteCommand(createTable, connection);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// Gets all artists being monitored.
    /// </summary>
    public async Task<List<MonitoredArtist>> GetMonitoredArtistsAsync(CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var artists = new List<MonitoredArtist>();
        var cmd = new SqliteCommand(@"
            SELECT user_id, user_name, last_checked_at, last_submission_id, is_enabled, created_at
            FROM artist_monitor_state WHERE is_enabled = 1", connection);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            artists.Add(new MonitoredArtist
            {
                UserId = reader.GetString("user_id"),
                UserName = reader.GetString("user_name"),
                LastCheckedAt = reader.IsDBNull("last_checked_at") ? null : reader.GetDateTime("last_checked_at"),
                LastSubmissionId = reader.IsDBNull("last_submission_id") ? null : reader.GetString("last_submission_id"),
                IsEnabled = reader.GetInt32("is_enabled") == 1,
                CreatedAt = reader.GetDateTime("created_at")
            });
        }

        return artists;
    }

    /// <summary>
    /// Adds an artist to monitoring.
    /// </summary>
    public async Task AddMonitoredArtistAsync(string userId, string userName, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand(@"
            INSERT OR REPLACE INTO artist_monitor_state (user_id, user_name, last_checked_at, is_enabled, created_at)
            VALUES (@userId, @userName, NULL, 1, @createdAt)", connection);

        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@userName", userName);
        cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Added artist {UserId} ({UserName}) to monitoring", userId, userName);
    }

    /// <summary>
    /// Updates the last checked timestamp and submission ID for an artist.
    /// </summary>
    public async Task UpdateLastCheckedAsync(string userId, string? lastSubmissionId, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand(@"
            UPDATE artist_monitor_state
            SET last_checked_at = @now, last_submission_id = @lastSubmissionId
            WHERE user_id = @userId", connection);

        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@lastSubmissionId", (object?)lastSubmissionId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Records a known submission (prevents duplicate notifications).
    /// </summary>
    public async Task RecordKnownSubmissionAsync(string userId, string artworkId, string? title, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand(@"
            INSERT OR IGNORE INTO known_submissions (user_id, artwork_id, title, created_at, notified_at)
            VALUES (@userId, @artworkId, @title, @createdAt, @notifiedAt)", connection);

        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@artworkId", artworkId);
        cmd.Parameters.AddWithValue("@title", (object?)title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@notifiedAt", DateTime.UtcNow);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Checks if a submission has already been recorded.
    /// </summary>
    public async Task<bool> IsKnownSubmissionAsync(string userId, string artworkId, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand(@"
            SELECT 1 FROM known_submissions WHERE user_id = @userId AND artwork_id = @artworkId",
            connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@artworkId", artworkId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }

    /// <summary>
    /// Removes an artist from monitoring.
    /// </summary>
    public async Task RemoveMonitoredArtistAsync(string userId, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var cmd = new SqliteCommand(@"
            DELETE FROM artist_monitor_state WHERE user_id = @userId", connection);
        cmd.Parameters.AddWithValue("@userId", userId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Gets artists that haven't been checked recently.
    /// </summary>
    public async Task<List<MonitoredArtist>> GetArtistsToCheckAsync(TimeSpan maxAge, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var artists = new List<MonitoredArtist>();
        var cmd = new SqliteCommand(@"
            SELECT user_id, user_name, last_checked_at, last_submission_id, is_enabled, created_at
            FROM artist_monitor_state
            WHERE is_enabled = 1 AND (last_checked_at IS NULL OR last_checked_at < @cutoff)
            ORDER BY last_checked_at ASC NULLS FIRST", connection);

        cmd.Parameters.AddWithValue("@cutoff", cutoff);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            artists.Add(new MonitoredArtist
            {
                UserId = reader.GetString("user_id"),
                UserName = reader.GetString("user_name"),
                LastCheckedAt = reader.IsDBNull("last_checked_at") ? null : reader.GetDateTime("last_checked_at"),
                LastSubmissionId = reader.IsDBNull("last_submission_id") ? null : reader.GetString("last_submission_id"),
                IsEnabled = reader.GetInt32("is_enabled") == 1,
                CreatedAt = reader.GetDateTime("created_at")
            });
        }

        return artists;
    }

    public void Dispose()
    {
        // Connection is disposed per-operation
    }
}

/// <summary>
/// Represents an artist being monitored for new submissions.
/// </summary>
public class MonitoredArtist
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime? LastCheckedAt { get; set; }
    public string? LastSubmissionId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
