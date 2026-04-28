using System.Diagnostics.Metrics;

namespace HiveLog.Api.Features.Ingest;

/// <summary>
/// HiveLog server-side metrics using System.Diagnostics.Metrics (native .NET 8+, OTel-compatible).
///
/// Instruments:
///   hivelog_ingest_total          — HTTP requests to /ingest
///   hivelog_ingest_entries_total  — log entries accepted
///   hivelog_ingest_latency_ms     — latency per ingest request (histogram)
///   hivelog_buffer_depth          — current buffer fill level (observable gauge)
///   hivelog_dropped_total         — entries dropped due to full buffer
///   hivelog_query_latency_ms      — latency per query request (histogram)
/// </summary>
public sealed class IngestMetrics
{
    private readonly Counter<long> _ingestTotal;
    private readonly Counter<long> _ingestEntriesTotal;
    private readonly Histogram<double> _ingestLatencyMs;
    private readonly Counter<long> _droppedTotal;
    private readonly Counter<long> _flushedTotal;
    private readonly Histogram<double> _queryLatencyMs;

    private long _droppedCount;

    public IngestMetrics(IMeterFactory meterFactory, IngestBuffer buffer)
    {
        var meter = meterFactory.Create("HiveLog", "1.0.0");

        _ingestTotal = meter.CreateCounter<long>(
            "hivelog_ingest_total",
            unit: "requests",
            description: "HTTP requests received at /ingest");

        _ingestEntriesTotal = meter.CreateCounter<long>(
            "hivelog_ingest_entries_total",
            unit: "entries",
            description: "Log entries accepted into the ingest pipeline");

        _ingestLatencyMs = meter.CreateHistogram<double>(
            "hivelog_ingest_latency_ms",
            unit: "ms",
            description: "Latency of ingest HTTP requests in milliseconds");

        meter.CreateObservableGauge(
            "hivelog_buffer_depth",
            () => buffer.Count,
            unit: "entries",
            description: "Current number of log entries waiting in the ingest buffer");

        _droppedTotal = meter.CreateCounter<long>(
            "hivelog_dropped_total",
            unit: "entries",
            description: "Log entries dropped due to full buffer or flush failure");

        _flushedTotal = meter.CreateCounter<long>(
            "hivelog_flushed_total",
            unit: "entries",
            description: "Log entries successfully flushed to the database");

        _queryLatencyMs = meter.CreateHistogram<double>(
            "hivelog_query_latency_ms",
            unit: "ms",
            description: "Latency of query HTTP requests in milliseconds");
    }

    public void RecordIngestRequest() => _ingestTotal.Add(1);

    public void RecordIngestEntries(int count) => _ingestEntriesTotal.Add(count);

    public void RecordIngestLatency(double ms) => _ingestLatencyMs.Record(ms);

    public void RecordDropped(int count)
    {
        Interlocked.Add(ref _droppedCount, count);
        _droppedTotal.Add(count);
    }

    /// <summary>Record entries successfully flushed to the database.</summary>
    public void RecordFlushed(int count) => _flushedTotal.Add(count);

    public void RecordQueryLatency(double ms) => _queryLatencyMs.Record(ms);

    /// <summary>Total dropped entries since server start — exposed to /health.</summary>
    public long DroppedTotal => Interlocked.Read(ref _droppedCount);
}
