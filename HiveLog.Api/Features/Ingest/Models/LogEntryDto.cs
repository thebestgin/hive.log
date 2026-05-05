using System.ComponentModel.DataAnnotations;

namespace HiveLog.Api.Features.Ingest.Models;

/// <summary>Single log entry in an ingest batch.</summary>
public class LogEntryDto
{
    [Required]
    public DateTimeOffset Timestamp { get; set; }

    public Guid? Id { get; set; }

    [StringLength(64)]
    public string? TraceId { get; set; }

    [StringLength(64)]
    public string? SpanId { get; set; }

    [StringLength(64)]
    public string? ParentSpanId { get; set; }

    [Required, Range(0, 5)]
    public short Level { get; set; }

    [Required, StringLength(256)]
    public string Category { get; set; } = null!;

    [Required, StringLength(8000)]
    public string Message { get; set; } = null!;

    [StringLength(2000)]
    public string? MessageTemplate { get; set; }

    /// <summary>JSON object as string.</summary>
    [StringLength(65536)]
    public string? Properties { get; set; }

    /// <summary>JSON object as string (exception details).</summary>
    [StringLength(65536)]
    public string? Exception { get; set; }

    public Guid? UserId { get; set; }

    [StringLength(64)]
    public string? RequestId { get; set; }

    [StringLength(64)]
    public string? SessionId { get; set; }

    [MaxLength(32)]
    public string[]? Tags { get; set; }

    [StringLength(64)]
    public string Stream { get; set; } = "app";

    /// <summary>
    /// Source file and line of the log call (e.g. "talent-card.svelte:42").
    /// Populated by AppLogger via stack capture on the frontend.
    /// </summary>
    [StringLength(256)]
    public string? Caller { get; set; }
}
