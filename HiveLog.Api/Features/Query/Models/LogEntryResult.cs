namespace HiveLog.Api.Features.Query.Models;

/// <summary>
/// Single log entry returned from a query.
/// Mirrors the log_entries table columns; Properties and Exception are raw JSON strings.
/// </summary>
public class LogEntryResult
{
    public DateTimeOffset Timestamp { get; set; }
    public Guid Id { get; set; }

    public string? TraceId { get; set; }
    public string? SpanId { get; set; }

    public string Source { get; set; } = null!;
    public string SourceType { get; set; } = null!;
    public string? InstanceId { get; set; }

    /// <summary>0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Fatal</summary>
    public short Level { get; set; }

    public string Category { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? MessageTemplate { get; set; }

    /// <summary>Raw JSON string (JSONB column).</summary>
    public string? Properties { get; set; }

    /// <summary>Raw JSON string (JSONB column).</summary>
    public string? Exception { get; set; }

    public Guid? UserId { get; set; }
    public string? RequestId { get; set; }
    public string? SessionId { get; set; }

    public string[]? Tags { get; set; }
    public string Stream { get; set; } = null!;
}
