namespace HiveLog.Api.Features.Connectors.Manifest;

/// <summary>Optional per-connector ingest rate limit. Counts log ENTRIES (not requests)
/// per window per client — the real flood vector is 1000-entry batches (00712).</summary>
public class ConnectorRateLimit
{
    /// <summary>Max accepted entries per window per client identifier.</summary>
    public int MaxEntries { get; set; }
    /// <summary>Window length in seconds.</summary>
    public int WindowSeconds { get; set; }
}
