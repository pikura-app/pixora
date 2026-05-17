using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pixora.Core.Models;

/// <summary>
/// A saved download configuration preset that can be reused.
/// </summary>
public sealed class DownloadPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DownloadJobType Type { get; set; }

    // The saved configuration
    public SettingsOverride Settings { get; set; } = new();
    public string? DefaultPageRange { get; set; }
    public bool UsePerArtistPageRanges { get; set; }
    public List<PresetArtist> Artists { get; set; } = new();

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public int UseCount { get; set; }
}

/// <summary>
/// Artist information saved in a preset.
/// </summary>
public sealed class PresetArtist
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? PageRange { get; set; }
}
