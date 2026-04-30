using HiveLog.Api.Features.Logs.Models;
using HiveLog.Api.Features.Stream;

namespace HiveLog.Api.Tests.Features.Stream;

public class StreamFilterTests
{
    // --- Helpers ---

    private static LogEntry MakeEntry(
        string source = "test-api",
        short level = 2,
        string stream = "app",
        string[]? tags = null)
    {
        return new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Source = source,
            SourceType = "backend",
            Level = level,
            Category = "Test",
            Message = "test message",
            Stream = stream,
            Tags = tags,
        };
    }

    // --- No filter ---

    [Fact]
    public void NoFilter_MatchesAnyEntry()
    {
        var filter = new StreamFilter(null, null, null, null);
        var entry = MakeEntry();

        Assert.True(filter.Matches(entry));
    }

    // --- Source filter ---

    [Fact]
    public void SourceFilter_MatchesExactSource()
    {
        var filter = new StreamFilter(["talents-api"], null, null, null);
        var entry = MakeEntry(source: "talents-api");

        Assert.True(filter.Matches(entry));
    }

    [Fact]
    public void SourceFilter_RejectsOtherSource()
    {
        var filter = new StreamFilter(["talents-api"], null, null, null);
        var entry = MakeEntry(source: "userprofiles-api");

        Assert.False(filter.Matches(entry));
    }

    // --- Level filter ---

    [Fact]
    public void LevelFilter_MatchesExactLevel()
    {
        var filter = new StreamFilter(null, [4], null, null);
        var entry = MakeEntry(level: 4);

        Assert.True(filter.Matches(entry));
    }

    [Fact]
    public void LevelFilter_RejectsOtherLevel()
    {
        var filter = new StreamFilter(null, [4], null, null);
        var entry = MakeEntry(level: 3);

        Assert.False(filter.Matches(entry));
    }

    // --- Stream filter ---

    [Fact]
    public void StreamFilter_MatchesExactStream()
    {
        var filter = new StreamFilter(null, null, "agent", null);
        var entry = MakeEntry(stream: "agent");

        Assert.True(filter.Matches(entry));
    }

    [Fact]
    public void StreamFilter_RejectsOtherStream()
    {
        var filter = new StreamFilter(null, null, "agent", null);
        var entry = MakeEntry(stream: "app");

        Assert.False(filter.Matches(entry));
    }

    // --- Tag filter ---

    [Fact]
    public void TagFilter_MatchesIfAnyTagPresent()
    {
        var filter = new StreamFilter(null, null, null, ["error", "critical"]);
        var entry = MakeEntry(tags: ["info", "error"]);

        Assert.True(filter.Matches(entry));
    }

    [Fact]
    public void TagFilter_RejectsIfNoTagMatches()
    {
        var filter = new StreamFilter(null, null, null, ["critical"]);
        var entry = MakeEntry(tags: ["info"]);

        Assert.False(filter.Matches(entry));
    }

    [Fact]
    public void TagFilter_RejectsEntryWithNoTags()
    {
        var filter = new StreamFilter(null, null, null, ["critical"]);
        var entry = MakeEntry(tags: null);

        Assert.False(filter.Matches(entry));
    }

    // --- Combined filter ---

    [Fact]
    public void CombinedFilter_AllConditionsMustMatch()
    {
        var filter = new StreamFilter(["talents-api"], [4], null, null);

        // Both conditions met
        var match = MakeEntry(source: "talents-api", level: 4);
        Assert.True(filter.Matches(match));

        // Source wrong
        var wrongSource = MakeEntry(source: "discovery-api", level: 4);
        Assert.False(filter.Matches(wrongSource));

        // Level wrong
        var wrongLevel = MakeEntry(source: "talents-api", level: 2);
        Assert.False(filter.Matches(wrongLevel));
    }
}
