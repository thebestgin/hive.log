using HiveLog.Client.Generated;
using HiveLog.Client.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiveLog.Client;

/// <summary>
/// BackgroundService that drains the HiveLogBatchBuffer and sends batches to the HiveLog server.
/// Uses 3-Trigger-OR flush (Window / IdleAfter / MaxSize).
/// On failure: Exponential Backoff (1s, 2s, 4s), then Silent Drop.
/// On shutdown: Drain remaining entries (up to 5s timeout).
/// Sends to the BackendServices connector endpoint via the NSwag-generated HiveLogBackendClient.
/// </summary>
internal sealed class HiveLogSenderService : BackgroundService
{
    private readonly HiveLogBatchBuffer _buffer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly HiveLogClientMetrics _metrics;
    private readonly HiveLogOptions _opts;
    private readonly ILogger<HiveLogSenderService> _logger;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    public HiveLogSenderService(
        HiveLogBatchBuffer buffer,
        IHttpClientFactory httpClientFactory,
        HiveLogClientMetrics metrics,
        IOptions<HiveLogOptions> opts,
        ILogger<HiveLogSenderService> logger)
    {
        _buffer = buffer;
        _httpClientFactory = httpClientFactory;
        _metrics = metrics;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var batch = new List<ClientLogEntry>(_opts.Buffer.MaxSize);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait for at least one item to arrive
                if (!await _buffer.WaitToReadAsync(ct)) break;

                batch.Clear();
                _buffer.DrainTo(batch, _opts.Buffer.MaxSize);

                // Already full — flush immediately without waiting for more
                if (batch.Count >= _opts.Buffer.MaxSize)
                {
                    await SendBatchWithRetryAsync(batch, ct);
                    continue;
                }

                // Adaptive window: keep collecting until idle OR cap OR full
                await CollectBatchAdaptiveAsync(batch, ct);
                await SendBatchWithRetryAsync(batch, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HiveLog] SenderService unexpected error");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Collects more items using the adaptive window strategy:
    /// - Idle: no new items for IdleAfter (3ms)
    /// - Full: batch reaches MaxSize (200)
    /// - Cap: Window elapsed (10ms)
    /// </summary>
    private async Task CollectBatchAdaptiveAsync(List<ClientLogEntry> batch, CancellationToken ct)
    {
        var maxDeadline = DateTime.UtcNow.Add(_opts.Buffer.Window);
        var idleMs = (int)_opts.Buffer.IdleAfter.TotalMilliseconds;

        while (batch.Count < _opts.Buffer.MaxSize)
        {
            var now = DateTime.UtcNow;
            if (now >= maxDeadline) break;

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
                // Idle timeout — flush what we have
                break;
            }

            if (!gotItem) break;

            _buffer.DrainTo(batch, _opts.Buffer.MaxSize - batch.Count);
        }
    }

    private async Task SendBatchWithRetryAsync(List<ClientLogEntry> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        // Map internal buffer entries to generated DTO types
        var request = new IngestRequest
        {
            Source = _opts.Source,
            SourceType = _opts.SourceType,
            InstanceId = _opts.InstanceId ?? Environment.MachineName,
            Entries = batch.Select(MapToDto).ToList(),
        };

        for (int attempt = 0; attempt < RetryDelays.Length; attempt++)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("hivelog");
                var client = new HiveLogBackendClient(_opts.BaseUrl, httpClient);
                var result = await client.IngestAsync(request, ct);

                _metrics.RecordSent(batch.Count);
                return;
            }
            catch (HiveLogBackendClientException ex) when (ex.StatusCode == 503 && attempt < RetryDelays.Length - 1)
            {
                // Server backpressure — retry with backoff
                _metrics.RecordRetry();
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (HiveLogBackendClientException ex)
            {
                // Other non-success (4xx) — no point retrying
                _logger.LogWarning("[HiveLog] Ingest returned {StatusCode}, dropping {Count} entries",
                    ex.StatusCode, batch.Count);
                _metrics.RecordDropped(batch.Count);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt < RetryDelays.Length - 1)
                {
                    _metrics.RecordRetry();
                    await Task.Delay(RetryDelays[attempt], ct);
                }
                else
                {
                    // Permanent failure — silent drop
                    _logger.LogWarning("[HiveLog] Send failed after {Attempts} attempts, dropping {Count} entries",
                        RetryDelays.Length, batch.Count);
                    _metrics.RecordDropped(batch.Count);
                }
            }
        }
    }

    private static LogEntryDto MapToDto(ClientLogEntry entry) => new()
    {
        Timestamp = entry.Timestamp,
        TraceId = entry.TraceId,
        SpanId = entry.SpanId,
        ParentSpanId = entry.ParentSpanId,
        Level = entry.Level,
        Category = entry.Category,
        Message = entry.Message,
        MessageTemplate = entry.MessageTemplate,
        Properties = entry.Properties,
        Exception = entry.Exception,
        UserId = Guid.TryParse(entry.UserId, out var userId) ? userId : null,
        RequestId = entry.RequestId,
        SessionId = entry.SessionId,
        Tags = entry.Tags,
        Stream = entry.Stream,
    };

    public override async Task StopAsync(CancellationToken ct)
    {
        _buffer.Complete();

        // Drain remaining entries before stopping (max 5s)
        using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var remaining = new List<ClientLogEntry>(_opts.Buffer.MaxSize);
        try
        {
            while (_buffer.DrainTo(remaining, _opts.Buffer.MaxSize) > 0)
            {
                await SendBatchWithRetryAsync(remaining, drainCts.Token);
                remaining.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[HiveLog] Drain timeout on shutdown — some buffered entries may be lost");
        }

        await base.StopAsync(ct);
    }
}
