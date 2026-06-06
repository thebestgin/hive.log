namespace HiveLog.Api.Features.Retention;

public class RetentionOptions
{
    public const string SectionName = "HiveLog";

    /// <summary>TimescaleDB chunk retention — oldest chunks are dropped after this many days. Default: 30.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// TimescaleDB chunk_time_interval in hours. Default: 1.
    /// WHY (00711): with a too-large chunk interval (TimescaleDB default = 7 days),
    /// the active chunk never ages past compress_after/drop_after, so compression AND
    /// retention never fire -> unbounded growth. Chunks must be small (hours) relative
    /// to RetentionDays so they roll over, compress (after 2h) and drop (after RetentionDays).
    /// Prod with longer RetentionDays may raise this.
    /// </summary>
    public int ChunkIntervalHours { get; set; } = 1;

    /// <summary>Audit stream retention in days. Default: 365.</summary>
    public int AuditRetentionDays { get; set; } = 365;

    /// <summary>Fine-grained retention: Debug (level 1) and Trace (level 0) entries older than this are deleted. Default: 7.</summary>
    public int DebugTraceRetentionDays { get; set; } = 7;

    /// <summary>Fine-grained retention: Info (level 2) entries older than this are deleted. Default: 30.</summary>
    public int InfoRetentionDays { get; set; } = 30;
}
