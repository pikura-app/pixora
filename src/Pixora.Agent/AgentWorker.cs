using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pixora.Core.Services;
using Pixora.Core.Settings;

namespace Pixora.Agent;

/// <summary>
/// .NET Worker that hosts <see cref="ScheduleExecutorService"/> in a background process.
/// Broadcasts progress events over the named pipe so the Pixora UI can display them.
/// </summary>
public sealed class AgentWorker : BackgroundService
{
    private readonly ScheduleExecutorService _executor;
    private readonly SettingsService _settings;
    private readonly IpcServer _ipc;
    private readonly ILogger<AgentWorker> _logger;

    public AgentWorker(
        ScheduleExecutorService executor,
        SettingsService settings,
        IpcServer ipc,
        ILogger<AgentWorker> logger)
    {
        _executor = executor;
        _settings = settings;
        _ipc      = ipc;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pixora Agent starting");

        // Start IPC pipe server
        _ipc.Start();

        // Wire executor events → IPC broadcasts
        _executor.ScheduleExecuting += OnScheduleExecuting;
        _executor.ScheduleCompleted += OnScheduleCompleted;

        // Start the schedule timer
        _executor.Start();

        // Run startup schedules immediately on first launch
        try { await _executor.ExecuteStartupSchedulesAsync(stoppingToken); }
        catch (Exception ex) { _logger.LogWarning(ex, "Startup schedules error"); }

        // Heartbeat loop — lets UI know the agent is alive
        using var ticker = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await ticker.WaitForNextTickAsync(stoppingToken))
            {
                _ipc.Broadcast(new IpcMessage { Event = "heartbeat" });
            }
        }
        catch (OperationCanceledException) { }

        // Shutdown
        _executor.Stop();
        _executor.ScheduleExecuting -= OnScheduleExecuting;
        _executor.ScheduleCompleted -= OnScheduleCompleted;
        await _ipc.DisposeAsync().ConfigureAwait(false);

        _logger.LogInformation("Pixora Agent stopped");
    }

    private void OnScheduleExecuting(object? sender, ScheduleExecutingEventArgs e)
    {
        _logger.LogInformation("Schedule starting: {Name}", e.Schedule.Name);
        _ipc.Broadcast(new IpcMessage
        {
            Event        = "schedule_started",
            ScheduleName = e.Schedule.Name,
            StatusText   = $"Starting schedule: {e.Schedule.Name}",
        });
    }

    private void OnScheduleCompleted(object? sender, ScheduleCompletedEventArgs e)
    {
        _logger.LogInformation("Schedule completed: {Name} success={Success}", e.Schedule.Name, e.Success);
        _ipc.Broadcast(new IpcMessage
        {
            Event        = e.Success ? "schedule_completed" : "schedule_failed",
            ScheduleName = e.Schedule.Name,
            StatusText   = e.Success
                ? $"{e.Schedule.Name} completed"
                : $"{e.Schedule.Name}: failed — {e.Error}",
        });
    }
}
