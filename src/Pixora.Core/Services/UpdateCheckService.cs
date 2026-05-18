using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Pixora.Core.Settings;

namespace Pixora.Core.Services;

/// <summary>
/// Checks the GitHub Releases API for a newer version of Pixora,
/// downloads the asset, and launches an updater script that swaps
/// the binary and restarts the application.
/// </summary>
public sealed class UpdateCheckService
{
    public static string CurrentVersion { get; } =
        System.Reflection.Assembly.GetEntryAssembly()
            ?.GetName().Version
            ?.ToString(3)
        ?? "0.0.0";
    private const string Owner           = "pikura-app";
    private const string Repo            = "pixora";
    private const string ReleasesApiUrl  = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string PreReleaseApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases";
    private const string ReleasesPageUrl = $"https://github.com/{Owner}/{Repo}/releases/latest";

    private readonly ILogger<UpdateCheckService> _logger;
    private readonly SettingsService _settings;
    private readonly HttpClient _http;

    public UpdateCheckService(ILogger<UpdateCheckService> logger, SettingsService settings)
    {
        _logger   = logger;
        _settings = settings;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Pixora/" + CurrentVersion);
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks for a newer release respecting the configured channel and frequency.
    /// Returns <see cref="UpdateInfo"/> if an update is available, null otherwise.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;

        if (!ShouldCheck(s)) return null;

        try
        {
            var url     = s.UpdateChannel == "PreRelease" ? PreReleaseApiUrl : ReleasesApiUrl;
            GitHubRelease? release;

            if (s.UpdateChannel == "PreRelease")
            {
                // List endpoint — pick first pre-release (or any release if none marked)
                var list = await _http.GetFromJsonAsync<GitHubRelease[]>(url, ct).ConfigureAwait(false);
                release  = list?.FirstOrDefault(r => r.Prerelease)
                           ?? list?.FirstOrDefault();
            }
            else
            {
                // Latest endpoint returns the newest non-prerelease by default.
                // Double-check the flag in case the API ever returns a pre-release.
                var candidate = await _http.GetFromJsonAsync<GitHubRelease>(url, ct).ConfigureAwait(false);
                release = candidate?.Prerelease == true ? null : candidate;
            }

            _settings.Update(s2 => s2.LastUpdateCheck = DateTime.UtcNow);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName)) return null;

            var latestTag     = release.TagName.TrimStart('v');
            var latestVersion = Version.Parse(latestTag);
            var current       = Version.Parse(CurrentVersion);

            if (latestVersion <= current) return null;

            _logger.LogInformation("Update available: {Latest} (current: {Current})", latestTag, CurrentVersion);

            var asset = FindWindowsAsset(release);

            return new UpdateInfo(
                latestTag,
                release.Name ?? $"Pixora v{latestTag}",
                release.Body ?? string.Empty,
                ReleasesPageUrl,
                asset?.BrowserDownloadUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Update check failed (non-fatal)");
            return null;
        }
    }

    private static bool ShouldCheck(AppSettings s)
    {
        return s.UpdateCheckFrequency switch
        {
            "Never"   => false,
            "Daily"   => s.LastUpdateCheck is null || (DateTime.UtcNow - s.LastUpdateCheck.Value).TotalHours >= 24,
            "Weekly"  => s.LastUpdateCheck is null || (DateTime.UtcNow - s.LastUpdateCheck.Value).TotalDays  >= 7,
            _         => true,  // "Startup" — always check
        };
    }

    private static GitHubAsset? FindWindowsAsset(GitHubRelease release)
        => release.Assets?.FirstOrDefault(a =>
            a.Name != null &&
            a.Name.Contains("win", StringComparison.OrdinalIgnoreCase) &&
            (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the update asset to a temp file, reporting progress 0–100.
    /// Returns the local path of the downloaded file.
    /// </summary>
    public async Task<string> DownloadUpdateAsync(
        UpdateInfo info,
        IProgress<int> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
            throw new InvalidOperationException("No download URL available for this release.");

        var ext      = Path.GetExtension(info.DownloadUrl.Split('?')[0]);
        var destPath = Path.Combine(Path.GetTempPath(), $"Pixora-update-{info.Version}{ext}");

        using var response = await _http
            .GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total  = response.Content.Headers.ContentLength ?? -1L;
        var buffer = new byte[81920];
        long read  = 0;

        await using var src  = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dest = File.Create(destPath);

        int bytesRead;
        while ((bytesRead = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            read += bytesRead;
            if (total > 0) progress.Report((int)(read * 100 / total));
        }
        progress.Report(100);

        _logger.LogInformation("Update downloaded to {Path}", destPath);
        return destPath;
    }

    // ── Install & restart ─────────────────────────────────────────────────────

    /// <summary>
    /// Writes a batch/shell script that waits for this process to exit,
    /// replaces the executable, then relaunches it. Then exits the app.
    /// </summary>
    public void InstallAndRestart(string downloadedPath)
    {
        var rawPath = Environment.ProcessPath
                      ?? System.Reflection.Assembly.GetEntryAssembly()?.Location
                      ?? throw new InvalidOperationException("Cannot determine current executable path.");

        // Environment.ProcessPath can return a \??\ kernel-style prefix on Windows
        // when running as a single-file executable — strip it so cmd.exe can use it.
        var exePath = rawPath.StartsWith(@"\??\", StringComparison.Ordinal)
            ? rawPath[4..]
            : rawPath;

        if (OperatingSystem.IsWindows())
            LaunchWindowsUpdater(downloadedPath, exePath);
        else
            LaunchUnixUpdater(downloadedPath, exePath);

        // Exit the app — the script takes over from here
        System.Diagnostics.Process.GetCurrentProcess().Kill();
    }

    private static void LaunchWindowsUpdater(string src, string dest)
    {
        var srcName = Path.GetFileName(src).ToLowerInvariant();

        // If the downloaded file is an Inno Setup installer, run it directly.
        // The installer handles replacing the binary and can relaunch Pixora itself.
        if (srcName.Contains("setup") || srcName.Contains("install"))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = src,
                UseShellExecute = true,
            });
            return;
        }

        // Portable .exe — wait for process to exit then copy over and relaunch.
        var script = Path.Combine(Path.GetTempPath(), "pixora_update.bat");
        File.WriteAllText(script, $"""
            @echo off
            echo Waiting for Pixora to exit...
            timeout /t 2 /nobreak >nul
            :waitloop
            tasklist /fi "imagename eq Pixora.exe" 2>nul | find /i "Pixora.exe" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto waitloop
            )
            echo Installing update...
            copy /y "{src}" "{dest}"
            echo Restarting Pixora...
            start "" "{dest}"
            del "%~f0"
            """);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "cmd.exe",
            Arguments       = $"/c \"{script}\"",
            CreateNoWindow  = true,
            UseShellExecute = false,
        });
    }

    private static void LaunchUnixUpdater(string src, string dest)
    {
        var script = Path.Combine(Path.GetTempPath(), "pixora_update.sh");
        File.WriteAllText(script, $"""
            #!/bin/bash
            sleep 2
            while pgrep -x Pixora > /dev/null; do sleep 1; done
            cp -f "{src}" "{dest}"
            chmod +x "{dest}"
            "{dest}" &
            rm -- "$0"
            """);
        System.Diagnostics.Process.Start("chmod", $"+x {script}");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "/bin/bash",
            Arguments       = script,
            UseShellExecute = false,
        });
    }

    // ── Private types ─────────────────────────────────────────────────────────

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]   public string?        TagName    { get; init; }
        [JsonPropertyName("name")]       public string?        Name       { get; init; }
        [JsonPropertyName("body")]       public string?        Body       { get; init; }
        [JsonPropertyName("assets")]     public GitHubAsset[]? Assets     { get; init; }
        [JsonPropertyName("prerelease")] public bool           Prerelease { get; init; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                 public string? Name                { get; init; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl  { get; init; }
    }
}

/// <summary>Information about an available update.</summary>
public sealed record UpdateInfo(
    string  Version,
    string  Title,
    string  ReleaseNotes,
    string  ReleasePageUrl,
    string? DownloadUrl);
