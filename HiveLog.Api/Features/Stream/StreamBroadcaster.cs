using System.Collections.Concurrent;
using System.Threading.Channels;
using HiveLog.Api.Features.Logs.Models;

namespace HiveLog.Api.Features.Stream;

/// <summary>
/// Broadcasts log entries to SSE subscribers, filtered per subscriber's StreamFilter.
/// Each subscriber gets a bounded channel (capacity 256, DropOldest) — a slow consumer
/// cannot block the ingest pipeline.
///
/// Pattern adapted from SyncServer ChangeRevisionNotificationBroadcaster,
/// extended with multi-field filter support (source, level, stream, tags).
/// </summary>
public sealed class StreamBroadcaster
{
    private const int SubscriberChannelCapacity = 256;

    private readonly ConcurrentDictionary<Guid, SubscriberEntry> _subscribers = new();
    private readonly ILogger<StreamBroadcaster> _logger;

    public StreamBroadcaster(ILogger<StreamBroadcaster> logger)
    {
        _logger = logger;
    }

    /// <summary>Current number of active SSE subscribers.</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <summary>
    /// Registers a new subscriber with the given filter.
    /// Returns a StreamSubscription that must be disposed on HTTP disconnect.
    /// </summary>
    public StreamSubscription Subscribe(StreamFilter filter)
    {
        var channel = Channel.CreateBounded<LogEntry[]>(new BoundedChannelOptions(SubscriberChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        var id = Guid.NewGuid();
        _subscribers[id] = new SubscriberEntry(channel, filter);

        _logger.LogDebug("[StreamBroadcaster] Subscriber {Id} registered (total: {Count})", id, _subscribers.Count);

        return new StreamSubscription(id, channel.Reader, RemoveSubscriber);
    }

    /// <summary>
    /// Publishes a batch of log entries to all matching subscribers.
    /// Sync and non-blocking — TryWrite drops entries for slow consumers.
    /// Called from IngestBackgroundService after each DB write.
    /// </summary>
    public void Publish(IReadOnlyList<LogEntry> entries)
    {
        if (entries.Count == 0 || _subscribers.IsEmpty)
            return;

        foreach (var (_, entry) in _subscribers)
        {
            var matching = FilterEntries(entries, entry.Filter);
            if (matching.Length == 0)
                continue;

            // TryWrite — non-blocking. DropOldest policy handles a full channel.
            entry.Channel.Writer.TryWrite(matching);
        }
    }

    private static LogEntry[] FilterEntries(IReadOnlyList<LogEntry> entries, StreamFilter filter)
    {
        // Fast path: no filter at all
        if (filter.Sources is null or { Count: 0 } &&
            filter.Levels is null or { Count: 0 } &&
            filter.Stream is null &&
            filter.Tags is null or { Count: 0 })
        {
            return entries is LogEntry[] arr ? arr : [.. entries];
        }

        var result = new List<LogEntry>(entries.Count);
        foreach (var e in entries)
        {
            if (filter.Matches(e))
                result.Add(e);
        }
        return result.Count == 0 ? [] : [.. result];
    }

    private void RemoveSubscriber(Guid id)
    {
        if (_subscribers.TryRemove(id, out var entry))
        {
            entry.Channel.Writer.TryComplete();
            _logger.LogDebug("[StreamBroadcaster] Subscriber {Id} removed (remaining: {Count})", id, _subscribers.Count);
        }
    }

    private sealed record SubscriberEntry(Channel<LogEntry[]> Channel, StreamFilter Filter);
}
