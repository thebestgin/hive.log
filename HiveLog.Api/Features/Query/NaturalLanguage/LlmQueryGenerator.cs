using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Query.NaturalLanguage;

/// <summary>
/// Stufe 2 of the NL-to-SQL pipeline: sends the user question to OpenAI Chat Completions
/// and receives a SQL SELECT query in return.
///
/// Privacy guarantee: the prompt contains ONLY the database schema and few-shot examples —
/// never any log data or user-generated content from the log_entries table.
///
/// Security: all generated SQL is validated before execution:
///   1. Read-only check — no INSERT/UPDATE/DELETE/DROP/TRUNCATE/CREATE/ALTER
///   2. Whitelist table check — only log_entries and log_summary_5min are allowed
///
/// When OpenAI is unavailable or the API key is not configured, returns null.
/// The caller falls back to { error: "no_match", confidence: 0 }.
/// </summary>
public class LlmQueryGenerator
{
    private static readonly string SystemPrompt = BuildSystemPrompt();

    private static readonly string[] ForbiddenKeywords =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "CREATE", "ALTER"
    ];

    private static readonly string[] AllowedTables =
    [
        "log_entries", "log_summary_5min"
    ];

    // Matches table names in FROM / JOIN clauses — word-boundary anchored
    private static readonly Regex RxTableName = new(
        @"\bFROM\s+([\w.]+)|\bJOIN\s+([\w.]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NlQueryOptions _options;
    private readonly ILogger<LlmQueryGenerator> _logger;

    public LlmQueryGenerator(
        IHttpClientFactory httpClientFactory,
        IOptions<NlQueryOptions> options,
        ILogger<LlmQueryGenerator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends the user query to OpenAI and returns validated SQL.
    /// Returns null when the API key is not configured or the request fails.
    /// Throws <see cref="SqlValidationException"/> when the generated SQL fails security checks.
    /// </summary>
    public async Task<string?> GenerateAsync(string userQuery, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey.StartsWith("<"))
        {
            _logger.LogWarning("OpenAI API key is not configured — NL-to-SQL LLM fallback disabled.");
            return null;
        }

        var payload = new
        {
            model = _options.Model,
            temperature = 0.0,
            max_tokens = 500,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userQuery }
            }
        };

        try
        {
            var http = _httpClientFactory.CreateClient("hivelog.nlquery");
            http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var content = new StringContent(
                JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");

            using var response = await http.PostAsync("v1/chat/completions", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenAI returned non-success status {Status} for model {Model}",
                    (int)response.StatusCode, _options.Model);
                return null;
            }

            var sql = ExtractSql(body);

            if (sql is null)
            {
                _logger.LogWarning(
                    "OpenAI response did not contain a parseable SQL statement. Model: {Model}",
                    _options.Model);
                return null;
            }

            ValidateSql(sql);
            return sql;
        }
        catch (SqlValidationException)
        {
            // Re-throw — caller distinguishes this from "unavailable"
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogInformation(
                "OpenAI not available or request timed out ({ExType}). Falling back to no_match.",
                ex.GetType().Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error calling OpenAI. Falling back to no_match.");
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SQL Extraction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the SQL statement from the OpenAI Chat Completions response.
    /// OpenAI returns {"choices":[{"message":{"content":"SELECT ..."}}]}.
    /// </summary>
    private static string? ExtractSql(string jsonBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonBody);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();

            if (string.IsNullOrWhiteSpace(content))
                return null;

            // Strip markdown code fences (```sql ... ```)
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var lines = content.Split('\n');
                var sqlLines = lines
                    .Skip(1)
                    .TakeWhile(l => !l.TrimStart().StartsWith("```", StringComparison.Ordinal))
                    .ToList();
                content = string.Join('\n', sqlLines).Trim();
            }

            // Must start with SELECT
            if (!content.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                return null;

            return content;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SQL Validation
    // ──────────────────────────────────────────────────────────────────────────

    private static void ValidateSql(string sql)
    {
        foreach (var keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(sql, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
                throw new SqlValidationException($"SQL contains forbidden keyword: {keyword}");
        }

        var tableMatches = RxTableName.Matches(sql);
        foreach (Match match in tableMatches)
        {
            var tableName = (match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : match.Groups[2].Value)
                .ToLowerInvariant()
                .Trim();

            if (!AllowedTables.Contains(tableName))
                throw new SqlValidationException($"SQL references disallowed table: {tableName}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // System Prompt — schema-only, no log data
    // ──────────────────────────────────────────────────────────────────────────

    private static string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Du bist ein SQL-Generator für TimescaleDB.");
        sb.AppendLine("Generiere NUR lesende SELECT-Queries auf diese Tabellen:");
        sb.AppendLine();
        sb.AppendLine("Tabelle: log_entries");
        sb.AppendLine("  timestamp       TIMESTAMPTZ  -- Zeitstempel des Log-Eintrags (UTC)");
        sb.AppendLine("  id              UUID         -- Eindeutige ID");
        sb.AppendLine("  trace_id        TEXT         -- W3C TraceId: gleich über alle Ebenen eines Requests (nullable)");
        sb.AppendLine("  span_id         TEXT         -- W3C SpanId: eindeutig für diesen Verarbeitungsschritt (nullable)");
        sb.AppendLine("  parent_span_id  TEXT         -- W3C ParentSpanId: SpanId des aufrufenden Schritts (nullable, null=Root)");
        sb.AppendLine("  source          TEXT         -- Service-Name, z.B. 'talents-api', 'connect-app'");
        sb.AppendLine("  level           SMALLINT     -- 0=Trace, 1=Debug, 2=Info, 3=Warn, 4=Error, 5=Fatal");
        sb.AppendLine("  category        TEXT         -- Logger-Name / Komponenten-Name");
        sb.AppendLine("  message         TEXT         -- Formatierter Log-Text");
        sb.AppendLine("  properties      JSONB        -- Strukturierte Key-Value-Daten (nullable)");
        sb.AppendLine("  tags            TEXT[]       -- Array von Tags (nullable)");
        sb.AppendLine("  stream          TEXT         -- Log-Stream, z.B. 'app', 'audit', 'agent'");
        sb.AppendLine();
        sb.AppendLine("Tabelle: log_summary_5min");
        sb.AppendLine("  bucket          TIMESTAMPTZ  -- 5-Minuten-Zeitfenster (TimescaleDB continuous aggregate)");
        sb.AppendLine("  source          TEXT         -- Service-Name");
        sb.AppendLine("  level           SMALLINT     -- Log-Level (0-5)");
        sb.AppendLine("  stream          TEXT         -- Log-Stream");
        sb.AppendLine("  count           BIGINT       -- Anzahl Log-Einträge in diesem Zeitfenster");
        sb.AppendLine();
        sb.AppendLine("Regeln:");
        sb.AppendLine("  - Nur SELECT-Statements, kein INSERT/UPDATE/DELETE/DROP/TRUNCATE/CREATE/ALTER");
        sb.AppendLine("  - Nur log_entries und log_summary_5min als Tabellen");
        sb.AppendLine("  - Kein Semikolon am Ende");
        sb.AppendLine("  - Kein Markdown, nur reines SQL");
        sb.AppendLine("  - Standardmäßig ORDER BY timestamp DESC LIMIT 100");
        sb.AppendLine();
        sb.AppendLine("Beispiele:");
        sb.AppendLine();
        sb.AppendLine("Frage: Alle Fehler der letzten Stunde");
        sb.AppendLine("SQL: SELECT timestamp, id, source, level, category, message FROM log_entries WHERE level >= 4 AND timestamp >= NOW() - INTERVAL '1 hour' ORDER BY timestamp DESC LIMIT 100");
        sb.AppendLine();
        sb.AppendLine("Frage: Wie viele Logs pro Service heute?");
        sb.AppendLine("SQL: SELECT source, COUNT(*) AS count FROM log_entries WHERE timestamp >= CURRENT_DATE ORDER BY count DESC");
        sb.AppendLine();
        sb.AppendLine("Frage: Fatal errors in talents-api where properties contains userId");
        sb.AppendLine("SQL: SELECT timestamp, id, source, level, message, properties FROM log_entries WHERE level = 5 AND source = 'talents-api' AND properties ? 'userId' ORDER BY timestamp DESC LIMIT 100");
        sb.AppendLine();
        sb.AppendLine("Frage: Log-Volumen pro 5 Minuten der letzten 2 Stunden");
        sb.AppendLine("SQL: SELECT bucket, source, SUM(count) AS total FROM log_summary_5min WHERE bucket >= NOW() - INTERVAL '2 hours' GROUP BY bucket, source ORDER BY bucket DESC");
        sb.AppendLine();
        sb.AppendLine("Frage: Alle Warnungen und Fehler vom engagement-api mit Tag 'sync'");
        sb.AppendLine("SQL: SELECT timestamp, id, source, level, category, message, tags FROM log_entries WHERE level >= 3 AND source = 'engagement-api' AND 'sync' = ANY(tags) ORDER BY timestamp DESC LIMIT 100");

        return sb.ToString().TrimEnd();
    }
}
