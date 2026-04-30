using HiveLog.Api.Features.Ingest;
using HiveLog.Api.Features.Logs.Models;

namespace HiveLog.Api.Tests.Features.Ingest;

public class IngestBufferTests
{
    private static LogEntry MakeEntry() => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Source = "test",
        SourceType = "backend",
        Level = 2,
        Category = "Test",
        Message = "test",
        Stream = "app",
    };

    [Fact]
    public async Task TryWriteAsync_ReturnsTrue()
    {
        var buffer = new IngestBuffer(capacity: 10);
        var entry = MakeEntry();

        var result = await buffer.TryWriteAsync(entry, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void TryWriteSync_ReturnsTrue_WhenSpaceAvailable()
    {
        var buffer = new IngestBuffer(capacity: 10);
        var entry = MakeEntry();

        var result = buffer.TryWriteSync(entry);

        Assert.True(result);
    }

    [Fact]
    public async Task DrainTo_ExtractsAllEntries()
    {
        var buffer = new IngestBuffer(capacity: 10);
        await buffer.TryWriteAsync(MakeEntry(), TimeSpan.FromSeconds(1), CancellationToken.None);
        await buffer.TryWriteAsync(MakeEntry(), TimeSpan.FromSeconds(1), CancellationToken.None);
        await buffer.TryWriteAsync(MakeEntry(), TimeSpan.FromSeconds(1), CancellationToken.None);

        var batch = new List<LogEntry>();
        var drained = buffer.DrainTo(batch, 10);

        Assert.Equal(3, drained);
        Assert.Equal(3, batch.Count);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task DrainTo_RespectsMaxLimit()
    {
        var buffer = new IngestBuffer(capacity: 10);
        for (var i = 0; i < 5; i++)
            await buffer.TryWriteAsync(MakeEntry(), TimeSpan.FromSeconds(1), CancellationToken.None);

        var batch = new List<LogEntry>();
        var drained = buffer.DrainTo(batch, 3);

        Assert.Equal(3, drained);
        Assert.Equal(3, batch.Count);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public async Task Count_ReflectsCurrentSize()
    {
        var buffer = new IngestBuffer(capacity: 10);
        Assert.Equal(0, buffer.Count);

        await buffer.TryWriteAsync(MakeEntry(), TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(1, buffer.Count);

        var batch = new List<LogEntry>();
        buffer.DrainTo(batch, 10);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public async Task TryWriteAsync_FullBuffer_Timeout_ReturnsFalse()
    {
        var buffer = new IngestBuffer(capacity: 1);
        // Fill the buffer
        await buffer.TryWriteAsync(MakeEntry(), TimeSpan.FromSeconds(1), CancellationToken.None);

        // Next write should timeout
        var result = await buffer.TryWriteAsync(MakeEntry(), TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public void TryWriteSync_FullBuffer_ReturnsFalse()
    {
        var buffer = new IngestBuffer(capacity: 1);
        buffer.TryWriteSync(MakeEntry()); // Fill

        var result = buffer.TryWriteSync(MakeEntry()); // Full

        Assert.False(result);
    }

    [Fact]
    public void Complete_PreventsNewWrites()
    {
        var buffer = new IngestBuffer(capacity: 10);
        buffer.Complete();

        // TryWriteSync should return false on a completed channel
        var result = buffer.TryWriteSync(MakeEntry());
        Assert.False(result);
    }

    [Fact]
    public async Task CustomCapacity_IsRespected()
    {
        var buffer = new IngestBuffer(capacity: 5);

        // Write 5 entries synchronously
        for (var i = 0; i < 5; i++)
            Assert.True(buffer.TryWriteSync(MakeEntry()));

        // 6th sync write should fail (buffer full)
        Assert.False(buffer.TryWriteSync(MakeEntry()));
    }
}
