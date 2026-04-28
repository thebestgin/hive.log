using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Features.Rules.Models;
using HiveLog.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiveLog.Api.Features.Rules;

/// <summary>
/// Evaluates webhook rules against a batch of newly ingested log entries.
/// For each matching rule, checks throttle state (DB-persisted) and fires the webhook if allowed.
/// Uses IServiceScopeFactory to create short-lived scopes for DB access.
/// </summary>
public sealed class RulesEngine
{
    private static readonly string[] LevelNames = ["Trace", "Debug", "Info", "Warn", "Error", "Fatal"];

    private readonly RulesCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RulesEngine> _logger;

    public RulesEngine(
        RulesCache cache,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<RulesEngine> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates all active rules against the given batch.
    /// Fires matching, non-throttled rules and updates throttle state in DB.
    /// </summary>
    public async Task EvaluateAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct)
    {
        var rules = _cache.GetActiveRules();
        if (rules.Count == 0 || batch.Count == 0) return;

        foreach (var rule in rules)
        {
            // Find the first entry in the batch that matches this rule
            var matchingEntry = FindMatch(rule, batch);
            if (matchingEntry is null) continue;

            await TryFireAsync(rule, matchingEntry, ct);
        }
    }

    private static LogEntry? FindMatch(WebhookRule rule, IReadOnlyList<LogEntry> batch)
    {
        foreach (var entry in batch)
        {
            if (rule.TriggerLevelMin.HasValue && entry.Level < rule.TriggerLevelMin.Value)
                continue;

            if (rule.TriggerSource != null &&
                !string.Equals(entry.Source, rule.TriggerSource, StringComparison.OrdinalIgnoreCase))
                continue;

            if (rule.TriggerStream != null &&
                !string.Equals(entry.Stream, rule.TriggerStream, StringComparison.OrdinalIgnoreCase))
                continue;

            if (rule.TriggerTags is { Length: > 0 })
            {
                var hasTag = entry.Tags != null &&
                             rule.TriggerTags.Any(t => entry.Tags.Contains(t, StringComparer.OrdinalIgnoreCase));
                if (!hasTag) continue;
            }

            return entry;
        }

        return null;
    }

    private async Task TryFireAsync(WebhookRule rule, LogEntry triggerEntry, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HiveLogDbContext>();

            // Load fresh throttle state from DB (may have been updated by another instance)
            var fresh = await db.WebhookRules
                .Where(r => r.Id == rule.Id)
                .Select(r => new { r.ThrottleWindowSeconds, r.ThrottleMaxFires, r.WindowStartAt, r.FireCountInWindow, r.LastFiredAt })
                .FirstOrDefaultAsync(ct);

            if (fresh is null) return; // Rule was deleted between cache load and evaluation

            var now = DateTimeOffset.UtcNow;
            var windowDuration = TimeSpan.FromSeconds(fresh.ThrottleWindowSeconds);

            int newFireCount;
            DateTimeOffset newWindowStart;

            if (fresh.WindowStartAt is null || now > fresh.WindowStartAt.Value.Add(windowDuration))
            {
                // No window yet, or window has expired — start a new window
                newWindowStart = now;
                newFireCount = 1;
            }
            else if (fresh.FireCountInWindow < fresh.ThrottleMaxFires)
            {
                // Within window, still under the limit
                newWindowStart = fresh.WindowStartAt.Value;
                newFireCount = fresh.FireCountInWindow + 1;
            }
            else
            {
                // Throttled — skip firing
                return;
            }

            // Fire the webhook
            var body = RenderTemplate(rule.ActionBodyTemplate, triggerEntry);
            var fired = await PostWebhookAsync(rule.ActionUrl, body, ct);

            if (!fired) return;

            // Update throttle state in DB
            await db.WebhookRules
                .Where(r => r.Id == rule.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.WindowStartAt, newWindowStart)
                    .SetProperty(r => r.FireCountInWindow, newFireCount)
                    .SetProperty(r => r.LastFiredAt, now),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RulesEngine] Error evaluating rule {RuleId} ({RuleName})",
                rule.Id, rule.Name);
        }
    }

    private static string RenderTemplate(string? template, LogEntry entry)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        var levelName = entry.Level >= 0 && entry.Level < LevelNames.Length
            ? LevelNames[entry.Level]
            : entry.Level.ToString();

        return template
            .Replace("{{source}}", entry.Source, StringComparison.Ordinal)
            .Replace("{{message}}", entry.Message, StringComparison.Ordinal)
            .Replace("{{level}}", levelName, StringComparison.Ordinal)
            .Replace("{{timestamp}}", entry.Timestamp.ToString("O"), StringComparison.Ordinal);
    }

    private async Task<bool> PostWebhookAsync(string url, string body, CancellationToken ct)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("webhooks");
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[RulesEngine] Webhook {Url} returned {Status}",
                    url, (int)response.StatusCode);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RulesEngine] Webhook POST to {Url} failed", url);
            return false;
        }
    }
}
