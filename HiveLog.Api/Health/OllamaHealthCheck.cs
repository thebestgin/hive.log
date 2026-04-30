using System.Net.Http.Headers;
using HiveLog.Api.Features.Query.NaturalLanguage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Health;

/// <summary>
/// Checks the NL-to-SQL OpenAI integration: config presence + actual API reachability.
/// Pings GET /v1/models — lightweight, no token consumption, verifies API key validity.
/// Returns Degraded (not Unhealthy) when OpenAI is unreachable so HiveLog stays operational
/// with template-matcher fallback. Skipped when NlQuery.Enabled = false.
/// </summary>
public sealed class OllamaHealthCheck : IHealthCheck
{
    private readonly NlQueryOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public OllamaHealthCheck(IOptions<NlQueryOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return HealthCheckResult.Healthy(data: new Dictionary<string, object>
            {
                ["enabled"] = false,
            });
        }

        var data = new Dictionary<string, object>
        {
            ["enabled"] = true,
            ["model"] = _options.Model,
        };

        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey.StartsWith("<"))
        {
            return HealthCheckResult.Degraded(
                "HiveLog:NlQuery:ApiKey is not configured", data: data);
        }

        // Ping /v1/models — no token consumption, verifies key + connectivity
        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(5);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var response = await http.GetAsync("v1/models", cancellationToken);

            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy(data: data);

            data["statusCode"] = (int)response.StatusCode;
            return HealthCheckResult.Degraded(
                $"OpenAI returned HTTP {(int)response.StatusCode}", data: data);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            data["error"] = ex.Message;
            return HealthCheckResult.Degraded("OpenAI not reachable", data: data);
        }
    }
}
