namespace HiveLog.Api.Features.Stream;

/// <summary>
/// Filter criteria for an SSE stream subscription.
/// Null or empty collections mean "accept all".
/// </summary>
public sealed record StreamFilter(
    HashSet<string>? Sources,
    HashSet<short>? Levels,
    string? Stream,
    HashSet<string>? Tags);
