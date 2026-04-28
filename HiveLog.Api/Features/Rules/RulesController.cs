using HiveLog.Api.Features.Admin;
using HiveLog.Api.Features.Rules.Models;
using HiveLog.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HiveLog.Api.Features.Rules;

[ApiController]
[Route("api/hivelog/v1/admin/rules")]
[ServiceFilter(typeof(AdminApiKeyFilter))]
public sealed class RulesController : ControllerBase
{
    private readonly HiveLogDbContext _db;
    private readonly RulesCache _cache;

    public RulesController(HiveLogDbContext db, RulesCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// Creates a new webhook alert rule.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<RuleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateRuleRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var rule = new WebhookRule
        {
            Name = request.Name,
            IsActive = request.IsActive,
            TriggerLevelMin = request.Trigger?.LevelMin,
            TriggerSource = request.Trigger?.Source,
            TriggerStream = request.Trigger?.Stream,
            TriggerTags = request.Trigger?.Tags,
            ActionUrl = request.Action.Url,
            ActionBodyTemplate = request.Action.BodyTemplate,
            ThrottleWindowSeconds = request.Throttle?.WindowSeconds ?? 60,
            ThrottleMaxFires = request.Throttle?.MaxFires ?? 1,
        };

        _db.WebhookRules.Add(rule);
        await _db.SaveChangesAsync(ct);

        // Invalidate cache so next evaluation picks up the new rule
        await _cache.RefreshAsync(ct);

        return CreatedAtAction(nameof(List), ToResponse(rule));
    }

    /// <summary>
    /// Lists all webhook alert rules with throttle status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<List<RuleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rules = await _db.WebhookRules
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        return Ok(rules.Select(ToResponse).ToList());
    }

    /// <summary>
    /// Deletes a webhook alert rule by ID.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var rule = await _db.WebhookRules.FindAsync([id], ct);
        if (rule is null)
            return NotFound();

        _db.WebhookRules.Remove(rule);
        await _db.SaveChangesAsync(ct);

        // Invalidate cache
        await _cache.RefreshAsync(ct);

        return NoContent();
    }

    private static RuleResponse ToResponse(WebhookRule rule) => new(
        Id: rule.Id,
        Name: rule.Name,
        IsActive: rule.IsActive,
        Trigger: (rule.TriggerLevelMin.HasValue || rule.TriggerSource != null ||
                  rule.TriggerStream != null || rule.TriggerTags != null)
            ? new RuleTriggerResponse(rule.TriggerLevelMin, rule.TriggerSource, rule.TriggerStream, rule.TriggerTags)
            : null,
        Action: new RuleActionResponse(rule.ActionUrl, rule.ActionBodyTemplate),
        Throttle: new RuleThrottleResponse(rule.ThrottleWindowSeconds, rule.ThrottleMaxFires),
        ThrottleStatus: new RuleThrottleStatusResponse(rule.LastFiredAt, rule.FireCountInWindow, rule.WindowStartAt)
    );
}
