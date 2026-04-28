namespace HiveLog.Api.Features.Query.Models;

/// <summary>
/// Structured query request for POST /api/hivelog/v1/query.
/// All filters are optional and combined with AND logic.
/// </summary>
public class QueryRequest
{
    /// <summary>Filter by stream (e.g. "app", "agent", "e2e", "audit"). Null = all streams.</summary>
    public string[]? Streams { get; set; }

    /// <summary>Filter by source name (e.g. "talents-api"). Null = all sources.</summary>
    public string[]? Sources { get; set; }

    /// <summary>Level filter (inclusive minimum). Null = all levels.</summary>
    public LevelFilter? Levels { get; set; }

    /// <summary>UTC time range filter. Null = no time constraint.</summary>
    public TimeRangeFilter? TimeRange { get; set; }

    /// <summary>Exact trace ID match. Null = no trace filter.</summary>
    public Guid? TraceId { get; set; }

    /// <summary>Tag filters. Null = no tag filter.</summary>
    public TagFilter? Tags { get; set; }

    /// <summary>Full-text search on message (case-insensitive LIKE). Null = no search.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// JSONB containment filter on properties column.
    /// Key = property name, Value = expected value (as JSON-serializable object).
    /// E.g. { "UserId": "abc123" } → WHERE properties @> '{"UserId":"abc123"}'
    /// </summary>
    public Dictionary<string, object?>? Properties { get; set; }

    /// <summary>Sort order. Default: timestamp_desc.</summary>
    public string OrderBy { get; set; } = "timestamp_desc";

    /// <summary>Maximum number of entries to return. Default: 100, max: 1000.</summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// Pagination cursor from a previous response.
    /// Format: "{ISO8601-timestamp}|{uuid}" e.g. "2026-04-28T14:30:00.123Z|a1b2c3d4-..."
    /// </summary>
    public string? Cursor { get; set; }
}

public class LevelFilter
{
    /// <summary>Minimum log level (inclusive). 0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Fatal.</summary>
    public short? Min { get; set; }
}

public class TimeRangeFilter
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class TagFilter
{
    /// <summary>Entry must have at least one of these tags (OR logic).</summary>
    public string[]? Any { get; set; }

    /// <summary>Entry must have all of these tags (AND logic).</summary>
    public string[]? All { get; set; }
}
