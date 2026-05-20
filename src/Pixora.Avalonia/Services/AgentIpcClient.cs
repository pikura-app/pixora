using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Pixora.Avalonia.Services;

/// <summary>
/// Connects to the Pixora.Agent named pipe and surfaces progress events
/// so the main UI can show live activity from the background agent.
/// </summary>
public sealed class AgentIpcClient : IAsyncDisposable
{
    public const string PipeName = "pixora-agent";

    private readonly ILogger<AgentIpcClient> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _readTask;

    /// <summary>Raised on the UI thread when the agent sends any message.</summary>
    public event EventHandler<AgentMessage>? MessageReceived;

    /// <summary>True while the named pipe is connected to the agent.</summary>
    public bool IsConnected { get; private set; }

    public AgentIpcClient(ILogger<AgentIpcClient> logger)
    {
        _logger = logger;
    }

    /// <summary>Starts the background reconnect + read loop.</summary>
    public void Start()
    {
        _readTask = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.In, PipeOptions.Asynchronous);

                _logger.LogDebug("IPC client connecting to agent pipe...");
                await pipe.ConnectAsync(3000, ct).ConfigureAwait(false);
                IsConnected = true;
                _logger.LogInformation("IPC client connected to Pixora Agent");

                using var reader = new StreamReader(pipe);
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var msg = JsonSerializer.Deserialize<AgentMessage>(line);
                        if (msg is not null)
                            Dispatcher.UIThread.Post(() => MessageReceived?.Invoke(this, msg));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Malformed IPC message ignored");
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IPC client disconnected, retrying in 10s");
            }
            finally
            {
                IsConnected = false;
            }

            // Back-off before reconnecting — only retry if agent is likely running
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_readTask is not null)
        {
            try { await _readTask.ConfigureAwait(false); } catch { }
        }
        _cts.Dispose();
    }
}

/// <summary>A message received from the background agent over the IPC pipe.</summary>
public sealed class AgentMessage
{
    public string Event { get; init; } = "";
    public string? ScheduleName { get; init; }
    public string? JobId { get; init; }
    public int? Progress { get; init; }
    public string? StatusText { get; init; }
    public long TimestampUtc { get; init; }
}
