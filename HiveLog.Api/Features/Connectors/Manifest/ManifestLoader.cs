using System.Text.Json;

namespace HiveLog.Api.Features.Connectors.Manifest;

/// <summary>
/// Loads and validates the hivelog-manifest.json at startup.
/// Fails fast if the file is missing, unreadable, or structurally invalid.
/// The validated manifest is registered as a singleton — it does not change at runtime.
/// </summary>
public static class ManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static HiveLogManifest LoadAndValidate(string filePath)
    {
        if (!File.Exists(filePath))
            throw new InvalidOperationException(
                $"HiveLog manifest not found at '{filePath}'. " +
                "Set HiveLogManifest__FilePath to a valid hivelog-manifest.json path.");

        var json = File.ReadAllText(filePath);
        var manifest = JsonSerializer.Deserialize<HiveLogManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("HiveLog manifest deserialized to null.");

        Validate(manifest);
        return manifest;
    }

    private static void Validate(HiveLogManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ManifestVersion))
            throw new InvalidOperationException("HiveLog manifest: manifestVersion is required.");

        if (manifest.Connectors.Count == 0)
            throw new InvalidOperationException("HiveLog manifest: at least one connector is required.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in manifest.Connectors)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
                throw new InvalidOperationException("HiveLog manifest: connector id is required.");

            if (!ids.Add(c.Id))
                throw new InvalidOperationException($"HiveLog manifest: duplicate connector id '{c.Id}'.");

            if (string.IsNullOrWhiteSpace(c.Source))
                throw new InvalidOperationException($"HiveLog manifest: connector '{c.Id}' has no source.");

            if (string.IsNullOrWhiteSpace(c.SourceType))
                throw new InvalidOperationException($"HiveLog manifest: connector '{c.Id}' has no sourceType.");

            if (c.Auth is null)
                throw new InvalidOperationException($"HiveLog manifest: connector '{c.Id}' has no auth.");

            var authType = c.Auth.Type?.ToLowerInvariant();
            if (authType is not ("apikey" or "jwt" or "none"))
                throw new InvalidOperationException(
                    $"HiveLog manifest: connector '{c.Id}' has invalid auth type '{c.Auth.Type}'. " +
                    "Allowed: apiKey, jwt, none.");

            if (authType == "apikey")
            {
                if (string.IsNullOrWhiteSpace(c.Auth.HeaderName))
                    throw new InvalidOperationException(
                        $"HiveLog manifest: apiKey connector '{c.Id}' requires auth.headerName.");

                if (c.Auth.ApiAccesses is null || c.Auth.ApiAccesses.Count == 0)
                    throw new InvalidOperationException(
                        $"HiveLog manifest: apiKey connector '{c.Id}' requires at least one apiAccess entry.");

                var keys = new HashSet<string>();
                foreach (var a in c.Auth.ApiAccesses)
                {
                    if (string.IsNullOrWhiteSpace(a.ApiKey))
                        throw new InvalidOperationException(
                            $"HiveLog manifest: apiAccess '{a.Id}' in connector '{c.Id}' has no apiKey.");

                    if (!keys.Add(a.ApiKey))
                        throw new InvalidOperationException(
                            $"HiveLog manifest: duplicate apiKey in connector '{c.Id}'.");
                }
            }
        }
    }
}
