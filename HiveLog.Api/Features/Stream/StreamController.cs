using System.Text;
using System.Text.Json;
using HiveLog.Api.Features.Logs.Models;
using Microsoft.AspNetCore.Mvc;

namespace HiveLog.Api.Features.Stream;

[ApiController]
[Route("api/hivelog/v1")]
public class StreamController : ControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly StreamBroadcaster _broadcaster;
    private readonly ILogger<StreamController> _logger;

    public StreamController(StreamBroadcaster broadcaster, ILogger<StreamController> logger)
    {
        _broadcaster = broadcaster;
        _logger = logger;
    }

    /// <summary>
    /// Server-Sent Events endpoint for real-time log forwarding.
    /// Streams filtered log entries as they arrive after each DB write.
    /// Query parameters (all optional, comma-separated):
    ///   sources=talents-api,sync-api  — filter by log source
    ///   levels=3,4,5                  — filter by numeric log level (0=Trace..5=Fatal)
    ///   stream=app                    — filter by log stream (app|agent|e2e|audit|perf)
    ///   tags=sync,error               — filter by tags (any match)
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(
        [FromQuery] string? sources,
        [FromQuery] string? levels,
        [FromQuery] string? stream,
        [FromQuery] string? tags,
        CancellationToken ct)
    {
        var filter = ParseFilter(sources, levels, stream, tags);

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.Headers.Append("Connection", "keep-alive");

        // Disable response buffering so events are flushed immediately
        Response.Headers.Remove("Transfer-Encoding");

        await Response.Body.FlushAsync(ct);

        using var subscription = _broadcaster.Subscribe(filter);
        _logger.LogDebug("[StreamController] SSE connection opened (subscriber {Id})", subscription.Id);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeatCts.CancelAfter(HeartbeatInterval);

                bool hasItem;
                try
                {
                    hasItem = await subscription.Reader.WaitToReadAsync(heartbeatCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Heartbeat interval elapsed — no new entries. Send heartbeat.
                    await WriteHeartbeatAsync(ct);
                    continue;
                }

                if (!hasItem)
                    break; // Channel completed (broadcaster shutting down)

                // Drain all available batches
                while (subscription.Reader.TryRead(out var batch))
                {
                    foreach (var entry in batch)
                    {
                        await WriteLogEventAsync(entry, ct);
                    }
                }

                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // HTTP disconnect — normal exit
        }
        finally
        {
            _logger.LogDebug("[StreamController] SSE connection closed (subscriber {Id})", subscription.Id);
            // subscription.Dispose() is called automatically by using statement
        }
    }

    private async Task WriteLogEventAsync(LogEntry entry, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        var eventBytes = Encoding.UTF8.GetBytes($"event: log\ndata: {json}\n\n");
        await Response.Body.WriteAsync(eventBytes, ct);
    }

    private async Task WriteHeartbeatAsync(CancellationToken ct)
    {
        var heartbeatBytes = Encoding.UTF8.GetBytes("event: heartbeat\ndata: \n\n");
        await Response.Body.WriteAsync(heartbeatBytes, ct);
        await Response.Body.FlushAsync(ct);
    }

    private static StreamFilter ParseFilter(
        string? sources,
        string? levels,
        string? stream,
        string? tags)
    {
        var sourcesSet = ParseStringSet(sources);
        var levelsSet = ParseLevelsSet(levels);
        var streamVal = string.IsNullOrWhiteSpace(stream) ? null : stream.Trim();
        var tagsSet = ParseStringSet(tags);

        return new StreamFilter(sourcesSet, levelsSet, streamVal, tagsSet);
    }

    private static HashSet<string>? ParseStringSet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : new HashSet<string>(parts, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<short>? ParseLevelsSet(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var result = new HashSet<short>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (short.TryParse(part, out var level))
                result.Add(level);
        }
        return result.Count == 0 ? null : result;
    }
}
