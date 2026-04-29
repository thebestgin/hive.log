namespace HiveLog.Client.Models;

/// <summary>Single log entry queued in the client buffer.</summary>
internal sealed class ClientLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? ParentSpanId { get; set; }
    public int Level { get; set; }
    public string Category { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? MessageTemplate { get; set; }
    public string? Properties { get; set; }
    public string? Exception { get; set; }
    public string? UserId { get; set; }
    public string? RequestId { get; set; }
    public string? SessionId { get; set; }
    public string[]? Tags { get; set; }
    public string Stream { get; set; } = "app";
}
