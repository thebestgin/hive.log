using HiveLog.Api.Features.Ingest.Models;
using HiveLog.Api.Features.Logs.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Ingest;

[ApiController]
[Route("api/hivelog/v1")]
public class IngestController : ControllerBase
{
    private readonly IngestBuffer _buffer;
    private readonly IngestMetrics _metrics;
    private readonly IngestOptions _opts;

    public IngestController(
        IngestBuffer buffer,
        IngestMetrics metrics,
        IOptions<IngestOptions> opts)
    {
        _buffer = buffer;
        _metrics = metrics;
        _opts = opts.Value;
    }

    /// <summary>
    /// Ingest a batch of log entries.
    /// Returns 202 Accepted with count of accepted entries.
    /// Returns 400 on validation error.
    /// Returns 503 if buffer is full (backpressure).
    /// </summary>
    [HttpPost("ingest")]
    [ProducesResponseType<IngestResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        int accepted = 0;
        foreach (var dto in request.Entries)
        {
            var entry = MapToEntity(dto);

            var written = await _buffer.TryWriteAsync(entry, _opts.WriteTimeout, ct);
            if (!written)
            {
                // Buffer full — drop remaining entries and report 503
                var dropped = request.Entries.Count - accepted;
                _metrics.RecordDropped(dropped);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "Buffer full", accepted, dropped });
            }

            accepted++;
        }

        return Accepted(new IngestResponse { Accepted = accepted });
    }

    private static LogEntry MapToEntity(LogEntryDto dto) => new()
    {
        Timestamp = dto.Timestamp,
        Id = dto.Id ?? Guid.NewGuid(),
        TraceId = dto.TraceId,
        SpanId = dto.SpanId,
        Source = dto.Source,
        SourceType = dto.SourceType,
        InstanceId = dto.InstanceId,
        Level = dto.Level,
        Category = dto.Category,
        Message = dto.Message,
        MessageTemplate = dto.MessageTemplate,
        Properties = dto.Properties,
        Exception = dto.Exception,
        UserId = dto.UserId,
        RequestId = dto.RequestId,
        SessionId = dto.SessionId,
        Tags = dto.Tags,
        Stream = dto.Stream,
    };
}
