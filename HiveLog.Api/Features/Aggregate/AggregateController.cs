using System.Text;
using HiveLog.Api.Features.Aggregate.Models;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;

namespace HiveLog.Api.Features.Aggregate;

[ApiController]
[Route("api/hivelog/v1")]
public sealed class AggregateController : ControllerBase
{
    private static readonly HashSet<string> AllowedGroupByFields =
        new(StringComparer.OrdinalIgnoreCase) { "source", "level", "stream" };

    private readonly NpgsqlDataSource _dataSource;

    public AggregateController(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>
    /// Time-bucket aggregation over log entries.
    /// Uses the log_summary_5min continuous aggregate when bucket >= 5 minutes,
    /// otherwise queries log_entries directly.
    /// </summary>
    [HttpPost("aggregate")]
    [ProducesResponseType<AggregateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Aggregate([FromBody] AggregateRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (request.Metric != "count")
            return BadRequest(new { error = "Only metric 'count' is supported" });

        if (!TryParseBucket(request.Bucket, out var bucketMinutes, out var bucketInterval))
            return BadRequest(new { error = $"Invalid bucket format '{request.Bucket}'. Use e.g. '5m', '1h', '1d'." });

        var invalidGroupBy = request.GroupBy
            .Where(f => !AllowedGroupByFields.Contains(f))
            .ToArray();

        if (invalidGroupBy.Length > 0)
            return BadRequest(new { error = $"Invalid groupBy fields: {string.Join(", ", invalidGroupBy)}. Allowed: source, level, stream." });

        if (request.TimeRange.From >= request.TimeRange.To)
            return BadRequest(new { error = "timeRange.from must be before timeRange.to" });

        var buckets = bucketMinutes >= 5
            ? await QuerySummaryView(request, bucketInterval, ct)
            : await QueryLogEntriesDirect(request, bucketInterval, ct);

        return Ok(new AggregateResponse { Buckets = buckets });
    }

    // -------------------------------------------------------------------------
    // Summary view query (bucket >= 5 min)
    // -------------------------------------------------------------------------

    private async Task<AggregateBucket[]> QuerySummaryView(
        AggregateRequest request,
        string bucketInterval,
        CancellationToken ct)
    {
        var sql = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();

        // SELECT time_bucket(interval, bucket) AS time, [groupBy cols], SUM(log_count) AS count
        sql.Append("SELECT time_bucket(@bucket_interval, bucket) AS time");

        if (request.GroupBy.Contains("source", StringComparer.OrdinalIgnoreCase))
            sql.Append(", source");
        if (request.GroupBy.Contains("level", StringComparer.OrdinalIgnoreCase))
            sql.Append(", level");
        if (request.GroupBy.Contains("stream", StringComparer.OrdinalIgnoreCase))
            sql.Append(", stream");

        sql.Append(", SUM(log_count)::bigint AS count");
        sql.AppendLine(" FROM log_summary_5min");
        sql.AppendLine(" WHERE bucket >= @from AND bucket < @to");

        parameters.Add(new NpgsqlParameter("bucket_interval", NpgsqlDbType.Interval) { Value = ParseInterval(bucketInterval) });
        parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = request.TimeRange.From });
        parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = request.TimeRange.To });

        if (request.Stream is not null)
        {
            sql.AppendLine(" AND stream = @stream");
            parameters.Add(new NpgsqlParameter("stream", NpgsqlDbType.Text) { Value = request.Stream });
        }

        sql.Append(" GROUP BY time");
        if (request.GroupBy.Contains("source", StringComparer.OrdinalIgnoreCase))
            sql.Append(", source");
        if (request.GroupBy.Contains("level", StringComparer.OrdinalIgnoreCase))
            sql.Append(", level");
        if (request.GroupBy.Contains("stream", StringComparer.OrdinalIgnoreCase))
            sql.Append(", stream");

        sql.AppendLine(" ORDER BY time ASC");

        return await ExecuteAggregateQuery(sql.ToString(), parameters, request.GroupBy, ct);
    }

    // -------------------------------------------------------------------------
    // Direct log_entries query (bucket < 5 min)
    // -------------------------------------------------------------------------

    private async Task<AggregateBucket[]> QueryLogEntriesDirect(
        AggregateRequest request,
        string bucketInterval,
        CancellationToken ct)
    {
        var sql = new StringBuilder();
        var parameters = new List<NpgsqlParameter>();

        sql.Append("SELECT time_bucket(@bucket_interval, timestamp) AS time");

        if (request.GroupBy.Contains("source", StringComparer.OrdinalIgnoreCase))
            sql.Append(", source");
        if (request.GroupBy.Contains("level", StringComparer.OrdinalIgnoreCase))
            sql.Append(", level");
        if (request.GroupBy.Contains("stream", StringComparer.OrdinalIgnoreCase))
            sql.Append(", stream");

        sql.AppendLine(", count(*)::bigint AS count");
        sql.AppendLine(" FROM log_entries");
        sql.AppendLine(" WHERE timestamp >= @from AND timestamp < @to");

        parameters.Add(new NpgsqlParameter("bucket_interval", NpgsqlDbType.Interval) { Value = ParseInterval(bucketInterval) });
        parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = request.TimeRange.From });
        parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = request.TimeRange.To });

        if (request.Stream is not null)
        {
            sql.AppendLine(" AND stream = @stream");
            parameters.Add(new NpgsqlParameter("stream", NpgsqlDbType.Text) { Value = request.Stream });
        }

        sql.Append(" GROUP BY time");
        if (request.GroupBy.Contains("source", StringComparer.OrdinalIgnoreCase))
            sql.Append(", source");
        if (request.GroupBy.Contains("level", StringComparer.OrdinalIgnoreCase))
            sql.Append(", level");
        if (request.GroupBy.Contains("stream", StringComparer.OrdinalIgnoreCase))
            sql.Append(", stream");

        sql.AppendLine(" ORDER BY time ASC");

        return await ExecuteAggregateQuery(sql.ToString(), parameters, request.GroupBy, ct);
    }

    // -------------------------------------------------------------------------
    // Shared execution
    // -------------------------------------------------------------------------

    private async Task<AggregateBucket[]> ExecuteAggregateQuery(
        string sql,
        List<NpgsqlParameter> parameters,
        string[] groupBy,
        CancellationToken ct)
    {
        var includeSource = groupBy.Contains("source", StringComparer.OrdinalIgnoreCase);
        var includeLevel = groupBy.Contains("level", StringComparer.OrdinalIgnoreCase);
        var includeStream = groupBy.Contains("stream", StringComparer.OrdinalIgnoreCase);

        var results = new List<AggregateBucket>();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            int col = 0;
            var time = reader.GetFieldValue<DateTimeOffset>(col++);
            string? source = null;
            short? level = null;
            string? stream = null;

            if (includeSource) source = reader.GetString(col++);
            if (includeLevel) level = reader.GetInt16(col++);
            if (includeStream) stream = reader.GetString(col++);

            var count = reader.GetInt64(col);

            results.Add(new AggregateBucket
            {
                Time = time,
                Source = source,
                Level = level,
                Stream = stream,
                Count = count,
            });
        }

        return results.ToArray();
    }

    // -------------------------------------------------------------------------
    // Bucket parsing helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses bucket strings like "5m", "1h", "1d" into minutes and a PostgreSQL interval string.
    /// Returns false if the format is invalid.
    /// </summary>
    private static bool TryParseBucket(string bucket, out int totalMinutes, out string intervalLiteral)
    {
        totalMinutes = 0;
        intervalLiteral = string.Empty;

        if (string.IsNullOrWhiteSpace(bucket) || bucket.Length < 2)
            return false;

        var unit = bucket[^1];
        if (!int.TryParse(bucket[..^1], out var value) || value <= 0)
            return false;

        switch (unit)
        {
            case 'm':
                totalMinutes = value;
                intervalLiteral = $"{value} minutes";
                return true;
            case 'h':
                totalMinutes = value * 60;
                intervalLiteral = $"{value} hours";
                return true;
            case 'd':
                totalMinutes = value * 60 * 24;
                intervalLiteral = $"{value} days";
                return true;
            default:
                return false;
        }
    }

    private static TimeSpan ParseInterval(string intervalLiteral)
    {
        // intervalLiteral is one of: "{N} minutes", "{N} hours", "{N} days"
        var parts = intervalLiteral.Split(' ');
        var value = int.Parse(parts[0]);
        return parts[1] switch
        {
            "minutes" => TimeSpan.FromMinutes(value),
            "hours"   => TimeSpan.FromHours(value),
            "days"    => TimeSpan.FromDays(value),
            _         => throw new ArgumentException($"Unknown interval unit: {parts[1]}")
        };
    }
}
