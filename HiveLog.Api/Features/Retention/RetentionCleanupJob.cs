using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace HiveLog.Api.Features.Retention;

/// <summary>
/// Nightly cleanup job for level-based fine-grained retention.
///
/// TimescaleDB retention policy drops whole chunks (1-hour intervals).
/// This job deletes individual rows by log level within surviving chunks,
/// allowing shorter retention for verbose levels (Debug/Trace, Info)
/// while keeping higher-severity entries longer.
///
/// Audit stream (stream = 'audit') is excluded from all fine-grained deletes.
///
/// Scheduling (hardened 00925, mirrors SyncServer CollectionCleanup 00895):
///   - Polls every 5 minutes; runs only inside the configured UTC night window
///     (CleanupWindowStartUtc–CleanupWindowEndUtc, default 02:00–03:00).
///     WHY: the old "1h after startup, then every 24h" schedule drifted with deploy
///     time — a daytime deploy meant a daily bulk delete during peak ingest.
///   - Once-per-night guard is set only AFTER a successful run: a transient failure
///     (DB restart, connection blip) retries on the next poll while the window is
///     still open (bounded to ~window/poll ≈ 12 attempts, no retry storm) instead
///     of losing a full day. Both deletes are idempotent — re-running is safe.
///   - Deletes are batched via the primary key (timestamp, id) with breathing pauses.
///     WHY batched: one statement over a full day of Debug/Trace rows spikes lock/I/O
///     on the hypertable. WHY PK keyset and NOT ctid: log_entries is a partitioned
///     table (TimescaleDB chunks are child tables); ctid is only unique WITHIN one
///     chunk — a ctid-IN-subquery on the parent can match wrong rows (00895 lesson).
///
/// WHY no distributed lock (unlike CollectionCleanup): HiveLog is a standalone
///   single-instance service (no horizontal replicas in any environment); both
///   deletes are idempotent, so even a hypothetical double-runner is safe.
///   DO NOT add DistributedLock here without an actual multi-replica deployment.
/// </summary>
public sealed class RetentionCleanupJob : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RetentionOptions _opts;
    private readonly ILogger<RetentionCleanupJob> _logger;

    // 5-minute poll — nightly job does not need sub-minute reactivity.
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMinutes(5);

    // Pause between delete batches so live ingest (bulk COPY) is not starved.
    private static readonly TimeSpan BatchBreather = TimeSpan.FromMilliseconds(50);

    // Once-per-night guard stored as DayNumber (int) so volatile is valid (struct restriction).
    // 0 = DateOnly.MinValue.DayNumber → job runs on the very first night the service is up.
    private volatile int _lastRunDayNumber;

    public RetentionCleanupJob(
        NpgsqlDataSource dataSource,
        IOptions<RetentionOptions> opts,
        ILogger<RetentionCleanupJob> logger)
    {
        _dataSource = dataSource;
        _opts = opts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[HiveLog] RetentionCleanupJob started (window {Start}–{End} UTC)",
            _opts.CleanupWindowStartUtc, _opts.CleanupWindowEndUtc);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TryRunIfWindowOpenAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Guard was NOT set → next poll retries while the window is still open.
                _logger.LogError(ex, "[HiveLog] RetentionCleanupJob failed — will retry on next poll inside the window");
            }

            await Task.Delay(PollingInterval, ct);
        }
    }

    private async Task TryRunIfWindowOpenAsync(CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var timeOfDay = nowUtc.TimeOfDay;
        var windowStart = _opts.ParsedCleanupStartUtc;
        var windowEnd = _opts.ParsedCleanupEndUtc;

        var inWindow = windowStart < windowEnd
            ? timeOfDay >= windowStart && timeOfDay < windowEnd   // normal window (e.g. 02:00–03:00)
            : timeOfDay >= windowStart || timeOfDay < windowEnd;  // midnight-crossing window

        if (!inWindow)
            return;

        var todayDayNumber = DateOnly.FromDateTime(nowUtc).DayNumber;
        if (_lastRunDayNumber == todayDayNumber)
            return; // Already ran successfully tonight.

        await RunCleanupAsync(ct);

        // Mark as ran only AFTER success — a thrown exception above skips this line,
        // so the next poll retries while the window is open (00925, mirrors 00895).
        _lastRunDayNumber = todayDayNumber;
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        _logger.LogInformation("[HiveLog] RetentionCleanupJob starting");

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Delete Trace (level=0) and Debug (level=1) entries older than DebugTraceRetentionDays.
        // Audit stream is excluded.
        var debugTraceDeleted = await BatchedDeleteAsync(
            conn,
            levelPredicate: "level <= @level",
            level: 1,
            retentionDays: _opts.DebugTraceRetentionDays,
            ct);

        // Delete Info (level=2) entries older than InfoRetentionDays.
        // Audit stream is excluded.
        var infoDeleted = await BatchedDeleteAsync(
            conn,
            levelPredicate: "level = @level",
            level: 2,
            retentionDays: _opts.InfoRetentionDays,
            ct);

        _logger.LogInformation(
            "[HiveLog] RetentionCleanupJob complete — debug/trace deleted: {DebugTrace}, info deleted: {Info}",
            debugTraceDeleted, infoDeleted);
    }

    /// <summary>
    /// Deletes matching rows in batches of CleanupBatchSize, keyed on the primary key
    /// (timestamp, id). The timestamp predicate in the subquery lets TimescaleDB prune
    /// to old chunks only. Loops until a batch deletes fewer rows than the batch size.
    /// </summary>
    private async Task<long> BatchedDeleteAsync(
        NpgsqlConnection conn,
        string levelPredicate,
        short level,
        int retentionDays,
        CancellationToken ct)
    {
        var sql = $"""
            DELETE FROM log_entries
            WHERE (timestamp, id) IN (
                SELECT timestamp, id FROM log_entries
                WHERE {levelPredicate}
                  AND stream != @auditStream
                  AND timestamp < NOW() - @interval
                LIMIT @batchSize
            )
            """;

        long total = 0;
        int affected;
        do
        {
            ct.ThrowIfCancellationRequested();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new NpgsqlParameter("level", NpgsqlDbType.Smallint) { Value = level });
            cmd.Parameters.Add(new NpgsqlParameter("auditStream", NpgsqlDbType.Text) { Value = "audit" });
            cmd.Parameters.Add(new NpgsqlParameter("interval", NpgsqlDbType.Interval)
                { Value = TimeSpan.FromDays(retentionDays) });
            cmd.Parameters.Add(new NpgsqlParameter("batchSize", NpgsqlDbType.Integer)
                { Value = _opts.CleanupBatchSize });

            affected = await cmd.ExecuteNonQueryAsync(ct);
            total += affected;

            if (affected > 0)
                await Task.Delay(BatchBreather, ct);
        }
        while (affected >= _opts.CleanupBatchSize); // Last batch smaller than the limit → no rows remain.

        return total;
    }
}
