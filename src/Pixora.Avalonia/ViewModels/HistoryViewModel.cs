using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixora.Core.Data;
using Pixora.Core.Models;
using Pixora.Core.Services;
using Pixora.Avalonia.Services;

namespace Pixora.Avalonia.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly DownloadJobRepository _jobRepository;
    private readonly DownloadCoordinator _coordinator;
    private readonly DialogService _dialogService;
    private readonly PixivImageLoader _imageLoader;

    [ObservableProperty] private ObservableCollection<DownloadJobViewModel> _activeJobs = new();
    [ObservableProperty] private ObservableCollection<DownloadJobViewModel> _completedJobs = new();

    public HistoryViewModel(DownloadJobRepository jobRepository, DownloadCoordinator coordinator, DialogService dialogService, PixivImageLoader imageLoader)
    {
        _jobRepository = jobRepository;
        _coordinator = coordinator;
        _dialogService = dialogService;
        _imageLoader = imageLoader;

        coordinator.JobStarted  += OnJobStarted;
        coordinator.JobCompleted += OnJobCompleted;

        LoadJobs();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task ClearCompletedAsync()
    {
        var completedIds = CompletedJobs.Select(j => j.Job.Id).ToList();
        foreach (var id in completedIds)
        {
            await _jobRepository.DeleteJobAsync(id);
        }
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task RetryJobAsync(DownloadJobViewModel jobVm)
    {
        if (jobVm.Job.Status == JobStatus.Failed || jobVm.HasFailedItems)
        {
            try
            {
                // Update job status and timestamps
                jobVm.Job.Status = JobStatus.Pending;
                jobVm.Job.LastRetriedAt = DateTime.UtcNow;
                jobVm.Job.RetryCount++;
                
                // Reset failed targets to pending
                foreach (var target in jobVm.Job.Targets.Where(t => t.Status == TargetStatus.Failed))
                {
                    target.Status = TargetStatus.Pending;
                    target.ErrorMessage = null;
                }
                
                // Save changes
                await _jobRepository.SaveJobAsync(jobVm.Job);
                
                // Start the job via coordinator
                await _coordinator.StartJobAsync(jobVm.Job.Id);
                
                await LoadJobsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Retry failed: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private async Task RetryAllFailedAsync()
    {
        var failedJobs = CompletedJobs.Where(j => j.HasFailedItems).ToList();
        if (failedJobs.Count == 0) return;
        
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Retry All Failed",
            $"Retry {failedJobs.Count} failed jobs?");
        
        if (!confirmed) return;
        
        foreach (var jobVm in failedJobs)
        {
            await RetryJobAsync(jobVm);
        }
    }

    private void OnJobStarted(object? sender, JobCompletedEventArgs e)
    {
        var jobVm = new DownloadJobViewModel(e.Job, _imageLoader);
        var progressHandler = new Progress<JobProgress>(p => OnProgressReceived(p, jobVm));
        _coordinator.SubscribeToProgress(e.Job.Id, progressHandler);
        void AddToActive()
        {
            if (ActiveJobs.Any(j => j.Job.Id == e.Job.Id)) return;
            ActiveJobs.Add(jobVm);
        }
        if (Dispatcher.UIThread.CheckAccess())
            AddToActive();
        else
            Dispatcher.UIThread.Post(AddToActive, global::Avalonia.Threading.DispatcherPriority.Send);
    }

    private void OnJobCompleted(object? sender, JobCompletedEventArgs e)
    {
        var completedJob = e.Job;
        void MoveToCompleted()
        {
            var active = ActiveJobs.FirstOrDefault(j => j.Job.Id == completedJob.Id);
            if (active != null) ActiveJobs.Remove(active);
            if (!CompletedJobs.Any(j => j.Job.Id == completedJob.Id))
                CompletedJobs.Insert(0, new DownloadJobViewModel(completedJob, _imageLoader));
        }
        if (Dispatcher.UIThread.CheckAccess())
            MoveToCompleted();
        else
            Dispatcher.UIThread.Post(MoveToCompleted, global::Avalonia.Threading.DispatcherPriority.Normal);
    }

    private void OnProgressReceived(JobProgress progress, DownloadJobViewModel jobVm)
    {
        Dispatcher.UIThread.Post(() => jobVm.ApplyProgress(progress));
    }

    private void LoadJobs()
    {
        _ = LoadJobsAsync();
    }

    private async Task LoadJobsAsync()
    {
        var jobs = await _jobRepository.GetJobsAsync();

        ActiveJobs.Clear();
        CompletedJobs.Clear();

        foreach (var job in jobs.Where(j => j.Status == JobStatus.Pending ||
                                            j.Status == JobStatus.Running))
        {
            ActiveJobs.Add(new DownloadJobViewModel(job, _imageLoader));
        }

        foreach (var job in jobs.Where(j => j.Status == JobStatus.Completed ||
                                            j.Status == JobStatus.Failed ||
                                            j.Status == JobStatus.Cancelled)
                                .OrderByDescending(j => j.CompletedAt))
        {
            CompletedJobs.Add(new DownloadJobViewModel(job, _imageLoader));
        }
    }
}

public partial class DownloadJobViewModel : ObservableObject
{
    public DownloadJob Job { get; }

    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _resultSummary = "";
    [ObservableProperty] private bool _hasFailedItems;
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private string? _currentTargetName;
    [ObservableProperty] private string? _currentArtist;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _currentFileLabel;
    [ObservableProperty] private double _currentFilePct;
    [ObservableProperty] private Bitmap? _currentFileThumbnail;
    [ObservableProperty] private bool _hasCurrentFile;

    private string? _lastThumbnailUrl;
    private PixivImageLoader? _imageLoader;

    public bool HasOutputFolder => !string.IsNullOrWhiteSpace(Job.OutputFolder) && Directory.Exists(Job.OutputFolder);
    public bool HasThumbnail => Thumbnail != null;

    public string TypeLabel => Job.Type switch
    {
        DownloadJobType.Artist        => "Artist",
        DownloadJobType.ImageId       => "Image",
        DownloadJobType.BookmarkArtist => "Bookmarks",
        DownloadJobType.BookmarkImage  => "Bookmarks",
        DownloadJobType.ListFile      => "List",
        _                             => Job.Type.ToString()
    };

    public string? ArtistInfo
    {
        get
        {
            var t = Job.Targets.FirstOrDefault();
            if (t == null) return null;
            if (!string.IsNullOrEmpty(t.UserName) && !string.IsNullOrEmpty(t.UserId))
                return $"{t.UserName} (ID {t.UserId})";
            if (!string.IsNullOrEmpty(t.UserName)) return t.UserName;
            return null;
        }
    }
    public bool HasArtistInfo => !string.IsNullOrEmpty(ArtistInfo);

    public DownloadJobViewModel(DownloadJob job, PixivImageLoader imageLoader)
    {
        Job = job;
        _imageLoader = imageLoader;
        UpdateStatus();
        var firstTarget = job.Targets.FirstOrDefault();
        if (firstTarget != null && !string.IsNullOrEmpty(firstTarget.UserName))
            CurrentArtist = firstTarget.UserName;
        var thumbUrl = firstTarget?.ThumbnailUrl
            ?? job.Targets.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl;
        if (thumbUrl != null)
            _ = LoadThumbnailAsync(thumbUrl, imageLoader);
    }

    private async Task LoadThumbnailAsync(string url, PixivImageLoader loader)
    {
        try
        {
            var bytes = await loader.FetchBytesAsync(url);
            if (bytes is null && url.Contains("_master1200"))
                bytes = await loader.FetchBytesAsync(url.Replace("_master1200", "_square1200"));
            if (bytes != null)
                Thumbnail = new Bitmap(new System.IO.MemoryStream(bytes));
        }
        catch { }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(Job.OutputFolder)) return;
        try
        {
            var folder = ResolveArtistRootFolder(Job.OutputFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch { }
    }

    /// <summary>
    /// If <paramref name="outputFolder"/> is an artwork subfolder, returns its parent
    /// (the artist folder). Falls back to <paramref name="outputFolder"/> itself if the
    /// parent doesn't exist or is a drive root.
    /// </summary>
    private static string ResolveArtistRootFolder(string outputFolder)
    {
        try
        {
            var full = Path.GetFullPath(outputFolder);
            var folderName = Path.GetFileName(full);
            // Artwork subfolders are named "{numericId}_title" — walk up to the artist folder
            if (!string.IsNullOrEmpty(folderName) && char.IsDigit(folderName[0]))
            {
                var parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)
                    && !string.IsNullOrEmpty(Path.GetDirectoryName(parent))) // not a drive root
                    return parent;
            }
        }
        catch { }
        return outputFolder;
    }

    public void ApplyProgress(JobProgress progress)
    {
        ProgressPercent   = progress.PercentComplete;
        StatusText        = "▶ Running";
        CompletedCount    = progress.CompletedTargets;
        TotalCount        = progress.TotalTargets;
        if (progress.CurrentTargetName != null)
            CurrentTargetName = progress.CurrentTargetName;
        ProgressText = progress.TotalTargets > 0
            ? $"{progress.CompletedTargets} / {progress.TotalTargets}"
            : "Running…";

        var t = Job.Targets.FirstOrDefault();
        if (t != null && !string.IsNullOrEmpty(t.UserName))
            CurrentArtist = t.UserName;

        // Per-file detail
        if (progress.CurrentArtworkId != null)
        {
            var pageLabel = progress.CurrentPageTotal > 1
                ? $"p{progress.CurrentPageIndex + 1}/{progress.CurrentPageTotal}"
                : null;
            var pct = progress.CurrentTotalBytes > 0
                ? (int)(100 * progress.CurrentBytesSoFar / progress.CurrentTotalBytes.Value)
                : 0;
            var sizeLabel = progress.CurrentTotalBytes > 0
                ? $"{progress.CurrentBytesSoFar / 1024} / {progress.CurrentTotalBytes.Value / 1024} KB"
                : progress.CurrentBytesSoFar > 0 ? $"{progress.CurrentBytesSoFar / 1024} KB" : null;

            CurrentFileLabel = string.Join("  ",
                new[] { progress.CurrentArtworkId, pageLabel, sizeLabel }
                    .Where(s => !string.IsNullOrEmpty(s)));
            CurrentFilePct   = pct;
            HasCurrentFile   = true;

            // Swap live thumbnail when artwork changes
            if (progress.CurrentThumbnailUrl != null && progress.CurrentThumbnailUrl != _lastThumbnailUrl)
            {
                _lastThumbnailUrl = progress.CurrentThumbnailUrl;
                if (_imageLoader != null)
                    _ = LoadCurrentThumbnailAsync(progress.CurrentThumbnailUrl);
            }
        }
    }

    private async Task LoadCurrentThumbnailAsync(string url)
    {
        try
        {
            var bytes = await _imageLoader!.FetchBytesAsync(url);
            if (bytes is null && url.Contains("_master1200"))
                bytes = await _imageLoader.FetchBytesAsync(url.Replace("_master1200", "_square1200"));
            if (bytes != null)
                CurrentFileThumbnail = new Bitmap(new System.IO.MemoryStream(bytes));
        }
        catch { }
    }

    private void UpdateStatus()
    {
        StatusText = Job.Status switch
        {
            JobStatus.Pending => "⏳ Queued",
            JobStatus.Running => "▶ Running",
            JobStatus.Paused => "⏸ Paused",
            JobStatus.Completed => "✅ Completed",
            JobStatus.Failed => "❌ Failed",
            JobStatus.Cancelled => "🚫 Cancelled",
            _ => Job.Status.ToString()
        };

        if (Job.Status == JobStatus.Completed ||
            Job.Status == JobStatus.Failed)
        {
            var success = Job.CompletedItems;
            var failed = Job.FailedItems;
            ResultSummary = $"{success} succeeded, {failed} failed";
            HasFailedItems = failed > 0;
            ProgressPercent = 100;
            ProgressText = ResultSummary;
        }
        else if (Job.Status == JobStatus.Running)
        {
            ProgressPercent = Job.ProgressPercent;
            ProgressText = $"{Job.CompletedItems} of {Job.TotalItems} completed";
        }
    }
}
