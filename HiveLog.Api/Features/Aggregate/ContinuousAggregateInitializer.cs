using Npgsql;

namespace HiveLog.Api.Features.Aggregate;

/// <summary>
/// Creates the log_summary_5min continuous aggregate view and its refresh policy on startup.
/// Idempotent: checks pg_matviews before CREATE, uses if_not_exists for the policy.
/// Wrapped in a DO block — no-op if TimescaleDB extension is not installed.
/// </summary>
public sealed class ContinuousAggregateInitializer : IHostedService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<ContinuousAggregateInitializer> _logger;

    public ContinuousAggregateInitializer(
        NpgsqlDataSource dataSource,
        ILogger<ContinuousAggregateInitializer> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // NpgsqlParameter cannot be used inside anonymous PL/pgSQL DO blocks.
        // All values here are hard-coded constants — no user input involved.
        const string sql = """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_available_extensions WHERE name = 'timescaledb'
                ) THEN
                    RAISE NOTICE 'TimescaleDB not available — skipping continuous aggregate setup';
                    RETURN;
                END IF;

                -- Create continuous aggregate view only if it does not exist yet
                IF NOT EXISTS (
                    SELECT 1 FROM pg_matviews WHERE matviewname = 'log_summary_5min'
                ) THEN
                    EXECUTE $sql$
                        CREATE MATERIALIZED VIEW log_summary_5min
                        WITH (timescaledb.continuous) AS
                        SELECT
                            time_bucket('5 minutes', timestamp) AS bucket,
                            source,
                            stream,
                            level,
                            count(*) AS log_count
                        FROM log_entries
                        GROUP BY bucket, source, stream, level
                        WITH NO DATA
                    $sql$;

                    RAISE NOTICE 'log_summary_5min continuous aggregate view created';
                ELSE
                    RAISE NOTICE 'log_summary_5min already exists — skipping CREATE';
                END IF;

                -- Add refresh policy (if_not_exists = idempotent)
                PERFORM add_continuous_aggregate_policy(
                    'log_summary_5min',
                    start_offset => INTERVAL '1 hour',
                    end_offset   => INTERVAL '5 minutes',
                    schedule_interval => INTERVAL '5 minutes',
                    if_not_exists => true
                );

                RAISE NOTICE 'log_summary_5min continuous aggregate policy ensured';
            END;
            $$;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("[HiveLog] Continuous aggregate log_summary_5min initialized");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
