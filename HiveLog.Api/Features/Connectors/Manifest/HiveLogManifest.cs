namespace HiveLog.Api.Features.Connectors.Manifest;

/// <summary>
/// Root model for hivelog-manifest.json.
/// Defines all connectors — each connector has an ID, auth config, and source defaults.
/// The manifest is loaded once at startup and registered as a singleton.
/// </summary>
public class HiveLogManifest
{
    public string ManifestVersion { get; set; } = null!;
    public List<ConnectorDefinition> Connectors { get; set; } = new();
}
