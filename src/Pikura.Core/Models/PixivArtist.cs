using System.Text.Json.Serialization;

namespace Pikura.Core.Models;

/// <summary>
/// One entry in the "followed artists" list returned by
/// <c>GET /ajax/user/{id}/following</c>.
/// </summary>
public sealed record FollowedArtist
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = string.Empty;
    [JsonPropertyName("userName")] public string UserName { get; init; } = string.Empty;
    [JsonPropertyName("profileImageUrl")] public string? ProfileImageUrl { get; init; }
    [JsonPropertyName("userComment")] public string? UserComment { get; init; }
    [JsonPropertyName("following")] public bool Following { get; init; }

    /// <summary>Recent illustrations preview returned alongside the user record.</summary>
    [JsonPropertyName("illusts")] public IReadOnlyList<ArtworkPreview> Illustrations { get; init; } = [];
}

public sealed record FollowingResponseBody
{
    [JsonPropertyName("users")] public IReadOnlyList<FollowedArtist> Users { get; init; } = [];
    [JsonPropertyName("total")] public int Total { get; init; }
}
