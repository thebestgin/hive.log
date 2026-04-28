namespace HiveLog.Api.Features.Aggregate.Models;

public sealed class AggregateBucket
{
    public DateTimeOffset Time { get; init; }
    public string? Source { get; init; }
    public short? Level { get; init; }
    public string? Stream { get; init; }
    public long Count { get; init; }
}
