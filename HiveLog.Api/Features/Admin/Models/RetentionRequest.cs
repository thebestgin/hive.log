using System.ComponentModel.DataAnnotations;

namespace HiveLog.Api.Features.Admin.Models;

public sealed class RetentionRequest
{
    [Required]
    public RetentionDaysRequest RetentionDays { get; set; } = null!;
}

public sealed class RetentionDaysRequest
{
    [Range(1, 3650)]
    public int? App { get; set; }

    [Range(1, 3650)]
    public int? Agent { get; set; }

    [Range(1, 3650)]
    public int? Audit { get; set; }
}
