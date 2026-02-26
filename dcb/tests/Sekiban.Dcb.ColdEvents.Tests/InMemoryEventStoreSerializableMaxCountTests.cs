using System.Text;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.ColdEvents.Tests;

/// <summary>
///     Tests for InMemoryEventStore.ReadAllSerializableEventsAsync(since, maxCount) overload.
///     The 2-arg overload should respect the maxCount parameter to support batch-based catch-up.
///     Uses IEventStore interface to access the 2-arg default interface method overload.
/// </summary>
public class InMemoryEventStoreSerializableMaxCountTests
{
    private readonly IEventStore _store;
    private readonly InMemoryEventStore _concreteStore;

    public InMemoryEventStoreSerializableMaxCountTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestPayload>();
        _concreteStore = new InMemoryEventStore(eventTypes);
        _store = _concreteStore;
    }

    private async Task<List<string>> WriteTestEvents(int count)
    {
        var sortableIds = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var eventId = Guid.NewGuid();
            var sortableId = SortableUniqueId.Generate(
                DateTime.UtcNow.AddSeconds(i), eventId);
            var evt = new Event(
                new TestPayload($"Event{i}"),
                sortableId,
                nameof(TestPayload),
                eventId,
                new EventMetadata("cause", "corr", "user"),
                [$"test:{i}"]);
            await _concreteStore.WriteEventAsync(evt);
            sortableIds.Add(sortableId);
        }
        return sortableIds;
    }

    [Fact]
    public async Task Should_limit_results_when_maxCount_specified()
    {
        // Given: 5 events written to the store
        await WriteTestEvents(5);

        // When: reading with maxCount = 3
        var result = await _store.ReadAllSerializableEventsAsync(since: null, maxCount: 3);

        // Then: only 3 events are returned
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task Should_filter_by_since_and_limit_by_maxCount()
    {
        // Given: 5 events written to the store
        var sortableIds = await WriteTestEvents(5);

        // When: reading since the 2nd event with maxCount = 2
        var since = new SortableUniqueId(sortableIds[1]);
        var result = await _store.ReadAllSerializableEventsAsync(since, maxCount: 2);

        // Then: 2 events after the 2nd event are returned (events 3 and 4)
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(2, events.Count);
        Assert.True(
            string.Compare(events[0].SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
        Assert.True(
            string.Compare(events[1].SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
    }

    [Fact]
    public async Task Should_return_all_events_when_maxCount_is_null()
    {
        // Given: 5 events written to the store
        await WriteTestEvents(5);

        // When: reading with null maxCount
        var result = await _store.ReadAllSerializableEventsAsync(since: null, maxCount: null);

        // Then: all 5 events are returned
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(5, events.Count);
    }

    [Fact]
    public async Task Should_return_all_events_when_maxCount_exceeds_total()
    {
        // Given: 3 events written to the store
        await WriteTestEvents(3);

        // When: maxCount is larger than total event count
        var result = await _store.ReadAllSerializableEventsAsync(since: null, maxCount: 100);

        // Then: all 3 events are returned
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task Should_return_events_in_sortable_order()
    {
        // Given: 5 events written to the store
        await WriteTestEvents(5);

        // When: reading with maxCount
        var result = await _store.ReadAllSerializableEventsAsync(since: null, maxCount: 3);

        // Then: returned events are in SortableUniqueId order
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        for (var i = 0; i < events.Count - 1; i++)
        {
            Assert.True(
                string.Compare(
                    events[i].SortableUniqueIdValue,
                    events[i + 1].SortableUniqueIdValue,
                    StringComparison.Ordinal) < 0,
                $"Events not ordered: {events[i].SortableUniqueIdValue} should be before {events[i + 1].SortableUniqueIdValue}");
        }
    }

    [Fact]
    public async Task Should_return_empty_when_no_events_after_since()
    {
        // Given: 3 events written to the store
        var sortableIds = await WriteTestEvents(3);

        // When: since is the last event
        var since = new SortableUniqueId(sortableIds[^1]);
        var result = await _store.ReadAllSerializableEventsAsync(since, maxCount: 10);

        // Then: no events returned
        Assert.True(result.IsSuccess);
        Assert.Empty(result.GetValue());
    }

    private sealed record TestPayload(string Name) : IEventPayload;
}
