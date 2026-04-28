using System.Diagnostics;
using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Query.Models;
using HiveLog.Api.Features.Query.NaturalLanguage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace HiveLog.Api.Features.Query;

[ApiController]
[Route("api/hivelog/v1")]
public class QueryController : ControllerBase
{
    private const double SlowQueryThresholdMs = 500.0;

    // Confidence for LLM-generated SQL — lower than template-matcher minimum (0.7)
    // so callers can distinguish template-matched vs LLM-generated results.
    private const double LlmConfidence = 0.6;

    private readonly NpgsqlDataSource _dataSource;
    private readonly IngestMetrics _metrics;
    private readonly SelfLogger _selfLogger;
    private readonly LlmQueryGenerator _llmQueryGenerator;
    private readonly NlQueryOptions _nlQueryOptions;

    public QueryController(
        NpgsqlDataSource dataSource,
        IngestMetrics metrics,
        SelfLogger selfLogger,
        LlmQueryGenerator llmQueryGenerator,
        IOptions<NlQueryOptions> nlQueryOptions)
    {
        _dataSource = dataSource;
        _metrics = metrics;
        _selfLogger = selfLogger;
        _llmQueryGenerator = llmQueryGenerator;
        _nlQueryOptions = nlQueryOptions.Value;
    }

    /// <summary>
    /// Structured query for log entries with filtering and cursor-based pagination.
    /// Returns up to <c>limit</c> entries matching all specified filters.
    /// </summary>
    [HttpPost("query")]
    [ProducesResponseType<QueryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Query([FromBody] QueryRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (request.Limit is < 1 or > 1000)
            return BadRequest(new { error = "limit must be between 1 and 1000" });

        string sql;
        NpgsqlParameter[] parameters;
        try
        {
            (sql, parameters) = QueryBuilder.Build(request);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var effectiveLimit = Math.Clamp(request.Limit, 1, 1000);
        var entries = new List<LogEntryResult>();

        var sw = Stopwatch.StartNew();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entries.Add(MapRow(reader));
        }

        sw.Stop();
        var elapsedMs = sw.Elapsed.TotalMilliseconds;
        _metrics.RecordQueryLatency(elapsedMs);

        if (elapsedMs > SlowQueryThresholdMs)
        {
            _selfLogger.Warn(
                "Slow query detected",
                new { elapsedMs = (long)elapsedMs, endpoint = "query" });
        }

        // We fetched limit+1 — if we got more than limit, there's a next page
        string? nextCursor = null;
        if (entries.Count > effectiveLimit)
        {
            entries.RemoveAt(entries.Count - 1);
            var last = entries[^1];
            nextCursor = QueryBuilder.BuildCursor(last.Timestamp, last.Id);
        }

        return Ok(new QueryResponse
        {
            Entries = entries,
            NextCursor = nextCursor
        });
    }

    /// <summary>
    /// Natural language query for log entries.
    /// Translates a plain-text question into a parameterized SQL query via a regex-based
    /// template matcher. Covers ~70-80% of common agent queries without AI.
    ///
    /// Security: runs in a read-only transaction. Only log_entries and log_summary_5min
    /// are allowed tables. All values are NpgsqlParameters — no string interpolation of input.
    ///
    /// When no pattern matches: returns 200 with confidence=0 and error="no_match" (no 500).
    /// </summary>
    [HttpPost("query/natural")]
    [ProducesResponseType<NaturalQueryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> QueryNatural([FromBody] NaturalQueryRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "query must not be empty" });

        var parsed = TemplateQueryParser.TryParse(request.Query);
        if (parsed is null)
        {
            // Stufe 2: LLM fallback via Ollama
            if (!_nlQueryOptions.Enabled)
            {
                return Ok(new NaturalQueryResponse
                {
                    Confidence = 0,
                    Error = "no_match"
                });
            }

            return await QueryNaturalWithLlm(request.Query, ct);
        }

        string sql;
        NpgsqlParameter[] parameters;
        try
        {
            if (parsed.Kind == TemplateQueryParser.QueryKind.Count)
                (sql, parameters) = QueryBuilder.BuildCount(parsed.Request);
            else
                (sql, parameters) = QueryBuilder.Build(parsed.Request);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var sw = Stopwatch.StartNew();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Read-only transaction — security guard: generated SQL must not write data
        await using var tx = await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, ct);
        await using var setReadOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx);
        await setReadOnly.ExecuteNonQueryAsync(ct);

        try
        {
            if (parsed.Kind == TemplateQueryParser.QueryKind.Count)
            {
                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                cmd.Parameters.AddRange(parameters);
                var scalar = await cmd.ExecuteScalarAsync(ct);
                var count = Convert.ToInt64(scalar ?? 0L);

                await tx.RollbackAsync(ct);

                sw.Stop();
                RecordQueryMetrics(sw.Elapsed.TotalMilliseconds, "query/natural");

                return Ok(new NaturalQueryResponse
                {
                    InterpretedQuery = parsed.Request,
                    Sql = sql,
                    Count = count,
                    Confidence = parsed.Confidence
                });
            }
            else
            {
                var effectiveLimit = Math.Clamp(parsed.Request.Limit, 1, 1000);
                var entries = new List<LogEntryResult>();

                await using var cmd = new NpgsqlCommand(sql, conn, tx);
                cmd.Parameters.AddRange(parameters);

                // Reader must be closed before calling tx.RollbackAsync —
                // Npgsql does not allow a second command on a connection while a reader is still open.
                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    while (await reader.ReadAsync(ct))
                    {
                        entries.Add(MapRow(reader));
                    }
                }

                string? nextCursor = null;
                if (entries.Count > effectiveLimit)
                {
                    entries.RemoveAt(entries.Count - 1);
                    var last = entries[^1];
                    nextCursor = QueryBuilder.BuildCursor(last.Timestamp, last.Id);
                }

                await tx.RollbackAsync(ct);

                sw.Stop();
                RecordQueryMetrics(sw.Elapsed.TotalMilliseconds, "query/natural");

                return Ok(new NaturalQueryResponse
                {
                    InterpretedQuery = parsed.Request,
                    Sql = sql,
                    Result = new QueryResponse { Entries = entries, NextCursor = nextCursor },
                    Confidence = parsed.Confidence
                });
            }
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Stufe 2: Falls the template-matcher found no match, asks Ollama to generate SQL.
    /// The generated SQL is validated before execution (read-only + whitelist tables).
    /// </summary>
    private async Task<IActionResult> QueryNaturalWithLlm(string userQuery, CancellationToken ct)
    {
        string? sql;
        try
        {
            sql = await _llmQueryGenerator.GenerateAsync(userQuery, ct);
        }
        catch (SqlValidationException ex)
        {
            _selfLogger.Warn("LLM generated unsafe SQL", new { reason = ex.Message, query = userQuery });
            return Ok(new NaturalQueryResponse
            {
                Confidence = 0,
                Error = "unsafe_sql"
            });
        }

        if (sql is null)
        {
            return Ok(new NaturalQueryResponse
            {
                Confidence = 0,
                Error = "no_match"
            });
        }

        var sw = Stopwatch.StartNew();

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Read-only transaction — defence-in-depth on top of SQL validation
        await using var tx = await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, ct);
        await using var setReadOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx);
        await setReadOnly.ExecuteNonQueryAsync(ct);

        try
        {
            var entries = new List<LogEntryResult>();
            await using var cmd = new NpgsqlCommand(sql, conn, tx);

            // Reader must be closed before calling tx.RollbackAsync —
            // Npgsql does not allow a second command on a connection while a reader is still open.
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    entries.Add(MapRow(reader));
                }
            }

            await tx.RollbackAsync(ct);

            sw.Stop();
            RecordQueryMetrics(sw.Elapsed.TotalMilliseconds, "query/natural/llm");

            return Ok(new NaturalQueryResponse
            {
                Sql = sql,
                Result = new QueryResponse { Entries = entries },
                Confidence = LlmConfidence
            });
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private void RecordQueryMetrics(double elapsedMs, string endpoint)
    {
        _metrics.RecordQueryLatency(elapsedMs);

        if (elapsedMs > SlowQueryThresholdMs)
        {
            _selfLogger.Warn(
                "Slow query detected",
                new { elapsedMs = (long)elapsedMs, endpoint });
        }
    }

    private static LogEntryResult MapRow(NpgsqlDataReader reader) => new()
    {
        Timestamp = reader.GetFieldValue<DateTimeOffset>(0),
        Id = reader.GetGuid(1),
        TraceId = reader.IsDBNull(2) ? null : reader.GetString(2),
        SpanId = reader.IsDBNull(3) ? null : reader.GetString(3),
        Source = reader.GetString(4),
        SourceType = reader.GetString(5),
        InstanceId = reader.IsDBNull(6) ? null : reader.GetString(6),
        Level = reader.GetInt16(7),
        Category = reader.GetString(8),
        Message = reader.GetString(9),
        MessageTemplate = reader.IsDBNull(10) ? null : reader.GetString(10),
        Properties = reader.IsDBNull(11) ? null : reader.GetString(11),
        Exception = reader.IsDBNull(12) ? null : reader.GetString(12),
        UserId = reader.IsDBNull(13) ? null : reader.GetGuid(13),
        RequestId = reader.IsDBNull(14) ? null : reader.GetString(14),
        SessionId = reader.IsDBNull(15) ? null : reader.GetString(15),
        Tags = reader.IsDBNull(16) ? null : reader.GetFieldValue<string[]>(16),
        Stream = reader.GetString(17),
    };
}
