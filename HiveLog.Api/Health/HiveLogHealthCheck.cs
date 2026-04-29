using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Stream;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiveLog.Api.Health;

/// <summary>
/// Custom health check that exposes HiveLog-specific metrics in the response:
///   bufferDepth      — current entries waiting in the ingest buffer
///   droppedTotal     — entries dropped since server start
///   subscriberCount  — active SSE stream subscribers
///   workerAlive      — whether the ingest background worker is flushing
///
/// Worker-Liveness: Degraded when buffer is non-empty AND no flush has occurred
/// for more than 60 seconds (indicates the background worker has stalled).
/// </summary>
public sealed class HiveLogHealthCheck : IHealthCheck
{
    private static readonly TimeSpan WorkerStallThreshold = TimeSpan.FromSeconds(60);

    private readonly IngestBuffer _buffer;
    private readonly IngestMetrics _metrics;
    private readonly StreamBroadcaster _broadcaster;

    public HiveLogHealthCheck(IngestBuffer buffer, IngestMetrics metrics, StreamBroadcaster broadcaster)
    {
        _buffer = buffer;
        _metrics = metrics;
        _broadcaster = broadcaster;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var bufferDepth = _buffer.Count;
        var lastFlushAt = _metrics.LastFlushAt;

        // Worker is considered stalled when entries are sitting in the buffer
        // but no flush has happened within the threshold.
        var workerStalled = bufferDepth > 0
            && lastFlushAt is not null
            && DateTimeOffset.UtcNow - lastFlushAt.Value > WorkerStallThreshold;

        var data = new Dictionary<string, object>
        {
            ["bufferDepth"] = bufferDepth,
            ["droppedTotal"] = _metrics.DroppedTotal,
            ["subscriberCount"] = _broadcaster.SubscriberCount,
            ["lastFlushAt"] = lastFlushAt?.ToString("O") ?? "never",
            ["workerAlive"] = !workerStalled,
        };

        if (workerStalled)
            return Task.FromResult(HealthCheckResult.Degraded(
                "Ingest worker appears stalled — buffer non-empty but no flush in 60s", data: data));

        return Task.FromResult(HealthCheckResult.Healthy(data: data));
    }
}
