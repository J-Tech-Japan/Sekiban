using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using ResultBoxes;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class OrleansTagStatePersistentTests
{
    private readonly ITagStatePayloadTypes _tagStatePayloadTypes;

    public OrleansTagStatePersistentTests()
    {
        var simpleTypes = new SimpleTagStatePayloadTypes(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        // Register test payload types
        simpleTypes.RegisterPayloadType<TestStatePayload>();
        _tagStatePayloadTypes = simpleTypes;
    }

    [Fact]
    public async Task SaveAndLoadState_WithSerializableTagState_ShouldSucceed()
    {
        // Arrange
        var testPayload = new TestStatePayload
        {
            Id = Guid.NewGuid(),
            Name = "Test State",
            Value = 100
        };

        var originalState = new TagState(
            testPayload,
            1,
            "unique-id-123",
            "TestGroup",
            "TestContent",
            "TestProjector",
            "1.0.0");

        // Create in-memory cache state for testing
        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);

        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

        // Act - Save
        await persistent.SaveStateAsync(originalState);

        // Assert - Saved as SerializableTagState
        Assert.NotNull(persistentState.State.CachedState);
        var serializable = persistentState.State.CachedState;
        Assert.Equal(originalState.Version, serializable.Version);
        Assert.Equal(originalState.TagGroup, serializable.TagGroup);
        Assert.Equal(nameof(TestStatePayload), serializable.TagPayloadName);
        Assert.NotEmpty(serializable.Payload);

        // Act - Load
        var loadedState = await persistent.LoadStateAsync();

        // Assert - Loaded correctly
        Assert.NotNull(loadedState);
        Assert.Equal(originalState.Version, loadedState.Version);
        Assert.Equal(originalState.TagGroup, loadedState.TagGroup);
        Assert.Equal(originalState.TagContent, loadedState.TagContent);

        var loadedPayload = loadedState.Payload as TestStatePayload;
        Assert.NotNull(loadedPayload);
        Assert.Equal(testPayload.Id, loadedPayload.Id);
        Assert.Equal(testPayload.Name, loadedPayload.Name);
        Assert.Equal(testPayload.Value, loadedPayload.Value);
    }

    [Fact]
    public async Task LoadState_WithEmptyCache_ShouldReturnNull()
    {
        // Arrange
        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

        // Act
        var result = await persistent.LoadStateAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ClearState_ShouldRemoveCachedState()
    {
        // Arrange
        var testPayload = new TestStatePayload { Id = Guid.NewGuid(), Name = "Test" };
        var state = new TagState(testPayload, 1, "id", "group", "content", "projector", "1.0");

        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

        await persistent.SaveStateAsync(state);
        Assert.NotNull(persistentState.State.CachedState);

        // Act
        await persistent.ClearStateAsync();

        // Assert
        Assert.Null(persistentState.State.CachedState);
    }

    [Fact]
    public async Task SaveAndLoad_WithEmptyTagStatePayload_ShouldSucceed()
    {
        // Arrange
        var emptyPayload = new EmptyTagStatePayload();
        var state = new TagState(
            emptyPayload,
            0,
            string.Empty,
            "EmptyGroup",
            "EmptyContent",
            "EmptyProjector",
            "1.0.0");

        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

        // Act
        await persistent.SaveStateAsync(state);
        var loaded = await persistent.LoadStateAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.IsType<EmptyTagStatePayload>(loaded.Payload);
        Assert.Equal(state.Version, loaded.Version);
        Assert.Equal(state.TagGroup, loaded.TagGroup);
    }

    [Fact]
    public async Task SaveAndLoadSerializableState_DirectPath_ShouldSucceed()
    {
        // Arrange
        var serializable = new SerializableTagState(
            Payload: System.Text.Encoding.UTF8.GetBytes("{\"count\":1}"),
            Version: 3,
            LastSortedUniqueId: "uid-003",
            TagGroup: "Group",
            TagContent: "Content",
            TagProjector: "Projector",
            TagPayloadName: "WasmJsonTagState",
            ProjectorVersion: "v1");

        var cacheState = new TagStateCacheState();
        var persistentState = new TestPersistentState<TagStateCacheState>(cacheState);
        var persistent = new OrleansTagStatePersistent(persistentState, _tagStatePayloadTypes);

        // Act
        await persistent.SaveSerializableStateAsync(serializable);
        var loaded = await persistent.LoadSerializableStateAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(serializable.Version, loaded.Version);
        Assert.Equal(serializable.LastSortedUniqueId, loaded.LastSortedUniqueId);
        Assert.Equal(serializable.TagPayloadName, loaded.TagPayloadName);
        Assert.Equal(serializable.Payload, loaded.Payload);
    }

    [Fact]
    public async Task GetStateAsync_Should_Dispose_TagStateAccumulator()
    {
        var domainTypes = new DcbDomainTypes(
            eventTypes: new SimpleEventTypes(),
            tagTypes: new SimpleTagTypes(),
            tagProjectorTypes: new SimpleTagProjectorTypes(),
            tagStatePayloadTypes: _tagStatePayloadTypes,
            multiProjectorTypes: new SimpleMultiProjectorTypes(),
            queryTypes: new SimpleQueryTypes(),
            jsonSerializerOptions: new JsonSerializerOptions());
        var primitive = new TrackingTagStateProjectionPrimitive();
        var grain = new TagStateGrain(
            new EmptyEventStore(),
            domainTypes,
            primitive,
            new MissingActorAccessor(),
            new TestPersistentState<TagStateCacheState>(new TagStateCacheState()));
        var tagStateId = new TagStateId(new FallbackTag("TestGroup", "TestContent"), "MissingProjector");

        typeof(TagStateGrain)
            .GetField("_tagStateId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(grain, tagStateId);

        var state = await grain.GetStateAsync();

        Assert.Equal(1, primitive.DisposeCount);
        Assert.Equal(nameof(EmptyTagStatePayload), state.TagPayloadName);
        Assert.Equal("TestGroup", state.TagGroup);
        Assert.Equal("TestContent", state.TagContent);
    }

    [Fact]
    public async Task GetStateAsync_UsesTaggedStreamAndDefaultPerEventAccumulatorMemberWithoutTheListApi()
    {
        var domainTypes = new DcbDomainTypes(
            eventTypes: new SimpleEventTypes(),
            tagTypes: new SimpleTagTypes(),
            tagProjectorTypes: new SimpleTagProjectorTypes(),
            tagStatePayloadTypes: _tagStatePayloadTypes,
            multiProjectorTypes: new SimpleMultiProjectorTypes(),
            queryTypes: new SimpleQueryTypes(),
            jsonSerializerOptions: new JsonSerializerOptions());
        var primitive = new TrackingTagStateProjectionPrimitive();
        var eventStore = new StreamingEventStore();
        var grain = new TagStateGrain(
            eventStore,
            domainTypes,
            primitive,
            new MissingActorAccessor(),
            new TestPersistentState<TagStateCacheState>(new TagStateCacheState()));
        var tagStateId = new TagStateId(new FallbackTag("TestGroup", "stream"), "MissingProjector");

        typeof(TagStateGrain)
            .GetField("_tagStateId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(grain, tagStateId);

        await grain.GetStateAsync();

        Assert.Equal(1, eventStore.StreamCalls);
        Assert.Equal(0, eventStore.ListCalls);
        Assert.Equal(1, primitive.ApplyEventsCalls);
    }

    // Test helper class
    private class TestPersistentState<T> : IPersistentState<T> where T : new()
    {
        public TestPersistentState(T state)
        {
            State = state;
        }

        public T State { get; set; }
        public string Etag => "test-etag";
        public bool RecordExists => State != null;

        public Task ClearStateAsync()
        {
            State = new T();
            return Task.CompletedTask;
        }

        public Task ReadStateAsync() => Task.CompletedTask;

        public Task WriteStateAsync()
        {
            // Simulate write
            return Task.CompletedTask;
        }
    }

    // Test payload type
    public record TestStatePayload : ITagStatePayload
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Value { get; init; }
    }

    private sealed class TrackingTagStateProjectionPrimitive : ITagStateProjectionPrimitive
    {
        public int DisposeCount { get; private set; }
        public int ApplyEventsCalls { get; private set; }

        public ITagStateProjectionAccumulator CreateAccumulator(TagStateId tagStateId) =>
            new TrackingAccumulator(tagStateId, () => DisposeCount++, () => ApplyEventsCalls++);
    }

    private sealed class TrackingAccumulator : ITagStateProjectionAccumulator
    {
        private readonly TagStateId _tagStateId;
        private readonly Action _onDispose;
        private readonly Action _onApplyEvents;

        public TrackingAccumulator(TagStateId tagStateId, Action onDispose, Action onApplyEvents)
        {
            _tagStateId = tagStateId;
            _onDispose = onDispose;
            _onApplyEvents = onApplyEvents;
        }

        public bool ApplyState(SerializableTagState? cachedState) => true;

        public bool ApplyEvents(
            IReadOnlyList<SerializableEvent> events,
            string? latestSortableUniqueId,
            CancellationToken cancellationToken = default)
        {
            _onApplyEvents();
            return true;
        }

        public SerializableTagState GetSerializedState() =>
            new(
                Payload: Array.Empty<byte>(),
                Version: 0,
                LastSortedUniqueId: string.Empty,
                TagGroup: _tagStateId.TagGroup,
                TagContent: _tagStateId.TagContent,
                TagProjector: _tagStateId.TagProjectorName,
                TagPayloadName: nameof(EmptyTagStatePayload),
                ProjectorVersion: string.Empty);

        public void Dispose() => _onDispose();
    }

    private class EmptyEventStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagStream>()));

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) =>
            Task.FromResult(ResultBox.Error<TagState>(new NotImplementedException()));

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) =>
            Task.FromResult(ResultBox.FromValue(false));

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue(0L));

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<TagInfo>()));

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
            Task.FromResult(ResultBox.FromValue(string.Empty));

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<SerializableEvent>()));

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<SerializableEvent>()));

        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            Task.FromResult(ResultBox.Error<SerializableEvent>(new NotImplementedException()));

        public virtual Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            Task.FromResult(ResultBox.FromValue(Enumerable.Empty<SerializableEvent>()));

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) =>
            Task.FromResult(ResultBox.Error<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>(new NotImplementedException()));
    }

    private sealed class StreamingEventStore : EmptyEventStore, IStreamingTaggedSerializableEventStore,
        ITaggedStreamCapabilityProvider
    {
        private readonly SerializableEvent _event = new(
            new byte[] { 1 },
            "001",
            Guid.NewGuid(),
            new EventMetadata("cause", "correlation", "user"),
            new List<string> { "TestGroup:stream" },
            "TestEvent");

        public int StreamCalls { get; private set; }
        public int ListCalls { get; private set; }

        public TaggedStreamCapabilityDescriptor DescribeTaggedStream() =>
            TaggedStreamCapabilityDescriptor.Native("grain streaming test");

        public override Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null)
        {
            ListCalls++;
            throw new InvalidOperationException("The tagged stream grain path must not read the list API.");
        }

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since,
            SortableUniqueId? until,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            StreamCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            await onEvent(_event);
            return ResultBox.FromValue(new SerializableEventStreamReadResult(1, _event.SortableUniqueIdValue));
        }
    }

    private sealed class MissingActorAccessor : IActorObjectAccessor
    {
        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class =>
            Task.FromResult(ResultBox.Error<T>(new KeyNotFoundException(actorId)));

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(false);
    }

}
