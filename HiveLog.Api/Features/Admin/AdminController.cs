using System.Diagnostics;
using HiveLog.Api.Features.Admin.Models;
using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Stream;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace HiveLog.Api.Features.Admin;

[ApiController]
[Route("api/hivelog/v1/admin")]
[ServiceFilter(typeof(AdminApiKeyFilter))]
public sealed class AdminController : ControllerBase
{
    private readonly IngestBackgroundService _ingestService;
    private readonly IngestBuffer _buffer;
    private readonly IngestMetrics _metrics;
    private readonly StreamBroadcaster _broadcaster;
    private readonly RuntimeRetentionService _retention;
    private readonly NpgsqlDataSource _dataSource;

    public AdminController(
        IngestBackgroundService ingestService,
        IngestBuffer buffer,
        IngestMetrics metrics,
        StreamBroadcaster broadcaster,
        RuntimeRetentionService retention,
        NpgsqlDataSource dataSource)
    {
        _ingestService = ingestService;
        _buffer = buffer;
        _metrics = metrics;
        _broadcaster = broadcaster;
        _retention = retention;
        _dataSource = dataSource;
    }

    /// <summary>
    /// Forces an immediate flush of the ingest buffer to the database.
    /// Waits up to 5 seconds for the flush to complete before responding.
    /// </summary>
    [HttpPost("flush")]
    [ProducesResponseType<FlushResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Flush(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var flushed = await _ingestService.ForceFlushAsync(TimeSpan.FromSeconds(5));
        sw.Stop();

        return Ok(new FlushResponse(
            EntriesFlushed: flushed < 0 ? 0 : flushed,
            ElapsedMs: Math.Round(sw.Elapsed.TotalMilliseconds, 2)));
    }

    /// <summary>
    /// Returns current system statistics: buffer depth, ingest rate, dropped total,
    /// active SSE subscribers, and TimescaleDB chunk count.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType<StatsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var chunkCount = await GetChunkCountAsync(ct);

        return Ok(new StatsResponse(
            BufferDepth: _buffer.Count,
            IngestRatePerSecond: Math.Round(_metrics.GetRatePerSecond(), 2),
            DroppedTotal: _metrics.DroppedTotal,
            ActiveSubscribers: _broadcaster.SubscriberCount,
            ChunkCount: chunkCount));
    }

    /// <summary>
    /// Updates retention policies at runtime without requiring a server restart.
    /// </summary>
    [HttpPost("retention")]
    [ProducesResponseType<RetentionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Retention([FromBody] RetentionRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        _retention.Update(
            appDays: request.RetentionDays.App,
            agentDays: request.RetentionDays.Agent,
            auditDays: request.RetentionDays.Audit);

        return Ok(new RetentionResponse(
            AppDays: _retention.AppDays,
            AgentDays: _retention.AgentDays,
            AuditDays: _retention.AuditDays));
    }

    private async Task<long> GetChunkCountAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();

            // Check whether the timescaledb_information schema exists before querying it.
            // This avoids an error when TimescaleDB is not installed.
            cmd.CommandText = """
                SELECT COUNT(*)::bigint
                FROM information_schema.tables
                WHERE table_schema = 'timescaledb_information'
                  AND table_name = 'chunks';
                """;

            var schemaExists = (long)(await cmd.ExecuteScalarAsync(ct))!;
            if (schemaExists == 0) return 0;

            cmd.CommandText = """
                SELECT COUNT(*)::bigint
                FROM timescaledb_information.chunks
                WHERE hypertable_name = 'log_entries';
                """;

            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        }
        catch
        {
            return 0;
        }
    }
}
