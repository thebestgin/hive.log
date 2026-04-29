namespace HiveLog.Api.Features.Rules.Models;

/// <summary>
/// Represents a webhook alert rule stored in the webhook_rules table.
/// Triggers an HTTP POST when matching log entries are ingested.
/// Column names and table name are configured via WebhookRuleConfiguration (snake_case convention).
/// </summary>
public class WebhookRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    // --- Trigger conditions (all nullable = wildcard) ---

    /// <summary>Minimum log level to match (0=Trace .. 5=Fatal). Null = any level.</summary>
    public short? TriggerLevelMin { get; set; }
    public string? TriggerSource { get; set; }
    public string? TriggerStream { get; set; }
    public string[]? TriggerTags { get; set; }

    // --- Action ---

    public string ActionUrl { get; set; } = null!;

    /// <summary>Template supporting {{source}}, {{message}}, {{level}}, {{timestamp}}.</summary>
    public string? ActionBodyTemplate { get; set; }

    // --- Throttle ---

    public int ThrottleWindowSeconds { get; set; } = 60;
    public int ThrottleMaxFires { get; set; } = 1;

    // --- Throttle state (persisted in DB) ---

    public DateTimeOffset? LastFiredAt { get; set; }
    public int FireCountInWindow { get; set; } = 0;
    public DateTimeOffset? WindowStartAt { get; set; }
}
