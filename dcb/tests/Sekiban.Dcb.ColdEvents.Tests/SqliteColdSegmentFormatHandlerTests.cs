using System.Text;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.ColdEvents.Tests;

public class SqliteColdSegmentFormatHandlerTests
{
    private static SerializableEvent CreateEvent(DateTime utcTime, string payloadName, string payloadJson)
    {
        var sortableId = SortableUniqueId.Generate(utcTime, Guid.NewGuid());
        return new SerializableEvent(
            Payload: Encoding.UTF8.GetBytes(payloadJson),
            SortableUniqueIdValue: sortableId,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "corr", "user"),
            Tags: ["tag1"],
            EventPayloadName: payloadName);
    }

    [Fact]
    public async Task Should_write_and_read_sqlite_segment_with_same_handler()
    {
        var handler = new SqliteColdSegmentFormatHandler();
        var events = new[]
        {
            CreateEvent(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Event1", "{\"value\":1}"),
            CreateEvent(new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc), "Event2", "{\"value\":2}"),
            CreateEvent(new DateTime(2025, 1, 1, 0, 0, 2, DateTimeKind.Utc), "Event3", "{\"value\":3}")
        };

        var builder = await handler.CreateBuilderAsync(events[0], CancellationToken.None);
        try
        {
            await builder.AppendAsync(events[1], CancellationToken.None);
            await builder.AppendAsync(events[2], CancellationToken.None);
            var artifact = await builder.CompleteAsync("default", CancellationToken.None);

            var readEvents = new List<SerializableEvent>();
            await using var stream = File.OpenRead(artifact.FilePath);
            var result = await handler.StreamSegmentAsync(
                stream,
                since: null,
                maxCount: 100,
                evt =>
                {
                    readEvents.Add(evt);
                    return ValueTask.CompletedTask;
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
            var value = result.GetValue();
            Assert.Equal(3, value.EventsRead);
            Assert.True(value.ReachedEndOfSegment);
            Assert.Equal(events[^1].SortableUniqueIdValue, value.LastSortableUniqueId);

            Assert.Equal(events.Select(x => x.SortableUniqueIdValue), readEvents.Select(x => x.SortableUniqueIdValue));
            Assert.Equal(events.Select(x => x.EventPayloadName), readEvents.Select(x => x.EventPayloadName));
            Assert.Equal(
                events.Select(x => Encoding.UTF8.GetString(x.Payload)),
                readEvents.Select(x => Encoding.UTF8.GetString(x.Payload)));
        }
        finally
        {
            await builder.DisposeAsync();
        }
    }
}
