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
}
