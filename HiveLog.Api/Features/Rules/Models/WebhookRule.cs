using System.ComponentModel.DataAnnotations.Schema;

namespace HiveLog.Api.Features.Rules.Models;

/// <summary>
/// Represents a webhook alert rule stored in the webhook_rules table.
/// Triggers an HTTP POST when matching log entries are ingested.
/// </summary>
[Table("webhook_rules")]
public class WebhookRule
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = null!;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    // --- Trigger conditions (all nullable = wildcard) ---

    /// <summary>Minimum log level to match (0=Trace .. 5=Fatal). Null = any level.</summary>
    [Column("trigger_level_min")]
    public short? TriggerLevelMin { get; set; }

    [Column("trigger_source")]
    public string? TriggerSource { get; set; }

    [Column("trigger_stream")]
    public string? TriggerStream { get; set; }

    [Column("trigger_tags", TypeName = "text[]")]
    public string[]? TriggerTags { get; set; }

    // --- Action ---

    [Column("action_url")]
    public string ActionUrl { get; set; } = null!;

    /// <summary>Template supporting {{source}}, {{message}}, {{level}}, {{timestamp}}.</summary>
    [Column("action_body_template")]
    public string? ActionBodyTemplate { get; set; }

    // --- Throttle ---

    [Column("throttle_window_seconds")]
    public int ThrottleWindowSeconds { get; set; } = 60;

    [Column("throttle_max_fires")]
    public int ThrottleMaxFires { get; set; } = 1;

    // --- Throttle state (persisted in DB) ---

    [Column("last_fired_at")]
    public DateTimeOffset? LastFiredAt { get; set; }

    [Column("fire_count_in_window")]
    public int FireCountInWindow { get; set; } = 0;

    [Column("window_start_at")]
    public DateTimeOffset? WindowStartAt { get; set; }
}
