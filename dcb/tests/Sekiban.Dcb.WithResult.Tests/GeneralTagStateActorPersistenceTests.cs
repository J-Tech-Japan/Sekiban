using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;

namespace Sekiban.Dcb.Tests;

public class GeneralTagStateActorPersistenceTests
{
    [Fact]
    public async Task Should_Initialize_Empty_State()
    {
        var eventStore = new InMemoryEventStore();
        var domainTypes = BuildDomainTypes();
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:empty", string.Empty);

        var actor = new GeneralTagStateActor(
            "TestTag:empty:TestIncrementalProjector",
            eventStore,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            new InMemoryTagStatePersistent());

        var state = await actor.GetTagStateAsync();

        Assert.IsType<EmptyTagStatePayload>(state.Payload);
        Assert.Equal(0, state.Version);
        Assert.Equal(string.Empty, state.LastSortedUniqueId);
    }

    [Fact]
    public async Task Should_Catchup_By_Reading_Only_New_Events_After_Cached_State()
    {
        var eventStore = new InMemoryEventStore();
        var domainTypes = BuildDomainTypes();
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:c1", "004");

        var actor = new GeneralTagStateActor(
            "TestTag:c1:TestIncrementalProjector",
            eventStore,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            new InMemoryTagStatePersistent());

        var tag = new TestTag("c1");
        await eventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 10 }, tag, "001"),
                CreateEvent(new IncrementEvent { Increment = 4 }, tag, "002")
            });

        var initial = await actor.GetTagStateAsync();
        Assert.Equal(14, ((TestIncrementalState)initial.Payload).Total);

        await eventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new IncrementEvent { Increment = 3 }, tag, "003"),
                CreateEvent(new IncrementEvent { Increment = 5 }, tag, "004")
            });

        var updated = await actor.GetTagStateAsync();
        Assert.Equal(22, ((TestIncrementalState)updated.Payload).Total);
        Assert.Equal("004", updated.LastSortedUniqueId);
    }

    [Fact]
    public async Task Should_Restore_From_Persistence_When_Version_Is_Unchanged()
    {
        var baseEventStore = new InMemoryEventStore();
        var domainTypes = BuildDomainTypes();
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:r1", "014");

        var persistent = new InMemoryTagStatePersistent();

        var tag = new TestTag("r1");
        await baseEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 6 }, tag, "012"),
                CreateEvent(new IncrementEvent { Increment = 8 }, tag, "014")
            });

        var firstActor = new GeneralTagStateActor(
            "TestTag:r1:TestIncrementalProjector",
            baseEventStore,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        var first = await firstActor.GetTagStateAsync();
        Assert.Equal(14, ((TestIncrementalState)first.Payload).Total);
        Assert.Equal("1.0", first.ProjectorVersion);

        var spyStore = new CountingEventStore(baseEventStore);
        var secondActor = new GeneralTagStateActor(
            "TestTag:r1:TestIncrementalProjector",
            spyStore,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        var second = await secondActor.GetTagStateAsync();
        Assert.Equal(14, ((TestIncrementalState)second.Payload).Total);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(0, spyStore.ReadEventsByTagCallCount);
    }

    [Fact]
    public async Task Should_Rebuild_When_Projector_Version_Changes_After_Restore()
    {
        var baseEventStore = new InMemoryEventStore();
        var domainTypesV1 = BuildDomainTypes();
        var domainTypesV2 = BuildDomainTypes(useVersionTwoSameName: true);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:r2", "009");

        var persistent = new InMemoryTagStatePersistent();
        var tag = new TestTag("r2");

        await baseEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 3 }, tag, "007"),
                CreateEvent(new IncrementEvent { Increment = 6 }, tag, "009")
            });

        var actorV1 = new GeneralTagStateActor(
            "TestTag:r2:TestIncrementalProjector",
            baseEventStore,
            domainTypesV1.TagProjectorTypes,
            domainTypesV1.TagTypes,
            domainTypesV1.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        var stateV1 = await actorV1.GetTagStateAsync();
        Assert.Equal("1.0", stateV1.ProjectorVersion);
        Assert.Equal(9, ((TestIncrementalState)stateV1.Payload).Total);

        var spyStore = new CountingEventStore(baseEventStore);
        var actorV2 = new GeneralTagStateActor(
            "TestTag:r2:TestIncrementalProjector",
            spyStore,
            domainTypesV2.TagProjectorTypes,
            domainTypesV2.TagTypes,
            domainTypesV2.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        var stateV2 = await actorV2.GetTagStateAsync();
        Assert.Equal("2.0", stateV2.ProjectorVersion);
        Assert.Equal(18, ((TestIncrementalState)stateV2.Payload).Total);
        Assert.Equal(1, spyStore.ReadEventsByTagCallCount);
    }

    [Fact]
    public async Task Should_Serialize_TagState_Only_When_Persisting_State()
    {
        var eventStore = new InMemoryEventStore();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<TestIncrementalState>();
        var payloadTypes = new CountingTagStatePayloadTypes(tagStatePayloadTypes);
        var domainTypes = BuildDomainTypes(tagStatePayloadTypes: payloadTypes);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:s1", "001");

        var persistent = new InMemorySerializableTagStatePersistent();

        var actor = new GeneralTagStateActor(
            "TestTag:s1:TestIncrementalProjector",
            eventStore,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        var tag = new TestTag("s1");
        await eventStore.WriteEventsAsync(
            new[] { CreateEvent(new TestEvent { Value = 4 }, tag, "001") });

        var first = await actor.GetTagStateAsync();
        Assert.NotNull(first);
        Assert.Equal(1, payloadTypes.SerializePayloadCount);

        var second = await actor.GetTagStateAsync();
        Assert.NotNull(second);
        Assert.Equal(1, payloadTypes.SerializePayloadCount);

        var serialized = await actor.GetStateAsync();
        Assert.Equal(1, payloadTypes.SerializePayloadCount);
        Assert.Equal(1, serialized.Version);
        Assert.NotEqual("", serialized.TagPayloadName);
    }

    private static Event CreateEvent(IEventPayload payload, ITag tag, string sortableId)
    {
        var eventId = Guid.NewGuid();
        return new Event(
            payload,
            new SortableUniqueId(sortableId),
            "TestAggregate",
            eventId,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            new List<string> { tag.GetTag() });
    }

    private static DcbDomainTypes BuildDomainTypes(
        bool useVersionTwoSameName = false,
        ITagStatePayloadTypes? tagStatePayloadTypes = null)
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestEvent>();
        eventTypes.RegisterEventType<IncrementEvent>();

        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();

        if (useVersionTwoSameName)
        {
            tagProjectorTypes.RegisterProjector<TestIncrementalProjectorVersionTwoSameName>();
        }
        else
        {
            tagProjectorTypes.RegisterProjector<TestIncrementalProjector>();
        }

        var payloadTypes = tagStatePayloadTypes ?? new SimpleTagStatePayloadTypes();
        if (payloadTypes.GetPayloadType(nameof(TestIncrementalState)) is null)
        {
            if (payloadTypes is SimpleTagStatePayloadTypes simplePayloadTypes)
            {
                simplePayloadTypes.RegisterPayloadType<TestIncrementalState>();
            }
            else
            {
                throw new InvalidOperationException(
                    "TagStatePayloadTypes must register TestIncrementalState before passing in.");
            }
        }

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        var queryTypes = new SimpleQueryTypes();

        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            payloadTypes,
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions());
    }

    private record TestEvent : IEventPayload
    {
        public int Value { get; init; }
    }

    private record IncrementEvent : IEventPayload
    {
        public int Increment { get; init; }
    }

    private record TestTag : ITag
    {
        private readonly string _id;
        public TestTag(string id) => _id = id;
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "TestTag";
        public string GetTagContent() => _id;
        public string GetTag() => $"TestTag:{_id}";
    }

    private class TestIncrementalProjector : ITagProjector<TestIncrementalProjector>
    {
        public static string ProjectorVersion => "1.0";
        public static string ProjectorName => "TestIncrementalProjector";

        public static ITagStatePayload Project(ITagStatePayload current, Event ev)
        {
            var state = current as TestIncrementalState ?? new TestIncrementalState();
            return ev.Payload switch
            {
                TestEvent test => state with { Total = state.Total + test.Value },
                IncrementEvent inc => state with { Total = state.Total + inc.Increment },
                _ => state
            };
        }
    }

    private class TestIncrementalProjectorVersionTwoSameName : ITagProjector<TestIncrementalProjectorVersionTwoSameName>
    {
        public static string ProjectorVersion => "2.0";
        public static string ProjectorName => "TestIncrementalProjector";

        public static ITagStatePayload Project(ITagStatePayload current, Event ev)
        {
            var state = current as TestIncrementalState ?? new TestIncrementalState();
            return ev.Payload switch
            {
                TestEvent test => state with { Total = state.Total + test.Value * 2 },
                IncrementEvent inc => state with { Total = state.Total + inc.Increment * 2 },
                _ => state
            };
        }
    }

    private record TestIncrementalState : ITagStatePayload
    {
        public int Total { get; init; }
    }

    private class CountingEventStore(IEventStore inner) : IEventStore
    {
        private readonly IEventStore _inner = inner;
        public int ReadEventsByTagCallCount { get; private set; }

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null) =>
            _inner.ReadAllEventsAsync(since);

        public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
        {
            ReadEventsByTagCallCount++;
            return _inner.ReadEventsByTagAsync(tag, since);
        }

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId) => _inner.ReadEventAsync(eventId);

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
            IEnumerable<Event> events)
            => _inner.WriteEventsAsync(events);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            _inner.ReadAllSerializableEventsAsync(since);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
            => _inner.ReadSerializableEventsByTagAsync(tag, since);

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events)
            => _inner.WriteSerializableEventsAsync(events);
    }

    private class CountingTagStatePayloadTypes(ITagStatePayloadTypes inner) : ITagStatePayloadTypes
    {
        private readonly ITagStatePayloadTypes _inner = inner;
        public int SerializePayloadCount { get; private set; }
        public int DeserializePayloadCount { get; private set; }

        public Type? GetPayloadType(string payloadName) => _inner.GetPayloadType(payloadName);

        public ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] jsonBytes)
        {
            DeserializePayloadCount++;
            return _inner.DeserializePayload(payloadName, jsonBytes);
        }

        public ResultBox<byte[]> SerializePayload(ITagStatePayload payload)
        {
            SerializePayloadCount++;
            return _inner.SerializePayload(payload);
        }

        public void RegisterPayloadType<TPayload>() where TPayload : class, ITagStatePayload =>
            ((SimpleTagStatePayloadTypes)_inner).RegisterPayloadType<TPayload>();
    }

    private class InMemorySerializableTagStatePersistent : ITagStatePersistent, ISerializableTagStatePersistent
    {
        private SerializableTagState? _state;

        public Task<SerializableTagState?> LoadSerializableStateAsync() => Task.FromResult(_state);

        public Task SaveSerializableStateAsync(SerializableTagState state)
        {
            _state = state;
            return Task.CompletedTask;
        }

        public Task<TagState?> LoadStateAsync() => Task.FromResult<TagState?>(null);

        public Task SaveStateAsync(TagState state) => throw new NotSupportedException();

        public Task ClearStateAsync()
        {
            _state = null;
            return Task.CompletedTask;
        }
    }

    private class TestActorAccessor : IActorObjectAccessor
    {
        private readonly Dictionary<string, ITagConsistentActorCommon> _actors = new();
        private readonly Dictionary<string, string> _latestSortable = new();

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            if (typeof(T) != typeof(ITagConsistentActorCommon))
            {
                return Task.FromResult(ResultBox.Error<T>(new NotSupportedException()));
            }

            if (!_actors.TryGetValue(actorId, out var actor))
            {
                var mockActor = new MockTagConsistentActor();
                mockActor.SetActorId(actorId);
                mockActor.SetLastSortableUniqueId(_latestSortable.GetValueOrDefault(actorId, string.Empty));
                _actors[actorId] = mockActor;
                actor = mockActor;
            }

            return Task.FromResult(ResultBox.FromValue((T)actor));
        }

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(true);

        public void SetLatestSortableUniqueId(string actorId, string sortableUniqueId) =>
            _latestSortable[actorId] = sortableUniqueId;
    }

    private class MockTagConsistentActor : ITagConsistentActorCommon
    {
        private string _actorId = "";
        private string _lastSortableUniqueId = "";

        public Task<string> GetTagActorIdAsync() => Task.FromResult(_actorId);

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            Task.FromResult(ResultBox.FromValue(_lastSortableUniqueId));

        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId) =>
            Task.FromResult(
                ResultBox.FromValue(
                    new TagWriteReservation(
                        Guid.NewGuid().ToString(),
                        DateTime.UtcNow.AddMinutes(1).ToString("O"),
                        _actorId)));

        public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);

        public Task<bool> CancelReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);

        public Task NotifyEventWrittenAsync() => Task.CompletedTask;

        public void SetLastSortableUniqueId(string sortableUniqueId) => _lastSortableUniqueId = sortableUniqueId;

        public void SetActorId(string actorId) => _actorId = actorId;
    }
}
