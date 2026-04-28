using System.ComponentModel.DataAnnotations;

namespace HiveLog.Api.Features.Aggregate.Models;

public sealed class AggregateRequest
{
    /// <summary>
    /// Metric to compute. Currently only "count" is supported.
    /// </summary>
    [Required]
    public string Metric { get; init; } = "count";

    /// <summary>
    /// Fields to group by. Supported values: "source", "level", "stream".
    /// </summary>
    public string[] GroupBy { get; init; } = [];

    /// <summary>
    /// Time bucket size. Format: "{N}m" (minutes), "{N}h" (hours), "{N}d" (days).
    /// Examples: "5m", "15m", "1h", "1d". Minimum: "1m".
    /// </summary>
    [Required]
    public string Bucket { get; init; } = "5m";

    /// <summary>
    /// Time range filter. Both from and to are required.
    /// </summary>
    [Required]
    public TimeRangeFilter TimeRange { get; init; } = null!;

    /// <summary>
    /// Optional stream filter. When null, all streams are included.
    /// </summary>
    public string? Stream { get; init; }
}

public sealed class TimeRangeFilter
{
    [Required]
    public DateTimeOffset From { get; init; }

    [Required]
    public DateTimeOffset To { get; init; }
}
