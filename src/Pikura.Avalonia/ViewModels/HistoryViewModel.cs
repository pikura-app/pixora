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
using Pikura.Core.Data;
using Pikura.Core.Models;
using Pikura.Core.Services;
using Pikura.Avalonia.Services;

namespace Pikura.Avalonia.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly DownloadJobRepository _jobRepository;
    private readonly DownloadCoordinator _coordinator;
    private readonly DialogService _dialogService;
    private readonly PixivImageLoader _imageLoader;

    [ObservableProperty] private ObservableCollection<DownloadJobViewModel> _activeJobs = new();
    [ObservableProperty] private ObservableCollection<DownloadJobViewModel> _completedJobs = new();
    [ObservableProperty] private ObservableCollection<DownloadJobViewModel> _failedJobs = new();
    [ObservableProperty] private ObservableCollection<DownloadJobViewModel> _cancelledJobs = new();

    public HistoryViewModel(DownloadJobRepository jobRepository, DownloadCoordinator coordinator, DialogService dialogService, PixivImageLoader imageLoader)
    {
        _jobRepository = jobRepository;
        _coordinator = coordinator;
        _dialogService = dialogService;
        _imageLoader = imageLoader;

        coordinator.JobStarted  += OnJobStarted;
        coordinator.JobCompleted += OnJobCompleted;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task ClearCompletedAsync()
    {
        var ids = CompletedJobs.Select(j => j.Job.Id).ToList();
        foreach (var id in ids) await _jobRepository.DeleteJobAsync(id);
        CompletedJobs.Clear();
    }

    [RelayCommand]
    private async Task ClearFailedAsync()
    {
        var ids = FailedJobs.Select(j => j.Job.Id).ToList();
        foreach (var id in ids) await _jobRepository.DeleteJobAsync(id);
        FailedJobs.Clear();
    }

    [RelayCommand]
    private async Task ClearCancelledAsync()
    {
        var ids = CancelledJobs.Select(j => j.Job.Id).ToList();
        foreach (var id in ids) await _jobRepository.DeleteJobAsync(id);
        CancelledJobs.Clear();
    }

    [RelayCommand]
    private async Task RemoveJobAsync(DownloadJobViewModel jobVm)
    {
        await _jobRepository.DeleteJobAsync(jobVm.Job.Id);
        CompletedJobs.Remove(jobVm);
        FailedJobs.Remove(jobVm);
        CancelledJobs.Remove(jobVm);
    }

    [RelayCommand]
    private async Task CancelJobAsync(DownloadJobViewModel jobVm)
    {
        await _coordinator.CancelJobAsync(jobVm.Job.Id);
    }

    [RelayCommand]
    private async Task RetryJobAsync(DownloadJobViewModel jobVm)
    {
        var retryable = jobVm.Job.Status == JobStatus.Failed
                     || jobVm.Job.Status == JobStatus.Cancelled
                     || jobVm.HasFailedItems;
        if (!retryable) return;
        try
        {
            jobVm.Job.Status = JobStatus.Pending;
            jobVm.Job.LastRetriedAt = DateTime.UtcNow;
            jobVm.Job.RetryCount++;

            foreach (var target in jobVm.Job.Targets.Where(
                t => t.Status == TargetStatus.Failed || t.Status == TargetStatus.Cancelled))
            {
                target.Status = TargetStatus.Pending;
                target.ErrorMessage = null;
            }

            await _jobRepository.SaveJobAsync(jobVm.Job);
            FailedJobs.Remove(jobVm);
            CancelledJobs.Remove(jobVm);
            await _coordinator.StartJobAsync(jobVm.Job.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Retry failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RetryAllFailedAsync()
    {
        var jobs = FailedJobs.ToList();
        if (jobs.Count == 0) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Retry All Failed",
            $"Retry {jobs.Count} failed jobs?");
        if (!confirmed) return;

        foreach (var jobVm in jobs)
            await RetryJobAsync(jobVm);
    }

    private void OnJobStarted(object? sender, JobCompletedEventArgs e)
    {
        Console.Error.WriteLine($"[History] OnJobStarted: {e.Job.Id} '{e.Job.Name}'");
        var jobVm = new DownloadJobViewModel(e.Job, _imageLoader, _coordinator);
        var progressHandler = new Progress<JobProgress>(p => OnProgressReceived(p, jobVm));
        _coordinator.SubscribeToProgress(e.Job.Id, progressHandler);
        void AddToActive()
        {
            Console.Error.WriteLine($"[History] AddToActive: {e.Job.Id} — already: {ActiveJobs.Any(j => j.Job.Id == e.Job.Id)}");
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
        Console.Error.WriteLine($"[History] OnJobCompleted: {e.Job.Id} '{e.Job.Name}' status={e.Job.Status}");
        var job = e.Job;
        void Route()
        {
            Console.Error.WriteLine($"[History] Route: status={job.Status} activeCount={ActiveJobs.Count}");
            var active = ActiveJobs.FirstOrDefault(j => j.Job.Id == job.Id);
            if (active != null) ActiveJobs.Remove(active);
            var vm = new DownloadJobViewModel(job, _imageLoader);
            switch (job.Status)
            {
                case JobStatus.Completed:
                    if (!CompletedJobs.Any(j => j.Job.Id == job.Id))  CompletedJobs.Insert(0, vm);  break;
                case JobStatus.Failed:
                    if (!FailedJobs.Any(j => j.Job.Id == job.Id))     FailedJobs.Insert(0, vm);     break;
                case JobStatus.Cancelled:
                    if (!CancelledJobs.Any(j => j.Job.Id == job.Id))  CancelledJobs.Insert(0, vm);  break;
            }
            Console.Error.WriteLine($"[History] After Route: completed={CompletedJobs.Count} failed={FailedJobs.Count}");
        }
        if (Dispatcher.UIThread.CheckAccess()) Route();
        else Dispatcher.UIThread.Post(Route, global::Avalonia.Threading.DispatcherPriority.Normal);
    }

    private void OnProgressReceived(JobProgress progress, DownloadJobViewModel jobVm)
    {
        Dispatcher.UIThread.Post(() =>
        {
            jobVm.ApplyProgress(progress);

            // Immediately move cancelled jobs from Active to Cancelled
            if (progress.Status == JobStatus.Cancelled)
            {
                var active = ActiveJobs.FirstOrDefault(j => j.Job.Id == progress.JobId);
                if (active != null)
                {
                    ActiveJobs.Remove(active);
                    active.Job.Status = JobStatus.Cancelled;
                    if (!CancelledJobs.Any(j => j.Job.Id == active.Job.Id))
                        CancelledJobs.Insert(0, active);
                }
            }
        });
    }

    public Task ReloadAsync() => LoadJobsAsync();

    private void LoadJobs()
    {
        _ = LoadJobsAsync();
    }

    private async Task LoadJobsAsync()
    {
        var activeJobs    = await _jobRepository.GetAllActiveJobsAsync();
        var completedJobs = await _jobRepository.GetJobsAsync();

        ActiveJobs.Clear();
        CompletedJobs.Clear();

        foreach (var job in activeJobs)
        {
            var jobVm = new DownloadJobViewModel(job, _imageLoader, _coordinator);
            var progressHandler = new Progress<JobProgress>(p => OnProgressReceived(p, jobVm));
            _coordinator.SubscribeToProgress(job.Id, progressHandler);
            ActiveJobs.Add(jobVm);
        }

        FailedJobs.Clear();
        CancelledJobs.Clear();

        foreach (var job in completedJobs.OrderByDescending(j => j.CompletedAt))
        {
            var vm = new DownloadJobViewModel(job, _imageLoader);
            switch (job.Status)
            {
                case JobStatus.Completed:  CompletedJobs.Add(vm);  break;
                case JobStatus.Failed:     FailedJobs.Add(vm);     break;
                case JobStatus.Cancelled:  CancelledJobs.Add(vm);  break;
            }
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
    private DownloadCoordinator? _coordinator;

    [ObservableProperty] private bool _isCancellable;

    public bool HasOutputFolder => !string.IsNullOrWhiteSpace(Job.OutputFolder) && Directory.Exists(Job.OutputFolder);
    public bool HasThumbnail => Thumbnail != null;

    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

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

    public DownloadJobViewModel(DownloadJob job, PixivImageLoader imageLoader, DownloadCoordinator? coordinator = null)
    {
        Job = job;
        _imageLoader = imageLoader;
        _coordinator = coordinator;
        UpdateStatus();
        IsCancellable = job.Status == JobStatus.Running || job.Status == JobStatus.Pending;
        var firstTarget = job.Targets.FirstOrDefault();
        if (firstTarget != null && !string.IsNullOrEmpty(firstTarget.UserName))
            CurrentArtist = firstTarget.UserName;
        var thumbUrl = firstTarget?.ThumbnailUrl
            ?? job.Targets.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl;
        if (thumbUrl != null)
            _ = LoadThumbnailAsync(thumbUrl, imageLoader);
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (_coordinator == null) return;
        await _coordinator.CancelJobAsync(Job.Id);
        IsCancellable = false;
    }

    private async Task LoadThumbnailAsync(string url, PixivImageLoader loader)
    {
        try
        {
            var bytes = await loader.FetchBytesAsync(url);
            if (bytes is null && url.Contains("_master1200"))
                bytes = await loader.FetchBytesAsync(url.Replace("_master1200", "_square1200"));
            if (bytes != null)
            {
                // Decode on background thread to avoid UI-thread jank
                var bmp = await Task.Run(() => new Bitmap(new System.IO.MemoryStream(bytes)));
                await Dispatcher.UIThread.InvokeAsync(() => Thumbnail = bmp);
            }
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
        StatusText        = progress.Status switch
        {
            JobStatus.Running => "▶ Running",
            JobStatus.Cancelled => "🚫 Cancelling…",
            JobStatus.Paused => "⏸ Paused",
            _ => progress.Status.ToString()
        };
        IsCancellable     = progress.Status == JobStatus.Running || progress.Status == JobStatus.Pending;
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
            {
                // Decode on background thread, then assign on UI thread
                var bmp = await Task.Run(() => new Bitmap(new System.IO.MemoryStream(bytes)));
                await Dispatcher.UIThread.InvokeAsync(() => CurrentFileThumbnail = bmp);
            }
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

        IsCancellable = Job.Status == JobStatus.Running || Job.Status == JobStatus.Pending;

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
