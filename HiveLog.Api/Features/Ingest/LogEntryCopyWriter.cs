using HiveLog.Api.Features.Logs.Models;
using Npgsql;
using NpgsqlTypes;

namespace HiveLog.Api.Features.Ingest;

/// <summary>
/// Writes log entries to PostgreSQL using the COPY binary protocol.
/// Significantly faster than individual INSERTs for bulk writes.
/// </summary>
public sealed class LogEntryCopyWriter
{
    private readonly NpgsqlDataSource _dataSource;

    public LogEntryCopyWriter(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task WriteBatchAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Column order must match exactly with WriteAsync calls below
        const string copyCommand = """
            COPY log_entries (
                timestamp, id, trace_id, span_id,
                source, source_type, instance_id,
                level, category, message, message_template,
                properties, exception,
                user_id, request_id, session_id,
                tags, stream
            ) FROM STDIN (FORMAT binary)
            """;

        await using var writer = await conn.BeginBinaryImportAsync(copyCommand, ct);

        foreach (var e in entries)
        {
            await writer.StartRowAsync(ct);

            await writer.WriteAsync(e.Timestamp, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(e.Id, NpgsqlDbType.Uuid, ct);

            if (e.TraceId.HasValue)
                await writer.WriteAsync(e.TraceId.Value, NpgsqlDbType.Uuid, ct);
            else
                await writer.WriteNullAsync(ct);

            if (e.SpanId.HasValue)
                await writer.WriteAsync(e.SpanId.Value, NpgsqlDbType.Uuid, ct);
            else
                await writer.WriteNullAsync(ct);

            await writer.WriteAsync(e.Source, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.SourceType, NpgsqlDbType.Text, ct);

            if (e.InstanceId is not null)
                await writer.WriteAsync(e.InstanceId, NpgsqlDbType.Text, ct);
            else
                await writer.WriteNullAsync(ct);

            await writer.WriteAsync(e.Level, NpgsqlDbType.Smallint, ct);
            await writer.WriteAsync(e.Category, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.Message, NpgsqlDbType.Text, ct);

            if (e.MessageTemplate is not null)
                await writer.WriteAsync(e.MessageTemplate, NpgsqlDbType.Text, ct);
            else
                await writer.WriteNullAsync(ct);

            if (e.Properties is not null)
                await writer.WriteAsync(e.Properties, NpgsqlDbType.Jsonb, ct);
            else
                await writer.WriteNullAsync(ct);

            if (e.Exception is not null)
                await writer.WriteAsync(e.Exception, NpgsqlDbType.Jsonb, ct);
            else
                await writer.WriteNullAsync(ct);

            if (e.UserId.HasValue)
                await writer.WriteAsync(e.UserId.Value, NpgsqlDbType.Uuid, ct);
            else
                await writer.WriteNullAsync(ct);

            if (e.RequestId is not null)
                await writer.WriteAsync(e.RequestId, NpgsqlDbType.Text, ct);
            else
                await writer.WriteNullAsync(ct);

            if (e.SessionId is not null)
                await writer.WriteAsync(e.SessionId, NpgsqlDbType.Text, ct);
            else
                await writer.WriteNullAsync(ct);

            await writer.WriteAsync(e.Tags ?? Array.Empty<string>(), NpgsqlDbType.Array | NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.Stream, NpgsqlDbType.Text, ct);
        }

        await writer.CompleteAsync(ct);
    }
}
