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

    /// <summary>
    /// UTC start of the nightly window in which the fine-grained cleanup may run. Format: HH:mm.
    /// WHY a night window (00925): the row-level deletes compete with live ingest (bulk COPY)
    /// and query traffic. The old schedule ("1h after startup, then every 24h") drifted with
    /// deploy time — a 14:00 deploy meant a daily bulk delete at ~15:00, during peak.
    /// Mirrors the SyncServer CollectionCleanup pattern (00895).
    /// </summary>
    public string CleanupWindowStartUtc { get; set; } = "02:00";

    /// <summary>UTC end (exclusive) of the nightly cleanup window. Format: HH:mm.</summary>
    public string CleanupWindowEndUtc { get; set; } = "03:00";

    /// <summary>
    /// Maximum rows deleted per DELETE statement per level-group. Controls DB load.
    /// WHY batched (00925): a single DELETE over a full day of Debug/Trace logs can hit
    /// hundreds of thousands of rows in one statement — lock/I/O spike on the hypertable.
    /// Smaller batches with breathing pauses interleave with live ingest.
    /// </summary>
    public int CleanupBatchSize { get; set; } = 5000;

    /// <summary>Parses CleanupWindowStartUtc for comparison with DateTime.UtcNow.TimeOfDay.</summary>
    public TimeSpan ParsedCleanupStartUtc => TimeSpan.Parse(CleanupWindowStartUtc);

    /// <summary>Parses CleanupWindowEndUtc for comparison with DateTime.UtcNow.TimeOfDay.</summary>
    public TimeSpan ParsedCleanupEndUtc => TimeSpan.Parse(CleanupWindowEndUtc);
}
