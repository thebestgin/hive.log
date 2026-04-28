namespace HiveLog.Api.Features.Query.Models;

/// <summary>
/// Request for POST /api/hivelog/v1/query/natural.
/// </summary>
public class NaturalQueryRequest
{
    /// <summary>
    /// Natural language query string. Examples:
    ///   "Zeige mir alle Fehler von heute"
    ///   "errors in talents-api last 30 minutes"
    ///   "trace a1b2c3d4-..."
    ///   "wie viele Fehler gab es heute?"
    /// </summary>
    public string Query { get; set; } = null!;
}
