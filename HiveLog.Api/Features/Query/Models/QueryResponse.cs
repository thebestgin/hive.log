namespace HiveLog.Api.Features.Query.Models;

/// <summary>
/// Response from POST /api/hivelog/v1/query.
/// </summary>
public class QueryResponse
{
    /// <summary>Matching log entries (up to Limit).</summary>
    public List<LogEntryResult> Entries { get; set; } = [];

    /// <summary>
    /// Cursor for the next page. Null means this is the last page.
    /// Format: "{ISO8601-timestamp}|{uuid}"
    /// </summary>
    public string? NextCursor { get; set; }
}
