using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Pikura.Core.Models;
using System.Text.Json;

namespace Pikura.Core.Data;

/// <summary>
/// SQLite-backed repository for per-artist download settings.
/// </summary>
public sealed class ArtistSettingsRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<ArtistSettingsRepository> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ArtistSettingsRepository(string dbPath, ILogger<ArtistSettingsRepository> logger)
    {
        _logger = logger;
        _connectionString = $"Data Source={dbPath};Foreign Keys=True;";
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        EnsureDatabaseCreated();
    }

    private void EnsureDatabaseCreated()
    {
        using var connection = CreateConnection();
        connection.Open();

        var createTable = @"
            CREATE TABLE IF NOT EXISTS artist_settings (
                user_id TEXT PRIMARY KEY,
                user_name TEXT NOT NULL,
                settings_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_artist_settings_name ON artist_settings(user_name);
        ";

        using var cmd = new SqliteCommand(createTable, connection);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// Gets settings for a specific artist by user ID.
    /// </summary>
    public async Task<SettingsOverride?> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "SELECT settings_json FROM artist_settings WHERE user_id = @userId";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var settingsJson = reader.GetString(reader.GetOrdinal("settings_json"));
            return JsonSerializer.Deserialize<SettingsOverride>(settingsJson, _jsonOptions);
        }

        return null;
    }

    /// <summary>
    /// Saves or updates settings for an artist.
    /// </summary>
    public async Task SaveAsync(string userId, string userName, SettingsOverride settings, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = @"
            INSERT INTO artist_settings (user_id, user_name, settings_json, updated_at)
            VALUES (@userId, @userName, @settings, @updatedAt)
            ON CONFLICT(user_id) DO UPDATE SET
                user_name = @userName,
                settings_json = @settings,
                updated_at = @updatedAt";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@userName", userName);
        cmd.Parameters.AddWithValue("@settings", JsonSerializer.Serialize(settings, _jsonOptions));
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes settings for an artist.
    /// </summary>
    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        using var cmd = new SqliteCommand("DELETE FROM artist_settings WHERE user_id = @userId", connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Gets all artists with custom settings.
    /// </summary>
    public async Task<List<(string UserId, string UserName, SettingsOverride Settings)>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "SELECT user_id, user_name, settings_json FROM artist_settings ORDER BY user_name";
        using var cmd = new SqliteCommand(sql, connection);

        var result = new List<(string, string, SettingsOverride)>();
        using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var userId = reader.GetString(reader.GetOrdinal("user_id"));
            var userName = reader.GetString(reader.GetOrdinal("user_name"));
            var settingsJson = reader.GetString(reader.GetOrdinal("settings_json"));
            var settings = JsonSerializer.Deserialize<SettingsOverride>(settingsJson, _jsonOptions) ?? new();
            result.Add((userId, userName, settings));
        }

        return result;
    }

    public void Dispose()
    {
        // Connection pooling handles cleanup
    }
}
