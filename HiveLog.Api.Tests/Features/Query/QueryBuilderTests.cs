using HiveLog.Api.Features.Query;
using HiveLog.Api.Features.Query.Models;
using NpgsqlTypes;

namespace HiveLog.Api.Tests.Features.Query;

public class QueryBuilderTests
{
    // --- Helpers ---

    private static (string Sql, Npgsql.NpgsqlParameter[] Parameters) Build(QueryRequest request)
        => QueryBuilder.Build(request);

    // --- Base query ---

    [Fact]
    public void EmptyRequest_ProducesBaseSelect()
    {
        var (sql, parameters) = Build(new QueryRequest());

        Assert.Contains("FROM log_entries", sql);
        Assert.Contains("LIMIT", sql);
        Assert.NotEmpty(parameters); // at least the LIMIT parameter
    }

    // --- Limit clamping ---

    [Fact]
    public void LimitClamped_Below1()
    {
        var (sql, parameters) = Build(new QueryRequest { Limit = 0 });

        // Limit 0 → clamped to 1 → LIMIT = 1+1 = 2
        var limitParam = parameters.Last();
        Assert.Equal(2, limitParam.Value);
    }

    [Fact]
    public void LimitClamped_Above1000()
    {
        var (sql, parameters) = Build(new QueryRequest { Limit = 5000 });

        // Limit 5000 → clamped to 1000 → LIMIT = 1000+1 = 1001
        var limitParam = parameters.Last();
        Assert.Equal(1001, limitParam.Value);
    }

    // --- Source filter ---

    [Fact]
    public void SourceFilter_AddsAnyClause()
    {
        var (sql, parameters) = Build(new QueryRequest
        {
            Sources = ["talents-api"]
        });

        Assert.Contains("AND source = ANY(", sql);
        var p = parameters.First(x => x.NpgsqlDbType == (NpgsqlDbType.Array | NpgsqlDbType.Text));
        Assert.Equal(new[] { "talents-api" }, p.Value);
    }

    // --- Level filter ---

    [Fact]
    public void LevelFilter_AddsGteClause()
    {
        var (sql, parameters) = Build(new QueryRequest
        {
            Levels = new LevelFilter { Min = 4 }
        });

        Assert.Contains("AND level >= ", sql);
        var p = parameters.First(x => x.NpgsqlDbType == NpgsqlDbType.Smallint);
        Assert.Equal((short)4, p.Value);
    }

    // --- Time range ---

    [Fact]
    public void TimeRange_From_AddsGte()
    {
        var from = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var (sql, parameters) = Build(new QueryRequest
        {
            TimeRange = new TimeRangeFilter { From = from }
        });

        Assert.Contains("AND timestamp >= ", sql);
        var p = parameters.First(x => x.NpgsqlDbType == NpgsqlDbType.TimestampTz && Equals(x.Value, from));
        Assert.Equal(from, p.Value);
    }

    [Fact]
    public void TimeRange_To_AddsLte()
    {
        var to = new DateTimeOffset(2026, 4, 30, 23, 59, 59, TimeSpan.Zero);
        var (sql, parameters) = Build(new QueryRequest
        {
            TimeRange = new TimeRangeFilter { To = to }
        });

        Assert.Contains("AND timestamp <= ", sql);
        var p = parameters.First(x => x.NpgsqlDbType == NpgsqlDbType.TimestampTz && Equals(x.Value, to));
        Assert.Equal(to, p.Value);
    }

    // --- Search ---

    [Fact]
    public void Search_AddsIlike()
    {
        var (sql, parameters) = Build(new QueryRequest { Search = "crash" });

        Assert.Contains("AND message ILIKE ", sql);
        var p = parameters.First(x => x.Value is string s && s.Contains("crash"));
        Assert.Equal("%crash%", p.Value);
    }

    // --- TraceId ---

    [Fact]
    public void TraceId_AddsEqClause()
    {
        var traceId = "abc-123";
        var (sql, parameters) = Build(new QueryRequest { TraceId = traceId });

        Assert.Contains("AND trace_id = ", sql);
        var p = parameters.First(x => x.NpgsqlDbType == NpgsqlDbType.Text && Equals(x.Value, traceId));
        Assert.Equal(traceId, p.Value);
    }

    // --- Tags ---

    [Fact]
    public void Tags_Any_AddsOverlap()
    {
        var (sql, _) = Build(new QueryRequest
        {
            Tags = new TagFilter { Any = ["critical", "error"] }
        });

        Assert.Contains("AND tags && ", sql);
    }

    [Fact]
    public void Tags_All_AddsContains()
    {
        var (sql, _) = Build(new QueryRequest
        {
            Tags = new TagFilter { All = ["critical", "error"] }
        });

        Assert.Contains("AND tags @> ", sql);
    }

    // --- Properties ---

    [Fact]
    public void Properties_AddsJsonbContains()
    {
        var (sql, _) = Build(new QueryRequest
        {
            Properties = new Dictionary<string, object?> { { "UserId", "abc123" } }
        });

        Assert.Contains("AND properties @> ", sql);
    }

    // --- ORDER BY ---

    [Fact]
    public void OrderBy_Desc_Default()
    {
        var (sql, _) = Build(new QueryRequest { OrderBy = "timestamp_desc" });

        Assert.Contains("ORDER BY timestamp DESC, id DESC", sql);
    }

    [Fact]
    public void OrderBy_Asc()
    {
        var (sql, _) = Build(new QueryRequest { OrderBy = "timestamp_asc" });

        Assert.Contains("ORDER BY timestamp ASC, id ASC", sql);
    }

    // --- Cursor ---

    [Fact]
    public void Cursor_Desc_AddsLtClause()
    {
        var ts = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var cursor = QueryBuilder.BuildCursor(ts, id);

        var (sql, _) = Build(new QueryRequest { Cursor = cursor, OrderBy = "timestamp_desc" });

        Assert.Contains("AND (timestamp, id) < (", sql);
    }

    [Fact]
    public void Cursor_Asc_AddsGtClause()
    {
        var ts = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var cursor = QueryBuilder.BuildCursor(ts, id);

        var (sql, _) = Build(new QueryRequest { Cursor = cursor, OrderBy = "timestamp_asc" });

        Assert.Contains("AND (timestamp, id) > (", sql);
    }

    [Fact]
    public void Cursor_InvalidFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Build(new QueryRequest { Cursor = "no-separator-here" }));
    }

    // --- BuildCount ---

    [Fact]
    public void BuildCount_ProducesCountStar()
    {
        var (sql, _) = QueryBuilder.BuildCount(new QueryRequest());

        Assert.StartsWith("SELECT count(*)", sql.TrimStart());
    }
}
