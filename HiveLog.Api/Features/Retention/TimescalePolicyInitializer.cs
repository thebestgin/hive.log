using Microsoft.Extensions.Options;
using Npgsql;

namespace HiveLog.Api.Features.Retention;

/// <summary>
/// Sets TimescaleDB compression and retention policies on startup.
/// Idempotent: ALTER TABLE compress settings are safe to repeat;
/// add_compression_policy and add_retention_policy both use if_not_exists => true.
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
        // RetentionDays and AuditRetentionDays are integers from application config,
        // not user input — safe to interpolate directly into the DO block.
        // NpgsqlParameter cannot be used inside anonymous PL/pgSQL DO blocks.
        var sql = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_available_extensions WHERE name = 'timescaledb'
                ) THEN
                    RAISE NOTICE 'TimescaleDB not available — skipping compression/retention policies';
                    RETURN;
                END IF;

                -- Enable compression with segmentby and orderby (idempotent — safe to repeat)
                ALTER TABLE log_entries SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'source, stream',
                    timescaledb.compress_orderby = 'timestamp DESC'
                );

                -- Compress chunks older than 2 hours (if_not_exists = idempotent)
                PERFORM add_compression_policy(
                    'log_entries',
                    INTERVAL '2 hours',
                    if_not_exists => true
                );

                -- Drop chunks older than {_opts.RetentionDays} days (if_not_exists = idempotent)
                PERFORM add_retention_policy(
                    'log_entries',
                    INTERVAL '{_opts.RetentionDays} days',
                    if_not_exists => true
                );

                RAISE NOTICE 'TimescaleDB policies applied: compression=2h, retention={_opts.RetentionDays}d';
            END;
            $$;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "[HiveLog] TimescaleDB policies initialized (retention={RetentionDays}d)",
            _opts.RetentionDays);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
