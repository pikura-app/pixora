using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Pikura.Core.Models;
using System.Collections.Generic;
using System.Text.Json;

namespace Pikura.Core.Data;

/// <summary>
/// SQLite-backed repository for download presets.
/// </summary>
public sealed class DownloadPresetRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<DownloadPresetRepository> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DownloadPresetRepository(string dbPath, ILogger<DownloadPresetRepository> logger)
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
            CREATE TABLE IF NOT EXISTS download_presets (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                type INTEGER NOT NULL,
                settings_json TEXT,
                default_page_range TEXT,
                use_per_artist_ranges INTEGER DEFAULT 0,
                artists_json TEXT,
                created_at TEXT NOT NULL,
                last_used_at TEXT,
                use_count INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_presets_type ON download_presets(type);
            CREATE INDEX IF NOT EXISTS idx_presets_name ON download_presets(name);
        ";

        using var cmd = new SqliteCommand(createTable, connection);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    public async Task<List<DownloadPreset>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "SELECT * FROM download_presets ORDER BY last_used_at DESC, created_at DESC";
        using var cmd = new SqliteCommand(sql, connection);

        var presets = new List<DownloadPreset>();
        using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            presets.Add(ReadPresetFromReader(reader));
        }

        return presets;
    }

    public async Task<DownloadPreset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "SELECT * FROM download_presets WHERE id = @id";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadPresetFromReader(reader);
        }

        return null;
    }

    public async Task SaveAsync(DownloadPreset preset, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = @"
            INSERT INTO download_presets
            (id, name, description, type, settings_json, default_page_range, use_per_artist_ranges, artists_json, created_at, last_used_at, use_count)
            VALUES
            (@id, @name, @desc, @type, @settings, @pageRange, @perArtist, @artists, @created, @lastUsed, @useCount)
            ON CONFLICT(id) DO UPDATE SET
                name = @name,
                description = @desc,
                type = @type,
                settings_json = @settings,
                default_page_range = @pageRange,
                use_per_artist_ranges = @perArtist,
                artists_json = @artists,
                last_used_at = @lastUsed,
                use_count = @useCount";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", preset.Id.ToString());
        cmd.Parameters.AddWithValue("@name", preset.Name);
        cmd.Parameters.AddWithValue("@desc", preset.Description ?? "");
        cmd.Parameters.AddWithValue("@type", (int)preset.Type);
        cmd.Parameters.AddWithValue("@settings", JsonSerializer.Serialize(preset.Settings, _jsonOptions));
        cmd.Parameters.AddWithValue("@pageRange", preset.DefaultPageRange ?? "");
        cmd.Parameters.AddWithValue("@perArtist", preset.UsePerArtistPageRanges ? 1 : 0);
        cmd.Parameters.AddWithValue("@artists", JsonSerializer.Serialize(preset.Artists, _jsonOptions));
        cmd.Parameters.AddWithValue("@created", preset.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastUsed", preset.LastUsedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@useCount", preset.UseCount);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        using var cmd = new SqliteCommand("DELETE FROM download_presets WHERE id = @id", connection);
        cmd.Parameters.AddWithValue("@id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordUsageAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "UPDATE download_presets SET last_used_at = @now, use_count = use_count + 1 WHERE id = @id";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private DownloadPreset ReadPresetFromReader(SqliteDataReader reader)
    {
        var settingsJson = reader.GetString(reader.GetOrdinal("settings_json"));
        var artistsJson = reader.GetString(reader.GetOrdinal("artists_json"));

        return new DownloadPreset
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Description = reader.IsDBNull(reader.GetOrdinal("description")) ? "" : reader.GetString(reader.GetOrdinal("description")),
            Type = (DownloadJobType)reader.GetInt32(reader.GetOrdinal("type")),
            Settings = JsonSerializer.Deserialize<SettingsOverride>(settingsJson, _jsonOptions) ?? new(),
            DefaultPageRange = reader.IsDBNull(reader.GetOrdinal("default_page_range")) ? null : reader.GetString(reader.GetOrdinal("default_page_range")),
            UsePerArtistPageRanges = reader.GetInt32(reader.GetOrdinal("use_per_artist_ranges")) == 1,
            Artists = JsonSerializer.Deserialize<List<PresetArtist>>(artistsJson, _jsonOptions) ?? new(),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            LastUsedAt = reader.IsDBNull(reader.GetOrdinal("last_used_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_used_at"))),
            UseCount = reader.GetInt32(reader.GetOrdinal("use_count")),
        };
    }

    public void Dispose()
    {
        // Connection pooling handles cleanup
    }
}
