using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
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

        coordinator.JobCompleted += (_, _) => LoadJobs();

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
        UpdateStatus();
        var thumbUrl = job.Targets.FirstOrDefault(t => !string.IsNullOrEmpty(t.ThumbnailUrl))?.ThumbnailUrl;
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
            Process.Start(new ProcessStartInfo
            {
                FileName = Job.OutputFolder,
                UseShellExecute = true,
                Verb = "open"
            });
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
