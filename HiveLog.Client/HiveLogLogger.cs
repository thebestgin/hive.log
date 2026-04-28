using System.Diagnostics;
using System.Text.Json;
using HiveLog.Client.Models;
using Microsoft.Extensions.Logging;

namespace HiveLog.Client;

internal sealed class HiveLogLogger : ILogger
{
    private readonly string _category;
    private readonly HiveLogBatchBuffer _buffer;
    private readonly HiveLogOptions _options;

    internal HiveLogLogger(string category, HiveLogBatchBuffer buffer, HiveLogOptions options)
    {
        _category = category;
        _buffer = buffer;
        _options = options;
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _options.MinLevel;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        // Activity.Current is thread-local — read NOW on the calling thread
        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString();
        var spanId = activity?.SpanId.ToString();

        // Map .NET LogLevel to HiveLog numeric level (Trace=0..Fatal=5)
        // .NET: Trace=0, Debug=1, Information=2, Warning=3, Error=4, Critical=5
        // HiveLog: Trace=0, Debug=1, Info=2, Warn=3, Error=4, Fatal=5
        var level = (int)logLevel; // Direct mapping works for 0-5

        string? propertiesJson = null;
        if (state is IReadOnlyList<KeyValuePair<string, object?>> structuredState)
        {
            var props = structuredState
                .Where(kv => kv.Key != "{OriginalFormat}")
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (props.Count > 0)
            {
                try { propertiesJson = JsonSerializer.Serialize(props); }
                catch { /* unserializable property values (e.g. IPEndPoint.ScopeId on Linux) — skip */ }
            }
        }

        string? exceptionJson = null;
        if (exception != null)
        {
            var exObj = new
            {
                type = exception.GetType().FullName,
                message = exception.Message,
                stackTrace = exception.StackTrace,
                inner = exception.InnerException?.Message,
            };
            exceptionJson = JsonSerializer.Serialize(exObj);
        }

        // Extract message template from structured state
        string? messageTemplate = null;
        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateList)
        {
            var templateKv = stateList.FirstOrDefault(kv => kv.Key == "{OriginalFormat}");
            messageTemplate = templateKv.Value?.ToString();
        }

        var entry = new ClientLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TraceId = traceId,
            SpanId = spanId,
            Level = level,
            Category = _category,
            Message = formatter(state, exception),
            MessageTemplate = messageTemplate,
            Properties = propertiesJson,
            Exception = exceptionJson,
            Stream = "app",
        };

        _buffer.TryWrite(entry);
    }
}
