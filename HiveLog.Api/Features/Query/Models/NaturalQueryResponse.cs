namespace HiveLog.Api.Features.Query.Models;

/// <summary>
/// Response from POST /api/hivelog/v1/query/natural.
/// Transparency principle: always return interpretedQuery + sql so the caller can verify the interpretation.
/// </summary>
public class NaturalQueryResponse
{
    /// <summary>
    /// The structured query derived from the natural language input.
    /// Null when confidence is 0 (no match).
    /// </summary>
    public QueryRequest? InterpretedQuery { get; set; }

    /// <summary>
    /// The parameterized SQL that was executed. Null when confidence is 0.
    /// Returned for transparency / debugging — agents can verify correctness.
    /// </summary>
    public string? Sql { get; set; }

    /// <summary>
    /// Query result. Null when confidence is 0 or for count-queries (use Count instead).
    /// </summary>
    public QueryResponse? Result { get; set; }

    /// <summary>
    /// Result for count-queries ("wie viele", "how many", "anzahl").
    /// Null for entry-listing queries.
    /// </summary>
    public long? Count { get; set; }

    /// <summary>
    /// Match confidence between 0.0 (no match) and 1.0 (certain).
    /// 0 means no pattern matched — see Error field.
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Set to "no_match" when no pattern matched the query.
    /// Null when a pattern matched.
    /// </summary>
    public string? Error { get; set; }
}
