using HiveLog.Api.Features.Logs.Models;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Ingest;

/// <summary>
/// Background service that flushes the IngestBuffer to PostgreSQL using 3-Trigger-OR:
/// - Idle: no new items for BatchIdleAfter (5ms default)
/// - Full: batch reaches BatchMaxSize (1000 default)
/// - Cap: BatchWindow elapsed since first item (25ms default)
///
/// Pattern based on HiveCache BatchFlushService.
/// </summary>
public sealed class IngestBackgroundService : BackgroundService
{
    private readonly IngestBuffer _buffer;
    private readonly LogEntryCopyWriter _writer;
    private readonly IngestMetrics _metrics;
    private readonly IngestOptions _opts;
    private readonly ILogger<IngestBackgroundService> _logger;

    public IngestBackgroundService(
        IngestBuffer buffer,
        LogEntryCopyWriter writer,
        IngestMetrics metrics,
        IOptions<IngestOptions> opts,
        ILogger<IngestBackgroundService> logger)
    {
        _buffer = buffer;
        _writer = writer;
        _metrics = metrics;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var batch = new List<LogEntry>(_opts.BatchMaxSize);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait for at least one item to arrive
                if (!await _buffer.WaitToReadAsync(ct)) break;

                batch.Clear();
                _buffer.DrainTo(batch, _opts.BatchMaxSize);

                // Batch already full — flush immediately without waiting for more
                if (batch.Count >= _opts.BatchMaxSize)
                {
                    await FlushBatchAsync(batch, ct);
                    continue;
                }

                // Adaptive window: keep collecting until idle OR cap OR full
                await CollectBatchAdaptiveAsync(batch, ct);
                await FlushBatchAsync(batch, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HiveLog] IngestBackgroundService flush error");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Collects more items using the adaptive window strategy:
    /// - Idle flush: fires after BatchIdleAfter of silence (5ms)
    /// - Full flush: fires immediately when batch reaches BatchMaxSize (1000)
    /// - Cap flush: fires after BatchWindow regardless of activity (25ms)
    /// </summary>
    private async Task CollectBatchAdaptiveAsync(List<LogEntry> batch, CancellationToken ct)
    {
        var maxDeadline = DateTime.UtcNow.Add(_opts.BatchWindow);
        var idleMs = (int)_opts.BatchIdleAfter.TotalMilliseconds;

        while (batch.Count < _opts.BatchMaxSize)
        {
            var now = DateTime.UtcNow;
            if (now >= maxDeadline) break; // hard cap hit

            var remainingMax = (int)(maxDeadline - now).TotalMilliseconds;
            var waitMs = Math.Min(idleMs, remainingMax);
            if (waitMs <= 0) break;

            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idleCts.CancelAfter(waitMs);

            bool gotItem;
            try
            {
                gotItem = await _buffer.WaitToReadAsync(idleCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Idle timeout expired — no new items arrived, flush what we have
                break;
            }

            if (!gotItem) break; // channel completed

            // New items arrived — drain and reset idle timer (stay in loop)
            _buffer.DrainTo(batch, _opts.BatchMaxSize - batch.Count);
        }
    }

    private async Task FlushBatchAsync(List<LogEntry> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        try
        {
            await _writer.WriteBatchAsync(batch, ct);
            _metrics.RecordFlushed(batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HiveLog] Batch write failed ({Count} entries)", batch.Count);
            _metrics.RecordDropped(batch.Count);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        // Signal no more writes to the channel
        _buffer.Complete();

        // Drain remaining items before stopping (max DrainTimeout = 5s)
        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        drainCts.CancelAfter(_opts.DrainTimeout);

        var remaining = new List<LogEntry>(_opts.BatchMaxSize);
        try
        {
            while (_buffer.DrainTo(remaining, _opts.BatchMaxSize) > 0)
            {
                await FlushBatchAsync(remaining, drainCts.Token);
                remaining.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[HiveLog] Drain timeout — some buffered entries may be lost");
        }

        await base.StopAsync(ct);
    }
}
