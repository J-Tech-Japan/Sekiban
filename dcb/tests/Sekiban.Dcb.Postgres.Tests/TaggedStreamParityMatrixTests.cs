using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     Provider-real parity matrix for SEK-G53. It drives the same GeneralTagStateActor through streaming cold rebuild,
///     legacy list cold rebuild, and cached plus incremental streaming for InMemory, SQLite, and the fixture's real
///     Postgres container, comparing serialized payload bytes as well as version and last sortable id.
/// </summary>
public sealed class TaggedStreamParityMatrixTests : PostgresTestBase
{
    private static readonly DateTime BaseTime = new(2026, 2, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string P1 = SortableUniqueId.Generate(BaseTime, Guid.Empty);
    private static readonly string P2 = SortableUniqueId.Generate(BaseTime.AddSeconds(1), Guid.Empty);
    private static readonly JsonSerializerOptions PayloadJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TaggedStreamParityMatrixTests(PostgresTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task InMemorySqliteAndRealPostgres_ThreeRoutes_KeepPayloadVersionAndLastSuidInParity()
    {
        var domain = BuildDomainTypes();
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"g53-parity-{Guid.NewGuid():N}.db");
        try
        {
            var inMemory = await RunProviderMatrixAsync(
                "in-memory",
                new CoreInMemoryEventStore(domain.EventTypes),
                domain);
            var sqlite = await RunProviderMatrixAsync(
                "sqlite",
                new SqliteEventStore(sqlitePath, domain.EventTypes),
                domain);
            var postgres = await RunProviderMatrixAsync("postgres", Fixture.EventStore, domain);

            AssertEquivalent(inMemory.StreamingCold, sqlite.StreamingCold, "InMemory / SQLite streaming cold");
            AssertEquivalent(inMemory.StreamingCold, postgres.StreamingCold, "InMemory / Postgres streaming cold");
        }
        finally
        {
            if (File.Exists(sqlitePath))
            {
                File.Delete(sqlitePath);
            }
        }
    }

    private static async Task<ProviderMatrixResult> RunProviderMatrixAsync(
        string providerName,
        IEventStore provider,
        DcbDomainTypes domain)
    {
        var tag = new ParityTag(providerName);
        var write = await provider.WriteSerializableEventsAsync(new[]
        {
            ToSerializableEvent(domain, tag, P1, 3),
            ToSerializableEvent(domain, tag, P2, 4)
        });
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());

        var streamingColdStore = new StreamingCountingStore(provider, providerName);
        var streamingCold = await ReadColdAsync(streamingColdStore, domain, tag, P2);
        Assert.Equal(1, streamingColdStore.StreamCalls);
        Assert.Equal(0, streamingColdStore.ListCalls);
        AssertExpected(streamingCold);

        var listColdStore = new ListCountingStore(provider);
        var listCold = await ReadColdAsync(listColdStore, domain, tag, P2);
        Assert.Equal(0, listColdStore.StreamCalls);
        Assert.Equal(1, listColdStore.ListCalls);
        AssertExpected(listCold);

        var cachedIncrementalStore = new StreamingCountingStore(provider, providerName);
        var cachedIncremental = await ReadCachedIncrementalAsync(cachedIncrementalStore, domain, tag);
        Assert.Equal(2, cachedIncrementalStore.StreamCalls);
        Assert.Equal(0, cachedIncrementalStore.ListCalls);
        AssertExpected(cachedIncremental);

        // The routes must agree before this provider's result joins the three-provider equality check. This makes a
        // skipped callback, version increment, last-id update, or stream-to-list fallback mutation fail locally.
        AssertEquivalent(streamingCold, listCold, $"{providerName} streaming / list cold");
        AssertEquivalent(streamingCold, cachedIncremental, $"{providerName} streaming / cached incremental");
        return new ProviderMatrixResult(streamingCold, listCold, cachedIncremental);
    }

    private static async Task<SerializableTagState> ReadColdAsync(
        IEventStore store,
        DcbDomainTypes domain,
        ParityTag tag,
        string head)
    {
        var accessor = new HeadActorAccessor(head);
        var actor = CreateActor(store, domain, tag, accessor, new InMemoryTagStatePersistent());
        return await actor.GetStateAsync();
    }

    private static async Task<SerializableTagState> ReadCachedIncrementalAsync(
        IEventStore store,
        DcbDomainTypes domain,
        ParityTag tag)
    {
        var accessor = new HeadActorAccessor(P1);
        var actor = CreateActor(store, domain, tag, accessor, new InMemoryTagStatePersistent());
        var initial = await actor.GetStateAsync();
        Assert.Equal(1, initial.Version);
        Assert.Equal(P1, initial.LastSortedUniqueId);

        accessor.Head = P2;
        var incremental = await actor.GetStateAsync();
        var cacheHit = await actor.GetStateAsync();
        Assert.Equal(incremental.Payload, cacheHit.Payload);
        Assert.Equal(incremental.Version, cacheHit.Version);
        Assert.Equal(incremental.LastSortedUniqueId, cacheHit.LastSortedUniqueId);
        return incremental;
    }

    private static GeneralTagStateActor CreateActor(
        IEventStore store,
        DcbDomainTypes domain,
        ParityTag tag,
        IActorObjectAccessor accessor,
        ITagStatePersistent persistent) =>
        new(
            $"{tag.GetTag()}:{ParityProjector.ProjectorName}",
            store,
            domain.EventTypes,
            domain.TagProjectorTypes,
            domain.TagTypes,
            domain.TagStatePayloadTypes,
            new TagStateOptions(),
            accessor,
            persistent);

    private static SerializableEvent ToSerializableEvent(DcbDomainTypes domain, ParityTag tag, string sortableId, int delta) =>
        new Event(
                new ParityAdded(delta),
                sortableId,
                nameof(ParityAdded),
                Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "parity"),
                new List<string> { tag.GetTag() })
            .ToSerializableEvent(domain.EventTypes);

    private static DcbDomainTypes BuildDomainTypes()
    {
        var events = new SimpleEventTypes();
        events.RegisterEventType<ParityAdded>();
        var tagProjectors = new SimpleTagProjectorTypes();
        tagProjectors.RegisterProjector<ParityProjector>();
        var payloads = new SimpleTagStatePayloadTypes();
        payloads.RegisterPayloadType<ParityState>();
        return new DcbDomainTypes(
            events,
            new SimpleTagTypes(),
            tagProjectors,
            payloads,
            new SimpleMultiProjectorTypes(),
            new SimpleQueryTypes(),
            new JsonSerializerOptions());
    }

    private static void AssertExpected(SerializableTagState state)
    {
        Assert.Equal(2, state.Version);
        Assert.Equal(P2, state.LastSortedUniqueId);
        var payload = JsonSerializer.Deserialize<ParityState>(state.Payload, PayloadJsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(7, payload.Total);
    }

    private static void AssertEquivalent(SerializableTagState expected, SerializableTagState actual, string route)
    {
        Assert.Equal(expected.Payload, actual.Payload);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.LastSortedUniqueId, actual.LastSortedUniqueId);
    }

    private sealed record ProviderMatrixResult(
        SerializableTagState StreamingCold,
        SerializableTagState ListCold,
        SerializableTagState CachedIncremental);

    private sealed record ParityAdded(int Delta) : IEventPayload;

    private sealed record ParityState(int Total) : ITagStatePayload;

    private sealed class ParityProjector : ITagProjector<ParityProjector>
    {
        public static string ProjectorVersion => "1";
        public static string ProjectorName => nameof(ParityProjector);

        public static ITagStatePayload Project(ITagStatePayload current, Event @event)
        {
            var state = current as ParityState ?? new ParityState(0);
            return @event.Payload is ParityAdded added
                ? state with { Total = state.Total + added.Delta }
                : state;
        }
    }

    private sealed class ParityTag(string providerName) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "G53Parity";
        public string GetTagContent() => providerName;
        public string GetTag() => $"G53Parity:{providerName}";
    }

    private sealed class HeadActorAccessor(string head) : IActorObjectAccessor
    {
        private readonly HeadActor _actor = new();
        public string Head { get; set; } = head;

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            if (typeof(T) != typeof(ITagConsistentActorCommon))
            {
                return Task.FromResult(ResultBox.Error<T>(new NotSupportedException()));
            }

            _actor.Head = Head;
            return Task.FromResult(ResultBox.FromValue((T)(object)_actor));
        }

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(true);
    }

    private sealed class HeadActor : ITagConsistentActorCommon
    {
        public string Head { get; set; } = string.Empty;
        public Task<string> GetTagActorIdAsync() => Task.FromResult("G53Parity:head");
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => Task.FromResult(ResultBox.FromValue(Head));
        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId) =>
            Task.FromResult(ResultBox.FromValue(new TagWriteReservation("reservation", DateTime.UtcNow.ToString("O"), "G53Parity:head")));
        public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);
        public Task<bool> CancelReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);
        public Task NotifyEventWrittenAsync() => Task.CompletedTask;
    }

    private abstract class CountingStore(IEventStore inner) : IEventStore
    {
        protected IEventStore Inner { get; } = inner;
        public int StreamCalls { get; protected set; }
        public int ListCalls { get; private set; }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => Inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => Inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => Inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => Inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => Inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            Inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            Inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => Inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => Inner.WriteSerializableEventsAsync(events);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => Inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListCalls++;
            return Inner.ReadSerializableEventsByTagAsync(tag, since);
        }
    }

    private sealed class ListCountingStore(IEventStore inner) : CountingStore(inner);

    private sealed class StreamingCountingStore(IEventStore inner, string providerName) : CountingStore(inner),
        IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native($"{providerName} parity wrapper");

        public Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            return ((IStreamingTaggedSerializableEventStore)Inner).StreamSerializableEventsByTagAsync(
                tag,
                since,
                until,
                onEvent,
                cancellationToken);
        }
    }
}
