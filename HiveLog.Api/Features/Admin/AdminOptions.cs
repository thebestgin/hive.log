namespace HiveLog.Api.Features.Admin;

public class AdminOptions
{
    public const string SectionName = "HiveLog";

    /// <summary>API key for Admin-Api-Key header. All admin endpoints require this.</summary>
    public string AdminApiKey { get; set; } = string.Empty;
}
