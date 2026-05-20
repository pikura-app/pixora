using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pixora.Agent;

/// <summary>
/// Lightweight named-pipe server. Accepts connections from the main Pixora UI
/// and broadcasts JSON-line progress messages to all connected clients.
/// Pipe name: "pixora-agent" on Windows, "/tmp/pixora-agent" socket on Unix.
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    public const string PipeName = "pixora-agent";

    private readonly ILogger<IpcServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<StreamWriter> _clients = new();
    private readonly Lock _lock = new();

    public IpcServer(ILogger<IpcServer> logger)
    {
        _logger = logger;
    }

    /// <summary>Starts accepting pipe connections in the background.</summary>
    public void Start()
    {
        _ = AcceptLoopAsync(_cts.Token);
        _logger.LogInformation("IPC server listening on pipe '{Pipe}'", PipeName);
    }

    /// <summary>Broadcast a message to every connected UI client.</summary>
    public void Broadcast(IpcMessage message)
    {
        var json = JsonSerializer.Serialize(message) + "\n";
        List<StreamWriter> dead = new();

        lock (_lock)
        {
            foreach (var w in _clients)
            {
                try { w.Write(json); w.Flush(); }
                catch { dead.Add(w); }
            }
            foreach (var d in dead) _clients.Remove(d);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _logger.LogDebug("IPC client connected");

                var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: false) { AutoFlush = false };
                lock (_lock) _clients.Add(writer);

                // Fire-and-forget: clean up when the client disconnects
                _ = MonitorDisconnectAsync(pipe, writer, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "IPC accept loop error (retrying)");
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task MonitorDisconnectAsync(NamedPipeServerStream pipe, StreamWriter writer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
                await Task.Delay(500, ct).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            lock (_lock) _clients.Remove(writer);
            try { await writer.DisposeAsync().ConfigureAwait(false); } catch { }
            _logger.LogDebug("IPC client disconnected");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}

/// <summary>A JSON-serialisable progress message sent over the IPC pipe.</summary>
public sealed class IpcMessage
{
    /// <summary>Event type: "schedule_started" | "schedule_completed" | "schedule_failed" | "job_progress" | "heartbeat"</summary>
    public string Event { get; init; } = "";
    public string? ScheduleName { get; init; }
    public string? JobId { get; init; }
    public int? Progress { get; init; }
    public string? StatusText { get; init; }
    public long TimestampUtc { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
