using System.Threading.Channels;
using HiveLog.Api.Features.Logs.Models;

namespace HiveLog.Api.Features.Stream;

/// <summary>
/// Represents an active SSE subscription. Dispose removes the subscriber from the broadcaster.
/// </summary>
public sealed class StreamSubscription : IDisposable
{
    private readonly Action<Guid> _onDispose;
    private bool _disposed;

    public Guid Id { get; }
    public ChannelReader<LogEntry[]> Reader { get; }

    internal StreamSubscription(Guid id, ChannelReader<LogEntry[]> reader, Action<Guid> onDispose)
    {
        Id = id;
        Reader = reader;
        _onDispose = onDispose;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _onDispose(Id);
    }
}
