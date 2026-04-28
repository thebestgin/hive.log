using System.Diagnostics.Metrics;

namespace HiveLog.Api.Features.Ingest;

/// <summary>
/// Ingest metrics using System.Diagnostics.Metrics (native .NET 8+, OTel-compatible).
/// </summary>
public sealed class IngestMetrics
{
    private readonly Counter<long> _droppedCounter;
    private readonly Counter<long> _flushedCounter;

    public IngestMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("HiveLog.Ingest");
        _droppedCounter = meter.CreateCounter<long>(
            "hivelog_dropped_total",
            unit: "entries",
            description: "Entries dropped due to full buffer or write failure");
        _flushedCounter = meter.CreateCounter<long>(
            "hivelog_flushed_total",
            unit: "entries",
            description: "Entries successfully flushed to database");
    }

    public void RecordDropped(int count = 1) => _droppedCounter.Add(count);
    public void RecordFlushed(int count) => _flushedCounter.Add(count);
}
