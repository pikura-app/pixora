using System.Text.Json.Serialization;

namespace Pikura.Core.Models;

/// <summary>
/// Generic envelope used by Pixiv's ajax endpoints:
/// <c>{ "error": false, "message": "", "body": { ... } }</c>.
/// </summary>
public sealed record PixivAjaxResponse<T>
{
    [JsonPropertyName("error")] public bool Error { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("body")] public T? Body { get; init; }
}
