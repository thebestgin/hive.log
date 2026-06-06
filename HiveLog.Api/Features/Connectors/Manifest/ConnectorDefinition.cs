namespace HiveLog.Api.Features.Connectors.Manifest;

/// <summary>
/// A single connector in the manifest. Each connector defines:
/// - How callers authenticate (apiKey, jwt, none)
/// - What source/sourceType is stamped on log entries (server-controlled, not caller-supplied)
/// </summary>
public class ConnectorDefinition
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Source { get; set; } = null!;
    public string SourceType { get; set; } = null!;
    public ConnectorAuth Auth { get; set; } = null!;

    /// <summary>Optional server-side minimum level (smallint: 0=Trace,1=Debug,2=Info,3=Warn,4=Error,5=Fatal).
    /// Entries below this are dropped at ingest. Null = accept all. Server-side guardrail for untrusted
    /// connectors; the primary level filter stays client-side (00712).</summary>
    public short? MinLevel { get; set; }

    /// <summary>Optional per-connector ingest rate limit. Null = unlimited.</summary>
    public ConnectorRateLimit? RateLimit { get; set; }
}
