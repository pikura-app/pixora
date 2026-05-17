using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pixora.Core.Models;

/// <summary>Response from /ajax/user/{userId}/novels endpoint.</summary>
public sealed class UserNovelsResponse
{
    [JsonPropertyName("novels")] public List<NovelPreview> Novels { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("lastIndex")] public int LastIndex { get; set; }
}

/// <summary>Response from /ajax/search/novels/{keyword} endpoint.</summary>
public sealed class NovelSearchResult
{
    [JsonPropertyName("novels")] public List<NovelPreview> Novels { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
}

/// <summary>Response from /ajax/novel/{id} endpoint.</summary>
public sealed class NovelDetailResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("userId")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("userName")] public string UserName { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("coverUrl")] public string? CoverUrl { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("bookmarkCount")] public int BookmarkCount { get; set; }
    [JsonPropertyName("viewCount")] public int ViewCount { get; set; }
    [JsonPropertyName("likeCount")] public int LikeCount { get; set; }
    [JsonPropertyName("commentCount")] public int CommentCount { get; set; }
    [JsonPropertyName("textLength")] public int TextLength { get; set; }
    [JsonPropertyName("seriesId")] public string? SeriesId { get; set; }
    [JsonPropertyName("seriesTitle")] public string? SeriesTitle { get; set; }
    [JsonPropertyName("isOriginal")] public bool IsOriginal { get; set; }
    [JsonPropertyName("isR18")] public bool IsR18 { get; set; }
    [JsonPropertyName("createDate")] public string CreateDate { get; set; } = string.Empty;
}

/// <summary>Preview of a novel in search/list results.</summary>
public sealed class NovelPreview
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("userId")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("userName")] public string UserName { get; set; } = string.Empty;
    [JsonPropertyName("coverUrl")] public string? CoverUrl { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("bookmarkCount")] public int BookmarkCount { get; set; }
    [JsonPropertyName("viewCount")] public int ViewCount { get; set; }
    [JsonPropertyName("textLength")] public int TextLength { get; set; }
    [JsonPropertyName("isR18")] public bool IsR18 { get; set; }
    [JsonPropertyName("createDate")] public string CreateDate { get; set; } = string.Empty;
}
