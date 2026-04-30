using HiveLog.Api.Features.Logs.Models;

namespace HiveLog.Api.Features.Stream;

/// <summary>
/// Filter criteria for an SSE stream subscription.
/// Null or empty collections mean "accept all".
/// </summary>
public sealed record StreamFilter(
    HashSet<string>? Sources,
    HashSet<short>? Levels,
    string? Stream,
    HashSet<string>? Tags)
{
    /// <summary>
    /// Returns true if the given log entry passes all filter criteria.
    /// Null or empty collections match any value for that dimension.
    /// </summary>
    public bool Matches(LogEntry entry)
    {
        if (Sources is { Count: > 0 } && !Sources.Contains(entry.Source))
            return false;

        if (Levels is { Count: > 0 } && !Levels.Contains(entry.Level))
            return false;

        if (Stream is not null && entry.Stream != Stream)
            return false;

        if (Tags is { Count: > 0 })
        {
            if (entry.Tags is null or { Length: 0 })
                return false;

            var hasMatchingTag = false;
            foreach (var tag in entry.Tags)
            {
                if (Tags.Contains(tag))
                {
                    hasMatchingTag = true;
                    break;
                }
            }

            if (!hasMatchingTag)
                return false;
        }

        return true;
    }
}
