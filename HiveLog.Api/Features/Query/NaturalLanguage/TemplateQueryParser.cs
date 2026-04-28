using System.Text.RegularExpressions;
using HiveLog.Api.Features.Query.Models;

namespace HiveLog.Api.Features.Query.NaturalLanguage;

/// <summary>
/// Regex-based template matcher for natural language log queries.
/// Covers ~70-80% of common agent queries without AI or external dependencies.
///
/// Confidence scale:
///   1.0  — exact structured match (trace ID, explicit level + source + time)
///   0.85 — strong pattern match (known keyword combo)
///   0.7  — partial pattern match (single dimension)
///   0.4  — free-text fallback (no structured pattern found, 3+ words)
///   0.0  — no match
///
/// All patterns are evaluated in priority order; first match wins.
/// </summary>
public static class TemplateQueryParser
{
    public enum QueryKind { Entries, Count }

    public sealed record ParseResult(
        QueryRequest Request,
        QueryKind Kind,
        double Confidence
    );

    // ──────────────────────────────────────────────────────────────────────────
    // Compiled regexes (static, reused across requests)
    // ──────────────────────────────────────────────────────────────────────────

    // Rule 3: Trace-ID lookup — UUID in any position
    private static readonly Regex RxTraceId = new(
        @"\b(?<uuid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Rule 4: Last N minutes
    private static readonly Regex RxLastMinutes = new(
        @"\b(letzte[n]?|last)\s+(?<n>\d+)\s+(minuten?|min|minutes?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Rule 1 variant: Errors today (German + English)
    private static readonly Regex RxErrorsToday = new(
        @"\b(fehler|errors?|error)\b.*\b(heute|today)\b|\b(heute|today)\b.*\b(fehler|errors?|error)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Rule 2: Errors in <service>
    private static readonly Regex RxErrorsInService = new(
        @"\b(errors?|fehler)\s+(?:in|bei|from|von)\s+(?<svc>[\w][\w.-]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Rule 6: Source filter — "von <x>-api" or "from <x>-api" or "service <x>"
    private static readonly Regex RxSourceFilter = new(
        @"\b(?:von|from|service|source)\s+(?<svc>[\w][\w.-]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Rule 8: Count queries
    private static readonly Regex RxCount = new(
        @"\b(wie\s+viele|how\s+many|anzahl|count)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Rule 5: Level keywords
    private static readonly Regex RxLevelFatal = new(
        @"\b(fatal|kritisch|critical)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxLevelError = new(
        @"\b(errors?|fehler)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RxLevelWarn = new(
        @"\b(warn(?:ing)?s?|warnungen?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Time: today / heute
    private static readonly Regex RxToday = new(
        @"\b(heute|today)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Time: yesterday / gestern
    private static readonly Regex RxYesterday = new(
        @"\b(gestern|yesterday)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Time: last hour / letzte Stunde
    private static readonly Regex RxLastHour = new(
        @"\b(letzte[n]?\s+stunde|last\s+hour)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse a natural language query into a structured QueryRequest.
    /// Returns null when no pattern matches (caller should return confidence=0, error=no_match).
    /// </summary>
    public static ParseResult? TryParse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        // ── Rule 3: Trace-ID lookup (highest priority — very specific) ──────
        var traceMatch = RxTraceId.Match(query);
        if (traceMatch.Success && Guid.TryParse(traceMatch.Groups["uuid"].Value, out var traceId))
        {
            return new ParseResult(
                new QueryRequest { TraceId = traceId, Limit = 100 },
                QueryKind.Entries,
                1.0
            );
        }

        // ── Rule 8: Count query ──────────────────────────────────────────────
        if (RxCount.IsMatch(query))
        {
            var countRequest = BuildBaseRequest(query);
            return new ParseResult(countRequest, QueryKind.Count, 0.85);
        }

        // ── Rule 1: Errors today ─────────────────────────────────────────────
        if (RxErrorsToday.IsMatch(query))
        {
            var todayStart = GetTodayStart();
            return new ParseResult(
                new QueryRequest
                {
                    Levels = new LevelFilter { Min = 4 },
                    TimeRange = new TimeRangeFilter { From = todayStart },
                    Limit = 100
                },
                QueryKind.Entries,
                0.85
            );
        }

        // ── Rule 2: Errors in <service> ──────────────────────────────────────
        var errInSvc = RxErrorsInService.Match(query);
        if (errInSvc.Success)
        {
            var svc = errInSvc.Groups["svc"].Value;
            var req = new QueryRequest
            {
                Sources = [svc],
                Levels = new LevelFilter { Min = 4 },
                Limit = 100
            };
            ApplyTimeContext(query, req);
            return new ParseResult(req, QueryKind.Entries, 0.85);
        }

        // ── Rule 4: Last N minutes ───────────────────────────────────────────
        var lastMinMatch = RxLastMinutes.Match(query);
        if (lastMinMatch.Success && int.TryParse(lastMinMatch.Groups["n"].Value, out var minutes))
        {
            var req = new QueryRequest
            {
                TimeRange = new TimeRangeFilter
                {
                    From = DateTimeOffset.UtcNow.AddMinutes(-minutes)
                },
                Limit = 100
            };
            ApplyLevelFilter(query, req);
            ApplySourceFilter(query, req);
            return new ParseResult(req, QueryKind.Entries, 0.85);
        }

        // ── Rule 6: Source filter with time/level context ────────────────────
        var srcMatch = RxSourceFilter.Match(query);
        if (srcMatch.Success)
        {
            var svc = srcMatch.Groups["svc"].Value;
            var req = new QueryRequest
            {
                Sources = [svc],
                Limit = 100
            };
            ApplyLevelFilter(query, req);
            ApplyTimeContext(query, req);
            return new ParseResult(req, QueryKind.Entries, 0.7);
        }

        // ── Rule 5: Level-only filter ────────────────────────────────────────
        short? level = ExtractLevel(query);
        if (level.HasValue)
        {
            var req = new QueryRequest
            {
                Levels = new LevelFilter { Min = level.Value },
                Limit = 100
            };
            ApplyTimeContext(query, req);
            return new ParseResult(req, QueryKind.Entries, 0.7);
        }

        // ── Rule 7: Free-text fallback (3+ words, no pattern matched) ────────
        var wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount >= 3)
        {
            return new ParseResult(
                new QueryRequest { Search = query, Limit = 100 },
                QueryKind.Entries,
                0.4
            );
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a base QueryRequest from a count query — applies level/source/time context.
    /// </summary>
    private static QueryRequest BuildBaseRequest(string query)
    {
        var req = new QueryRequest { Limit = 1 };
        ApplyLevelFilter(query, req);
        ApplySourceFilter(query, req);
        ApplyTimeContext(query, req);
        return req;
    }

    /// <summary>Applies level filter from level keywords if found.</summary>
    private static void ApplyLevelFilter(string query, QueryRequest req)
    {
        var level = ExtractLevel(query);
        if (level.HasValue)
            req.Levels = new LevelFilter { Min = level.Value };
    }

    /// <summary>Applies source filter from "von/from/service <name>" if found.</summary>
    private static void ApplySourceFilter(string query, QueryRequest req)
    {
        var m = RxSourceFilter.Match(query);
        if (m.Success)
            req.Sources = [m.Groups["svc"].Value];
    }

    /// <summary>Applies time range from today/yesterday/last-hour keywords if found.</summary>
    private static void ApplyTimeContext(string query, QueryRequest req)
    {
        if (RxLastHour.IsMatch(query))
        {
            req.TimeRange = new TimeRangeFilter { From = DateTimeOffset.UtcNow.AddHours(-1) };
            return;
        }
        if (RxYesterday.IsMatch(query))
        {
            var yesterday = DateTimeOffset.UtcNow.Date.AddDays(-1);
            req.TimeRange = new TimeRangeFilter
            {
                From = new DateTimeOffset(yesterday, TimeSpan.Zero),
                To = new DateTimeOffset(yesterday.AddDays(1), TimeSpan.Zero)
            };
            return;
        }
        if (RxToday.IsMatch(query))
        {
            req.TimeRange = new TimeRangeFilter { From = GetTodayStart() };
        }
    }

    /// <summary>Returns level value from level keywords. Null if no level keyword found.</summary>
    private static short? ExtractLevel(string query)
    {
        if (RxLevelFatal.IsMatch(query)) return 5;
        if (RxLevelError.IsMatch(query)) return 4;
        if (RxLevelWarn.IsMatch(query)) return 3;
        return null;
    }

    private static DateTimeOffset GetTodayStart() =>
        new(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
}
