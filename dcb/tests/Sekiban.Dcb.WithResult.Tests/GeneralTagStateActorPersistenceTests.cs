using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;

namespace Sekiban.Dcb.Tests;

public class GeneralTagStateActorPersistenceTests
{
    [Fact]
    public async Task Should_Initialize_Empty_State()
    {
        var domainTypes = BuildDomainTypes();
        var eventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
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
        var domainTypes = BuildDomainTypes();
        var eventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
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
            },
            domainTypes.EventTypes);

        var initial = await actor.GetTagStateAsync();
        Assert.Equal(14, ((TestIncrementalState)initial.Payload).Total);

        await eventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new IncrementEvent { Increment = 3 }, tag, "003"),
                CreateEvent(new IncrementEvent { Increment = 5 }, tag, "004")
            },
            domainTypes.EventTypes);

        var updated = await actor.GetTagStateAsync();
        Assert.Equal(22, ((TestIncrementalState)updated.Payload).Total);
        Assert.Equal("004", updated.LastSortedUniqueId);
    }

    [Fact]
    public async Task Should_Restore_From_Persistence_When_Version_Is_Unchanged()
    {
        var domainTypes = BuildDomainTypes();
        var baseEventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:r1", "014");

        var persistent = new InMemoryTagStatePersistent();

        var tag = new TestTag("r1");
        await baseEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 6 }, tag, "012"),
                CreateEvent(new IncrementEvent { Increment = 8 }, tag, "014")
            },
            domainTypes.EventTypes);

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
            domainTypes.EventTypes,
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
        var domainTypesV1 = BuildDomainTypes();
        var domainTypesV2 = BuildDomainTypes(useVersionTwoSameName: true);
        var baseEventStore = new CoreInMemoryEventStore(domainTypesV1.EventTypes);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:r2", "009");

        var persistent = new InMemoryTagStatePersistent();
        var tag = new TestTag("r2");

        await baseEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 3 }, tag, "007"),
                CreateEvent(new IncrementEvent { Increment = 6 }, tag, "009")
            },
            domainTypesV1.EventTypes);

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
            domainTypesV2.EventTypes,
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
    public async Task ColdRebuild_UsesTheVerifiedTaggedStream_AndCacheHitPerformsNoRead()
    {
        var domainTypes = BuildDomainTypes();
        var baseEventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
        var eventStore = new StreamingCountingEventStore(baseEventStore);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:stream", "002");
        var tag = new TestTag("stream");

        await baseEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 10 }, tag, "001"),
                CreateEvent(new IncrementEvent { Increment = 2 }, tag, "002")
            },
            domainTypes.EventTypes);

        var actor = new GeneralTagStateActor(
            "TestTag:stream:TestIncrementalProjector",
            eventStore,
            domainTypes.EventTypes,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            new InMemoryTagStatePersistent());

        var rebuilt = await actor.GetTagStateAsync();
        Assert.Equal(12, ((TestIncrementalState)rebuilt.Payload).Total);
        Assert.Equal(1, eventStore.StreamCallCount);
        Assert.Equal(0, eventStore.ReadEventsByTagCallCount);

        var cached = await actor.GetTagStateAsync();
        Assert.Equal(rebuilt.Version, cached.Version);
        Assert.Equal(1, eventStore.StreamCallCount);
        Assert.Equal(0, eventStore.ReadEventsByTagCallCount);
    }

    [Fact]
    public async Task CancellationToken_StopsTheActorStreamBeforeCachePublication_AndTheNextCallRebuilds()
    {
        var domainTypes = BuildDomainTypes();
        var baseEventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
        var eventStore = new GatedStreamingEventStore(baseEventStore);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:cancel", "002");
        var persistent = new InMemorySerializableTagStatePersistent();
        var tag = new TestTag("cancel");

        await baseEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 10 }, tag, "001"),
                CreateEvent(new IncrementEvent { Increment = 2 }, tag, "002")
            },
            domainTypes.EventTypes);

        var actor = new GeneralTagStateActor(
            "TestTag:cancel:TestIncrementalProjector",
            eventStore,
            domainTypes.EventTypes,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        using var cancellation = new CancellationTokenSource();
        var cancelledRead = actor.GetStateAsync(cancellation.Token);
        try
        {
            await eventStore.FirstRowReached.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelledRead);
        }
        finally
        {
            eventStore.ReleaseFirstRow();
        }

        Assert.Equal(cancellation.Token, eventStore.ReceivedCancellationToken);
        Assert.Equal(1, eventStore.RowsRead);
        Assert.Equal(0, eventStore.ConsumerCallbacks);
        Assert.Equal(0, persistent.SaveSerializableStateCalls);
        Assert.Null(persistent.SavedState);

        var recovered = await actor.GetStateAsync();
        Assert.Equal(2, recovered.Version);
        Assert.Equal(12, Assert.IsType<TestIncrementalState>(
            domainTypes.TagStatePayloadTypes.DeserializePayload(recovered.ResolvedPayloadName, recovered.Payload).GetValue()).Total);
        Assert.Equal(1, persistent.SaveSerializableStateCalls);
    }

    [Fact]
    public async Task PreCancelledToken_DoesNotReachTheActorStreamingProvider()
    {
        var domainTypes = BuildDomainTypes();
        var baseEventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
        var eventStore = new GatedStreamingEventStore(baseEventStore);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:pre-cancel", "001");
        var actor = new GeneralTagStateActor(
            "TestTag:pre-cancel:TestIncrementalProjector",
            eventStore,
            domainTypes.EventTypes,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            new InMemorySerializableTagStatePersistent());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await actor.GetStateAsync(cancellation.Token));
        Assert.Equal(0, eventStore.StreamCallCount);
        Assert.Equal(0, eventStore.RowsRead);
    }

    [Fact]
    public async Task TokenlessNoneAndLiveToken_ProduceIdenticalTaggedStreamStateAndProviderCounts()
    {
        var domainTypes = BuildDomainTypes();
        var baseEventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
        var tag = new TestTag("cancellation-parity");
        await baseEventStore.WriteEventsAsync(
            [
                CreateEvent(new TestEvent { Value = 10 }, tag, "001"),
                CreateEvent(new IncrementEvent { Increment = 2 }, tag, "002")
            ],
            domainTypes.EventTypes);

        using var liveCancellation = new CancellationTokenSource();
        var results = new List<(SerializableTagState State, int StreamCalls, int ListCalls)>();
        foreach (var getState in new Func<GeneralTagStateActor, Task<SerializableTagState>>[]
                 {
                     actor => actor.GetStateAsync(),
                     actor => actor.GetStateAsync(CancellationToken.None),
                     actor => actor.GetStateAsync(liveCancellation.Token)
                 })
        {
            var eventStore = new StreamingCountingEventStore(baseEventStore);
            var actorAccessor = new TestActorAccessor();
            actorAccessor.SetLatestSortableUniqueId("TestTag:cancellation-parity", "002");
            var actor = new GeneralTagStateActor(
                "TestTag:cancellation-parity:TestIncrementalProjector",
                eventStore,
                domainTypes.EventTypes,
                domainTypes.TagProjectorTypes,
                domainTypes.TagTypes,
                domainTypes.TagStatePayloadTypes,
                new TagStateOptions(),
                actorAccessor,
                new InMemorySerializableTagStatePersistent());

            var state = await getState(actor);
            results.Add((state, eventStore.StreamCallCount, eventStore.ReadEventsByTagCallCount));
        }

        var baseline = results[0];
        Assert.All(results, result =>
        {
            Assert.Equal(baseline.State.Payload, result.State.Payload);
            Assert.Equal(baseline.State.Version, result.State.Version);
            Assert.Equal(baseline.State.LastSortedUniqueId, result.State.LastSortedUniqueId);
            Assert.Equal(1, result.StreamCalls);
            Assert.Equal(0, result.ListCalls);
        });
    }

    [Fact]
    public async Task CancellationDuringFinalSerialization_PreventsTheActorCacheWrite()
    {
        using var cancellation = new CancellationTokenSource();
        var serializingPayloadTypes = new CountingTagStatePayloadTypes(new SimpleTagStatePayloadTypes());
        serializingPayloadTypes.RegisterPayloadType<TestIncrementalState>();
        serializingPayloadTypes.OnSerializePayload = cancellation.Cancel;
        var domainTypes = BuildDomainTypes(tagStatePayloadTypes: serializingPayloadTypes);
        var eventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:cancel-before-save", "001");
        var persistent = new InMemorySerializableTagStatePersistent();
        var tag = new TestTag("cancel-before-save");

        await eventStore.WriteEventsAsync(
            [CreateEvent(new TestEvent { Value = 7 }, tag, "001")],
            domainTypes.EventTypes);

        var actor = new GeneralTagStateActor(
            "TestTag:cancel-before-save:TestIncrementalProjector",
            eventStore,
            domainTypes.EventTypes,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await actor.GetStateAsync(cancellation.Token));

        Assert.Equal(1, serializingPayloadTypes.SerializePayloadCount);
        Assert.Equal(0, persistent.SaveSerializableStateCalls);
        Assert.Null(persistent.SavedState);

        var recovered = await actor.GetStateAsync();
        Assert.Equal(1, recovered.Version);
        Assert.Equal(7, Assert.IsType<TestIncrementalState>(
            domainTypes.TagStatePayloadTypes.DeserializePayload(recovered.ResolvedPayloadName, recovered.Payload).GetValue()).Total);
        Assert.Equal(1, persistent.SaveSerializableStateCalls);
    }

    [Fact]
    public async Task OutOfOrderTaggedStream_FailsBeforeThePartialProjectionCanBeCached()
    {
        var domainTypes = BuildDomainTypes();
        var baseEventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
        var eventStore = new OutOfOrderStreamingStore(baseEventStore);
        var actorAccessor = new TestActorAccessor();
        actorAccessor.SetLatestSortableUniqueId("TestTag:out-of-order", "002");
        var tag = new TestTag("out-of-order");
        var persistent = new InMemoryTagStatePersistent();

        await baseEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new TestEvent { Value = 10 }, tag, "001"),
                CreateEvent(new IncrementEvent { Increment = 2 }, tag, "002")
            },
            domainTypes.EventTypes);

        var actor = new GeneralTagStateActor(
            "TestTag:out-of-order:TestIncrementalProjector",
            eventStore,
            domainTypes.EventTypes,
            domainTypes.TagProjectorTypes,
            domainTypes.TagTypes,
            domainTypes.TagStatePayloadTypes,
            new TagStateOptions(),
            actorAccessor,
            persistent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.GetTagStateAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.GetTagStateAsync());

        Assert.Equal(2, eventStore.StreamCallCount);
        Assert.Equal(0, eventStore.ReadEventsByTagCallCount);
    }

    [Fact]
    public async Task Should_Serialize_TagState_Only_When_Persisting_State()
    {
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<TestIncrementalState>();
        var payloadTypes = new CountingTagStatePayloadTypes(tagStatePayloadTypes);
        var domainTypes = BuildDomainTypes(tagStatePayloadTypes: payloadTypes);
        var eventStore = new CoreInMemoryEventStore(domainTypes.EventTypes);
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
            new[] { CreateEvent(new TestEvent { Value = 4 }, tag, "001") },
            domainTypes.EventTypes);

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
            payload.GetType().Name,
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
        protected IEventStore Inner => _inner;
        public int ReadEventsByTagCallCount { get; private set; }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            _inner.ReadAllSerializableEventsAsync(since);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
            => _inner.ReadAllSerializableEventsAsync(since, maxCount);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ReadEventsByTagCallCount++;
            return _inner.ReadSerializableEventsByTagAsync(tag, since);
        }

        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
            => _inner.ReadSerializableEventAsync(eventId);

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events)
            => _inner.WriteSerializableEventsAsync(events);
    }

    private sealed class StreamingCountingEventStore(IEventStore inner) : CountingEventStore(inner),
        IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        public int StreamCallCount { get; private set; }

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("test streaming wrapper");

        public Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCallCount++;
            return ((IStreamingTaggedSerializableEventStore)Inner).StreamSerializableEventsByTagAsync(
                tag,
                since,
                until,
                onEvent,
                cancellationToken);
        }
    }

    private sealed class GatedStreamingEventStore(IEventStore inner) : CountingEventStore(inner),
        IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        private readonly TaskCompletionSource _firstRowReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstRow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _streamCallCount;
        private int _rowsRead;
        private int _consumerCallbacks;

        public int StreamCallCount => _streamCallCount;
        public int RowsRead => _rowsRead;
        public int ConsumerCallbacks => _consumerCallbacks;
        public CancellationToken ReceivedCancellationToken { get; private set; }
        public Task FirstRowReached => _firstRowReached.Task;

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("cancellation gate");

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _streamCallCount);
            ReceivedCancellationToken = cancellationToken;
            return await ((IStreamingTaggedSerializableEventStore)Inner).StreamSerializableEventsByTagAsync(
                tag,
                since,
                until,
                async serializableEvent =>
                {
                    var row = Interlocked.Increment(ref _rowsRead);
                    if (row == 1)
                    {
                        _firstRowReached.TrySetResult();
                        await _releaseFirstRow.Task.WaitAsync(cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref _consumerCallbacks);
                    await onEvent(serializableEvent);
                },
                cancellationToken);
        }

        public void ReleaseFirstRow() => _releaseFirstRow.TrySetResult();
    }

    private sealed class OutOfOrderStreamingStore(IEventStore inner) : CountingEventStore(inner),
        IStreamingTaggedSerializableEventStore, ITaggedStreamCapabilityProvider
    {
        public int StreamCallCount { get; private set; }

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("out-of-order test double");

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCallCount++;
            try
            {
                var source = await Inner.ReadSerializableEventsByTagAsync(tag, since);
                if (!source.IsSuccess)
                {
                    return ResultBox.Error<SerializableEventStreamReadResult>(source.GetException());
                }

                var count = 0;
                string? lastSortableUniqueId = null;
                foreach (var serializableEvent in source.GetValue().OrderByDescending(e => e.SortableUniqueIdValue))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await onEvent(serializableEvent);
                    count++;
                    lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
                }

                return ResultBox.FromValue(new SerializableEventStreamReadResult(count, lastSortableUniqueId));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ResultBox.Error<SerializableEventStreamReadResult>(ex);
            }
        }
    }

    private class CountingTagStatePayloadTypes(ITagStatePayloadTypes inner) : ITagStatePayloadTypes
    {
        private readonly ITagStatePayloadTypes _inner = inner;
        public int SerializePayloadCount { get; private set; }
        public int DeserializePayloadCount { get; private set; }
        public Action? OnSerializePayload { get; set; }

        public Type? GetPayloadType(string payloadName) => _inner.GetPayloadType(payloadName);

        public ResultBox<string> GetPayloadName(ITagStatePayload payload) => _inner.GetPayloadName(payload);

        public ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] jsonBytes)
        {
            DeserializePayloadCount++;
            return _inner.DeserializePayload(payloadName, jsonBytes);
        }

        public ResultBox<byte[]> SerializePayload(ITagStatePayload payload)
        {
            SerializePayloadCount++;
            var serialized = _inner.SerializePayload(payload);
            OnSerializePayload?.Invoke();
            return serialized;
        }

        public void RegisterPayloadType<TPayload>() where TPayload : class, ITagStatePayload =>
            ((SimpleTagStatePayloadTypes)_inner).RegisterPayloadType<TPayload>();
    }

    private class InMemorySerializableTagStatePersistent : ITagStatePersistent, ISerializableTagStatePersistent
    {
        private SerializableTagState? _state;

        public int SaveSerializableStateCalls { get; private set; }
        public SerializableTagState? SavedState => _state;

        public Task<SerializableTagState?> LoadSerializableStateAsync() => Task.FromResult(_state);

        public Task SaveSerializableStateAsync(SerializableTagState state)
        {
            SaveSerializableStateCalls++;
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

        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId) =>
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
