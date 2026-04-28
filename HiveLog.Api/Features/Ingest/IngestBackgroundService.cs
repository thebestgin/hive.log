using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Features.Stream;
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
    private readonly SelfLogger _selfLogger;
    private readonly StreamBroadcaster _broadcaster;
    private readonly IngestOptions _opts;
    private readonly ILogger<IngestBackgroundService> _logger;

    // Force-flush support for /admin/flush endpoint
    private volatile TaskCompletionSource<int>? _flushCompletion;
    private readonly SemaphoreSlim _forceFlushSignal = new(0, 1);

    public IngestBackgroundService(
        IngestBuffer buffer,
        LogEntryCopyWriter writer,
        IngestMetrics metrics,
        SelfLogger selfLogger,
        StreamBroadcaster broadcaster,
        IOptions<IngestOptions> opts,
        ILogger<IngestBackgroundService> logger)
    {
        _buffer = buffer;
        _writer = writer;
        _metrics = metrics;
        _selfLogger = selfLogger;
        _broadcaster = broadcaster;
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
                // Check if a force-flush was requested (non-blocking)
                if (_forceFlushSignal.CurrentCount > 0)
                {
                    _forceFlushSignal.Wait(0); // consume the signal
                    await DrainAndSignalForceFlushAsync(batch, ct);
                    continue;
                }

                // Wait for at least one item to arrive
                if (!await _buffer.WaitToReadAsync(ct)) break;

                batch.Clear();
                _buffer.DrainTo(batch, _opts.BatchMaxSize);

                // Batch already full — flush immediately without waiting for more
                if (batch.Count >= _opts.BatchMaxSize)
                {
                    await FlushBatchAsync(batch, ct);
                    // Check if force-flush is waiting after this flush
                    if (_flushCompletion is not null)
                        await DrainAndSignalForceFlushAsync(batch, ct);
                    continue;
                }

                // Adaptive window: keep collecting until idle OR cap OR full
                await CollectBatchAdaptiveAsync(batch, ct);
                await FlushBatchAsync(batch, ct);

                // Check if force-flush is waiting after this flush
                if (_flushCompletion is not null)
                    await DrainAndSignalForceFlushAsync(batch, ct);
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
    /// Drains the remaining buffer and signals a pending ForceFlushAsync caller.
    /// </summary>
    private async Task DrainAndSignalForceFlushAsync(List<LogEntry> batch, CancellationToken ct)
    {
        int totalFlushed = 0;

        // Drain everything remaining in the buffer
        batch.Clear();
        while (_buffer.DrainTo(batch, _opts.BatchMaxSize) > 0)
        {
            totalFlushed += batch.Count;
            await FlushBatchAsync(batch, ct);
            batch.Clear();
        }

        // Signal the waiting caller
        var completion = Interlocked.Exchange(ref _flushCompletion, null);
        completion?.TrySetResult(totalFlushed);
    }

    /// <summary>
    /// Triggers an immediate buffer flush and waits until it completes (up to timeout).
    /// Returns number of entries flushed, or -1 on timeout.
    /// </summary>
    public async Task<int> ForceFlushAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Only one force-flush at a time — if one is already pending, just wait for it
        if (Interlocked.CompareExchange(ref _flushCompletion, tcs, null) != null)
        {
            await Task.Delay(timeout);
            return 0;
        }

        // Signal the background loop to flush immediately
        _forceFlushSignal.Release();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout — clear the pending TCS
            Interlocked.CompareExchange(ref _flushCompletion, null, tcs);
            tcs.TrySetCanceled();
            return -1;
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
            _broadcaster.Publish(batch); // Notify SSE subscribers after successful DB write
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HiveLog] Batch write failed ({Count} entries)", batch.Count);
            _metrics.RecordDropped(batch.Count);

            _selfLogger.Error(
                $"Flush failed: {ex.Message}",
                new { entryCount = batch.Count, exceptionType = ex.GetType().Name });
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
