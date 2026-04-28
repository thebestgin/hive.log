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
/// Schedule: first run 1 hour after startup, then every 24 hours.
/// </summary>
public sealed class RetentionCleanupJob : BackgroundService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RetentionOptions _opts;
    private readonly ILogger<RetentionCleanupJob> _logger;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan Period = TimeSpan.FromHours(24);

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
        try
        {
            await Task.Delay(InitialDelay, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Period);

        do
        {
            try
            {
                await RunCleanupAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HiveLog] RetentionCleanupJob failed");
            }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        _logger.LogInformation("[HiveLog] RetentionCleanupJob starting");

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Delete Trace (level=0) and Debug (level=1) entries older than DebugTraceRetentionDays.
        // Audit stream is excluded.
        var debugTraceDeleted = await ExecuteDeleteAsync(
            conn,
            sql: """
                DELETE FROM log_entries
                WHERE level <= @maxLevel
                  AND stream != @auditStream
                  AND timestamp < NOW() - @interval
                """,
            new NpgsqlParameter("maxLevel", NpgsqlDbType.Smallint) { Value = (short)1 },
            new NpgsqlParameter("auditStream", NpgsqlDbType.Text) { Value = "audit" },
            new NpgsqlParameter("interval", NpgsqlDbType.Interval)
                { Value = TimeSpan.FromDays(_opts.DebugTraceRetentionDays) },
            ct);

        // Delete Info (level=2) entries older than InfoRetentionDays.
        // Audit stream is excluded.
        var infoDeleted = await ExecuteDeleteAsync(
            conn,
            sql: """
                DELETE FROM log_entries
                WHERE level = @level
                  AND stream != @auditStream
                  AND timestamp < NOW() - @interval
                """,
            new NpgsqlParameter("level", NpgsqlDbType.Smallint) { Value = (short)2 },
            new NpgsqlParameter("auditStream", NpgsqlDbType.Text) { Value = "audit" },
            new NpgsqlParameter("interval", NpgsqlDbType.Interval)
                { Value = TimeSpan.FromDays(_opts.InfoRetentionDays) },
            ct);

        _logger.LogInformation(
            "[HiveLog] RetentionCleanupJob complete — debug/trace deleted: {DebugTrace}, info deleted: {Info}",
            debugTraceDeleted, infoDeleted);
    }

    private static async Task<int> ExecuteDeleteAsync(
        NpgsqlConnection conn,
        string sql,
        NpgsqlParameter p1,
        NpgsqlParameter p2,
        NpgsqlParameter p3,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(p1);
        cmd.Parameters.Add(p2);
        cmd.Parameters.Add(p3);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
