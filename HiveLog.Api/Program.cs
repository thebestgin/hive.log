using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Persistence;
using Microsoft.EntityFrameworkCore;
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

        // Separate NpgsqlDataSource for COPY writer (independent connection pool)
        var copyDataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        builder.Services.AddSingleton(copyDataSourceBuilder.Build());
        builder.Services.AddSingleton<LogEntryCopyWriter>();

        builder.Services.AddHostedService<IngestBackgroundService>();

        // ---------------------------------------------------------------------------
        // Health Checks
        // ---------------------------------------------------------------------------
        builder.Services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "database");

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

        app.MapHealthChecks("/health");

        app.Run();
    }
}
