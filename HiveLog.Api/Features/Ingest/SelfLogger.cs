using System.Text.Json;
using HiveLog.Api.Features.Logs.Models;

namespace HiveLog.Api.Features.Ingest;

/// <summary>
/// HiveLog self-instrumentation logger.
/// Writes directly into the IngestBuffer — no HTTP call, no recursion.
/// Used for internal events: buffer overflow, flush failures, slow queries.
///
/// source: "hivelog", stream: "app"
/// </summary>
public sealed class SelfLogger
{
    private const string Source = "hivelog";
    private const string SourceType = "backend";
    private const string Stream = "app";
    private const string Category = "HiveLog.Self";

    private readonly IngestBuffer _buffer;

    public SelfLogger(IngestBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <summary>Log level: 3 = Warn</summary>
    public void Warn(string message, object? properties = null)
        => TryWrite(3, message, properties);

    /// <summary>Log level: 4 = Error</summary>
    public void Error(string message, object? properties = null)
        => TryWrite(4, message, properties);

    private void TryWrite(short level, string message, object? properties)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Id = Guid.NewGuid(),
            Source = Source,
            SourceType = SourceType,
            Level = level,
            Category = Category,
            Message = message,
            Stream = Stream,
            Properties = properties is null
                ? null
                : JsonSerializer.Serialize(properties),
        };

        // Fire-and-forget: if buffer is full we skip — no recursion
        _buffer.TryWriteSync(entry);
    }
}
