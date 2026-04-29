using System.ComponentModel.DataAnnotations;
using HiveLog.Api.Features.Ingest.Models;

namespace HiveLog.Api.Features.Connectors.WebApp;

public class WebAppLogRequest
{
    [StringLength(128)]
    public string? InstanceId { get; set; }

    [Required, MinLength(1), MaxLength(1000)]
    public List<LogEntryDto> Entries { get; set; } = null!;
}
