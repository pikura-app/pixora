using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Pixora.Core.Models;
using System.Collections.Generic;
using System.Text.Json;

namespace Pixora.Core.Data;

/// <summary>
/// SQLite-backed repository for user image edit presets.
/// </summary>
public sealed class UserPresetsRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<UserPresetsRepository> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public UserPresetsRepository(string dbPath, ILogger<UserPresetsRepository> logger)
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
            CREATE TABLE IF NOT EXISTS user_image_presets (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                is_builtin INTEGER NOT NULL DEFAULT 0,
                device_preset INTEGER NOT NULL,
                resize_settings_json TEXT,
                adjustments_json TEXT,
                crop_region_json TEXT,
                save_as_new INTEGER NOT NULL DEFAULT 1,
                apply_to_all_pages INTEGER NOT NULL DEFAULT 1,
                custom_output_folder TEXT,
                created_at TEXT NOT NULL,
                last_used_at TEXT,
                use_count INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_user_presets_name ON user_image_presets(name);
            CREATE INDEX IF NOT EXISTS idx_user_presets_created ON user_image_presets(created_at);
        ";

        using var cmd = new SqliteCommand(createTable, connection);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    public async Task<List<ImageEditPreset>> GetAllAsync(CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "SELECT * FROM user_image_presets ORDER BY last_used_at DESC, created_at DESC";
        using var cmd = new SqliteCommand(sql, connection);

        var presets = new List<ImageEditPreset>();
        using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            presets.Add(ReadPresetFromReader(reader));
        }

        return presets;
    }

    public async Task<ImageEditPreset?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "SELECT * FROM user_image_presets WHERE id = @id";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return ReadPresetFromReader(reader);
        }

        return null;
    }

    public async Task<ImageEditPreset> SaveAsync(ImageEditPreset preset, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = @"
            INSERT OR REPLACE INTO user_image_presets (
                id, name, is_builtin, device_preset,
                resize_settings_json, adjustments_json, crop_region_json,
                save_as_new, apply_to_all_pages, custom_output_folder,
                created_at, last_used_at, use_count
            ) VALUES (
                @id, @name, @is_builtin, @device_preset,
                @resize_settings_json, @adjustments_json, @crop_region_json,
                @save_as_new, @apply_to_all_pages, @custom_output_folder,
                @created_at, @last_used_at, @use_count
            )";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", preset.Id.ToString());
        cmd.Parameters.AddWithValue("@name", preset.Name);
        cmd.Parameters.AddWithValue("@is_builtin", preset.IsBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("@device_preset", (int)preset.DevicePreset);
        cmd.Parameters.AddWithValue("@resize_settings_json", JsonSerializer.Serialize(preset.ResizeSettings, _jsonOptions));
        cmd.Parameters.AddWithValue("@adjustments_json", JsonSerializer.Serialize(preset.Adjustments, _jsonOptions));
        cmd.Parameters.AddWithValue("@crop_region_json", preset.CropRegion == null ? null : JsonSerializer.Serialize(preset.CropRegion, _jsonOptions));
        cmd.Parameters.AddWithValue("@save_as_new", preset.SaveAsNew ? 1 : 0);
        cmd.Parameters.AddWithValue("@apply_to_all_pages", preset.ApplyToAllPages ? 1 : 0);
        cmd.Parameters.AddWithValue("@custom_output_folder", preset.CustomOutputFolder ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", preset.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@last_used_at", preset.LastUsedAt?.ToString("O") ?? (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
        return preset;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = "DELETE FROM user_image_presets WHERE id = @id";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordUsageAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var sql = @"
            UPDATE user_image_presets
            SET use_count = use_count + 1,
                last_used_at = @now
            WHERE id = @id";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private ImageEditPreset ReadPresetFromReader(SqliteDataReader reader)
    {
        var preset = new ImageEditPreset
        {
            Id = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            IsBuiltIn = reader.GetInt32(2) == 1,
            DevicePreset = (DevicePreset)reader.GetInt32(3),
            SaveAsNew = reader.GetInt32(7) == 1,
            ApplyToAllPages = reader.GetInt32(8) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(11))
        };

        if (!reader.IsDBNull(4))
        {
            var resizeJson = reader.GetString(4);
            preset.ResizeSettings = JsonSerializer.Deserialize<ResizeSettings>(resizeJson, _jsonOptions) ?? new ResizeSettings();
        }

        if (!reader.IsDBNull(5))
        {
            var adjJson = reader.GetString(5);
            preset.Adjustments = JsonSerializer.Deserialize<ImageAdjustments>(adjJson, _jsonOptions) ?? new ImageAdjustments();
        }

        if (!reader.IsDBNull(6))
        {
            var cropJson = reader.GetString(6);
            preset.CropRegion = JsonSerializer.Deserialize<CropRegion>(cropJson, _jsonOptions);
        }

        if (!reader.IsDBNull(9))
        {
            preset.CustomOutputFolder = reader.GetString(9);
        }

        if (!reader.IsDBNull(12))
        {
            preset.LastUsedAt = DateTime.Parse(reader.GetString(12));
        }

        return preset;
    }

    public void Dispose()
    {
        // Connection pooling handles cleanup
    }
}
