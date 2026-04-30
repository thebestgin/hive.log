namespace HiveLog.Api.Features.Admin;

public class AdminOptions
{
    public const string SectionName = "HiveLog";

    /// <summary>API key for X-Api-Key header. Used by all non-admin ingest and query endpoints.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>API key for Admin-Api-Key header. All admin endpoints require this.</summary>
    public string AdminApiKey { get; set; } = string.Empty;
}
