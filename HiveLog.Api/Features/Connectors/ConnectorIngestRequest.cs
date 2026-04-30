using System.ComponentModel.DataAnnotations;
using HiveLog.Api.Features.Ingest.Models;

namespace HiveLog.Api.Features.Connectors;

/// <summary>
/// Ingest request body for the generic connector endpoint.
/// source and sourceType are NOT in the body — they come from the manifest.
/// </summary>
public class ConnectorIngestRequest
{
    [StringLength(128)]
    public string? InstanceId { get; set; }

    [Required, MinLength(1), MaxLength(1000)]
    public List<LogEntryDto> Entries { get; set; } = null!;
}
