using System.ComponentModel.DataAnnotations.Schema;

namespace HiveLog.Api.Features.Logs.Models;

/// <summary>
/// Represents a structured log entry stored in the log_entries TimescaleDB hypertable.
/// Composite PK: (Timestamp, Id) — TimescaleDB requires the hypertable dimension in the PK.
/// </summary>
[Table("log_entries")]
public class LogEntry
{
    // --- Time-Series Key (Hypertable Dimension) ---

    [Column("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Correlation ---

    [Column("trace_id")]
    public Guid? TraceId { get; set; }

    [Column("span_id")]
    public Guid? SpanId { get; set; }

    // --- Source ---

    [Column("source")]
    public string Source { get; set; } = null!;

    [Column("source_type")]
    public string SourceType { get; set; } = null!;

    [Column("instance_id")]
    public string? InstanceId { get; set; }

    // --- Log Data ---

    /// <summary>0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Fatal</summary>
    [Column("level")]
    public short Level { get; set; }

    [Column("category")]
    public string Category { get; set; } = null!;

    [Column("message")]
    public string Message { get; set; } = null!;

    [Column("message_template")]
    public string? MessageTemplate { get; set; }

    // --- Structured Data (JSONB) ---

    [Column("properties", TypeName = "jsonb")]
    public string? Properties { get; set; }

    [Column("exception", TypeName = "jsonb")]
    public string? Exception { get; set; }

    // --- Context ---

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("request_id")]
    public string? RequestId { get; set; }

    [Column("session_id")]
    public string? SessionId { get; set; }

    // --- Tags (TEXT[]) ---

    [Column("tags")]
    public string[]? Tags { get; set; }

    // --- Stream ---

    [Column("stream")]
    public string Stream { get; set; } = "app";
}
