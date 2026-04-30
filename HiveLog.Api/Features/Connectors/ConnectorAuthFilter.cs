using HiveLog.Api.Features.Connectors.Manifest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HiveLog.Api.Features.Connectors;

/// <summary>
/// Action filter that resolves the connector from the manifest and validates auth.
/// Runs before the controller action. Sets HttpContext.Items["Connector"] on success.
///
/// Auth validation per connector type:
/// - apiKey: X-Api-Key (or configured headerName) must match one of the apiAccesses entries
/// - jwt: User.Identity.IsAuthenticated must be true
/// - none: no check
/// </summary>
public sealed class ConnectorAuthFilter : IAsyncActionFilter
{
    private readonly HiveLogManifest _manifest;

    public ConnectorAuthFilter(HiveLogManifest manifest)
    {
        _manifest = manifest;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var connectorId = context.RouteData.Values["connectorId"]?.ToString();

        var connector = _manifest.Connectors
            .FirstOrDefault(c => string.Equals(c.Id, connectorId, StringComparison.OrdinalIgnoreCase));

        if (connector is null)
        {
            context.Result = new NotFoundObjectResult(
                new { error = $"Unknown connector '{connectorId}'." });
            return;
        }

        // Validate auth based on connector type
        var authType = connector.Auth.Type.ToLowerInvariant();

        switch (authType)
        {
            case "apikey":
            {
                var headerName = connector.Auth.HeaderName ?? "X-Api-Key";
                if (!context.HttpContext.Request.Headers.TryGetValue(headerName, out var keyValue) ||
                    string.IsNullOrEmpty(keyValue))
                {
                    context.Result = new UnauthorizedObjectResult(
                        new { error = $"Missing {headerName} header." });
                    return;
                }

                var matched = connector.Auth.ApiAccesses?
                    .Any(a => a.ApiKey == keyValue.ToString()) ?? false;

                if (!matched)
                {
                    context.Result = new UnauthorizedObjectResult(
                        new { error = $"Invalid {headerName}." });
                    return;
                }
                break;
            }
            case "jwt":
            {
                if (context.HttpContext.User.Identity?.IsAuthenticated != true)
                {
                    context.Result = new UnauthorizedObjectResult(
                        new { error = "Valid JWT Bearer token required." });
                    return;
                }
                break;
            }
            case "none":
                break;
        }

        // Store resolved connector for the controller to use
        context.HttpContext.Items["Connector"] = connector;

        await next();
    }
}
