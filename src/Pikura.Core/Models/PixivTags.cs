using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pikura.Core.Models;

/// <summary>Response from /ajax/tags/suggest endpoint.</summary>
public sealed class TagSuggestResponse
{
    [JsonPropertyName("candidates")] public List<TagSuggestion> Candidates { get; set; } = new();
}

/// <summary>A single tag suggestion from autocomplete.</summary>
public sealed class TagSuggestion
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
    [JsonPropertyName("tag_translation")] public string? TagTranslation { get; set; }
    [JsonPropertyName("access_count")] public string? AccessCount { get; set; }
}

/// <summary>Response from /ajax/user/{userId}/following/tags endpoint.</summary>
public sealed class FollowingTagsResponse
{
    [JsonPropertyName("tags")] public List<FollowingTag> Tags { get; set; } = new();
}

/// <summary>A tag used to organize followed users.</summary>
public sealed class FollowingTag
{
    [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
    [JsonPropertyName("userId")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("userName")] public string UserName { get; set; } = string.Empty;
    [JsonPropertyName("count")] public int Count { get; set; }
}
