using System.Text.Json;
using HiveLog.Api.Features.Admin;
using HiveLog.Api.Features.Aggregate;
using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Retention;
using HiveLog.Api.Features.Rules;
using HiveLog.Api.Features.Stream;
using HiveLog.Api.Health;
using HiveLog.Api.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace HiveLog.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        DotNetEnv.Env.Load();

        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddEnvironmentVariables();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

        // ---------------------------------------------------------------------------
        // Database
        // ---------------------------------------------------------------------------
        builder.Services.AddDbContext<HiveLogDbContext>(options =>
        {
            // Npgsql 9.x: Kerberos/GSSAPI is not the default — no GssEncryptionMode needed.
            // If upgrading to Npgsql 10+, add: dataSourceBuilder.ConnectionStringBuilder.GssEncryptionMode = GssEncryptionMode.Disable;
            options.UseNpgsql(connectionString);
        });


        // ---------------------------------------------------------------------------
        // Ingest Pipeline
        // ---------------------------------------------------------------------------
        builder.Services.Configure<IngestOptions>(
            builder.Configuration.GetSection(IngestOptions.SectionName));

        var ingestOpts = builder.Configuration
            .GetSection(IngestOptions.SectionName)
            .Get<IngestOptions>() ?? new IngestOptions();

        builder.Services.AddSingleton(new IngestBuffer(ingestOpts.ChannelCapacity));
        builder.Services.AddSingleton<IngestMetrics>();
        builder.Services.AddSingleton<SelfLogger>();
        builder.Services.AddSingleton<StreamBroadcaster>();

        // Separate NpgsqlDataSource for COPY writer (independent connection pool)
        var copyDataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        builder.Services.AddSingleton(copyDataSourceBuilder.Build());
        builder.Services.AddSingleton<LogEntryCopyWriter>();

        // Register as singleton so AdminController can inject it directly,
        // then add as hosted service pointing to the same singleton instance.
        builder.Services.AddSingleton<IngestBackgroundService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<IngestBackgroundService>());

        // ---------------------------------------------------------------------------
        // Retention + Compression
        // ---------------------------------------------------------------------------
        builder.Services.Configure<RetentionOptions>(
            builder.Configuration.GetSection(RetentionOptions.SectionName));

        builder.Services.AddHostedService<TimescalePolicyInitializer>();
        builder.Services.AddHostedService<RetentionCleanupJob>();

        // ---------------------------------------------------------------------------
        // Admin Endpoints
        // ---------------------------------------------------------------------------
        builder.Services.Configure<AdminOptions>(
            builder.Configuration.GetSection(AdminOptions.SectionName));
        builder.Services.AddSingleton<AdminApiKeyFilter>();
        builder.Services.AddSingleton<RuntimeRetentionService>();

        // ---------------------------------------------------------------------------
        // Webhook Rules Engine
        // ---------------------------------------------------------------------------
        // Named HttpClient for outgoing webhook POST requests (5s timeout)
        builder.Services.AddHttpClient("webhooks", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // RulesCache: singleton + hosted service (periodic 30s refresh)
        builder.Services.AddSingleton<RulesCache>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<RulesCache>());

        builder.Services.AddSingleton<RulesEngine>();

        // ---------------------------------------------------------------------------
        // Continuous Aggregates
        // ---------------------------------------------------------------------------
        builder.Services.AddHostedService<ContinuousAggregateInitializer>();

        // ---------------------------------------------------------------------------
        // Health Checks
        // ---------------------------------------------------------------------------
        builder.Services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "database")
            .AddCheck<HiveLogHealthCheck>("hivelog");

        // ---------------------------------------------------------------------------
        // Controllers + JSON
        // ---------------------------------------------------------------------------
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                var o = options.JsonSerializerOptions;
                o.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                o.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                o.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });

        builder.Services.AddRouting(options => options.LowercaseUrls = true);

        // ---------------------------------------------------------------------------
        // Swagger
        // ---------------------------------------------------------------------------
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "hive.log",
                Version = "v1"
            });
        });

        // ---------------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------------
        var app = builder.Build();

        // ---------------------------------------------------------------------------
        // Migrate Database on Startup
        // ---------------------------------------------------------------------------
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HiveLogDbContext>();
            await db.Database.MigrateAsync();
        }

        // ---------------------------------------------------------------------------
        // Middleware
        // ---------------------------------------------------------------------------
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapControllers();

        // Health endpoint returns JSON body including bufferDepth + droppedTotal
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthResponse
        });

        app.Run();
    }

    private static async Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var status = report.Status switch
        {
            HealthStatus.Healthy => "Healthy",
            HealthStatus.Degraded => "Degraded",
            _ => "Unhealthy"
        };

        // Extract HiveLog-specific data from the custom health check
        int bufferDepth = 0;
        long droppedTotal = 0;

        if (report.Entries.TryGetValue("hivelog", out var hiveEntry) && hiveEntry.Data is not null)
        {
            if (hiveEntry.Data.TryGetValue("bufferDepth", out var bd))
                bufferDepth = bd is int i ? i : 0;
            if (hiveEntry.Data.TryGetValue("droppedTotal", out var dt))
                droppedTotal = dt is long l ? l : 0;
        }

        var response = new
        {
            status,
            bufferDepth,
            droppedTotal,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
