using Microsoft.Extensions.Options;
using Npgsql;

namespace HiveLog.Api.Features.Retention;

/// <summary>
/// Sets TimescaleDB chunk sizing, compression and retention policies on startup.
/// Authoritative: existing policies are removed and re-added on every start so that
/// a changed RetentionDays (or ChunkIntervalHours) always takes effect immediately.
/// WHY remove+re-add instead of if_not_exists (00711/N1): if_not_exists silently
/// keeps a pre-existing policy — a changed RetentionDays would never be applied.
/// WHY set_chunk_time_interval FIRST (00711): the TimescaleDB default chunk interval
/// is 7 days; a 7-day chunk that is still being written never ages past compress_after
/// or drop_after, so neither compression nor retention ever fire -> unbounded growth.
/// Idempotent: ALTER TABLE compress settings are safe to repeat.
/// Wrapped in a DO block — no-op if TimescaleDB extension is not installed.
/// </summary>
public sealed class TimescalePolicyInitializer : IHostedService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly RetentionOptions _opts;
    private readonly ILogger<TimescalePolicyInitializer> _logger;

    public TimescalePolicyInitializer(
        NpgsqlDataSource dataSource,
        IOptions<RetentionOptions> opts,
        ILogger<TimescalePolicyInitializer> logger)
    {
        _dataSource = dataSource;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // RetentionDays, AuditRetentionDays and ChunkIntervalHours are integers from application config,
        // not user input — safe to interpolate directly into the DO block.
        // NpgsqlParameter cannot be used inside anonymous PL/pgSQL DO blocks.
        var sql = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_available_extensions WHERE name = 'timescaledb'
                ) THEN
                    RAISE NOTICE 'TimescaleDB not available — skipping chunk/compression/retention policies';
                    RETURN;
                END IF;

                -- Chunk sizing FIRST. WHY (00711): default chunk_time_interval = 7 days; a 7-day chunk that
                -- is still being written never ages past compress_after/drop_after -> neither compression nor
                -- retention ever fire -> unbounded growth. Small chunks roll over fast and become eligible.
                -- Affects NEW chunks only (existing oversized chunks age out naturally).
                PERFORM set_chunk_time_interval('log_entries', INTERVAL '{_opts.ChunkIntervalHours} hours');

                -- Enable compression (idempotent — safe to repeat)
                ALTER TABLE log_entries SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'source, stream',
                    timescaledb.compress_orderby = 'timestamp DESC'
                );

                -- Realign policies to the CONFIGURED values. WHY (00711/N1): add_*_policy(if_not_exists => true)
                -- silently keeps a pre-existing policy -> a changed RetentionDays would never take effect.
                -- remove + re-add makes startup authoritative for the configured value.
                PERFORM remove_compression_policy('log_entries', if_exists => true);
                PERFORM add_compression_policy('log_entries', INTERVAL '2 hours');

                -- N2 (Design-Note, 00711): A global chunk-drop at RetentionDays cannot honor the separate
                -- AuditRetentionDays=365 — audit rows in a shared chunk are dropped together with app logs.
                -- Currently moot (no audit data), but a proper fix would be a dedicated audit hypertable
                -- (separate ticket). Documented here so the constraint is visible.
                PERFORM remove_retention_policy('log_entries', if_exists => true);
                PERFORM add_retention_policy('log_entries', INTERVAL '{_opts.RetentionDays} days');

                RAISE NOTICE 'TimescaleDB policies applied: chunk={_opts.ChunkIntervalHours}h, compression=2h, retention={_opts.RetentionDays}d';
            END;
            $$;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "[HiveLog] TimescaleDB policies initialized (chunk={ChunkHours}h, retention={RetentionDays}d)",
            _opts.ChunkIntervalHours, _opts.RetentionDays);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
