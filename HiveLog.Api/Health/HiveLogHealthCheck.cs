using HiveLog.Api.Features.Ingest;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HiveLog.Api.Health;

/// <summary>
/// Custom health check that exposes HiveLog-specific metrics in the response:
///   bufferDepth   — current entries waiting in the ingest buffer
///   droppedTotal  — entries dropped since server start
/// </summary>
public sealed class HiveLogHealthCheck : IHealthCheck
{
    private readonly IngestBuffer _buffer;
    private readonly IngestMetrics _metrics;

    public HiveLogHealthCheck(IngestBuffer buffer, IngestMetrics metrics)
    {
        _buffer = buffer;
        _metrics = metrics;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["bufferDepth"] = _buffer.Count,
            ["droppedTotal"] = _metrics.DroppedTotal,
        };

        return Task.FromResult(HealthCheckResult.Healthy(data: data));
    }
}
