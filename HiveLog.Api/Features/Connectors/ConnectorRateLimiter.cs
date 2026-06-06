using HiveCache;
using HiveLog.Api.Features.Connectors.Manifest;

namespace HiveLog.Api.Features.Connectors;

/// <summary>
/// Per-connector ingest rate limiter using HiveCache (mirrors jobdate.entitlements ProvisionRateLimiter).
/// Sliding window: IncrementAsync(delta=entryCount, ttl=window) is atomic + distributed-safe (L2 Postgres).
/// WHY HiveCache, not ASP.NET AddRateLimiter (00712): existing monorepo pattern (4×), distributed over L2;
/// AddRateLimiter is in-memory-only and not shared across instances.
/// Key: hivelog:rl:{connectorId}:{clientKey}
/// </summary>
public sealed class ConnectorRateLimiter(IHiveCache<long> cache)
{
    /// <returns>Allowed=false when the window budget is exceeded; retryAfterSeconds for the header.</returns>
    public async Task<(bool Allowed, int RetryAfterSeconds)> TryConsumeAsync(
        string connectorId,
        string clientKey,
        int entryCount,
        ConnectorRateLimit limit,
        CancellationToken ct)
    {
        var key = $"hivelog:rl:{connectorId}:{clientKey}";
        var window = TimeSpan.FromSeconds(limit.WindowSeconds);
        var count = await cache.IncrementAsync(key, delta: entryCount, min: null, max: null, ttl: window, ct: ct);
        return count > limit.MaxEntries ? (false, limit.WindowSeconds) : (true, 0);
    }
}
