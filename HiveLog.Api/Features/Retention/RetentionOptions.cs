namespace HiveLog.Api.Features.Retention;

public class RetentionOptions
{
    public const string SectionName = "HiveLog";

    /// <summary>TimescaleDB chunk retention — oldest chunks are dropped after this many days. Default: 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Audit stream retention in days. Default: 365.</summary>
    public int AuditRetentionDays { get; set; } = 365;

    /// <summary>Fine-grained retention: Debug (level 1) and Trace (level 0) entries older than this are deleted. Default: 7.</summary>
    public int DebugTraceRetentionDays { get; set; } = 7;

    /// <summary>Fine-grained retention: Info (level 2) entries older than this are deleted. Default: 30.</summary>
    public int InfoRetentionDays { get; set; } = 30;
}
