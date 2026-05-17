using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Pixora.Core.Services;

/// <summary>
/// Checks the GitHub Releases API for a newer version of Pixora.
/// Runs silently in the background — never blocks startup.
/// </summary>
public sealed class UpdateCheckService
{
    private const string CurrentVersion  = "1.0.0";
    private const string ReleasesApiUrl  = "https://api.github.com/repos/pikura-app/pixora/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/pikura-app/pixora/releases/latest";

    private readonly ILogger<UpdateCheckService> _logger;
    private readonly HttpClient _http;

    public UpdateCheckService(ILogger<UpdateCheckService> logger)
    {
        _logger = logger;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Pixora/" + CurrentVersion);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Checks once for a newer release. Returns non-null <see cref="UpdateInfo"/>
    /// if an update is available, null otherwise.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await _http
                .GetFromJsonAsync<GitHubRelease>(ReleasesApiUrl, ct)
                .ConfigureAwait(false);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return null;

            var latestTag     = release.TagName.TrimStart('v');
            var latestVersion = Version.Parse(latestTag);
            var current       = Version.Parse(CurrentVersion);

            if (latestVersion <= current) return null;

            _logger.LogInformation("Update available: {Latest} (current: {Current})", latestTag, CurrentVersion);

            return new UpdateInfo(
                latestTag,
                release.Name ?? $"Pixora v{latestTag}",
                release.Body ?? string.Empty,
                ReleasesPageUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Update check failed (non-fatal)");
            return null;
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string?  TagName { get; init; }
        [JsonPropertyName("name")]     public string?  Name    { get; init; }
        [JsonPropertyName("body")]     public string?  Body    { get; init; }
    }
}

/// <summary>Information about an available update.</summary>
public sealed record UpdateInfo(
    string Version,
    string Title,
    string ReleaseNotes,
    string ReleasePageUrl);
