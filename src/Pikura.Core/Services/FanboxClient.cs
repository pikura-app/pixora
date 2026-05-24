using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pikura.Core.Models;

namespace Pikura.Core.Services;

/// <summary>Client for interacting with FANBOX API</summary>
public sealed class FanboxClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FanboxClient> _logger;
    private const string BaseUrl = "https://api.fanbox.cc";
    
    public FanboxClient(ILogger<FanboxClient> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.fanbox.cc");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.fanbox.cc/");
    }

    /// <summary>Set the FANBOX session cookie for authentication</summary>
    public void SetSession(string sessionCookie)
    {
        _httpClient.DefaultRequestHeaders.Remove("Cookie");
        _httpClient.DefaultRequestHeaders.Add("Cookie", $"FANBOXSESSID={sessionCookie}");
    }

    /// <summary>Get a specific FANBOX post by ID</summary>
    public async Task<FanboxPost?> GetPostAsync(string postId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/v1/post.info?postId={postId}", ct);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FanboxPostResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return result?.Body;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch FANBOX post {PostId}", postId);
            return null;
        }
    }

    /// <summary>Get posts from a creator</summary>
    public async Task<List<FanboxPost>?> GetCreatorPostsAsync(string creatorId, int page = 1, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/v1/post.listCreator?creatorId={creatorId}&page={page}", ct);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FanboxPostList>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return result?.Body;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch FANBOX posts for creator {CreatorId}", creatorId);
            return null;
        }
    }

    /// <summary>Get creator information</summary>
    public async Task<FanboxCreator?> GetCreatorAsync(string creatorId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/v1/creator.get?creatorId={creatorId}", ct);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<FanboxCreatorResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return result?.Body;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch FANBOX creator {CreatorId}", creatorId);
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    private record FanboxPostResponse
    {
        public FanboxPost? Body { get; init; }
    }

    private record FanboxCreatorResponse
    {
        public FanboxCreator? Body { get; init; }
    }
}
