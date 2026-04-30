using HiveLog.Api.Features.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Connectors.BackendServices;

/// <summary>
/// Action filter that validates the X-Api-Key header on backend-services ingest endpoints.
/// Returns 401 when the header is missing or does not match HiveLog:ApiKey.
/// </summary>
public sealed class IngestApiKeyFilter : IActionFilter
{
    private const string HeaderName = "X-Api-Key";

    private readonly AdminOptions _opts;

    public IngestApiKeyFilter(IOptions<AdminOptions> opts)
    {
        _opts = opts.Value;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (string.IsNullOrEmpty(_opts.ApiKey) ||
            !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var value) ||
            value != _opts.ApiKey)
        {
            context.Result = new UnauthorizedObjectResult(
                new { error = "Invalid or missing X-Api-Key header" });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
