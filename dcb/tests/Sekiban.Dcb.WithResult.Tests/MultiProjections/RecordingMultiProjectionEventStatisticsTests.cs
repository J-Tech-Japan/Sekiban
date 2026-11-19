using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Xunit;

namespace Sekiban.Dcb.Tests.MultiProjections;

public class RecordingMultiProjectionEventStatisticsTests
{
    private sealed record Payload : IEventPayload;

    [Fact]
    public async Task ConcurrentRecordingAndSnapshot_KeepExactCounts()
    {
        const int writers = 8;
        const int batchesPerWriter = 200;
        const int batchSize = 50;
        var stats = new RecordingMultiProjectionEventStatistics();
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                stats.Snapshot();
            }
        });
        var start = new Barrier(writers);
        var tasks = Enumerable.Range(0, writers).Select(writer => Task.Run(() =>
        {
            start.SignalAndWait();
            for (var batch = 0; batch < batchesPerWriter; batch++)
            {
                var events = Enumerable.Range(0, batchSize).Select(_ => NewEvent()).ToList();
                if ((writer + batch) % 2 == 0)
                {
                    stats.RecordStreamBatch(events);
                }
                else
                {
                    stats.RecordCatchUpBatch(events);
                }
            }
        })).ToArray();
        await Task.WhenAll(tasks);
        stop.Cancel();
        await reader;

        var snapshot = stats.Snapshot();
        const int expected = writers * batchesPerWriter * batchSize;
        Assert.Equal(expected, snapshot.totalUnique);
        Assert.Equal(expected, snapshot.totalDeliveries);
        Assert.Equal(expected, snapshot.streamDeliveries + snapshot.catchUpDeliveries);
        Assert.Equal(expected, snapshot.streamUnique + snapshot.catchUpUnique);
        Assert.Equal(0, snapshot.duplicateDeliveries);
    }

    private static Event NewEvent() => new(
        new Payload(),
        string.Empty,
        nameof(Payload),
        Guid.NewGuid(),
        new EventMetadata(string.Empty, string.Empty, "test"),
        []);
}
