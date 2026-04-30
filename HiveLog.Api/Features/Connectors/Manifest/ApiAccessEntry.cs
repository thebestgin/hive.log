namespace HiveLog.Api.Features.Connectors.Manifest;

/// <summary>
/// Per-service API key entry within an apiKey connector.
/// Follows the SyncServer apiAccesses pattern: each backend service gets its own key.
/// HiveLog knows who is logging because the key proves it — not because the caller claims it.
/// </summary>
public class ApiAccessEntry
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string ApiKey { get; set; } = null!;
}
