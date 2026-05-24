using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pikura.Core.Models;

/// <summary>Response from /ajax/illust/{id} endpoint with full artwork details and stats.</summary>
public sealed class ArtworkDetailResponse
{
    [JsonPropertyName("error")] public bool Error { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("body")] public ArtworkDetailBody? Body { get; init; }
}

public sealed class ArtworkDetailBody
{
    [JsonPropertyName("illustId")] public string? IllustId { get; init; }
    [JsonPropertyName("illustTitle")] public string? IllustTitle { get; init; }
    [JsonPropertyName("illustComment")] public string? IllustComment { get; init; }
    [JsonPropertyName("id")] public string? UserId { get; init; }
    [JsonPropertyName("name")] public string? UserName { get; init; }
    [JsonPropertyName("url")] public string? ThumbnailUrl { get; init; }

    // Stats - these are the key fields we need!
    [JsonPropertyName("bookmarkCount")] public int? BookmarkCount { get; init; }
    [JsonPropertyName("likeCount")] public int? LikeCount { get; init; }
    [JsonPropertyName("viewCount")] public int? ViewCount { get; init; }
    [JsonPropertyName("commentCount")] public int? CommentCount { get; init; }

    [JsonPropertyName("createDate")] public string? CreateDate { get; init; }
    [JsonPropertyName("uploadDate")] public string? UploadDate { get; init; }
    [JsonPropertyName("illustType")] public int IllustType { get; init; }
    [JsonPropertyName("xRestrict")] public int XRestrict { get; init; }
    [JsonPropertyName("sl")] public int? Sl { get; init; }

    [JsonPropertyName("tags")] public ArtworkDetailTags? Tags { get; init; }
    [JsonPropertyName("aiType")] public int AiType { get; init; }

    // Page count info
    [JsonPropertyName("pageCount")] public int PageCount { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
}

public sealed class ArtworkDetailTags
{
    [JsonPropertyName("tags")] public List<ArtworkDetailTag> Tags { get; init; } = [];
}

public sealed class ArtworkDetailTag
{
    [JsonPropertyName("tag")] public string? Tag { get; init; }
    [JsonPropertyName("locked")] public bool Locked { get; init; }
    [JsonPropertyName("deletable")] public bool Deletable { get; init; }
    [JsonPropertyName("userId")] public string? UserId { get; init; }
    [JsonPropertyName("userName")] public string? UserName { get; init; }
}
