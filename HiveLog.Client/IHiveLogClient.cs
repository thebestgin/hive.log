using Microsoft.Extensions.Logging;

namespace HiveLog.Client;

/// <summary>
/// Extended HiveLog client for structured logging with tags, properties, and stream routing.
/// Supplements ILogger with HiveLog-specific features.
/// </summary>
public interface IHiveLogClient
{
    /// <summary>
    /// Log an entry with optional fluent configuration (tags, properties, stream, trace).
    /// Activity.Current is automatically captured.
    /// </summary>
    void Log(LogLevel level, string category, string message,
             Action<HiveLogEntryBuilder>? configure = null);
}
