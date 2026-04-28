using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HiveLog.Client;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers HiveLog as an ILoggerProvider and IHiveLogClient.
    /// Reads configuration from the provided IConfiguration section.
    ///
    /// Usage:
    ///   builder.Logging.AddHiveLog(config.GetSection("HiveLog"))
    ///
    /// Required config keys: BaseUrl, ApiKey, Source
    /// </summary>
    public static ILoggingBuilder AddHiveLog(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        builder.Services.Configure<HiveLogOptions>(configuration);

        // Shared buffer — single instance shared by ILoggerProvider + IHiveLogClient
        builder.Services.AddSingleton<HiveLogBatchBuffer>(sp =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HiveLogOptions>>().Value;
            return new HiveLogBatchBuffer(opts.Buffer.Capacity);
        });

        // Metrics
        builder.Services.AddSingleton<HiveLogClientMetrics>();

        // Named HttpClient for ingest requests
        builder.Services.AddHttpClient("hivelog", (sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HiveLogOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Prevent feedback loop: suppress the HiveLog HTTP client's own logs from being forwarded to HiveLog
        builder.AddFilter<HiveLogProvider>("System.Net.Http.HttpClient.hivelog", LogLevel.None);
        builder.AddFilter<HiveLogProvider>("HiveLog.Client", LogLevel.None);

        // ILoggerProvider — register as both concrete and interface so the logging infrastructure picks it up
        builder.Services.AddSingleton<HiveLogProvider>();
        builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<HiveLogProvider>());

        // IHiveLogClient
        builder.Services.AddSingleton<IHiveLogClient, HiveLogClient>();

        // BackgroundService
        builder.Services.AddHostedService<HiveLogSenderService>();

        return builder;
    }
}
