using System.ComponentModel.DataAnnotations;

namespace HiveLog.Api.Features.Ingest.Models;

public class IngestRequest
{
    [Required, StringLength(256)]
    public string Source { get; set; } = null!;

    [Required, StringLength(64)]
    public string SourceType { get; set; } = null!;

    [StringLength(128)]
    public string? InstanceId { get; set; }

    [Required, MinLength(1), MaxLength(1000)]
    public List<LogEntryDto> Entries { get; set; } = null!;
}
