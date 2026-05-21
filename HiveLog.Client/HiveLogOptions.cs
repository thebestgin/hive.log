using Microsoft.Extensions.Logging;

namespace HiveLog.Client;

public class HiveLogOptions
{
    /// <summary>Master switch. When false, AddHiveLog skips all registrations — no provider, no HttpClient, no background service.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>HiveLog API base URL. Example: http://localhost:5099</summary>
    public string BaseUrl { get; set; } = null!;

    /// <summary>API key for X-Api-Key header. Identifies the service to HiveLog via the backend-service connector.</summary>
    public string ApiKey { get; set; } = null!;

    /// <summary>Optional instance identifier. Default: machine name.</summary>
    public string? InstanceId { get; set; }

    /// <summary>Minimum log level to send. Default: Information.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Information;

    /// <summary>Buffer configuration.</summary>
    public BufferOptions Buffer { get; set; } = new();

    public class BufferOptions
    {
        /// <summary>Hard latency cap. Default: 10ms.</summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromMilliseconds(10);

        /// <summary>Idle flush after no new items. Default: 3ms.</summary>
        public TimeSpan IdleAfter { get; set; } = TimeSpan.FromMilliseconds(3);

        /// <summary>Flush immediately when buffer reaches this size. Default: 200.</summary>
        public int MaxSize { get; set; } = 200;

        /// <summary>Channel capacity. DropOldest when full. Default: 1000.</summary>
        public int Capacity { get; set; } = 1000;
    }
}
