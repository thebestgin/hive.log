using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace HiveLog.Api.Identity;

public static class IdentityExtensions
{
    const string roleClaimType = "role";

    public static void AddKeycloakAuthentication(WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("JwtBearer");
        var authority = section["Authority"];
        var audience = section["Audience"];
        var metadataAddress = section["MetadataAddress"];

        if (string.IsNullOrEmpty(authority))
            return;

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(o =>
        {
            o.Authority = authority;
            o.Audience = audience;
            o.RequireHttpsMetadata = false;

            if (!string.IsNullOrEmpty(metadataAddress))
            {
                if (builder.Environment.IsProduction())
                    throw new InvalidOperationException(
                        "JwtBearer:MetadataAddress must not be set in Production.");

                var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
                var httpClient = new HttpClient(handler);
                var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    metadataAddress,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever(httpClient) { RequireHttps = false });
                o.ConfigurationManager = configManager;
            }

            o.TokenValidationParameters.NameClaimType = "preferred_username";
            o.TokenValidationParameters.RoleClaimType = roleClaimType;
            o.TokenValidationParameters.ValidateIssuer = false;
            o.IncludeErrorDetails = true;
        });

        builder.Services.AddAuthorization();
    }
}
