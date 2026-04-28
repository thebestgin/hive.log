using HiveLog.Client.Models;

namespace HiveLog.Client;

/// <summary>Fluent builder for advanced HiveLog entries (tags, properties, stream, trace).</summary>
public sealed class HiveLogEntryBuilder
{
    internal readonly ClientLogEntry Entry;

    internal HiveLogEntryBuilder(ClientLogEntry entry)
    {
        Entry = entry;
    }

    public HiveLogEntryBuilder WithTags(params string[] tags)
    {
        Entry.Tags = tags;
        return this;
    }

    public HiveLogEntryBuilder WithProperty(string key, object? value)
    {
        // Merge into existing properties or create new
        var dict = new Dictionary<string, object?> { [key] = value };
        Entry.Properties = System.Text.Json.JsonSerializer.Serialize(dict);
        return this;
    }

    public HiveLogEntryBuilder WithStream(string stream)
    {
        Entry.Stream = stream;
        return this;
    }

    public HiveLogEntryBuilder WithTrace(string? traceId, string? spanId = null)
    {
        Entry.TraceId = traceId;
        Entry.SpanId = spanId;
        return this;
    }

    public HiveLogEntryBuilder WithSessionId(string sessionId)
    {
        Entry.SessionId = sessionId;
        return this;
    }
}
