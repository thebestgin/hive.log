using System.ComponentModel.DataAnnotations;

namespace HiveLog.Api.Features.Rules.Models;

public record CreateRuleRequest(
    [Required] string Name,
    bool IsActive,
    RuleTrigger? Trigger,
    [Required] RuleAction Action,
    RuleThrottle? Throttle
);

public record RuleTrigger(
    short? LevelMin,
    string? Source,
    string? Stream,
    string[]? Tags
);

public record RuleAction(
    [Required] string Url,
    string? BodyTemplate
);

public record RuleThrottle(
    int WindowSeconds,
    int MaxFires
);
