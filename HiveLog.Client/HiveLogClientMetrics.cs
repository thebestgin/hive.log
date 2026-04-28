using System.Diagnostics.Metrics;

namespace HiveLog.Client;

/// <summary>
/// Client-side metrics using System.Diagnostics.Metrics (OTel-compatible).
/// </summary>
public sealed class HiveLogClientMetrics
{
    private readonly Counter<long> _sentCounter;
    private readonly Counter<long> _droppedCounter;
    private readonly Counter<long> _retryCounter;

    public HiveLogClientMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("HiveLog.Client");
        _sentCounter = meter.CreateCounter<long>(
            "hivelog_client_sent_total",
            unit: "entries",
            description: "Log entries successfully sent to HiveLog server");
        _droppedCounter = meter.CreateCounter<long>(
            "hivelog_client_dropped_total",
            unit: "entries",
            description: "Log entries dropped (buffer overflow or permanent send failure)");
        _retryCounter = meter.CreateCounter<long>(
            "hivelog_client_retry_total",
            unit: "attempts",
            description: "Send retry attempts");
    }

    internal void RecordSent(int count) => _sentCounter.Add(count);
    internal void RecordDropped(int count) => _droppedCounter.Add(count);
    internal void RecordRetry() => _retryCounter.Add(1);
}
