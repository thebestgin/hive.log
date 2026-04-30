using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Features.Rules;
using HiveLog.Api.Features.Rules.Models;
using HiveLog.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HiveLog.Api.Tests.Features.Rules;

/// <summary>
/// Tests for RulesEngine match logic — verifies which log entries trigger rule evaluation.
/// Uses InMemory EF for both RulesCache and RulesEngine DB access.
/// RulesCache is sealed, so tests seed InMemory DB and call RefreshAsync to pre-load rules.
/// </summary>
public class RulesEngineMatchTests
{
    private readonly HttpCallTracker _httpTracker = new();

    private async Task<(RulesEngine Engine, ServiceProvider ServiceProvider)> BuildEngineAsync(
        params WebhookRule[] rules)
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddDbContext<HiveLogDbContext>(o => o.UseInMemoryDatabase(dbName));
        var serviceProvider = services.BuildServiceProvider();

        // Seed rules into InMemory DB
        await using var seedScope = serviceProvider.CreateAsyncScope();
        var db = seedScope.ServiceProvider.GetRequiredService<HiveLogDbContext>();
        db.WebhookRules.AddRange(rules);
        await db.SaveChangesAsync();

        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        // Use real RulesCache, pre-loaded from InMemory DB
        var cache = new RulesCache(scopeFactory, NullLogger<RulesCache>.Instance);
        await cache.RefreshAsync();

        // IHttpClientFactory with counting handler
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var handler = new CountingHandler(_httpTracker);
        httpClientFactory.CreateClient(Arg.Any<string>())
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var engine = new RulesEngine(cache, scopeFactory, httpClientFactory, NullLogger<RulesEngine>.Instance);
        return (engine, serviceProvider);
    }

    private static WebhookRule MakeRule(
        short? levelMin = null,
        string? source = null,
        string? stream = null,
        string[]? tags = null,
        string url = "http://test-webhook/")
    {
        return new WebhookRule
        {
            Id = Guid.NewGuid(),
            Name = "test-rule",
            IsActive = true,
            TriggerLevelMin = levelMin,
            TriggerSource = source,
            TriggerStream = stream,
            TriggerTags = tags,
            ActionUrl = url,
            ThrottleWindowSeconds = 60,
            ThrottleMaxFires = 10,
        };
    }

    private static LogEntry MakeEntry(
        short level = 2,
        string source = "test-api",
        string stream = "app",
        string[]? tags = null)
    {
        return new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Source = source,
            SourceType = "backend",
            Level = level,
            Category = "Test",
            Message = "test message",
            Stream = stream,
            Tags = tags,
        };
    }

    [Fact]
    public async Task EmptyBatch_NothingFired()
    {
        var rule = MakeRule(levelMin: 3);
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([], CancellationToken.None);

        Assert.Equal(0, _httpTracker.Count);
    }

    [Fact]
    public async Task NoActiveRules_NothingFired()
    {
        var (engine, _) = await BuildEngineAsync(); // no rules

        await engine.EvaluateAsync([MakeEntry(level: 4)], CancellationToken.None);

        Assert.Equal(0, _httpTracker.Count);
    }

    [Fact]
    public async Task LevelFilter_BelowMin_NoMatch()
    {
        var rule = MakeRule(levelMin: 4);
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(level: 3)], CancellationToken.None);

        Assert.Equal(0, _httpTracker.Count);
    }

    [Fact]
    public async Task LevelFilter_AtMin_Matches()
    {
        var rule = MakeRule(levelMin: 4);
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(level: 4)], CancellationToken.None);

        Assert.Equal(1, _httpTracker.Count);
    }

    [Fact]
    public async Task LevelFilter_Null_MatchesAny()
    {
        var rule = MakeRule(levelMin: null);
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(level: 0)], CancellationToken.None);

        Assert.Equal(1, _httpTracker.Count);
    }

    [Fact]
    public async Task SourceFilter_ExactMatch_CaseInsensitive()
    {
        var rule = MakeRule(source: "Talents-API");
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(source: "talents-api")], CancellationToken.None);

        Assert.Equal(1, _httpTracker.Count);
    }

    [Fact]
    public async Task SourceFilter_Mismatch_NoMatch()
    {
        var rule = MakeRule(source: "talents-api");
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(source: "discovery-api")], CancellationToken.None);

        Assert.Equal(0, _httpTracker.Count);
    }

    [Fact]
    public async Task StreamFilter_Match()
    {
        var rule = MakeRule(stream: "agent");
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(stream: "agent")], CancellationToken.None);

        Assert.Equal(1, _httpTracker.Count);
    }

    [Fact]
    public async Task StreamFilter_Mismatch()
    {
        var rule = MakeRule(stream: "agent");
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(stream: "app")], CancellationToken.None);

        Assert.Equal(0, _httpTracker.Count);
    }

    [Fact]
    public async Task TagFilter_AnyTagPresent_Matches()
    {
        var rule = MakeRule(tags: ["critical"]);
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(tags: ["info", "critical"])], CancellationToken.None);

        Assert.Equal(1, _httpTracker.Count);
    }

    [Fact]
    public async Task TagFilter_NoTagMatch()
    {
        var rule = MakeRule(tags: ["critical"]);
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(tags: ["info"])], CancellationToken.None);

        Assert.Equal(0, _httpTracker.Count);
    }

    [Fact]
    public async Task TagFilter_Null_MatchesAll()
    {
        var rule = MakeRule(tags: null);
        var (engine, _) = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(tags: null)], CancellationToken.None);

        Assert.Equal(1, _httpTracker.Count);
    }

    [Fact]
    public async Task FirstMatchInBatch_FiresOnce()
    {
        var rule = MakeRule(levelMin: 4);
        var (engine, _) = await BuildEngineAsync(rule);

        // 3 entries all match — RulesEngine picks only the first per rule
        var batch = new List<LogEntry>
        {
            MakeEntry(level: 4),
            MakeEntry(level: 4),
            MakeEntry(level: 4),
        };

        await engine.EvaluateAsync(batch, CancellationToken.None);

        Assert.Equal(1, _httpTracker.Count);
    }

    // --- Test infrastructure ---

    internal sealed class HttpCallTracker
    {
        private int _count;
        public int Count => _count;
        public void Increment() => Interlocked.Increment(ref _count);
    }

    private sealed class CountingHandler(HttpCallTracker tracker) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            tracker.Increment();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
