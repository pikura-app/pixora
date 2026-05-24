using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pikura.Core.Models;

/// <summary>Response from /ajax/user/{userId}/illusts and /ajax/user/{userId}/manga endpoints.</summary>
public sealed class UserIllustsResponse
{
    [JsonPropertyName("illusts")] public List<ArtworkPreview> Illusts { get; set; } = new();
    [JsonPropertyName("manga")] public List<ArtworkPreview> Manga { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("lastIndex")] public int LastIndex { get; set; }
}
