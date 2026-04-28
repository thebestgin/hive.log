using HiveLog.Api.Features.Rules.Models;
using HiveLog.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HiveLog.Api.Features.Rules;

/// <summary>
/// Singleton cache that holds active webhook rules in memory.
/// Refreshes from the database periodically (every 30s) to avoid hitting
/// the DB on every batch evaluation.
/// Uses IServiceScopeFactory to create short-lived scopes for DB access,
/// which is the correct pattern for singletons that need scoped services.
/// </summary>
public sealed class RulesCache : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RulesCache> _logger;

    private volatile IReadOnlyList<WebhookRule> _activeRules = [];

    public RulesCache(IServiceScopeFactory scopeFactory, ILogger<RulesCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current in-memory list of active rules. No DB hit.
    /// </summary>
    public IReadOnlyList<WebhookRule> GetActiveRules() => _activeRules;

    /// <summary>
    /// Immediately reloads active rules from the database.
    /// Called after rule create/delete to keep the cache fresh.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HiveLogDbContext>();

            var rules = await db.WebhookRules
                .Where(r => r.IsActive)
                .AsNoTracking()
                .ToListAsync(ct);

            _activeRules = rules;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RulesCache] Failed to refresh rules from database");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial load on startup
        await RefreshAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RefreshInterval, ct);
                await RefreshAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RulesCache] Periodic refresh failed");
            }
        }
    }
}
