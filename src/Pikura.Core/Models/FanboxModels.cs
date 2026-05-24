using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Pikura.Core.Models;

/// <summary>FANBOX post information</summary>
public sealed record FanboxPost
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("body")] public FanboxBody? Body { get; init; }
    [JsonPropertyName("feeRequired")] public int FeeRequired { get; init; }
    [JsonPropertyName("publishedDatetime")] public DateTimeOffset PublishedDatetime { get; init; }
    [JsonPropertyName("creatorId")] public string CreatorId { get; init; } = string.Empty;
    [JsonPropertyName("userId")] public string UserId { get; init; } = string.Empty;
    [JsonPropertyName("hasAdultContent")] public bool HasAdultContent { get; init; }
    [JsonPropertyName("coverImageUrl")] public string? CoverImageUrl { get; init; }
    [JsonPropertyName("images")] public List<FanboxImage> Images { get; init; } = new();
    [JsonPropertyName("user")] public FanboxUser? User { get; init; }
}

/// <summary>FANBOX post body content</summary>
public sealed record FanboxBody
{
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("html")] public string? Html { get; init; }
}

/// <summary>FANBOX image information</summary>
public sealed record FanboxImage
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("originalUrl")] public string OriginalUrl { get; init; } = string.Empty;
    [JsonPropertyName("extension")] public string Extension { get; init; } = string.Empty;
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
}

/// <summary>FANBOX user information</summary>
public sealed record FanboxUser
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; init; }
}

/// <summary>FANBOX post list response</summary>
public sealed record FanboxPostList
{
    [JsonPropertyName("body")] public List<FanboxPost> Body { get; init; } = new();
}

/// <summary>FANBOX creator information</summary>
public sealed record FanboxCreator
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("creatorId")] public string CreatorId { get; init; } = string.Empty;
}
