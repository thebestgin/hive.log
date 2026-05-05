namespace HiveLog.Api.Features.Logs.Models;

/// <summary>
/// Represents a structured log entry stored in the log_entries TimescaleDB hypertable.
/// Composite PK: (Timestamp, Id) — TimescaleDB requires the hypertable dimension in the PK.
/// Column names and table name are configured via LogEntryConfiguration (snake_case convention).
/// </summary>
public class LogEntry
{
    // --- Time-Series Key (Hypertable Dimension) ---

    public DateTimeOffset Timestamp { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- W3C Trace Context (https://www.w3.org/TR/trace-context/) ---

    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }

    // --- Source ---

    public string Source { get; set; } = null!;
    public string SourceType { get; set; } = null!;
    public string? InstanceId { get; set; }

    // --- Log Data ---

    /// <summary>0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Fatal</summary>
    public short Level { get; set; }

    public string Category { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? MessageTemplate { get; set; }

    // --- Structured Data (JSONB) ---

    public string? Properties { get; set; }
    public string? Exception { get; set; }

    // --- Context ---

    public Guid? UserId { get; set; }
    public string? RequestId { get; set; }
    public string? SessionId { get; set; }

    // --- Tags (TEXT[]) ---

    public string[]? Tags { get; set; }

    // --- Stream ---

    public string Stream { get; set; } = "app";

    // --- Auth ---

    /// <summary>True when the ingest request carried a valid JWT Bearer token.</summary>
    public bool IsAuthenticated { get; set; }

    // --- Caller ---

    /// <summary>
    /// Source file and line number of the log call (e.g. "talent-card.svelte:42").
    /// Extracted client-side via stack capture by AppLogger — allows direct filtering
    /// without JSONB parsing. Null for backend service logs.
    /// </summary>
    public string? Caller { get; set; }
}
