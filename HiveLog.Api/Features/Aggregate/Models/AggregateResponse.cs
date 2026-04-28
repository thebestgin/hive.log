namespace HiveLog.Api.Features.Aggregate.Models;

public sealed class AggregateResponse
{
    public AggregateBucket[] Buckets { get; init; } = [];
}
