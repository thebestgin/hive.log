namespace HiveLog.Api.Features.Query.NaturalLanguage;

/// <summary>
/// Thrown when LLM-generated SQL fails the read-only or whitelist-table validation checks.
/// The caller must return { "error": "unsafe_sql" } — no query execution.
/// </summary>
public class SqlValidationException : Exception
{
    public SqlValidationException(string message) : base(message) { }
}
