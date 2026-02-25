using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.ColdEvents.Tests;

public class SafeWindowFilterTests
{
    private static SerializableEvent CreateEvent(DateTime timestamp)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new SerializableEvent(
            Payload: [1, 2, 3],
            SortableUniqueIdValue: sortableId,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "corr", "user"),
            Tags: ["tag1"],
            EventPayloadName: "TestEvent");
    }

    [Fact]
    public void Should_include_events_at_cutoff_boundary()
    {
        // Given
        var cutoff = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var events = new[] { CreateEvent(cutoff) };

        // When
        var result = SafeWindowFilter.Apply(events, cutoff);

        // Then
        Assert.Single(result);
    }

    [Fact]
    public void Should_include_events_before_cutoff()
    {
        // Given
        var cutoff = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var events = new[] { CreateEvent(cutoff.AddMilliseconds(-1)) };

        // When
        var result = SafeWindowFilter.Apply(events, cutoff);

        // Then
        Assert.Single(result);
    }

    [Fact]
    public void Should_exclude_events_after_cutoff()
    {
        // Given
        var cutoff = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var events = new[] { CreateEvent(cutoff.AddMilliseconds(1)) };

        // When
        var result = SafeWindowFilter.Apply(events, cutoff);

        // Then
        Assert.Empty(result);
    }

    [Fact]
    public void Should_filter_mixed_events_correctly()
    {
        // Given
        var cutoff = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var safe1 = CreateEvent(cutoff.AddMinutes(-5));
        var safe2 = CreateEvent(cutoff);
        var unsafe1 = CreateEvent(cutoff.AddSeconds(1));
        var unsafe2 = CreateEvent(cutoff.AddMinutes(10));

        // When
        var result = SafeWindowFilter.Apply([safe1, safe2, unsafe1, unsafe2], cutoff);

        // Then
        Assert.Equal(2, result.Count);
    }
}
