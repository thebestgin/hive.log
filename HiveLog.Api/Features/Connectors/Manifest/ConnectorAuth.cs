namespace HiveLog.Api.Features.Connectors.Manifest;

/// <summary>
/// Auth configuration for a connector.
/// Type determines the authentication mechanism:
/// - "apiKey": caller must provide a valid key in the configured header (per-service keys in apiAccesses)
/// - "jwt": caller must provide a valid Keycloak JWT Bearer token
/// - "none": no authentication required
/// </summary>
public class ConnectorAuth
{
    public string Type { get; set; } = null!;
    public string? HeaderName { get; set; }
    public List<ApiAccessEntry>? ApiAccesses { get; set; }
}
