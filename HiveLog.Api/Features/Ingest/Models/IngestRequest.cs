using System.ComponentModel.DataAnnotations;

namespace HiveLog.Api.Features.Ingest.Models;

public class IngestRequest
{
    [Required, MinLength(1), MaxLength(1000)]
    public List<LogEntryDto> Entries { get; set; } = null!;
}
