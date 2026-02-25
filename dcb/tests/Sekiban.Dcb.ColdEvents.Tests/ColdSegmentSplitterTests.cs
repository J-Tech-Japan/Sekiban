using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.ColdEvents.Tests;

public class ColdSegmentSplitterTests
{
    private static SerializableEvent CreateEvent(int payloadSize)
    {
        var sortableId = SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());
        return new SerializableEvent(
            Payload: new byte[payloadSize],
            SortableUniqueIdValue: sortableId,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "corr", "user"),
            Tags: ["tag1"],
            EventPayloadName: "TestEvent");
    }

    [Fact]
    public void Should_return_empty_when_no_events()
    {
        // When
        var result = ColdSegmentSplitter.Split([], maxEvents: 100, maxBytes: 1000);

        // Then
        Assert.Empty(result);
    }

    [Fact]
    public void Should_not_split_when_under_limits()
    {
        // Given
        var events = Enumerable.Range(0, 5).Select(_ => CreateEvent(10)).ToList();

        // When
        var result = ColdSegmentSplitter.Split(events, maxEvents: 100, maxBytes: 10_000);

        // Then
        Assert.Single(result);
        Assert.Equal(5, result[0].Count);
    }

    [Fact]
    public void Should_split_by_event_count()
    {
        // Given
        var events = Enumerable.Range(0, 250).Select(_ => CreateEvent(10)).ToList();

        // When
        var result = ColdSegmentSplitter.Split(events, maxEvents: 100, maxBytes: long.MaxValue);

        // Then
        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].Count);
        Assert.Equal(100, result[1].Count);
        Assert.Equal(50, result[2].Count);
    }

    [Fact]
    public void Should_split_by_byte_size()
    {
        // Given: 5 events of 100 bytes each, max 250 bytes
        var events = Enumerable.Range(0, 5).Select(_ => CreateEvent(100)).ToList();

        // When
        var result = ColdSegmentSplitter.Split(events, maxEvents: int.MaxValue, maxBytes: 250);

        // Then: first 2 fit (200 bytes), 3rd would exceed so new segment, etc.
        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(2, result[1].Count);
        Assert.Single(result[2]);
    }

    [Fact]
    public void Should_split_by_whichever_limit_is_reached_first()
    {
        // Given: 10 events of 50 bytes each; maxEvents=3, maxBytes=500
        var events = Enumerable.Range(0, 10).Select(_ => CreateEvent(50)).ToList();

        // When
        var result = ColdSegmentSplitter.Split(events, maxEvents: 3, maxBytes: 500);

        // Then: splits at 3 events each (count limit reached first)
        Assert.Equal(4, result.Count);
        Assert.Equal(3, result[0].Count);
        Assert.Equal(3, result[1].Count);
        Assert.Equal(3, result[2].Count);
        Assert.Single(result[3]);
    }
}
