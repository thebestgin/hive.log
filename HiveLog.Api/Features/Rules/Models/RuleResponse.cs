namespace HiveLog.Api.Features.Rules.Models;

public record RuleResponse(
    Guid Id,
    string Name,
    bool IsActive,
    RuleTriggerResponse? Trigger,
    RuleActionResponse Action,
    RuleThrottleResponse Throttle,
    RuleThrottleStatusResponse ThrottleStatus
);

public record RuleTriggerResponse(
    short? LevelMin,
    string? Source,
    string? Stream,
    string[]? Tags
);

public record RuleActionResponse(
    string Url,
    string? BodyTemplate
);

public record RuleThrottleResponse(
    int WindowSeconds,
    int MaxFires
);

public record RuleThrottleStatusResponse(
    DateTimeOffset? LastFiredAt,
    int FireCountInWindow,
    DateTimeOffset? WindowStartAt
);
