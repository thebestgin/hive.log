using System.Diagnostics;
using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Ingest.Models;
using HiveLog.Api.Features.Logs.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Connectors.BackendServices;

// BackendServices-Connector: ingest endpoint for all .NET backend services.
// Isolates this connector's OpenAPI spec under the "backend-services" group so
// NSwag can generate a typed C# client from swagger.backend-services.json.
[ApiController]
[Route("api/hivelog/v1/connectors/backend-services")]
[ApiExplorerSettings(GroupName = "backend-services")]
[ServiceFilter(typeof(IngestApiKeyFilter))]
public class BackendServicesConnectorController : ControllerBase
{
    private readonly IngestBuffer _buffer;
    private readonly IngestMetrics _metrics;
    private readonly SelfLogger _selfLogger;
    private readonly IngestOptions _opts;

    public BackendServicesConnectorController(
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
    /// Ingest a batch of log entries from a backend service.
    /// Returns 202 Accepted with count of accepted entries.
    /// Returns 400 on validation error.
    /// Returns 503 if buffer is full (backpressure).
    /// </summary>
    [HttpPost("ingest")]
    [ProducesResponseType<IngestResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var sw = Stopwatch.StartNew();
        _metrics.RecordIngestRequest();

        int accepted = 0;
        foreach (var dto in request.Entries)
        {
            var entry = MapToEntity(dto, request);

            var written = await _buffer.TryWriteAsync(entry, _opts.WriteTimeout, ct);
            if (!written)
            {
                // Buffer full — drop remaining entries and report 503
                var dropped = request.Entries.Count - accepted;
                _metrics.RecordDropped(dropped);

                _selfLogger.Warn(
                    "BackendServices ingest buffer full — entries dropped",
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

    private static LogEntry MapToEntity(LogEntryDto dto, IngestRequest request) => new()
    {
        Timestamp = dto.Timestamp,
        Id = dto.Id ?? Guid.NewGuid(),
        TraceId = dto.TraceId,
        SpanId = dto.SpanId,
        ParentSpanId = dto.ParentSpanId,
        Source = request.Source,
        SourceType = request.SourceType,
        InstanceId = request.InstanceId,
        Level = dto.Level,
        Category = dto.Category,
        Message = dto.Message,
        MessageTemplate = dto.MessageTemplate,
        Properties = dto.Properties,
        Exception = dto.Exception,
        UserId = dto.UserId,   // caller-supplied — trusted because X-Api-Key authenticated the service
        RequestId = dto.RequestId,
        SessionId = dto.SessionId,
        Tags = dto.Tags,
        Stream = dto.Stream,
        IsAuthenticated = false, // API-key auth identifies the service, not a user session
    };
}
