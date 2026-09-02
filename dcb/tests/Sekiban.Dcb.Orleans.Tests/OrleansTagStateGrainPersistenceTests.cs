using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

public class OrleansTagStateGrainPersistenceTests : IAsyncLifetime
{
    // Shared in-memory store per test run to keep state deterministic and inspectable
    private static readonly CountingEventStore SharedEventStore = new();
    private static readonly TagStateGrainRequestCounter SharedGrainRequestCounter = new();

    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"TestCluster-Counter-{uniqueId}";
        builder.Options.ServiceId = $"TestService-Counter-{uniqueId}";
        var portBase = 20_000 + (Environment.ProcessId % 5_000) * 4;
        builder.PortAllocator = new FixedPortAllocator(portBase, portBase + 100);
        builder.Options.BaseSiloPort = portBase;
        builder.Options.BaseGatewayPort = portBase + 1;
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();

        SharedEventStore.Clear();
        SharedEventStore.ClearCounts();
        SharedGrainRequestCounter.Clear();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Should_Initialize_Empty_State()
    {
        var tagStateId = BuildTagStateId(Guid.NewGuid());
        var grain = GetTagStateGrain(tagStateId);

        var state = await grain.GetStateAsync();

        Assert.Equal(nameof(EmptyTagStatePayload), state.TagPayloadName);
        Assert.Equal(0, state.Version);
        Assert.Equal(tagStateId.TagGroup, state.TagGroup);
        Assert.Equal(tagStateId.TagContent, state.TagContent);
        Assert.Equal(tagStateId.TagProjectorName, state.TagProjector);
    }

    [Fact]
    public async Task Should_Catchup_Only_New_Events_Incrementally()
    {
        var aggregateId = Guid.NewGuid();
        var tagStateId = BuildTagStateId(aggregateId);
        var tag = BuildTag(aggregateId);
        var grain = GetTagStateGrain(tagStateId);

        await SharedEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new CounterIncremented(1), tag, "001"),
                CreateEvent(new CounterIncremented(2), tag, "002"),
                CreateEvent(new CounterIncremented(3), tag, "003"),
                CreateEvent(new CounterIncremented(4), tag, "004")
            });

        var state = await WaitForStateAsync(grain, 4);
        Assert.Equal(4, state.Version);
        Assert.Equal("CounterState", state.TagPayloadName);
    }

    [Fact]
    public async Task Should_Reuse_Cached_State_When_No_New_Events()
    {
        var aggregateId = Guid.NewGuid();
        var tagStateId = BuildTagStateId(aggregateId);
        var tag = BuildTag(aggregateId);
        var grain = GetTagStateGrain(tagStateId);

        await SharedEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new CounterIncremented(5), tag, "001")
            });

        var first = await grain.GetStateAsync();
        Assert.Equal(1, first.Version);
        Assert.Equal(nameof(CounterState), first.TagPayloadName);

        SharedEventStore.ClearCounts();
        var second = await grain.GetStateAsync();

        Assert.Equal(first.TagPayloadName, second.TagPayloadName);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.LastSortedUniqueId, second.LastSortedUniqueId);
        Assert.Equal(first.TagProjector, second.TagProjector);
        Assert.Equal(first.ProjectorVersion, second.ProjectorVersion);
        Assert.Equal(first.Payload, second.Payload);
        Assert.Equal(0, SharedEventStore.ReadSerializableEventsByTagCallCount);
        Assert.Equal(0, SharedEventStore.StreamSerializableEventsByTagCallCount);
        Assert.Equal(1, second.Version);
    }

    [Fact]
    public async Task Should_Rebuild_When_Cached_ProjectorVersion_Is_Stale()
    {
        var aggregateId = Guid.NewGuid();
        var tagStateId = BuildTagStateId(aggregateId);
        var tag = BuildTag(aggregateId);
        var grain = GetTagStateGrain(tagStateId);

        await SharedEventStore.WriteEventsAsync(
            new[] { CreateEvent(new CounterIncremented(7), tag, "001") });

        var original = await grain.GetStateAsync();
        var staleState = new TagState(
            new CounterState(0),
            original.Version,
            original.LastSortedUniqueId,
            original.TagGroup,
            original.TagContent,
            original.TagProjector,
            "0.9");

        await grain.UpdateStateAsync(staleState);

        SharedEventStore.ClearCounts();
        var rebuilt = await grain.GetStateAsync();

        Assert.NotSame(original, rebuilt);
        Assert.Equal("1.0", rebuilt.ProjectorVersion);
        Assert.Equal(1, rebuilt.Version);
        Assert.Equal(0, SharedEventStore.ReadSerializableEventsByTagCallCount);
        Assert.Equal(1, SharedEventStore.StreamSerializableEventsByTagCallCount);
    }

    [Fact]
    public async Task Should_Restore_From_Persistence_When_Version_Is_Unchanged()
    {
        var aggregateId = Guid.NewGuid();
        var tagStateId = BuildTagStateId(aggregateId);
        var tag = BuildTag(aggregateId);
        var grain = GetTagStateGrain(tagStateId);

        await SharedEventStore.WriteEventsAsync(
            new[] { CreateEvent(new CounterIncremented(9), tag, "001") });

        var first = await grain.GetStateAsync();
        Assert.Equal(1, first.Version);

        SharedEventStore.ClearCounts();
        var second = await grain.GetStateAsync();

        Assert.Equal(1, second.Version);
        Assert.Equal("CounterState", second.TagPayloadName);
        Assert.Equal(0, SharedEventStore.ReadSerializableEventsByTagCallCount);
        Assert.Equal(0, SharedEventStore.StreamSerializableEventsByTagCallCount);
    }

    [Fact]
    public async Task CancellationTokenProbe_PreCancelledToken_DoesNotDispatchOrRead()
    {
        var grain = GetTagStateGrain(BuildTagStateId(Guid.NewGuid()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await grain.GetStateAsync(cancellation.Token));

        Assert.Equal(0, SharedGrainRequestCounter.GetStateRequestCount);
        Assert.Equal(0, SharedEventStore.ReadSerializableEventsByTagCallCount);
        Assert.Equal(0, SharedEventStore.StreamSerializableEventsByTagCallCount);
        Assert.Equal(0, SharedEventStore.StreamRowsRead);
        Assert.Equal(0, SharedEventStore.StreamCallbackCount);
    }

    [Fact]
    public async Task CancellationTokenProbe_PostDispatchCancellationAcknowledgedByGrainTokenStopsAfterFirstRow()
    {
        var aggregateId = Guid.NewGuid();
        var tagStateId = BuildTagStateId(aggregateId);
        var tag = BuildTag(aggregateId);
        var grain = GetTagStateGrain(tagStateId);

        await SharedEventStore.WriteEventsAsync(
            new[]
            {
                CreateEvent(new CounterIncremented(1), tag, "001"),
                CreateEvent(new CounterIncremented(2), tag, "002")
            });

        // Prime the tag-consistent actor, then clear the cache so the measured request must stream both rows.
        await WaitForStateAsync(grain, 2, TimeSpan.FromSeconds(10));
        await grain.ClearCacheAsync();
        SharedEventStore.ClearCounts();
        SharedGrainRequestCounter.Clear();
        var firstRowGate = SharedEventStore.GateFirstStreamRow();

        using var cancellation = new CancellationTokenSource();
        var stateTask = grain.GetStateAsync(cancellation.Token);
        try
        {
            await firstRowGate.FirstRowReached.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, SharedGrainRequestCounter.GetStateRequestCount);
            Assert.Equal(1, SharedEventStore.StreamSerializableEventsByTagCallCount);
            Assert.Equal(1, SharedEventStore.StreamRowsRead);

            cancellation.Cancel();
            var cancellationSignal = firstRowGate.GrainSideCancellationObserved;
            if (await Task.WhenAny(cancellationSignal, Task.Delay(TimeSpan.FromSeconds(10))) != cancellationSignal)
            {
                throw new TimeoutException(
                    $"Grain-side cancellation was not observed after caller cancellation. " +
                    $"incomingGetState={SharedGrainRequestCounter.GetStateRequestCount}, " +
                    $"streamCalls={SharedEventStore.StreamSerializableEventsByTagCallCount}, " +
                    $"providerRows={SharedEventStore.StreamRowsRead}, " +
                    $"callbacks={SharedEventStore.StreamCallbackCount}, " +
                    $"cancelableGrainToken={firstRowGate.ReceivedCancelableGrainToken}.");
            }
        }
        finally
        {
            // The provider may advance only after the grain-side cancellation callback has run.
            firstRowGate.ReleaseFirstRow();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stateTask);
        Assert.Equal(1, SharedEventStore.StreamRowsRead);
        Assert.Equal(0, SharedEventStore.StreamCallbackCount);
        Assert.True(firstRowGate.ReceivedCancelableGrainToken);
    }

    private ITagStateGrain GetTagStateGrain(TagStateId id) =>
        _client.GetGrain<ITagStateGrain>(id.GetTagStateId());

    private static TagStateId BuildTagStateId(Guid id) => new TagStateId(BuildTag(id), "CounterProjector");

    private static CounterTag BuildTag(Guid id) => new(id);

    private static Event CreateEvent(CounterIncremented payload, CounterTag tag, string sortableId)
    {
        return new Event(
            payload,
            new SortableUniqueId(sortableId),
            payload.GetType().Name,
            Guid.CreateVersion7(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            new List<string> { tag.GetTag() });
    }

    private async Task<SerializableTagState> WaitForStateAsync(
        ITagStateGrain grain,
        int minVersion,
        TimeSpan? timeout = null)
    {
        var due = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        SerializableTagState? result = null;

        while (DateTime.UtcNow < due)
        {
            result = await grain.GetStateAsync();
            if (result.Version >= minVersion)
            {
                return result;
            }

            await Task.Delay(50);
        }

            return result ?? new SerializableTagState(
                Array.Empty<byte>(),
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                nameof(EmptyTagStatePayload),
                string.Empty);
    }

    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .Configure<SiloMessagingOptions>(options => options.WaitForCancellationAcknowledgement = true)
                .AddIncomingGrainCallFilter(SharedGrainRequestCounter)
                .ConfigureServices(services =>
                {
                    services.AddSingleton<DcbDomainTypes>(provider =>
                    {
                        var domainTypes = new DcbDomainTypes(
                            eventTypes: BuildEventTypes(),
                            tagTypes: BuildTagTypes(),
                            tagProjectorTypes: BuildTagProjectorTypes(),
                            tagStatePayloadTypes: BuildTagStatePayloadTypes(),
                            multiProjectorTypes: BuildMultiProjectorTypes(),
                            queryTypes: new SimpleQueryTypes(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        return domainTypes;
                    });

                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore, Sekiban.Dcb.Testing.InMemoryMultiProjectionStateStore>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
                    services.AddTransient<Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions>(_ => new Sekiban.Dcb.Actors.GeneralMultiProjectionActorOptions
                    {
                        SafeWindowMs = 20000
                    });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }

        private static IEventTypes BuildEventTypes()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<CounterIncremented>();
            return eventTypes;
        }

    private static ITagTypes BuildTagTypes()
    {
        var tagTypes = new SimpleTagTypes();
        return tagTypes;
    }

        private static ITagProjectorTypes BuildTagProjectorTypes()
        {
            var projectorTypes = new SimpleTagProjectorTypes();
            projectorTypes.RegisterProjector<CounterProjector>();
            return projectorTypes;
        }

        private static ITagStatePayloadTypes BuildTagStatePayloadTypes()
        {
            var payloadTypes = new SimpleTagStatePayloadTypes();
            payloadTypes.RegisterPayloadType<CounterState>();
            return payloadTypes;
        }

        private static ICoreMultiProjectorTypes BuildMultiProjectorTypes()
        {
            var multiProjectorTypes = new SimpleMultiProjectorTypes();
            return multiProjectorTypes;
        }
        }

    private sealed class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.Configure<ClientMessagingOptions>(
                options => options.WaitForCancellationAcknowledgement = true);
        }
    }

    private sealed class FixedPortAllocator(int baseSiloPort, int baseGatewayPort) : ITestClusterPortAllocator
    {
        public (int, int) AllocateConsecutivePortPairs(int numPorts) => (baseSiloPort, baseGatewayPort);

        public void Dispose() { }
    }

    private sealed class TagStateGrainRequestCounter : IIncomingGrainCallFilter
    {
        private int _getStateRequestCount;

        public int GetStateRequestCount => Volatile.Read(ref _getStateRequestCount);

        public void Clear() => Interlocked.Exchange(ref _getStateRequestCount, 0);

        public Task Invoke(IIncomingGrainCallContext context)
        {
            if (context.Grain is TagStateGrain &&
                context.InterfaceMethod.Name == nameof(ITagStateGrain.GetStateAsync))
            {
                Interlocked.Increment(ref _getStateRequestCount);
            }

            return context.Invoke();
        }
    }

    public record CounterIncremented(int Delta) : IEventPayload;

    [GenerateSerializer]
    public record CounterState([property: Id(0)] int Value) : ITagStatePayload;

    public record CounterTag : ITag
    {
        public CounterTag(Guid id) => Id = id;

        public Guid Id { get; }

        public bool IsConsistencyTag() => false;

        public string GetTagGroup() => "Counter";

        public string GetTagContent() => Id.ToString();

        public string GetTag() => $"Counter:{Id}";
    }

    public class CounterProjector : ITagProjector<CounterProjector>
    {
        public static string ProjectorVersion => "1.0";

        public static string ProjectorName => "CounterProjector";

        public static ITagStatePayload Project(ITagStatePayload current, Event ev)
        {
            var currentState = current as CounterState ?? new CounterState(0);

            return ev.Payload is CounterIncremented incremented
                ? new CounterState(currentState.Value + incremented.Delta)
                : currentState;
        }
    }

    private class CountingEventStore : IEventStore, IStreamingTaggedSerializableEventStore,
        ITaggedStreamCapabilityProvider
    {
        private readonly InMemoryEventStore _inner = CreateInnerStore();
        private FirstRowGate? _firstRowGate;
        private int _streamRowsRead;
        private int _streamCallbackCount;

        public void Clear() => _inner.Clear();
        public int ReadSerializableEventsByTagCallCount { get; private set; }
        public int StreamSerializableEventsByTagCallCount { get; private set; }
        public int StreamRowsRead => Volatile.Read(ref _streamRowsRead);
        public int StreamCallbackCount => Volatile.Read(ref _streamCallbackCount);

        public void ClearCounts()
        {
            ReadSerializableEventsByTagCallCount = 0;
            StreamSerializableEventsByTagCallCount = 0;
            Interlocked.Exchange(ref _streamRowsRead, 0);
            Interlocked.Exchange(ref _streamCallbackCount, 0);
            _firstRowGate = null;
        }

        public FirstRowGate GateFirstStreamRow()
        {
            var gate = new FirstRowGate();
            _firstRowGate = gate;
            return gate;
        }

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("Orleans test InMemory");

        private static InMemoryEventStore CreateInnerStore()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<CounterIncremented>();
            return new InMemoryEventStore(eventTypes);
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null) =>
            _inner.ReadAllEventsAsync(since, maxCount);

        public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
        {
            return await _inner.ReadEventsByTagAsync(tag, since);
        }

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId) => _inner.ReadEventAsync(eventId);

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(IEnumerable<Event> events)
            => _inner.WriteEventsAsync(events);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
            => _inner.ReadAllSerializableEventsAsync(since);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
            => _inner.ReadAllSerializableEventsAsync(since, maxCount);

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ReadSerializableEventsByTagCallCount++;
            return _inner.ReadSerializableEventsByTagAsync(tag, since);
        }

        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
            => _inner.ReadSerializableEventAsync(eventId);

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamSerializableEventsByTagCallCount++;
            var firstRowGate = _firstRowGate;
            using var grainCancellationRegistration = firstRowGate is null
                ? default(CancellationTokenRegistration)
                : cancellationToken.Register(static state => ((FirstRowGate)state!).ObserveGrainSideCancellation(), firstRowGate);

            return await ((IStreamingTaggedSerializableEventStore)_inner).StreamSerializableEventsByTagAsync(
                tag,
                since,
                until,
                async serializableEvent =>
                {
                    Interlocked.Increment(ref _streamRowsRead);
                    if (firstRowGate?.TryReachFirstRow(cancellationToken) == true)
                    {
                        await firstRowGate.WaitForReleaseAsync(cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref _streamCallbackCount);
                    await onEvent(serializableEvent);
                },
                cancellationToken);
        }

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events)
            => _inner.WriteSerializableEventsAsync(events);

        public sealed class FirstRowGate
        {
            private readonly TaskCompletionSource<bool> _firstRowReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _grainSideCancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _releaseFirstRow = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _firstRowReachedOnce;
            private int _receivedCancelableGrainToken;

            public Task FirstRowReached => _firstRowReached.Task;
            public Task GrainSideCancellationObserved => _grainSideCancellationObserved.Task;
            public bool ReceivedCancelableGrainToken => Volatile.Read(ref _receivedCancelableGrainToken) == 1;

            public bool TryReachFirstRow(CancellationToken grainSideCancellationToken)
            {
                if (Interlocked.CompareExchange(ref _firstRowReachedOnce, 1, 0) != 0)
                {
                    return false;
                }

                if (grainSideCancellationToken.CanBeCanceled)
                {
                    Interlocked.Exchange(ref _receivedCancelableGrainToken, 1);
                }

                _firstRowReached.TrySetResult(true);
                return true;
            }

            public Task WaitForReleaseAsync(CancellationToken cancellationToken) =>
                _releaseFirstRow.Task.WaitAsync(cancellationToken);

            public void ObserveGrainSideCancellation() => _grainSideCancellationObserved.TrySetResult(true);

            public void ReleaseFirstRow() => _releaseFirstRow.TrySetResult(true);
        }
    }
}
