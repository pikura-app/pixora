using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pikura.Core.Models;

/// <summary>
/// Body shape returned by <c>GET /ajax/user/{id}/profile/all</c>.
/// The <c>illusts</c> and <c>manga</c> properties are returned as a JSON object
/// whose keys are artwork IDs when the user has works, but as an empty array
/// <c>[]</c> when they don't. We store them as <see cref="JsonElement"/> and
/// normalize in <see cref="AllArtworkIds"/>.
/// </summary>
public sealed record UserProfileAll
{
    [JsonPropertyName("illusts")] public JsonElement Illusts { get; init; }
    [JsonPropertyName("manga")] public JsonElement Manga { get; init; }

    /// <summary>All artwork IDs (illusts + manga) sorted newest-first.</summary>
    public IReadOnlyList<string> AllArtworkIds()
    {
        var ids = new List<string>();
        AddKeysFrom(Illusts, ids);
        AddKeysFrom(Manga, ids);
        return ids
            .Distinct()
            .OrderByDescending(id => long.TryParse(id, out var v) ? v : 0L)
            .ToList();
    }

    private static void AddKeysFrom(JsonElement el, List<string> into)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject()) into.Add(p.Name);
        }
        // Arrays / null / undefined mean "no works" — nothing to add.
    }
}

/// <summary>Body shape returned by <c>GET /ajax/user/{ids}/profile/illusts?ids[]=...</c>.</summary>
public sealed record UserProfileIllusts
{
    [JsonPropertyName("works")] public Dictionary<string, ArtworkPreview> Works { get; init; } = new();
}

/// <summary>
/// Body shape returned by <c>GET /ajax/follow_latest/illust?p=1&amp;mode=all</c>.
/// </summary>
public sealed record FollowLatestBody
{
    [JsonPropertyName("thumbnails")] public FollowLatestThumbnails Thumbnails { get; init; } = new();
    [JsonPropertyName("page")] public FollowLatestPage Page { get; init; } = new();
}

public sealed record FollowLatestThumbnails
{
    [JsonPropertyName("illust")] public IReadOnlyList<ArtworkPreview> Illusts { get; init; } = [];
}

public sealed record FollowLatestPage
{
    [JsonPropertyName("ids")] public IReadOnlyList<long> Ids { get; init; } = [];
}
