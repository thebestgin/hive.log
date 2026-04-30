using HiveLog.Api.Features.Query.NaturalLanguage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Health;

/// <summary>
/// Pings the configured Ollama instance (GET /) to verify NL-to-SQL availability.
/// Returns Degraded when Ollama is not reachable — the NL query endpoint will fall back
/// to template-matching only, so HiveLog remains operational but with reduced capability.
/// Skipped (always Healthy) when NlQuery.Enabled = false.
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
            ["url"] = _options.OllamaBaseUrl,
            ["model"] = _options.Model,
        };

        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);

            using var response = await http.GetAsync(
                new Uri(new Uri(_options.OllamaBaseUrl), "/"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
                return HealthCheckResult.Healthy(data: data);

            data["statusCode"] = (int)response.StatusCode;
            return HealthCheckResult.Degraded(
                $"Ollama returned HTTP {(int)response.StatusCode}", data: data);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            data["error"] = ex.Message;
            return HealthCheckResult.Degraded("Ollama not reachable", data: data);
        }
    }
}
