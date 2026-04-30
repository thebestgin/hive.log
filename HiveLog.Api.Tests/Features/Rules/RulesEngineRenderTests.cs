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
/// Tests for RulesEngine template rendering.
/// Captures the HTTP body sent to the webhook to verify {{placeholder}} substitution.
/// </summary>
public class RulesEngineRenderTests
{
    private string? _capturedBody;

    private async Task<RulesEngine> BuildEngineAsync(WebhookRule rule)
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<HiveLogDbContext>(o => o.UseInMemoryDatabase(dbName));
        var serviceProvider = services.BuildServiceProvider();

        await using var seedScope = serviceProvider.CreateAsyncScope();
        var db = seedScope.ServiceProvider.GetRequiredService<HiveLogDbContext>();
        db.WebhookRules.Add(rule);
        await db.SaveChangesAsync();

        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var cache = new RulesCache(scopeFactory, NullLogger<RulesCache>.Instance);
        await cache.RefreshAsync();

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var handler = new CapturingHandler(body => _capturedBody = body);
        httpClientFactory.CreateClient(Arg.Any<string>())
            .Returns(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        return new RulesEngine(cache, scopeFactory, httpClientFactory, NullLogger<RulesEngine>.Instance);
    }

    private static WebhookRule MakeRule(string? template) => new()
    {
        Id = Guid.NewGuid(),
        Name = "render-test",
        IsActive = true,
        ActionUrl = "http://test-webhook/",
        ActionBodyTemplate = template,
        ThrottleWindowSeconds = 60,
        ThrottleMaxFires = 10,
    };

    private static LogEntry MakeEntry(
        string source = "my-service",
        string message = "something happened",
        short level = 4,
        string stream = "app")
    {
        return new LogEntry
        {
            Timestamp = new DateTimeOffset(2026, 4, 30, 12, 0, 0, TimeSpan.Zero),
            Source = source,
            SourceType = "backend",
            Level = level,
            Category = "Test",
            Message = message,
            Stream = stream,
        };
    }

    [Fact]
    public async Task Template_ReplacesSource()
    {
        var rule = MakeRule("{{source}}");
        var engine = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(source: "talents-api")], CancellationToken.None);

        Assert.Equal("talents-api", _capturedBody);
    }

    [Fact]
    public async Task Template_ReplacesMessage()
    {
        var rule = MakeRule("{{message}}");
        var engine = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(message: "connection timeout")], CancellationToken.None);

        Assert.Equal("connection timeout", _capturedBody);
    }

    [Fact]
    public async Task Template_ReplacesLevel_ByName()
    {
        var rule = MakeRule("{{level}}");
        var engine = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(level: 4)], CancellationToken.None);

        Assert.Equal("Error", _capturedBody);
    }

    [Fact]
    public async Task Template_ReplacesTimestamp_ISO8601()
    {
        var rule = MakeRule("{{timestamp}}");
        var engine = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry()], CancellationToken.None);

        Assert.NotNull(_capturedBody);
        Assert.True(DateTimeOffset.TryParse(_capturedBody, out _),
            $"Expected ISO 8601 timestamp but got: {_capturedBody}");
    }

    [Fact]
    public async Task Template_Null_EmptyBody()
    {
        var rule = MakeRule(template: null);
        var engine = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry()], CancellationToken.None);

        Assert.Equal(string.Empty, _capturedBody);
    }

    [Fact]
    public async Task Level_OutOfRange_UsesNumber()
    {
        var rule = MakeRule("{{level}}");
        var engine = await BuildEngineAsync(rule);

        await engine.EvaluateAsync([MakeEntry(level: 99)], CancellationToken.None);

        Assert.Equal("99", _capturedBody);
    }

    private sealed class CapturingHandler(Action<string> capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content != null)
                capture(await request.Content.ReadAsStringAsync(cancellationToken));
            else
                capture(string.Empty);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
