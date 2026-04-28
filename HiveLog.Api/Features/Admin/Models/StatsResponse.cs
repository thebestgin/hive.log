namespace HiveLog.Api.Features.Admin.Models;

public sealed record StatsResponse(
    int BufferDepth,
    double IngestRatePerSecond,
    long DroppedTotal,
    int ActiveSubscribers,
    long ChunkCount);
