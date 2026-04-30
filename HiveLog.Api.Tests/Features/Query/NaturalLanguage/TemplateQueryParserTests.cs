using HiveLog.Api.Features.Query.NaturalLanguage;

namespace HiveLog.Api.Tests.Features.Query.NaturalLanguage;

public class TemplateQueryParserTests
{
    [Fact]
    public void NullInput_ReturnsNull()
    {
        var result = TemplateQueryParser.TryParse(null!);

        Assert.Null(result);
    }

    [Fact]
    public void WhitespaceInput_ReturnsNull()
    {
        var result = TemplateQueryParser.TryParse("   ");

        Assert.Null(result);
    }

    [Fact]
    public void TraceId_Detected()
    {
        var uuid = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        var result = TemplateQueryParser.TryParse(uuid);

        Assert.NotNull(result);
        Assert.Equal(uuid, result.Request.TraceId);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void CountQuery_German()
    {
        var result = TemplateQueryParser.TryParse("wie viele fehler heute");

        Assert.NotNull(result);
        Assert.Equal(TemplateQueryParser.QueryKind.Count, result.Kind);
        Assert.Equal(0.85, result.Confidence);
    }

    [Fact]
    public void CountQuery_English()
    {
        var result = TemplateQueryParser.TryParse("how many errors last hour");

        Assert.NotNull(result);
        Assert.Equal(TemplateQueryParser.QueryKind.Count, result.Kind);
    }

    [Fact]
    public void ErrorsToday_German()
    {
        var result = TemplateQueryParser.TryParse("Fehler heute");

        Assert.NotNull(result);
        Assert.Equal((short)4, result.Request.Levels?.Min);
        Assert.NotNull(result.Request.TimeRange?.From);
        // From should be today start (UTC)
        var todayStart = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        Assert.True(result.Request.TimeRange!.From >= todayStart);
    }

    [Fact]
    public void ErrorsToday_English()
    {
        var result = TemplateQueryParser.TryParse("errors today");

        Assert.NotNull(result);
        Assert.Equal((short)4, result.Request.Levels?.Min);
        Assert.NotNull(result.Request.TimeRange?.From);
    }

    [Fact]
    public void ErrorsInService()
    {
        var result = TemplateQueryParser.TryParse("errors in talents-api");

        Assert.NotNull(result);
        Assert.Contains("talents-api", result.Request.Sources!);
        Assert.Equal((short)4, result.Request.Levels?.Min);
    }

    [Fact]
    public void LastNMinutes_SetsTimeRange()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-31);
        var result = TemplateQueryParser.TryParse("letzte 30 minuten");

        Assert.NotNull(result);
        Assert.NotNull(result.Request.TimeRange?.From);
        Assert.True(result.Request.TimeRange!.From >= before);
    }

    [Fact]
    public void LastHour_SetsTimeRange()
    {
        // "letzte Stunde" alone is only 2 words (no top-level rule matches) →
        // combine with a level keyword so the level-only rule fires and ApplyTimeContext runs.
        var before = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(-1);
        var result = TemplateQueryParser.TryParse("errors last hour");

        Assert.NotNull(result);
        Assert.NotNull(result.Request.TimeRange?.From);
        Assert.True(result.Request.TimeRange!.From >= before);
    }

    [Fact]
    public void Yesterday_SetsFromTo()
    {
        var result = TemplateQueryParser.TryParse("gestern fehler");

        Assert.NotNull(result);
        Assert.NotNull(result.Request.TimeRange?.From);
        Assert.NotNull(result.Request.TimeRange?.To);

        var yesterday = DateTimeOffset.UtcNow.Date.AddDays(-1);
        Assert.Equal(new DateTimeOffset(yesterday, TimeSpan.Zero), result.Request.TimeRange!.From);
        Assert.Equal(new DateTimeOffset(yesterday.AddDays(1), TimeSpan.Zero), result.Request.TimeRange.To);
    }

    [Fact]
    public void FatalKeyword_SetsLevel5()
    {
        var result = TemplateQueryParser.TryParse("fatal errors");

        Assert.NotNull(result);
        Assert.Equal((short)5, result.Request.Levels?.Min);
    }

    [Fact]
    public void WarnKeyword_SetsLevel3()
    {
        var result = TemplateQueryParser.TryParse("warnungen anzeigen bitte");

        Assert.NotNull(result);
        Assert.Equal((short)3, result.Request.Levels?.Min);
    }

    [Fact]
    public void FreeText_ThreeWords()
    {
        var result = TemplateQueryParser.TryParse("something went wrong");

        Assert.NotNull(result);
        Assert.Equal("something went wrong", result.Request.Search);
        Assert.Equal(0.4, result.Confidence);
    }

    [Fact]
    public void FreeText_TwoWords_ReturnsNull()
    {
        // 2 words with no recognized keywords → no pattern matches → null
        // (Note: words like "error"/"warn"/"fatal" trigger the level-only rule at 2 words,
        // so we use a truly unrecognized phrase here.)
        var result = TemplateQueryParser.TryParse("nothing here");

        Assert.Null(result);
    }
}
