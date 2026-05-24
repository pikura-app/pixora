using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Pikura.Avalonia.Services;
using Pikura.Core.Models;
using Pikura.Core.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orientation = Avalonia.Layout.Orientation;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace Pikura.Avalonia.Views.Download;

public partial class DownloadQueueView : UserControl
{
    private readonly DownloadCoordinator _coordinator;
    private Window? _ownerWindow;
    private readonly Dictionary<Guid, (ProgressBar Bar, TextBlock Label, TextBlock SubLabel)> _liveCards = new();
    private DispatcherTimer? _refreshTimer;

    public DownloadQueueView()
    {
        InitializeComponent();
        _coordinator = AppServices.Get<DownloadCoordinator>();
        _coordinator.JobCompleted += (_, _) => _ = LoadJobsAsync();
        _ = LoadJobsAsync();
    }

    public void SetOwnerWindow(Window window)
    {
        _ownerWindow = window;
        // Start a refresh timer while the window is open
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => _ = LoadJobsAsync();
        _refreshTimer.Start();
        window.Closed += (_, _) => _refreshTimer?.Stop();
    }

    private async Task LoadJobsAsync()
    {
        var jobs = await _coordinator.GetJobsAsync(limit: 50);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _liveCards.Clear();
            JobsPanel.Children.Clear();
            if (jobs.Count == 0)
            {
                JobCountLabel.Text = "(empty)";
                JobsPanel.Children.Add(new TextBlock
                {
                    Text = "No download jobs yet.",
                    Foreground = Brush.Parse("#6B7280"),
                    Margin = new Thickness(8, 16),
                    FontSize = 13
                });
                return;
            }

            JobCountLabel.Text = $"({jobs.Count} jobs)";
            foreach (var job in jobs)
                JobsPanel.Children.Add(BuildJobCard(job));
        });
    }

    private void SubscribeToLiveProgress(DownloadJob job,
        ProgressBar bar, TextBlock label, TextBlock subLabel)
    {
        var progress = new Progress<JobProgress>(p =>
        {
            bar.Value = p.PercentComplete;
            label.Text = $"{p.PercentComplete:F0}%  {p.Message}";
            subLabel.Text = $"{p.CompletedTargets}/{p.TotalTargets} artworks";
        });
        _liveCards[job.Id] = (bar, label, subLabel);
        _coordinator.SubscribeToProgress(job.Id, progress);
    }

    private Border BuildJobCard(DownloadJob job)
    {
        var statusColor = job.Status switch
        {
            JobStatus.Completed => "#4ADE80",
            JobStatus.Failed => "#F87171",
            JobStatus.Running => "#60A5FA",
            JobStatus.Cancelled => "#9CA3AF",
            _ => "#9CA3AF"
        };

        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var info = new StackPanel { Spacing = 4 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = job.Name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13
        });
        titleRow.Children.Add(new Border
        {
            Background = Brush.Parse(statusColor + "33"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2),
            Child = new TextBlock
            {
                Text = job.Status.ToString(),
                FontSize = 10,
                Foreground = Brush.Parse(statusColor)
            }
        });
        info.Children.Add(titleRow);

        info.Children.Add(new TextBlock
        {
            Text = $"{job.CompletedItems}/{job.TotalItems} targets  ·  Created {job.CreatedAt:yyyy-MM-dd HH:mm}",
            FontSize = 11,
            Foreground = Brush.Parse("#9CA3AF")
        });

        if (job.Status == JobStatus.Running)
        {
            var bar = new ProgressBar
            {
                Value = 0,
                Maximum = 100,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                ShowProgressText = false,
                IsIndeterminate = false,
                Margin = new Thickness(0, 4, 0, 0)
            };
            var progressLabel = new TextBlock
            {
                Text = "0%",
                FontSize = 11,
                Foreground = Brush.Parse("#9CA3AF")
            };
            var artworkLabel = new TextBlock
            {
                Text = "Starting...",
                FontSize = 11,
                Foreground = Brush.Parse("#9CA3AF")
            };
            info.Children.Add(bar);
            info.Children.Add(progressLabel);
            info.Children.Add(artworkLabel);
            SubscribeToLiveProgress(job, bar, progressLabel, artworkLabel);
        }

        if (!string.IsNullOrEmpty(job.ErrorMessage))
        {
            info.Children.Add(new TextBlock
            {
                Text = "⚠ " + job.ErrorMessage,
                FontSize = 11,
                Foreground = Brush.Parse("#F87171"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        // Action buttons for active jobs
        if (job.Status == JobStatus.Running || job.Status == JobStatus.Pending)
        {
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (job.Status == JobStatus.Pending)
            {
                var startBtn = new Button
                {
                    Content = "▶ Start",
                    FontSize = 11,
                    Padding = new Thickness(8, 4),
                    CornerRadius = new CornerRadius(5)
                };
                startBtn.Click += async (_, _) =>
                {
                    await _coordinator.StartJobAsync(job.Id);
                    await LoadJobsAsync();
                };
                btnPanel.Children.Add(startBtn);
            }

            var cancelBtn = new Button
            {
                Content = "✕ Cancel",
                FontSize = 11,
                Padding = new Thickness(8, 4),
                CornerRadius = new CornerRadius(5)
            };
            cancelBtn.Click += async (_, _) =>
            {
                await _coordinator.CancelJobAsync(job.Id);
                await LoadJobsAsync();
            };
            btnPanel.Children.Add(cancelBtn);

            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);
        }

        card.Child = grid;
        return card;
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = LoadJobsAsync();

    private async void OnClearCompleted(object? sender, RoutedEventArgs e)
    {
        var jobs = await _coordinator.GetJobsAsync();
        foreach (var job in jobs)
        {
            if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                await _coordinator.DeleteJobAsync(job.Id);
        }
        await LoadJobsAsync();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        if (_ownerWindow is Window w)
            w.Close();
    }
}
