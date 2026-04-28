namespace HiveLog.Client.Models;

/// <summary>JSON payload for POST /api/hivelog/v1/ingest.</summary>
internal sealed class IngestRequest
{
    public string Source { get; set; } = null!;
    public string SourceType { get; set; } = null!;
    public string? InstanceId { get; set; }
    public List<ClientLogEntry> Entries { get; set; } = null!;
}
