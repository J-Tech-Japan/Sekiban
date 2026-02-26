using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.ColdEvents.Tests;

/// <summary>
///     Tests that validate the behavioral difference between ReadAllEventsAsync
///     and ReadAllSerializableEventsAsync in HybridEventStore, specifically for
///     catch-up scenarios where cold event access is critical.
///     These tests document why MultiProjectionGrain catch-up must prefer
///     ReadAllSerializableEventsAsync over ReadAllEventsAsync.
/// </summary>
public class HybridEventStoreCatchUpPathTests
{
    private const string TestServiceId = "default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly ColdEventStoreOptions EnabledOptions = new()
    {
        Enabled = true,
        PullInterval = TimeSpan.FromMinutes(30),
        SafeWindow = TimeSpan.FromMinutes(2),
        SegmentMaxEvents = 100_000,
        SegmentMaxBytes = 512L * 1024 * 1024
    };

    private readonly InMemoryColdObjectStorage _coldStorage = new();

    private static SerializableEvent CreateSerializableEvent(DateTime timestamp, string name)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new SerializableEvent(
            Payload: Encoding.UTF8.GetBytes("{\"name\":\"" + name + "\"}"),
            SortableUniqueIdValue: sortableId,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "corr", "user"),
            Tags: ["tag1"],
            EventPayloadName: name);
    }

    private HybridEventStore CreateHybrid(IEventStore hotStore)
    {
        return new HybridEventStore(
            hotStore,
            _coldStorage,
            new DefaultServiceIdProvider(),
            Options.Create(EnabledOptions),
            NullLogger<HybridEventStore>.Instance);
    }

    private async Task StoreColdManifestAndSegmentsAsync(
        IReadOnlyList<SerializableEvent> coldEvents,
        string latestSafe)
    {
        var segmentData = JsonlSegmentWriter.Write(coldEvents);
        var segmentPath = $"segments/{TestServiceId}/cold_segment.jsonl";
        await _coldStorage.PutAsync(segmentPath, segmentData, expectedETag: null, CancellationToken.None);

        var manifest = new ColdManifest(
            ServiceId: TestServiceId,
            ManifestVersion: "v1",
            LatestSafeSortableUniqueId: latestSafe,
            Segments:
            [
                new ColdSegmentInfo(
                    Path: segmentPath,
                    FromSortableUniqueId: coldEvents[0].SortableUniqueIdValue,
                    ToSortableUniqueId: coldEvents[^1].SortableUniqueIdValue,
                    EventCount: coldEvents.Count,
                    SizeBytes: segmentData.Length,
                    Sha256: "test",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var manifestPath = $"control/{TestServiceId}/manifest.json";
        await _coldStorage.PutAsync(manifestPath, manifestData, expectedETag: null, CancellationToken.None);
    }

    [Fact]
    public async Task ReadAllEventsAsync_should_only_return_hot_events_when_cold_events_exist()
    {
        // Given: cold events exist in storage and hot store has only recent events
        var coldTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var coldEvent1 = CreateSerializableEvent(coldTime, "ColdEvent1");
        var coldEvent2 = CreateSerializableEvent(coldTime.AddMinutes(1), "ColdEvent2");
        await StoreColdManifestAndSegmentsAsync([coldEvent1, coldEvent2], coldEvent2.SortableUniqueIdValue);

        var hotTime = coldTime.AddMinutes(10);
        var hotEvent = CreateSerializableEvent(hotTime, "HotEvent");
        var hotStore = new TrackingEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When: catch-up reads via ReadAllEventsAsync (the current grain behavior)
        var result = await hybrid.ReadAllEventsAsync(since: null, maxCount: 500);

        // Then: only hot events are returned — cold events are missed
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("HotEvent", events[0].EventType);
        Assert.True(hotStore.ReadAllEventsAsyncCalled);
        Assert.False(hotStore.ReadAllSerializableEventsAsyncCalled);
    }

    [Fact]
    public async Task ReadAllSerializableEventsAsync_should_return_cold_and_hot_events_merged()
    {
        // Given: cold events exist in storage and hot store has only recent events
        var coldTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var coldEvent1 = CreateSerializableEvent(coldTime, "ColdEvent1");
        var coldEvent2 = CreateSerializableEvent(coldTime.AddMinutes(1), "ColdEvent2");
        await StoreColdManifestAndSegmentsAsync([coldEvent1, coldEvent2], coldEvent2.SortableUniqueIdValue);

        var hotTime = coldTime.AddMinutes(10);
        var hotEvent = CreateSerializableEvent(hotTime, "HotEvent");
        var hotStore = new TrackingEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When: catch-up reads via ReadAllSerializableEventsAsync (the new grain behavior)
        var result = await hybrid.ReadAllSerializableEventsAsync(since: null, maxCount: 500);

        // Then: all events (cold + hot) are returned in order
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal("ColdEvent1", events[0].EventPayloadName);
        Assert.Equal("ColdEvent2", events[1].EventPayloadName);
        Assert.Equal("HotEvent", events[2].EventPayloadName);
    }

    [Fact]
    public async Task ReadAllSerializableEventsAsync_with_since_across_cold_boundary_should_return_remaining_cold_and_hot()
    {
        // Given: 3 cold events + 1 hot event, since points into middle of cold range
        var coldTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var coldEvent1 = CreateSerializableEvent(coldTime, "ColdEvent1");
        var coldEvent2 = CreateSerializableEvent(coldTime.AddMinutes(1), "ColdEvent2");
        var coldEvent3 = CreateSerializableEvent(coldTime.AddMinutes(2), "ColdEvent3");
        await StoreColdManifestAndSegmentsAsync(
            [coldEvent1, coldEvent2, coldEvent3],
            coldEvent3.SortableUniqueIdValue);

        var hotTime = coldTime.AddMinutes(10);
        var hotEvent = CreateSerializableEvent(hotTime, "HotEvent");
        var hotStore = new TrackingEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When: reading since the first cold event (should skip it, return the rest)
        var since = new SortableUniqueId(coldEvent1.SortableUniqueIdValue);
        var result = await hybrid.ReadAllSerializableEventsAsync(since, maxCount: 500);

        // Then: first cold event is excluded, remaining cold + hot are returned
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal("ColdEvent2", events[0].EventPayloadName);
        Assert.Equal("ColdEvent3", events[1].EventPayloadName);
        Assert.Equal("HotEvent", events[2].EventPayloadName);
    }

    [Fact]
    public async Task ReadAllSerializableEventsAsync_with_since_after_cold_boundary_should_only_read_hot()
    {
        // Given: cold events with boundary, since is after the cold boundary
        var coldTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var coldEvent = CreateSerializableEvent(coldTime, "ColdEvent");
        await StoreColdManifestAndSegmentsAsync([coldEvent], coldEvent.SortableUniqueIdValue);

        var hotTime = coldTime.AddMinutes(10);
        var hotEvent1 = CreateSerializableEvent(hotTime, "HotEvent1");
        var hotEvent2 = CreateSerializableEvent(hotTime.AddMinutes(1), "HotEvent2");
        var hotStore = new TrackingEventStore([hotEvent1, hotEvent2]);
        var hybrid = CreateHybrid(hotStore);

        // When: since is after cold boundary — hot store only
        var since = new SortableUniqueId(hotEvent1.SortableUniqueIdValue);
        var result = await hybrid.ReadAllSerializableEventsAsync(since, maxCount: 500);

        // Then: only events after since from hot store
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("HotEvent2", events[0].EventPayloadName);
    }

    [Fact]
    public async Task ReadAllSerializableEventsAsync_with_maxCount_should_limit_merged_results()
    {
        // Given: 2 cold events + 2 hot events
        var coldTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var coldEvent1 = CreateSerializableEvent(coldTime, "Cold1");
        var coldEvent2 = CreateSerializableEvent(coldTime.AddMinutes(1), "Cold2");
        await StoreColdManifestAndSegmentsAsync([coldEvent1, coldEvent2], coldEvent2.SortableUniqueIdValue);

        var hotTime = coldTime.AddMinutes(10);
        var hotEvent1 = CreateSerializableEvent(hotTime, "Hot1");
        var hotEvent2 = CreateSerializableEvent(hotTime.AddMinutes(1), "Hot2");
        var hotStore = new TrackingEventStore([hotEvent1, hotEvent2]);
        var hybrid = CreateHybrid(hotStore);

        // When: maxCount = 3 limits the merged result
        var result = await hybrid.ReadAllSerializableEventsAsync(since: null, maxCount: 3);

        // Then: only first 3 events in sorted order
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal("Cold1", events[0].EventPayloadName);
        Assert.Equal("Cold2", events[1].EventPayloadName);
        Assert.Equal("Hot1", events[2].EventPayloadName);
    }

    [Fact]
    public async Task ReadAllEventsAsync_should_not_call_serializable_path()
    {
        // Given: hot store with events
        var hotEvent = CreateSerializableEvent(DateTime.UtcNow.AddMinutes(-5), "Event1");
        var hotStore = new TrackingEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When: ReadAllEventsAsync is called
        await hybrid.ReadAllEventsAsync(since: null, maxCount: 100);

        // Then: only ReadAllEventsAsync was called on hot store, not serializable path
        Assert.True(hotStore.ReadAllEventsAsyncCalled);
        Assert.False(hotStore.ReadAllSerializableEventsAsyncCalled);
    }

    /// <summary>
    ///     Stub IEventStore that tracks which read methods were called
    ///     and supports both Event and SerializableEvent paths.
    /// </summary>
    private sealed class TrackingEventStore : IEventStore
    {
        private readonly IReadOnlyList<SerializableEvent> _serializableEvents;

        public bool ReadAllEventsAsyncCalled { get; private set; }
        public bool ReadAllSerializableEventsAsyncCalled { get; private set; }

        public TrackingEventStore(IReadOnlyList<SerializableEvent> events)
        {
            _serializableEvents = events;
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(
            SortableUniqueId? since = null, int? maxCount = null)
        {
            ReadAllEventsAsyncCalled = true;
            IEnumerable<SerializableEvent> filtered = since is null
                ? _serializableEvents
                : _serializableEvents.Where(e =>
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            if (maxCount.HasValue)
                filtered = filtered.Take(maxCount.Value);

            var events = filtered.Select(se => new Event(
                new StubPayload(se.EventPayloadName),
                se.SortableUniqueIdValue,
                se.EventPayloadName,
                se.Id,
                se.EventMetadata,
                se.Tags)).ToList();
            return Task.FromResult(ResultBox.FromValue<IEnumerable<Event>>(events));
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
            => throw new NotSupportedException();

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
            => throw new NotSupportedException();

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
            IEnumerable<Event> events)
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag)
            => throw new NotSupportedException();

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null)
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since = null)
        {
            ReadAllSerializableEventsAsyncCalled = true;
            IEnumerable<SerializableEvent> filtered = since is null
                ? _serializableEvents
                : _serializableEvents.Where(e =>
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            return Task.FromResult(ResultBox.FromValue(filtered));
        }

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since, int? maxCount)
        {
            ReadAllSerializableEventsAsyncCalled = true;
            IEnumerable<SerializableEvent> filtered = since is null
                ? _serializableEvents
                : _serializableEvents.Where(e =>
                    string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            if (maxCount.HasValue)
                filtered = filtered.Take(maxCount.Value);
            return Task.FromResult(ResultBox.FromValue(filtered));
        }
    }

    private sealed record StubPayload(string Name) : IEventPayload;
}
