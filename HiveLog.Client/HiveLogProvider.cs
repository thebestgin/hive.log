using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiveLog.Client;

/// <summary>
/// ILoggerProvider implementation for HiveLog.
/// Thread-safe: CreateLogger() is called concurrently by the DI container.
/// </summary>
[ProviderAlias("HiveLog")]
public sealed class HiveLogProvider : ILoggerProvider
{
    private readonly HiveLogBatchBuffer _buffer;
    private readonly HiveLogOptions _options;
    private readonly ConcurrentDictionary<string, HiveLogLogger> _loggers = new();

    internal HiveLogProvider(HiveLogBatchBuffer buffer, IOptions<HiveLogOptions> options)
    {
        _buffer = buffer;
        _options = options.Value;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName,
            cat => new HiveLogLogger(cat, _buffer, _options));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
