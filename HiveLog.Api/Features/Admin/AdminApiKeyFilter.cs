using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace HiveLog.Api.Features.Admin;

/// <summary>
/// Action filter that validates the Admin-Api-Key header on all admin endpoints.
/// Returns 401 if the header is missing or the value does not match configuration.
/// </summary>
public sealed class AdminApiKeyFilter : IActionFilter
{
    private const string HeaderName = "Admin-Api-Key";

    private readonly AdminOptions _opts;

    public AdminApiKeyFilter(IOptions<AdminOptions> opts)
    {
        _opts = opts.Value;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (string.IsNullOrEmpty(_opts.AdminApiKey) ||
            !context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var value) ||
            value != _opts.AdminApiKey)
        {
            context.Result = new UnauthorizedObjectResult(
                new { error = "Invalid or missing Admin-Api-Key header" });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
