using System.Diagnostics;
using HiveLog.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HiveLog.Client;

/// <summary>
/// Extended HiveLog client. Writes directly into the shared HiveLogBatchBuffer.
/// Activity.Current is read at call time (thread-local).
/// </summary>
public sealed class HiveLogClient : IHiveLogClient
{
    private readonly HiveLogBatchBuffer _buffer;
    private readonly HiveLogOptions _options;

    public HiveLogClient(HiveLogBatchBuffer buffer, IOptions<HiveLogOptions> options)
    {
        _buffer = buffer;
        _options = options.Value;
    }

    public void Log(LogLevel level, string category, string message,
                    Action<HiveLogEntryBuilder>? configure = null)
    {
        if (level < _options.MinLevel) return;

        // Activity.Current is thread-local — read NOW on the calling thread
        var activity = Activity.Current;

        var entry = new ClientLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            ParentSpanId = activity?.ParentSpanId.ToString(),
            Level = (int)level,
            Category = category,
            Message = message,
            Stream = "app",
        };

        if (configure != null)
        {
            var builder = new HiveLogEntryBuilder(entry);
            configure(builder);
        }

        _buffer.TryWrite(entry);
    }
}
