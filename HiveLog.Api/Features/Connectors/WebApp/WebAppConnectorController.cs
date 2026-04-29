using System.Diagnostics;
using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Ingest.Models;
using HiveLog.Api.Features.Logs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Connectors.WebApp;

[ApiController]
[Route("api/hivelog/v1/connectors/webapp")]
[ApiExplorerSettings(GroupName = "webapp")]
public class WebAppConnectorController : ControllerBase
{
    private const string Source = "webapp";
    private const string SourceType = "frontend";

    private readonly IngestBuffer _buffer;
    private readonly IngestMetrics _metrics;
    private readonly SelfLogger _selfLogger;
    private readonly IngestOptions _opts;

    public WebAppConnectorController(
        IngestBuffer buffer,
        IngestMetrics metrics,
        SelfLogger selfLogger,
        IOptions<IngestOptions> opts)
    {
        _buffer = buffer;
        _metrics = metrics;
        _selfLogger = selfLogger;
        _opts = opts.Value;
    }

    [HttpPost("ingest")]
    [AllowAnonymous]
    [ProducesResponseType<IngestResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ingest([FromBody] WebAppLogRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var sw = Stopwatch.StartNew();
        _metrics.RecordIngestRequest();

        // Optionale JWT-Authentifizierung: wenn gültiges Token vorhanden, UserId server-seitig setzen
        bool isAuthenticated = User.Identity?.IsAuthenticated == true;
        Guid? serverUserId = null;
        if (isAuthenticated)
        {
            var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(sub, out var parsedUserId))
                serverUserId = parsedUserId;
        }

        int accepted = 0;
        foreach (var dto in request.Entries)
        {
            var entry = MapToEntity(dto, request, isAuthenticated, serverUserId);

            var written = await _buffer.TryWriteAsync(entry, _opts.WriteTimeout, ct);
            if (!written)
            {
                var dropped = request.Entries.Count - accepted;
                _metrics.RecordDropped(dropped);

                _selfLogger.Warn(
                    "WebApp ingest buffer full — entries dropped",
                    new { droppedCount = dropped, accepted });

                sw.Stop();
                _metrics.RecordIngestLatency(sw.Elapsed.TotalMilliseconds);

                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "Buffer full", accepted, dropped });
            }

            accepted++;
        }

        _metrics.RecordIngestEntries(accepted);

        sw.Stop();
        _metrics.RecordIngestLatency(sw.Elapsed.TotalMilliseconds);

        return Accepted(new IngestResponse { Accepted = accepted });
    }

    private static LogEntry MapToEntity(LogEntryDto dto, WebAppLogRequest request, bool isAuthenticated, Guid? serverUserId) => new()
    {
        Timestamp = dto.Timestamp,
        Id = dto.Id ?? Guid.NewGuid(),
        TraceId = dto.TraceId,
        SpanId = dto.SpanId,
        Source = Source,
        SourceType = SourceType,
        InstanceId = request.InstanceId,
        Level = dto.Level,
        Category = dto.Category,
        Message = dto.Message,
        MessageTemplate = dto.MessageTemplate,
        Properties = dto.Properties,
        Exception = dto.Exception,
        UserId = isAuthenticated ? serverUserId : dto.UserId,
        RequestId = dto.RequestId,
        SessionId = dto.SessionId,
        Tags = dto.Tags,
        Stream = dto.Stream,
        IsAuthenticated = isAuthenticated,
    };
}
