using System.Threading.Channels;
using HiveLog.Client.Models;

namespace HiveLog.Client;

/// <summary>
/// Thread-safe bounded channel buffer for outgoing log entries.
/// Uses DropOldest — fire-and-forget, ILogger.Log() never blocks.
/// </summary>
internal sealed class HiveLogBatchBuffer
{
    private readonly Channel<ClientLogEntry> _channel;

    internal HiveLogBatchBuffer(int capacity)
    {
        _channel = Channel.CreateBounded<ClientLogEntry>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Fire-and-forget write. Drops oldest entry if channel is full.</summary>
    internal bool TryWrite(ClientLogEntry entry) => _channel.Writer.TryWrite(entry);

    internal ValueTask<bool> WaitToReadAsync(CancellationToken ct) =>
        _channel.Reader.WaitToReadAsync(ct);

    /// <summary>Drain up to max pending entries into batch.</summary>
    internal int DrainTo(List<ClientLogEntry> batch, int max)
    {
        int count = 0;
        while (count < max && _channel.Reader.TryRead(out var entry))
        {
            batch.Add(entry!);
            count++;
        }
        return count;
    }

    internal void Complete() => _channel.Writer.Complete();
}
