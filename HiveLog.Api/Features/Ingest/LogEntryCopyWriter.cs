using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace HiveLog.Api.Features.Ingest;

public sealed class LogEntryCopyWriter
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _copyCommand;

    public LogEntryCopyWriter(NpgsqlDataSource dataSource, IServiceProvider services)
    {
        _dataSource = dataSource;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HiveLogDbContext>();
        _copyCommand = BuildCopyCommand(db);
    }

    public async Task WriteBatchAsync(IReadOnlyList<LogEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var writer = await conn.BeginBinaryImportAsync(_copyCommand, ct);

        foreach (var e in entries)
        {
            await writer.StartRowAsync(ct);

            await writer.WriteAsync(e.Timestamp, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(e.Id, NpgsqlDbType.Uuid, ct);
            await WriteNullableAsync(writer, e.TraceId, NpgsqlDbType.Text, ct);
            await WriteNullableAsync(writer, e.SpanId, NpgsqlDbType.Text, ct);
            await WriteNullableAsync(writer, e.ParentSpanId, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.Source, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.SourceType, NpgsqlDbType.Text, ct);
            await WriteNullableAsync(writer, e.InstanceId, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.Level, NpgsqlDbType.Smallint, ct);
            await writer.WriteAsync(e.Category, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.Message, NpgsqlDbType.Text, ct);
            await WriteNullableAsync(writer, e.MessageTemplate, NpgsqlDbType.Text, ct);
            await WriteNullableAsync(writer, e.Properties, NpgsqlDbType.Jsonb, ct);
            await WriteNullableAsync(writer, e.Exception, NpgsqlDbType.Jsonb, ct);

            if (e.UserId.HasValue)
                await writer.WriteAsync(e.UserId.Value, NpgsqlDbType.Uuid, ct);
            else
                await writer.WriteNullAsync(ct);

            await WriteNullableAsync(writer, e.RequestId, NpgsqlDbType.Text, ct);
            await WriteNullableAsync(writer, e.SessionId, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.Tags ?? Array.Empty<string>(), NpgsqlDbType.Array | NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.Stream, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(e.IsAuthenticated, NpgsqlDbType.Boolean, ct);
        }

        await writer.CompleteAsync(ct);
    }

    private static async Task WriteNullableAsync(NpgsqlBinaryImporter writer, string? value, NpgsqlDbType type, CancellationToken ct)
    {
        if (value is not null)
            await writer.WriteAsync(value, type, ct);
        else
            await writer.WriteNullAsync(ct);
    }

    private static string BuildCopyCommand(HiveLogDbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(LogEntry))!;
        var table = entity.GetTableName()!;

        string Col(string propertyName) =>
            entity.FindProperty(propertyName)!.GetColumnName();

        var columns = string.Join(", ",
            Col(nameof(LogEntry.Timestamp)),
            Col(nameof(LogEntry.Id)),
            Col(nameof(LogEntry.TraceId)),
            Col(nameof(LogEntry.SpanId)),
            Col(nameof(LogEntry.ParentSpanId)),
            Col(nameof(LogEntry.Source)),
            Col(nameof(LogEntry.SourceType)),
            Col(nameof(LogEntry.InstanceId)),
            Col(nameof(LogEntry.Level)),
            Col(nameof(LogEntry.Category)),
            Col(nameof(LogEntry.Message)),
            Col(nameof(LogEntry.MessageTemplate)),
            Col(nameof(LogEntry.Properties)),
            Col(nameof(LogEntry.Exception)),
            Col(nameof(LogEntry.UserId)),
            Col(nameof(LogEntry.RequestId)),
            Col(nameof(LogEntry.SessionId)),
            Col(nameof(LogEntry.Tags)),
            Col(nameof(LogEntry.Stream)),
            Col(nameof(LogEntry.IsAuthenticated)));

        return $"COPY {table} ({columns}) FROM STDIN (FORMAT binary)";
    }
}
