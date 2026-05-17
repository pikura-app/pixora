using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pixora.Core.Data;
using Pixora.Core.Models;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using Pixora.Avalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Pixora.Avalonia.ViewModels;

public enum ScheduleTriggerMode { Daily, Startup, Once }

// ── A single artist staged in the Add-Schedule form ───────────────────────────
public sealed class StagedArtist
{
    public string UserId   { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string PageRange { get; set; } = "0";
    public string DisplayLabel => string.IsNullOrEmpty(UserName) ? UserId : $"{UserName} (ID {UserId})";
}

// ── Row VM shown in the schedule list ─────────────────────────────────────────
public partial class ScheduleRowViewModel : ObservableObject
{
    public DownloadSchedule Model { get; }

    [ObservableProperty] private bool _isEnabled;

    public string Name           => Model.Name;
    public string TypeLabel      => Model.Type switch
    {
        ScheduleType.SpecificArtists  => "Specific Artists",
        ScheduleType.FollowedArtists  => "Followed Artists",
        ScheduleType.DailyRankings    => "Daily Rankings",
        ScheduleType.Bookmarks        => "Bookmarks",
        _                             => Model.Type.ToString()
    };
    public string TriggerLabel   => Model.Trigger switch
    {
        ScheduleTrigger.DailyAtTime => $"Daily at {To12h(Model.TriggerHour, Model.TriggerMinute)}",
        ScheduleTrigger.Interval    => $"Every {Model.Interval?.TotalHours:N0} h",
        ScheduleTrigger.OnStartup   => "On Startup",
        ScheduleTrigger.Once        => $"Once — {Model.TriggerDateTime?.ToLocalTime():MMM d, yyyy h:mm tt}",
        _                           => Model.Trigger.ToString()
    };

    private static string To12h(int? hour24, int? minute)
    {
        if (hour24 == null || minute == null) return "—";
        var h = hour24.Value % 12;
        if (h == 0) h = 12;
        var ampm = hour24.Value < 12 ? "AM" : "PM";
        return $"{h}:{minute.Value:D2} {ampm}";
    }
    public string ArtistsSummary => Model.Artists.Count > 0
        ? string.Join(", ", Model.Artists.Take(3).Select(a => a.UserName)) +
          (Model.Artists.Count > 3 ? $" +{Model.Artists.Count - 3} more" : "")
        : string.Empty;
    public bool HasArtists       => Model.Artists.Count > 0;
    public string LastRun        => Model.LastRunAt.HasValue
        ? Model.LastRunAt.Value.ToLocalTime().ToString("MMM d, h:mm tt")
        : "Never";
    public string NextRun        => Model.NextRunAt.HasValue
        ? Model.NextRunAt.Value.ToLocalTime().ToString("MMM d, h:mm tt")
        : "—";
    public bool HasError         => !string.IsNullOrEmpty(Model.LastError);
    public string? LastError     => Model.LastError;

    public ScheduleRowViewModel(DownloadSchedule model)
    {
        Model      = model;
        _isEnabled = model.IsEnabled;
    }

    public void RefreshDisplayProperties()
    {
        OnPropertyChanged(nameof(LastRun));
        OnPropertyChanged(nameof(NextRun));
    }
}

// ── Main ViewModel ─────────────────────────────────────────────────────────────
public partial class SchedulesViewModel : ViewModelBase
{
    private readonly DownloadScheduleRepository _repo;
    private readonly PixivClient _client;
    private readonly SettingsService _settingsService;
    private readonly DialogService _dialogService;
    private readonly ScheduleExecutorService _executor;

    public ObservableCollection<ScheduleRowViewModel> Schedules { get; } = new();

    // ── Staging artist list (built before saving) ──────────────────────────
    public ObservableCollection<StagedArtist> StagedArtists { get; } = new();
    public bool HasStagedArtists => StagedArtists.Count > 0;

    // ── Add-form: artist entry ─────────────────────────────────────────────
    [ObservableProperty] private string _newArtistId   = string.Empty;
    [ObservableProperty] private string _newArtistName = string.Empty;
    [ObservableProperty] private bool   _isResolvingArtist;
    [ObservableProperty] private string _resolveError  = string.Empty;
    [ObservableProperty] private string _newScheduleName = string.Empty;

    // ── Trigger config ─────────────────────────────────────────────────────
    [ObservableProperty] private int    _triggerHour12  = 5;    // 1–12
    [ObservableProperty] private int    _triggerMinute  = 0;
    [ObservableProperty] private string _triggerAmPm    = "PM"; // "AM" or "PM"
    [ObservableProperty] private int    _checksPerDay   = 1;
    [ObservableProperty] private ScheduleTriggerMode _triggerMode = ScheduleTriggerMode.Daily;
    [ObservableProperty] private DateTimeOffset? _onceDateTime = DateTimeOffset.Now.AddDays(1);

    public bool IsTriggerDaily   { get => TriggerMode == ScheduleTriggerMode.Daily;   set { if (value) TriggerMode = ScheduleTriggerMode.Daily; } }
    public bool IsTriggerStartup { get => TriggerMode == ScheduleTriggerMode.Startup; set { if (value) TriggerMode = ScheduleTriggerMode.Startup; } }
    public bool IsTriggerOnce    { get => TriggerMode == ScheduleTriggerMode.Once;    set { if (value) TriggerMode = ScheduleTriggerMode.Once; } }

    partial void OnTriggerModeChanged(ScheduleTriggerMode value)
    {
        OnPropertyChanged(nameof(IsTriggerDaily));
        OnPropertyChanged(nameof(IsTriggerStartup));
        OnPropertyChanged(nameof(IsTriggerOnce));
    }

    /// <summary>Converts 12-h inputs to 0–23 hour for storage.</summary>
    private int TriggerHour24 =>
        TriggerAmPm == "AM"
            ? (TriggerHour12 == 12 ? 0 : TriggerHour12)
            : (TriggerHour12 == 12 ? 12 : TriggerHour12 + 12);

    public string[] AmPmOptions { get; } = { "AM", "PM" };

    // ── Per-schedule custom settings ─────────────────────────────────────────────────────
    [ObservableProperty] private bool   _useCustomSettings;
    [ObservableProperty] private string _customFolderTemplate   = string.Empty;
    [ObservableProperty] private string _customFilenameTemplate = string.Empty;
    [ObservableProperty] private string _pageRange = "0";

    // ── Content filters (always active, no need for UseCustomSettings toggle) ──────
    [ObservableProperty] private bool _filterSkipAi;
    [ObservableProperty] private bool _filterSkipManga;
    [ObservableProperty] private bool _filterSkipUgoira;
    [ObservableProperty] private bool _filterSkipR18;
    [ObservableProperty] private bool _filterSkipR18G;
    [ObservableProperty] private bool _filterOnlyNew;   // only artworks newer than last run
    [ObservableProperty] private string _filterIncludeTags  = string.Empty;
    [ObservableProperty] private string _filterExcludeTags  = string.Empty;
    [ObservableProperty] private bool   _useCustomNaming;
    [ObservableProperty] private bool   _customSeparateR18;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _isBusy;

    public int[] Hour12Options  { get; } = Enumerable.Range(1, 12).ToArray();
    public int[] MinuteOptions  { get; } = { 0, 15, 30, 45 };

    public SchedulesViewModel(
        DownloadScheduleRepository repo,
        PixivClient client,
        SettingsService settingsService,
        DialogService dialogService,
        ScheduleExecutorService executor)
    {
        _repo            = repo;
        _client          = client;
        _settingsService = settingsService;
        _dialogService   = dialogService;
        _executor        = executor;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Schedules.Clear();
            var all = await _repo.GetAllAsync();
            foreach (var s in all)
                Schedules.Add(new ScheduleRowViewModel(s));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load schedules");
            StatusMessage = $"Error loading: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task AddArtistToStagingAsync()
    {
        if (string.IsNullOrWhiteSpace(NewArtistId)) return;
        IsResolvingArtist = true;
        ResolveError      = string.Empty;
        try
        {
            var id = ExtractId(NewArtistId.Trim());
            if (StagedArtists.Any(a => a.UserId == id))
            {
                ResolveError = "Artist already in the list.";
                return;
            }
            var profile = await _client.GetArtistAsync(id);
            var name = profile?.Name ?? id;
            StagedArtists.Add(new StagedArtist { UserId = id, UserName = name, PageRange = PageRange });
            OnPropertyChanged(nameof(HasStagedArtists));
            NewArtistId   = string.Empty;
            NewArtistName = string.Empty;
            StatusMessage = $"Added {name} to staging list ({StagedArtists.Count} artists).";
        }
        catch (Exception ex)
        {
            ResolveError = $"Could not resolve: {ex.Message}";
        }
        finally { IsResolvingArtist = false; }
    }

    [RelayCommand]
    private void RemoveArtistFromStaging(StagedArtist artist)
    {
        StagedArtists.Remove(artist);
        OnPropertyChanged(nameof(HasStagedArtists));
    }

    [RelayCommand]
    private void ClearStagedArtists()
    {
        StagedArtists.Clear();
        OnPropertyChanged(nameof(HasStagedArtists));
    }

    [RelayCommand]
    private async Task AddScheduleAsync()
    {
        if (StagedArtists.Count == 0)
        {
            await _dialogService.ShowMessageAsync("Validation", "Add at least one artist to the list.");
            return;
        }

        var scheduleName = string.IsNullOrWhiteSpace(NewScheduleName)
            ? (StagedArtists.Count == 1 ? $"{StagedArtists[0].UserName} — Auto" : $"{StagedArtists.Count} Artists — Auto")
            : NewScheduleName;

        // Always build a SettingsOverride so filters are stored even without naming overrides
        var customSettings = SettingsOverride.FromGlobalSettings(_settingsService.Current);
        customSettings.UseGlobalSettings = false;

        // Content-type filters
        customSettings.FilterAiGenerated = FilterSkipAi   ? true : (bool?)null;
        customSettings.SkipManga         = FilterSkipManga  ? true : (bool?)null;
        customSettings.SkipUgoira        = FilterSkipUgoira ? true : (bool?)null;
        customSettings.SkipR18           = FilterSkipR18    ? true : (bool?)null;
        customSettings.SkipR18G          = FilterSkipR18G   ? true : (bool?)null;
        customSettings.OnlyNewSinceLastRun = FilterOnlyNew  ? true : (bool?)null;

        // Tag filters
        if (!string.IsNullOrWhiteSpace(FilterIncludeTags))
            customSettings.IncludeTags = FilterIncludeTags.Trim();
        if (!string.IsNullOrWhiteSpace(FilterExcludeTags))
            customSettings.ExcludeTagsFilter = FilterExcludeTags.Trim();

        // Naming overrides (only if opted in)
        if (UseCustomNaming)
        {
            if (!string.IsNullOrWhiteSpace(CustomFolderTemplate))
                customSettings.FolderTemplate = CustomFolderTemplate;
            if (!string.IsNullOrWhiteSpace(CustomFilenameTemplate))
                customSettings.FilenameTemplate = CustomFilenameTemplate;
            customSettings.SeparateR18Folder = CustomSeparateR18;
        }

        var schedule = new DownloadSchedule
        {
            Name      = scheduleName,
            Type      = ScheduleType.SpecificArtists,
            IsEnabled = true,
            Artists   = StagedArtists.Select(a => new ScheduledArtist
            {
                UserId    = a.UserId,
                UserName  = a.UserName,
                PageRange = a.PageRange,
            }).ToList(),
            PageRange = PageRange,
            Settings  = customSettings,
        };

        if (TriggerMode == ScheduleTriggerMode.Startup)
        {
            schedule.Trigger = ScheduleTrigger.OnStartup;
        }
        else if (TriggerMode == ScheduleTriggerMode.Once)
        {
            if (OnceDateTime == null)
            {
                await _dialogService.ShowMessageAsync("Validation", "Pick a date and time for the one-time run.");
                return;
            }
            // Merge the chosen date with the chosen time
            var d = OnceDateTime.Value.Date;
            var combined = new DateTime(d.Year, d.Month, d.Day, TriggerHour24, TriggerMinute, 0, DateTimeKind.Local);
            schedule.Trigger         = ScheduleTrigger.Once;
            schedule.TriggerDateTime = combined.ToUniversalTime();
        }
        else if (ChecksPerDay == 1)
        {
            schedule.Trigger       = ScheduleTrigger.DailyAtTime;
            schedule.TriggerHour   = TriggerHour24;
            schedule.TriggerMinute = TriggerMinute;
        }
        else
        {
            schedule.Trigger       = ScheduleTrigger.Interval;
            schedule.Interval      = TimeSpan.FromHours(24.0 / ChecksPerDay);
            schedule.TriggerHour   = TriggerHour24;
            schedule.TriggerMinute = TriggerMinute;
        }

        await _repo.SaveAsync(schedule);
        Schedules.Insert(0, new ScheduleRowViewModel(schedule));
        StatusMessage = $"Schedule '{scheduleName}' added ({StagedArtists.Count} artists).";

        StagedArtists.Clear();
        OnPropertyChanged(nameof(HasStagedArtists));
        NewScheduleName = string.Empty;
        UseCustomSettings = false;
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(ScheduleRowViewModel row)
    {
        row.Model.IsEnabled = row.IsEnabled;
        await _repo.SaveAsync(row.Model);
        StatusMessage = $"{row.Name} {(row.IsEnabled ? "enabled" : "disabled")}.";
    }

    [RelayCommand]
    private async Task RunNowAsync(ScheduleRowViewModel row)
    {
        try
        {
            StatusMessage = $"Running '{row.Name}'…";
            await _executor.ExecuteStartupSchedulesAsync(); // triggers immediately via coordinator
            await _repo.RecordRunAsync(row.Model.Id, true);
            row.Model.LastRunAt = DateTime.UtcNow;
            row.RefreshDisplayProperties();
            StatusMessage = $"'{row.Name}' triggered successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteScheduleAsync(ScheduleRowViewModel row)
    {
        var ok = await _dialogService.ShowConfirmationAsync(
            "Delete Schedule", $"Remove '{row.Name}'?");
        if (!ok) return;
        await _repo.DeleteAsync(row.Model.Id);
        Schedules.Remove(row);
        StatusMessage = $"Removed '{row.Name}'.";
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    private static string ExtractId(string input)
    {
        var m = System.Text.RegularExpressions.Regex.Match(input, @"users/(\d+)");
        return m.Success ? m.Groups[1].Value : input;
    }
}
