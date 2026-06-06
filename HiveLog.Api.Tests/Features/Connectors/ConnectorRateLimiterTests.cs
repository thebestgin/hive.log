using HiveCache;
using HiveLog.Api.Features.Connectors;
using HiveLog.Api.Features.Connectors.Manifest;
using NSubstitute;

namespace HiveLog.Api.Tests.Features.Connectors;

public class ConnectorRateLimiterTests
{
    private static ConnectorRateLimit MakeLimit(int maxEntries = 100, int windowSeconds = 60) =>
        new() { MaxEntries = maxEntries, WindowSeconds = windowSeconds };

    // --- Under limit ---

    [Fact]
    public async Task TryConsumeAsync_UnderLimit_ReturnsAllowed()
    {
        var cache = Substitute.For<IHiveCache<long>>();
        // First batch of 10 entries → count = 10, which is ≤ 100
        cache.IncrementAsync(
            key: Arg.Any<string>(),
            delta: 10L,
            min: null,
            max: null,
            ttl: Arg.Any<TimeSpan?>(),
            ct: Arg.Any<CancellationToken>())
            .Returns(10L);

        var limiter = new ConnectorRateLimiter(cache);
        var limit = MakeLimit(maxEntries: 100);

        var (allowed, retryAfter) = await limiter.TryConsumeAsync("webapp", "1.2.3.4", 10, limit, CancellationToken.None);

        Assert.True(allowed);
        Assert.Equal(0, retryAfter);
    }

    // --- Exactly at limit ---

    [Fact]
    public async Task TryConsumeAsync_ExactlyAtLimit_ReturnsAllowed()
    {
        var cache = Substitute.For<IHiveCache<long>>();
        cache.IncrementAsync(
            key: Arg.Any<string>(),
            delta: 100L,
            min: null,
            max: null,
            ttl: Arg.Any<TimeSpan?>(),
            ct: Arg.Any<CancellationToken>())
            .Returns(100L);

        var limiter = new ConnectorRateLimiter(cache);
        var limit = MakeLimit(maxEntries: 100);

        var (allowed, _) = await limiter.TryConsumeAsync("webapp", "1.2.3.4", 100, limit, CancellationToken.None);

        Assert.True(allowed);
    }

    // --- Over limit ---

    [Fact]
    public async Task TryConsumeAsync_OverLimit_ReturnsDenied()
    {
        var cache = Substitute.For<IHiveCache<long>>();
        // Accumulated count exceeds maxEntries
        cache.IncrementAsync(
            key: Arg.Any<string>(),
            delta: Arg.Any<long>(),
            min: null,
            max: null,
            ttl: Arg.Any<TimeSpan?>(),
            ct: Arg.Any<CancellationToken>())
            .Returns(101L);

        var limiter = new ConnectorRateLimiter(cache);
        var limit = MakeLimit(maxEntries: 100, windowSeconds: 60);

        var (allowed, retryAfter) = await limiter.TryConsumeAsync("webapp", "1.2.3.4", 10, limit, CancellationToken.None);

        Assert.False(allowed);
        Assert.Equal(60, retryAfter);
    }

    // --- Key includes connectorId and clientKey ---

    [Fact]
    public async Task TryConsumeAsync_UsesCorrectCacheKey()
    {
        var cache = Substitute.For<IHiveCache<long>>();
        cache.IncrementAsync(
            key: Arg.Any<string>(),
            delta: Arg.Any<long>(),
            min: null,
            max: null,
            ttl: Arg.Any<TimeSpan?>(),
            ct: Arg.Any<CancellationToken>())
            .Returns(1L);

        var limiter = new ConnectorRateLimiter(cache);
        var limit = MakeLimit();

        await limiter.TryConsumeAsync("webapp", "10.0.0.1", 1, limit, CancellationToken.None);

        await cache.Received(1).IncrementAsync(
            key: "hivelog:rl:webapp:10.0.0.1",
            delta: 1L,
            min: null,
            max: null,
            ttl: TimeSpan.FromSeconds(60),
            ct: CancellationToken.None);
    }

    // --- Window TTL is derived from WindowSeconds ---

    [Fact]
    public async Task TryConsumeAsync_PassesWindowAsTtl()
    {
        var cache = Substitute.For<IHiveCache<long>>();
        cache.IncrementAsync(
            key: Arg.Any<string>(),
            delta: Arg.Any<long>(),
            min: null,
            max: null,
            ttl: Arg.Any<TimeSpan?>(),
            ct: Arg.Any<CancellationToken>())
            .Returns(1L);

        var limiter = new ConnectorRateLimiter(cache);
        var limit = MakeLimit(windowSeconds: 30);

        await limiter.TryConsumeAsync("conn", "client", 5, limit, CancellationToken.None);

        await cache.Received(1).IncrementAsync(
            key: Arg.Any<string>(),
            delta: 5L,
            min: null,
            max: null,
            ttl: TimeSpan.FromSeconds(30),
            ct: CancellationToken.None);
    }
}
