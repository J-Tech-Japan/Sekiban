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

public class HybridEventStoreTests
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

    private static readonly ColdEventStoreOptions DisabledOptions = new()
    {
        Enabled = false
    };

    private readonly InMemoryColdObjectStorage _coldStorage = new();

    private static SerializableEvent CreateEvent(DateTime timestamp, string name)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return CreateEvent(sortableId, name);
    }

    private static SerializableEvent CreateEvent(string sortableId, string name)
    {
        return new SerializableEvent(
            Payload: Encoding.UTF8.GetBytes("{\"name\":\"" + name + "\"}"),
            SortableUniqueIdValue: sortableId,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata("cause", "corr", "user"),
            Tags: ["tag1"],
            EventPayloadName: name);
    }

    private HybridEventStore CreateHybrid(IEventStore hotStore, ColdEventStoreOptions? options = null)
    {
        return new HybridEventStore(
            hotStore,
            _coldStorage,
            new DefaultServiceIdProvider(),
            Options.Create(options ?? EnabledOptions),
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
    public async Task Should_delegate_to_hot_store_when_disabled()
    {
        // Given
        var hotEvent = CreateEvent(DateTime.UtcNow.AddMinutes(-5), "HotEvent");
        var hotStore = new StubSerializableEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore, DisabledOptions);

        // When
        var result = await hybrid.ReadAllSerializableEventsAsync();

        // Then
        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValue());
    }

    [Fact]
    public async Task Should_delegate_to_hot_store_when_no_manifest()
    {
        // Given: no manifest in cold storage
        var hotEvent = CreateEvent(DateTime.UtcNow.AddMinutes(-5), "HotEvent");
        var hotStore = new StubSerializableEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When
        var result = await hybrid.ReadAllSerializableEventsAsync();

        // Then
        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValue());
    }

    [Fact]
    public async Task Should_merge_cold_and_hot_events_in_order()
    {
        // Given: 2 cold events + 1 hot tail event
        var coldTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var coldEvent1 = CreateEvent(coldTime, "ColdEvent1");
        var coldEvent2 = CreateEvent(coldTime.AddMinutes(1), "ColdEvent2");
        var latestSafe = coldEvent2.SortableUniqueIdValue;

        await StoreColdManifestAndSegmentsAsync([coldEvent1, coldEvent2], latestSafe);

        var hotTime = coldTime.AddMinutes(5);
        var hotEvent = CreateEvent(hotTime, "HotEvent");
        var hotStore = new StubSerializableEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When
        var result = await hybrid.ReadAllSerializableEventsAsync();

        // Then
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal("ColdEvent1", events[0].EventPayloadName);
        Assert.Equal("ColdEvent2", events[1].EventPayloadName);
        Assert.Equal("HotEvent", events[2].EventPayloadName);
    }

    [Fact]
    public async Task Should_respect_maxCount()
    {
        // Given: 2 cold events + 1 hot event
        var coldTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var coldEvent1 = CreateEvent(coldTime, "Cold1");
        var coldEvent2 = CreateEvent(coldTime.AddMinutes(1), "Cold2");
        var latestSafe = coldEvent2.SortableUniqueIdValue;

        await StoreColdManifestAndSegmentsAsync([coldEvent1, coldEvent2], latestSafe);

        var hotEvent = CreateEvent(coldTime.AddMinutes(5), "Hot1");
        var hotStore = new StubSerializableEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When
        var result = await hybrid.ReadAllSerializableEventsAsync(since: null, maxCount: 2);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.GetValue().Count());
    }

    [Fact]
    public async Task Should_fall_back_to_hot_when_cold_segment_missing()
    {
        // Given: manifest exists but segment file is missing
        var manifest = new ColdManifest(
            ServiceId: TestServiceId,
            ManifestVersion: "v1",
            LatestSafeSortableUniqueId: "063923136000000000000000000000",
            Segments:
            [
                new ColdSegmentInfo(
                    Path: "segments/default/missing.jsonl",
                    FromSortableUniqueId: "063923136000000000000000000000",
                    ToSortableUniqueId: "063923136000000000000000000000",
                    EventCount: 1,
                    SizeBytes: 100,
                    Sha256: "abc",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        var manifestData = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await _coldStorage.PutAsync($"control/{TestServiceId}/manifest.json", manifestData, null, CancellationToken.None);

        var hotEvent = CreateEvent(DateTime.UtcNow.AddMinutes(-5), "HotFallback");
        var hotStore = new StubSerializableEventStore([hotEvent]);
        var hybrid = CreateHybrid(hotStore);

        // When
        var result = await hybrid.ReadAllSerializableEventsAsync();

        // Then: should fall back to hot store
        Assert.True(result.IsSuccess);
        Assert.Single(result.GetValue());
        Assert.Equal("HotFallback", result.GetValue().First().EventPayloadName);
    }

    [Fact]
    public async Task Should_not_drop_distinct_events_with_same_sortable_unique_id()
    {
        // Given: cold and hot have same sortable unique id but different event ids/payloads
        var sameSortableId = SortableUniqueId.Generate(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc), Guid.Empty);
        var coldEvent = CreateEvent(sameSortableId, "ColdEvent");
        var hotEvent = CreateEvent(sameSortableId, "HotEvent");

        await StoreColdManifestAndSegmentsAsync([coldEvent], sameSortableId);
        var hotStore = new StubSerializableEventStore([hotEvent], ignoreSinceFilter: true);
        var hybrid = CreateHybrid(hotStore);

        // When
        var result = await hybrid.ReadAllSerializableEventsAsync(since: null, maxCount: null);

        // Then
        Assert.True(result.IsSuccess);
        var events = result.GetValue().ToList();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.EventPayloadName == "ColdEvent");
        Assert.Contains(events, e => e.EventPayloadName == "HotEvent");
    }

    private sealed class StubSerializableEventStore : IEventStore
    {
        private readonly IReadOnlyList<SerializableEvent> _events;
        private readonly bool _ignoreSinceFilter;

        public StubSerializableEventStore(
            IReadOnlyList<SerializableEvent> events,
            bool ignoreSinceFilter = false)
        {
            _events = events;
            _ignoreSinceFilter = ignoreSinceFilter;
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
            => throw new NotSupportedException();

        public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
            => throw new NotSupportedException();

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
            => throw new NotSupportedException();

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(IEnumerable<Event> events)
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

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
        {
            IEnumerable<SerializableEvent> filtered = _ignoreSinceFilter || since is null
                ? _events
                : _events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            return Task.FromResult(ResultBox.FromValue(filtered));
        }

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount)
        {
            IEnumerable<SerializableEvent> filtered = _ignoreSinceFilter || since is null
                ? _events
                : _events.Where(e => string.Compare(e.SortableUniqueIdValue, since.Value, StringComparison.Ordinal) > 0);
            if (maxCount.HasValue)
            {
                filtered = filtered.Take(maxCount.Value);
            }
            return Task.FromResult(ResultBox.FromValue(filtered));
        }
    }
}
