using System.Threading.Channels;
using HiveLog.Api.Features.Logs.Models;

namespace HiveLog.Api.Features.Ingest;

/// <summary>
/// Bounded Channel buffer for incoming log entries.
/// Capacity: 10.000. Full channel + 100ms timeout → 503 Service Unavailable.
/// </summary>
public sealed class IngestBuffer
{
    private readonly Channel<LogEntry> _channel;

    public IngestBuffer(int capacity = 10_000)
    {
        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // Backpressure statt Drop
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>
    /// Writes a log entry to the buffer.
    /// Returns false if the channel is full and the timeout expires.
    /// </summary>
    public async ValueTask<bool> TryWriteAsync(LogEntry entry, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Writer.WriteAsync(entry, cts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout — channel was full for the entire timeout window
            return false;
        }
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken ct) =>
        _channel.Reader.WaitToReadAsync(ct);

    /// <summary>Drain up to max pending entries into batch.</summary>
    public int DrainTo(List<LogEntry> batch, int max)
    {
        int count = 0;
        while (count < max && _channel.Reader.TryRead(out var entry))
        {
            batch.Add(entry!);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Synchronous non-blocking write for internal self-logging.
    /// Returns false if the buffer is currently full — no wait, no exception.
    /// </summary>
    public bool TryWriteSync(LogEntry entry) => _channel.Writer.TryWrite(entry);

    /// <summary>Current number of items waiting in the buffer.</summary>
    public int Count => _channel.Reader.Count;

    public void Complete() => _channel.Writer.Complete();
}
