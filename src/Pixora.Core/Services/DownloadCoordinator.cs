using Microsoft.Extensions.Logging;
using Pixora.Core.Data;
using Pixora.Core.Models;
using Pixora.Core.Settings;
using Pixora.Core.Utilities;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Pixora.Core.Services;

/// <summary>
/// Progress update for a download job.
/// </summary>
public sealed record JobProgress(
    Guid JobId,
    JobStatus Status,
    int CompletedTargets,
    int TotalTargets,
    double PercentComplete,
    string? CurrentTargetName,
    string? Message);

/// <summary>
/// Coordinates batch download operations.
/// Manages download queue, job execution, and progress reporting.
/// </summary>
public sealed class DownloadCoordinator : IDisposable
{
    private readonly PixivClient _client;
    private readonly PixivDownloadService _downloadService;
    private readonly SettingsService _settingsService;
    private readonly DownloadJobRepository _jobRepository;
    private readonly ILogger<DownloadCoordinator> _logger;

    // Active job tracking
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeJobs = new();
    private readonly ConcurrentDictionary<Guid, Task> _runningTasks = new();

    // Progress reporting
    private readonly ConcurrentDictionary<Guid, List<IProgress<JobProgress>>> _progressListeners = new();

    /// <summary>
    /// Event raised when a job starts running.
    /// </summary>
    public event EventHandler<JobCompletedEventArgs>? JobStarted;

    /// <summary>
    /// Event raised when a job completes (successfully or with failures).
    /// </summary>
    public event EventHandler<JobCompletedEventArgs>? JobCompleted;

    /// <summary>
    /// Raises <see cref="JobCompleted"/> for a job that was saved externally (e.g. gallery single-download).
    /// </summary>
    public void NotifyJobSaved(DownloadJob job) => JobCompleted?.Invoke(this, new JobCompletedEventArgs(job));

    public DownloadCoordinator(
        PixivClient client,
        PixivDownloadService downloadService,
        SettingsService settingsService,
        DownloadJobRepository jobRepository,
        FanboxClient fanboxClient,
        ILogger<DownloadCoordinator> logger)
    {
        _client = client;
        _downloadService = downloadService;
        _settingsService = settingsService;
        _jobRepository = jobRepository;
        _fanboxClient = fanboxClient;
        _logger = logger;
    }

    private readonly FanboxClient _fanboxClient;

    #region Job Management

    /// <summary>
    /// Creates and optionally starts a new download job.
    /// </summary>
    public async Task<DownloadJob> CreateJobAsync(
        DownloadJobType type,
        string name,
        List<DownloadTarget> targets,
        SettingsOverride? settingsOverride = null,
        bool startImmediately = false,
        CancellationToken ct = default)
    {
        var job = new DownloadJob
        {
            Name = name,
            Type = type,
            Targets = targets,
            Settings = settingsOverride ?? new SettingsOverride { UseGlobalSettings = true },
            Status = startImmediately ? JobStatus.Pending : JobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        // Save to database
        await _jobRepository.SaveJobAsync(job, ct);
        _logger.LogInformation("Created download job {JobId} ({Name}) with {TargetCount} targets",
            job.Id, job.Name, job.Targets.Count);

        if (startImmediately)
        {
            await StartJobAsync(job.Id, ct);
        }

        return job;
    }

    /// <summary>
    /// Starts a pending job.
    /// </summary>
    public async Task<bool> StartJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _jobRepository.GetJobAsync(jobId, ct);
        if (job == null)
        {
            _logger.LogWarning("Cannot start job {JobId}: not found", jobId);
            return false;
        }

        if (job.Status != JobStatus.Pending && job.Status != JobStatus.Paused)
        {
            _logger.LogWarning("Cannot start job {JobId}: status is {Status}", jobId, job.Status);
            return false;
        }

        // Create cancellation token for this job
        var cts = new CancellationTokenSource();
        if (!_activeJobs.TryAdd(jobId, cts))
        {
            _logger.LogWarning("Job {JobId} is already running", jobId);
            return false;
        }

        // Update status
        await _jobRepository.UpdateJobStatusAsync(jobId, JobStatus.Running, null, ct);
        job.Status = JobStatus.Running;

        // Notify listeners that job is starting
        JobStarted?.Invoke(this, new JobCompletedEventArgs(job));

        // Start the job task
        var task = ExecuteJobAsync(job, cts.Token);
        _runningTasks.TryAdd(jobId, task);

        // Clean up when done
        _ = task.ContinueWith(async t =>
        {
            _activeJobs.TryRemove(jobId, out _);
            _runningTasks.TryRemove(jobId, out _);

            // Update final status
            var finalStatus = t.IsFaulted ? JobStatus.Failed :
                             t.IsCanceled ? JobStatus.Cancelled : JobStatus.Completed;

            var error = t.IsFaulted ? t.Exception?.InnerException?.Message : null;
            await _jobRepository.UpdateJobStatusAsync(jobId, finalStatus, error);

            // Fire completion event
            var completedJob = await _jobRepository.GetJobAsync(jobId);
            if (completedJob != null)
            {
                JobCompleted?.Invoke(this, new JobCompletedEventArgs(completedJob));
            }

            _logger.LogInformation("Job {JobId} completed with status {Status}", jobId, finalStatus);
        }, TaskContinuationOptions.ExecuteSynchronously);

        _logger.LogInformation("Started download job {JobId}", jobId);
        return true;
    }

    /// <summary>
    /// Cancels a running job.
    /// </summary>
    public async Task<bool> CancelJobAsync(Guid jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var cts))
        {
            await cts.CancelAsync();
            // Immediately notify subscribers so the UI hides the progress panel
            ReportProgress(jobId, new JobProgress(jobId, JobStatus.Cancelled, 0, 0, 0, null, "Cancelled"));
            _logger.LogInformation("Cancelled job {JobId}", jobId);
            return true;
        }

        // Job might not be in active dictionary but still in database as Running
        var job = await _jobRepository.GetJobAsync(jobId);
        if (job?.Status == JobStatus.Running)
        {
            await _jobRepository.UpdateJobStatusAsync(jobId, JobStatus.Cancelled);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all jobs with optional filtering.
    /// </summary>
    public Task<List<DownloadJob>> GetJobsAsync(JobStatus? status = null, int? limit = null, CancellationToken ct = default)
        => _jobRepository.GetJobsAsync(status, limit, ct);

    /// <summary>
    /// Deletes a job and all its targets.
    /// </summary>
    public async Task<bool> DeleteJobAsync(Guid jobId, CancellationToken ct = default)
    {
        // Cancel if running
        await CancelJobAsync(jobId);

        // Wait for task to complete
        if (_runningTasks.TryGetValue(jobId, out var task))
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch { /* ignore timeout */ }
        }

        await _jobRepository.DeleteJobAsync(jobId, ct);
        _logger.LogInformation("Deleted job {JobId}", jobId);
        return true;
    }

    #endregion

    #region Progress Reporting

    /// <summary>
    /// Subscribes to progress updates for a job.
    /// </summary>
    public void SubscribeToProgress(Guid jobId, IProgress<JobProgress> progress)
    {
        var listeners = _progressListeners.GetOrAdd(jobId, _ => new List<IProgress<JobProgress>>());
        lock (listeners)
        {
            listeners.Add(progress);
        }
    }

    /// <summary>
    /// Unsubscribes from progress updates.
    /// </summary>
    public void UnsubscribeFromProgress(Guid jobId, IProgress<JobProgress> progress)
    {
        if (_progressListeners.TryGetValue(jobId, out var listeners))
        {
            lock (listeners)
            {
                listeners.Remove(progress);
            }
        }
    }

    private void ReportProgress(Guid jobId, JobProgress progress)
    {
        if (_progressListeners.TryGetValue(jobId, out var listeners))
        {
            List<IProgress<JobProgress>> snapshot;
            lock (listeners)
            {
                snapshot = listeners.ToList();
            }

            foreach (var listener in snapshot)
            {
                try
                {
                    listener.Report(progress);
                }
                catch { /* ignore listener errors */ }
            }
        }
    }

    #endregion

    #region Job Execution

    private async Task ExecuteJobAsync(DownloadJob job, CancellationToken ct)
    {
        var completedCount = job.Targets.Count(t => t.Status == TargetStatus.Completed);
        var totalCount = job.Targets.Count;

        // Get effective settings for this job
        var effectiveSettings = job.Settings.UseGlobalSettings
            ? SettingsOverride.FromGlobalSettings(_settingsService.Current)
            : job.Settings;

        // Process each target
        foreach (var target in job.Targets.Where(t =>
            t.Status != TargetStatus.Completed &&
            t.Status != TargetStatus.Skipped))
        {
            ct.ThrowIfCancellationRequested();

            // Update target status
            await _jobRepository.UpdateTargetStatusAsync(target.Id, TargetStatus.Running);

            ReportProgress(job.Id, new JobProgress(
                job.Id,
                JobStatus.Running,
                completedCount,
                totalCount,
                completedCount * 100.0 / totalCount,
                target.Name,
                $"Processing {target.Name}..."
            ));

            var maxRetries = effectiveSettings.AutoRetryFailedDownloads == true ? (effectiveSettings.MaxRetryAttempts ?? 3) : 0;
            var retryDelay = TimeSpan.FromSeconds(effectiveSettings.RetryDelaySeconds ?? 5);
            var attempt = 0;
            var success = false;

            while (attempt <= maxRetries && !success)
            {
                try
                {
                    // Get target-specific settings if available
                    var targetSettings = target.HasCustomSettings
                        ? target.CustomSettings!.ApplyTo(effectiveSettings)
                        : effectiveSettings;

                    // Execute based on target type
                    var (found, downloaded) = target.Type switch
                    {
                        TargetType.Artist => await DownloadArtistAsync(job.Id, target, targetSettings, ct),
                        TargetType.Artwork => await DownloadArtworkAsync(target, targetSettings, ct),
                        TargetType.Post => await DownloadFanboxPostAsync(target, targetSettings, ct),
                        _ => throw new NotSupportedException($"Target type {target.Type} not supported")
                    };

                    await _jobRepository.UpdateTargetStatusAsync(
                        target.Id,
                        TargetStatus.Completed,
                        found,
                        downloaded);

                    completedCount++;
                    success = true;
                }
                catch (OperationCanceledException)
                {
                    await _jobRepository.UpdateTargetStatusAsync(target.Id, TargetStatus.Cancelled);
                    throw;
                }
                catch (Exception ex)
                {
                    attempt++;

                    if (attempt > maxRetries)
                    {
                        _logger.LogError(ex, "Failed to process target {TargetId} in job {JobId} after {Attempts} attempts",
                            target.TargetId, job.Id, attempt);

                        await _jobRepository.UpdateTargetStatusAsync(
                            target.Id,
                            TargetStatus.Failed,
                            errorMessage: ex.Message);

                        // Continue with next target (don't fail entire job for one error)
                    }
                    else
                    {
                        _logger.LogWarning(ex, "Attempt {Attempt} failed for target {TargetId}, retrying in {Delay}s...",
                            attempt, target.TargetId, retryDelay.TotalSeconds);

                        await Task.Delay(retryDelay, ct);
                    }
                }
            }
        }

        ReportProgress(job.Id, new JobProgress(
            job.Id,
            JobStatus.Completed,
            completedCount,
            totalCount,
            100,
            null,
            "Job completed"
        ));
    }

    private async Task<(int Found, int Downloaded)> DownloadArtistAsync(
        Guid jobId,
        DownloadTarget target,
        SettingsOverride settings,
        CancellationToken ct)
    {
        // Get all artwork IDs for this artist
        var profile = await _client.GetUserProfileAllAsync(target.TargetId, ct);
        var allArtworkIds = profile.AllArtworkIds();

        if (allArtworkIds.Count == 0)
            return (0, 0);

        // Fetch metadata in batches of 48 to avoid URL-too-long (414) errors
        const int batchSize = 48;
        var allMetadata = new Dictionary<string, ArtworkPreview>();
        for (int i = 0; i < allArtworkIds.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = allArtworkIds.Skip(i).Take(batchSize);
            var batchMeta = await _client.GetArtworksMetadataAsync(target.TargetId, batch, ct);
            foreach (var kv in batchMeta)
                allMetadata[kv.Key] = kv.Value;
        }

        // Apply page range filter
        var pageRange = target.HasCustomPageRange
            ? PageRangeParser.Parse(target.PageRange)
            : PageRangeParser.Parse("0"); // All pages

        // Pre-parse per-target tag/date filters (case-insensitive substring match for tags)
        var includeTagSet = ParseTagSet(settings.IncludeTags);
        var excludeTagSet = ParseTagSet(settings.ExcludeTagsFilter);
        var dateFromUtc = settings.DateFrom?.Date;
        var dateToUtc = settings.DateTo?.Date.AddDays(1).AddTicks(-1); // include the entire 'To' day

        // Filter the metadata list before counting "found"
        var filteredArtworks = allMetadata.Values.Where(artwork =>
        {
            if (settings.FilterAiGenerated == true && artwork.IsAiGenerated) return false;
            if (settings.SkipManga   == true && artwork.IllustType == 1) return false;
            if (settings.SkipUgoira  == true && artwork.IllustType == 2) return false;
            if (settings.SkipR18     == true && artwork.IsR18)           return false;
            if (settings.SkipR18G    == true && artwork.IsR18G)          return false;
            if (!MatchesTagFilters(artwork.Tags, includeTagSet, excludeTagSet)) return false;
            if (!MatchesDateRange(artwork.CreateDate, dateFromUtc, dateToUtc)) return false;
            return true;
        }).ToList();

        int found = filteredArtworks.Count;
        int downloaded = 0;
        int artworkIndex = 0;

        foreach (var artwork in filteredArtworks)
        {
            ct.ThrowIfCancellationRequested();
            artworkIndex++;

            // Report per-artwork progress
            ReportProgress(jobId, new JobProgress(
                jobId,
                JobStatus.Running,
                downloaded,
                found,
                found > 0 ? artworkIndex * 100.0 / found : 0,
                artwork.Title,
                $"Downloading {artworkIndex}/{found}: {artwork.Title}"
            ));

            try
            {
                var pages = await _client.GetArtworkPagesAsync(artwork.Id, ct);

                List<int>? pageIndices = null;
                if (!pageRange.IsAll)
                {
                    pageIndices = pageRange.ToZeroBasedIndices()
                        .Where(i => i < pages.Count)
                        .ToList();

                    if (pageIndices.Count == 0)
                        continue;
                }

                if (settings.DownloadDelaySeconds > 0)
                    await Task.Delay(settings.DownloadDelaySeconds.Value * 1000, ct);

                await _downloadService.DownloadArtworkPagesAsync(artwork, pageIndices, null, ct, settings);
                downloaded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download artwork {ArtworkId} from artist {ArtistId}",
                    artwork.Id, target.TargetId);
            }
        }

        return (found, downloaded);
    }

    private static HashSet<string> ParseTagSet(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesTagFilters(IReadOnlyList<string> tags, HashSet<string> include, HashSet<string> exclude)
    {
        // Exclude wins
        if (exclude.Count > 0 && tags.Any(t => exclude.Any(ex => t.Contains(ex, StringComparison.OrdinalIgnoreCase))))
            return false;
        // Include = at least one tag must match (substring, case-insensitive)
        if (include.Count > 0 && !tags.Any(t => include.Any(inc => t.Contains(inc, StringComparison.OrdinalIgnoreCase))))
            return false;
        return true;
    }

    private static bool MatchesDateRange(DateTimeOffset? createDate, DateTime? from, DateTime? to)
    {
        if (from == null && to == null) return true;
        if (createDate == null) return true; // unknown date → don't filter out
        var d = createDate.Value.UtcDateTime;
        if (from.HasValue && d < from.Value) return false;
        if (to.HasValue && d > to.Value) return false;
        return true;
    }

    private async Task<(int Found, int Downloaded)> DownloadArtworkAsync(
        DownloadTarget target,
        SettingsOverride settings,
        CancellationToken ct)
    {
        var detail = await _client.GetArtworkDetailAsync(target.TargetId, ct);
        if (detail == null)
            return (0, 0);

        // Apply content filters
        if (settings.FilterAiGenerated == true && detail.AiType is 1 or 2) return (1, 0);
        if (settings.SkipR18          == true && detail.XRestrict >= 1)    return (1, 0);
        if (settings.SkipR18G         == true && detail.XRestrict == 2)    return (1, 0);

        var pages = await _client.GetArtworkPagesAsync(target.TargetId, ct);

        // Apply page range
        var pageRange = target.HasCustomPageRange
            ? PageRangeParser.Parse(target.PageRange)
            : PageRangeParser.Parse("0");

        List<int>? pageIndices = null;
        if (!pageRange.IsAll)
        {
            pageIndices = pageRange.ToZeroBasedIndices()
                .Where(i => i < pages.Count)
                .ToList();
        }

        // Create ArtworkPreview from detail response
        var artworkPreview = new ArtworkPreview
        {
            Id = detail.IllustId ?? target.TargetId,
            Title = detail.IllustTitle ?? "",
            UserId = detail.UserId ?? "",
            UserName = detail.UserName ?? "",
            ThumbnailUrl = detail.ThumbnailUrl ?? "",
            PageCount = detail.PageCount,
            Width = detail.Width,
            Height = detail.Height,
            AiType = detail.AiType,
            Tags = detail.Tags?.Tags.Select(t => t.Tag ?? "").Where(t => !string.IsNullOrEmpty(t)).ToList() ?? new List<string>()
        };

        await _downloadService.DownloadArtworkPagesAsync(artworkPreview, pageIndices, null, ct, settings);

        return (1, 1);
    }

    private async Task<(int Found, int Downloaded)> DownloadFanboxPostAsync(
        DownloadTarget target,
        SettingsOverride settings,
        CancellationToken ct)
    {
        try
        {
            var post = await _fanboxClient.GetPostAsync(target.TargetId, ct);
            if (post == null)
            {
                _logger.LogWarning("FANBOX post {PostId} not found", target.TargetId);
                return (0, 0);
            }

            var s = _settingsService.Current;
            var downloadRoot = !string.IsNullOrWhiteSpace(settings.DownloadRoot) && !settings.UseGlobalSettings
                ? settings.DownloadRoot
                : s.DownloadRoot;
            
            // Create artist folder
            var artistName = post.User?.Name ?? post.CreatorId;
            var artistId = post.UserId ?? post.CreatorId;
            var artistFolder = Path.Combine(downloadRoot, $"FANBOX {artistName} ({artistId})");
            Directory.CreateDirectory(artistFolder);

            // Download cover image
            if (!string.IsNullOrEmpty(post.CoverImageUrl))
            {
                try
                {
                    var coverFilename = ApplyTemplate(s.FilenameFanboxCover, post, post.User);
                    var coverPath = Path.Combine(artistFolder, SanitizeFilename(coverFilename));
                    await DownloadFileFromUrlAsync(post.CoverImageUrl, coverPath, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download FANBOX cover for post {PostId}", post.Id);
                }
            }

            // Download content images
            var downloadedCount = 0;
            foreach (var image in post.Images)
            {
                try
                {
                    var imageFilename = ApplyTemplate(s.FilenameFanboxContent, post, post.User, image.Extension);
                    var imagePath = Path.Combine(artistFolder, SanitizeFilename(imageFilename));
                    await DownloadFileFromUrlAsync(image.OriginalUrl, imagePath, ct);
                    downloadedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download FANBOX image {ImageId} for post {PostId}", image.Id, post.Id);
                }
            }

            // Write metadata/info file
            if (s.WriteFanboxHtml || s.WriteImageInfo)
            {
                var infoFilename = ApplyTemplate(s.FilenameFanboxInfo, post, post.User, "txt");
                var infoPath = Path.Combine(artistFolder, SanitizeFilename(infoFilename));
                
                var info = new StringBuilder();
                info.AppendLine($"Title: {post.Title}");
                info.AppendLine($"Post ID: {post.Id}");
                info.AppendLine($"Creator: {artistName} ({artistId})");
                info.AppendLine($"Published: {post.PublishedDatetime:yyyy-MM-dd HH:mm:ss}");
                info.AppendLine($"Fee Required: {post.FeeRequired} JPY");
                info.AppendLine($"Adult Content: {post.HasAdultContent}");
                info.AppendLine($"Image Count: {post.Images.Count}");
                
                if (!string.IsNullOrEmpty(post.Body?.Text))
                {
                    info.AppendLine($"\nBody:\n{post.Body.Text}");
                }

                await File.WriteAllTextAsync(infoPath, info.ToString(), ct);
            }

            return (1, downloadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download FANBOX post {PostId}", target.TargetId);
            return (0, 0);
        }
    }

    private async Task DownloadFileFromUrlAsync(string url, string filePath, CancellationToken ct)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        await File.WriteAllBytesAsync(filePath, bytes, ct);
    }

    private string ApplyTemplate(string template, FanboxPost post, FanboxUser? user, string? extension = null)
    {
        var result = template
            .Replace("%artist%", user?.Name ?? post.CreatorId)
            .Replace("%member_id%", user?.UserId ?? post.CreatorId)
            .Replace("%creator_id%", post.CreatorId)
            .Replace("%image_id%", post.Id)
            .Replace("%title%", post.Title)
            .Replace("%urlFilename%", post.Id)
            .Replace("%image_ext%", extension ?? "jpg");

        return result;
    }

    private string SanitizeFilename(string filename)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", filename.Split(invalidChars));
    }

    #endregion

    public void Dispose()
    {
        // Cancel all active jobs
        foreach (var cts in _activeJobs.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _activeJobs.Clear();
        _runningTasks.Clear();
        _progressListeners.Clear();
    }
}

/// <summary>
/// Event arguments for job completion events.
/// </summary>
public class JobCompletedEventArgs : EventArgs
{
    public DownloadJob Job { get; }

    public JobCompletedEventArgs(DownloadJob job)
    {
        Job = job;
    }
}
