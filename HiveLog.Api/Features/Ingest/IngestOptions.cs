namespace HiveLog.Api.Features.Ingest;

public class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>Channel capacity. Default: 10.000.</summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Max items per flush batch. Default: 1000.</summary>
    public int BatchMaxSize { get; set; } = 1000;

    /// <summary>Idle flush after no new items. Default: 5ms.</summary>
    public TimeSpan BatchIdleAfter { get; set; } = TimeSpan.FromMilliseconds(5);

    /// <summary>Hard latency cap — flush regardless of activity. Default: 25ms.</summary>
    public TimeSpan BatchWindow { get; set; } = TimeSpan.FromMilliseconds(25);

    /// <summary>Timeout for channel write when buffer is full. Default: 100ms.</summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Max drain time on shutdown. Default: 5s.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
