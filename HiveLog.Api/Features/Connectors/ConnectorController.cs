using System.Diagnostics;
using HiveLog.Api.Features.Connectors.Manifest;
using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Ingest.Models;
using HiveLog.Api.Features.Logs.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Connectors;

/// <summary>
/// Generic connector ingest endpoint. Replaces BackendServicesConnectorController
/// and WebAppConnectorController. The connector ID in the route determines auth rules,
/// source/sourceType defaults, and trust level — all driven by hivelog-manifest.json.
/// </summary>
[ApiController]
[Route("api/hivelog/v1/connectors/{connectorId}")]
[ServiceFilter(typeof(ConnectorAuthFilter))]
public class ConnectorController : ControllerBase
{
    private readonly IngestBuffer _buffer;
    private readonly IngestMetrics _metrics;
    private readonly SelfLogger _selfLogger;
    private readonly IngestOptions _opts;

    public ConnectorController(
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

    /// <summary>
    /// Ingest a batch of log entries through a manifest-defined connector.
    /// source and sourceType are set by the manifest — not by the caller.
    /// Auth is validated per connector type (apiKey, jwt, none).
    /// Returns 202 Accepted, 400 on validation error, 401 on auth failure,
    /// 404 for unknown connector, 503 if buffer is full.
    /// </summary>
    [HttpPost("ingest")]
    [ProducesResponseType<IngestResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ingest(
        [FromRoute] string connectorId,
        [FromBody] ConnectorIngestRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var connector = (ConnectorDefinition)HttpContext.Items["Connector"]!;

        // JWT connectors: UserId + IsAuthenticated come from the validated token (server-side).
        // apiKey/none connectors: IsAuthenticated = false, UserId from request body (not trusted).
        bool isJwtConnector = string.Equals(connector.Auth.Type, "jwt", StringComparison.OrdinalIgnoreCase);
        bool isAuthenticated = isJwtConnector && User.Identity?.IsAuthenticated == true;
        Guid? serverUserId = null;

        if (isAuthenticated)
        {
            var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(sub, out var parsedUserId))
                serverUserId = parsedUserId;
        }

        var sw = Stopwatch.StartNew();
        _metrics.RecordIngestRequest();

        int accepted = 0;
        foreach (var dto in request.Entries)
        {
            var entry = new LogEntry
            {
                Timestamp = dto.Timestamp,
                Id = dto.Id ?? Guid.NewGuid(),
                TraceId = dto.TraceId,
                SpanId = dto.SpanId,
                ParentSpanId = dto.ParentSpanId,
                Source = connector.Source,
                SourceType = connector.SourceType,
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

            var written = await _buffer.TryWriteAsync(entry, _opts.WriteTimeout, ct);
            if (!written)
            {
                var dropped = request.Entries.Count - accepted;
                _metrics.RecordDropped(dropped);

                _selfLogger.Warn(
                    $"Connector '{connectorId}' ingest buffer full — entries dropped",
                    new { droppedCount = dropped, accepted, connectorId });

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
}
