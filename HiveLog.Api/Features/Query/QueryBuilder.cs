using System.Text;
using System.Text.Json;
using HiveLog.Api.Features.Query.Models;
using Npgsql;
using NpgsqlTypes;

namespace HiveLog.Api.Features.Query;

/// <summary>
/// Builds parameterized SQL queries for log_entries from a QueryRequest.
/// Security: only log_entries is an allowed table. All values are NpgsqlParameters — no string interpolation.
/// </summary>
public static class QueryBuilder
{
    private const string AllowedTable = "log_entries";

    public static (string Sql, NpgsqlParameter[] Parameters) Build(QueryRequest request)
    {
        var effectiveLimit = Math.Clamp(request.Limit, 1, 1000);

        var sql = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();
        int paramIndex = 0;

        sql.AppendLine($"SELECT timestamp, id, trace_id, span_id, source, source_type, instance_id,");
        sql.AppendLine($"       level, category, message, message_template, properties, exception,");
        sql.AppendLine($"       user_id, request_id, session_id, tags, stream");
        sql.AppendLine($"FROM {AllowedTable}");
        sql.AppendLine("WHERE 1=1");

        // --- Cursor (must be first WHERE clause for index efficiency) ---
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            var (cursorTs, cursorId) = ParseCursor(request.Cursor);
            var pTs = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.TimestampTz) { Value = cursorTs };
            var pId = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Uuid) { Value = cursorId };
            parameters.Add(pTs);
            parameters.Add(pId);

            if (IsDescending(request.OrderBy))
                sql.AppendLine($"  AND (timestamp, id) < ({pTs.ParameterName}, {pId.ParameterName})");
            else
                sql.AppendLine($"  AND (timestamp, id) > ({pTs.ParameterName}, {pId.ParameterName})");
        }

        // --- Streams ---
        if (request.Streams is { Length: > 0 })
        {
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = request.Streams
            };
            parameters.Add(p);
            sql.AppendLine($"  AND stream = ANY({p.ParameterName})");
        }

        // --- Sources ---
        if (request.Sources is { Length: > 0 })
        {
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Array | NpgsqlDbType.Text)
            {
                Value = request.Sources
            };
            parameters.Add(p);
            sql.AppendLine($"  AND source = ANY({p.ParameterName})");
        }

        // --- Level ---
        if (request.Levels?.Min is { } minLevel)
        {
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Smallint) { Value = minLevel };
            parameters.Add(p);
            sql.AppendLine($"  AND level >= {p.ParameterName}");
        }

        // --- Time Range ---
        if (request.TimeRange?.From is { } from)
        {
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.TimestampTz) { Value = from };
            parameters.Add(p);
            sql.AppendLine($"  AND timestamp >= {p.ParameterName}");
        }

        if (request.TimeRange?.To is { } to)
        {
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.TimestampTz) { Value = to };
            parameters.Add(p);
            sql.AppendLine($"  AND timestamp <= {p.ParameterName}");
        }

        // --- TraceId ---
        if (request.TraceId is { } traceId)
        {
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Uuid) { Value = traceId };
            parameters.Add(p);
            sql.AppendLine($"  AND trace_id = {p.ParameterName}");
        }

        // --- Tags ---
        if (request.Tags is not null)
        {
            // tags.any → overlap operator &&
            if (request.Tags.Any is { Length: > 0 })
            {
                var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = request.Tags.Any
                };
                parameters.Add(p);
                sql.AppendLine($"  AND tags && {p.ParameterName}");
            }

            // tags.all → contains operator @>
            if (request.Tags.All is { Length: > 0 })
            {
                var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = request.Tags.All
                };
                parameters.Add(p);
                sql.AppendLine($"  AND tags @> {p.ParameterName}");
            }
        }

        // --- Search ---
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Text)
            {
                Value = $"%{request.Search}%"
            };
            parameters.Add(p);
            sql.AppendLine($"  AND message ILIKE {p.ParameterName}");
        }

        // --- Properties (JSONB containment) ---
        if (request.Properties is { Count: > 0 })
        {
            var jsonFilter = JsonSerializer.Serialize(request.Properties);
            var p = new NpgsqlParameter($"@p{paramIndex++}", NpgsqlDbType.Jsonb) { Value = jsonFilter };
            parameters.Add(p);
            sql.AppendLine($"  AND properties @> {p.ParameterName}");
        }

        // --- ORDER BY ---
        sql.AppendLine(IsDescending(request.OrderBy)
            ? "ORDER BY timestamp DESC, id DESC"
            : "ORDER BY timestamp ASC, id ASC");

        // --- LIMIT (fetch one extra to determine if there's a next page) ---
        var limitPlusOne = effectiveLimit + 1;
        var pLimit = new NpgsqlParameter($"@p{paramIndex}", NpgsqlDbType.Integer) { Value = limitPlusOne };
        parameters.Add(pLimit);
        sql.AppendLine($"LIMIT {pLimit.ParameterName}");

        return (sql.ToString(), parameters.ToArray());
    }

    private static bool IsDescending(string orderBy) =>
        string.Equals(orderBy, "timestamp_asc", StringComparison.OrdinalIgnoreCase) is false;

    private static (DateTimeOffset Timestamp, Guid Id) ParseCursor(string cursor)
    {
        var separatorIndex = cursor.LastIndexOf('|');
        if (separatorIndex < 0)
            throw new ArgumentException($"Invalid cursor format. Expected '{{timestamp}}|{{uuid}}', got: '{cursor}'");

        var tsPart = cursor[..separatorIndex];
        var idPart = cursor[(separatorIndex + 1)..];

        if (!DateTimeOffset.TryParse(tsPart, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
            throw new ArgumentException($"Invalid cursor: cannot parse timestamp '{tsPart}'");

        if (!Guid.TryParse(idPart, out var id))
            throw new ArgumentException($"Invalid cursor: cannot parse UUID '{idPart}'");

        return (ts, id);
    }

    /// <summary>
    /// Builds the next cursor string from the last entry in the result set.
    /// Format: "{ISO8601-timestamp}|{uuid}"
    /// </summary>
    public static string BuildCursor(DateTimeOffset timestamp, Guid id) =>
        $"{timestamp:O}|{id}";
}
