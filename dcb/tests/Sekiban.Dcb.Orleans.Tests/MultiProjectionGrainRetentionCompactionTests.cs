using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Reflection;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class MultiProjectionGrainRetentionCompactionTests
{
    [Fact]
    public void ClearProcessedEventCache_ShouldReleaseRetainedCapacity()
    {
        var grain = CreateGrain();
        var processedEventIds = GetField<HashSet<Guid>>(grain, "_processedEventIds");
        var processedEventIdOrder = GetField<Queue<Guid>>(grain, "_processedEventIdOrder");

        var ids = Enumerable.Range(0, 4096).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            processedEventIds.Add(id);
            processedEventIdOrder.Enqueue(id);
        }

        var processedCapacityBefore = GetCollectionCapacity(processedEventIds);
        var orderCapacityBefore = GetCollectionCapacity(processedEventIdOrder);

        InvokePrivate(grain, "ClearProcessedEventCache");

        Assert.Empty(processedEventIds);
        Assert.Empty(processedEventIdOrder);
        Assert.True(GetCollectionCapacity(processedEventIds) < processedCapacityBefore);
        Assert.True(GetCollectionCapacity(processedEventIdOrder) < orderCapacityBefore);
    }

    [Fact]
    public void CompactRetainedCollections_ShouldTrimTransientBuffers()
    {
        var grain = CreateGrain();
        var eventBuffer = GetField<List<SerializableEvent>>(grain, "_eventBuffer");
        var unsafeEventIds = GetField<HashSet<string>>(grain, "_unsafeEventIds");
        var pendingStreamEvents = GetField<Queue<SerializableEvent>>(grain, "_pendingStreamEvents");

        for (var i = 0; i < 4096; i++)
        {
            var serializableEvent = CreateSerializableEvent(i);
            eventBuffer.Add(serializableEvent);
            unsafeEventIds.Add(serializableEvent.Id.ToString("N"));
            pendingStreamEvents.Enqueue(serializableEvent);
        }

        var eventBufferCapacityBefore = eventBuffer.Capacity;
        var unsafeEventIdsCapacityBefore = GetCollectionCapacity(unsafeEventIds);
        var pendingCapacityBefore = GetCollectionCapacity(pendingStreamEvents);

        eventBuffer.Clear();
        unsafeEventIds.Clear();
        pendingStreamEvents.Clear();

        InvokePrivate(grain, "CompactRetainedCollections");

        Assert.Empty(eventBuffer);
        Assert.Empty(unsafeEventIds);
        Assert.Empty(pendingStreamEvents);
        Assert.True(eventBuffer.Capacity < eventBufferCapacityBefore);
        Assert.True(GetCollectionCapacity(unsafeEventIds) < unsafeEventIdsCapacityBefore);
        Assert.True(GetCollectionCapacity(pendingStreamEvents) < pendingCapacityBefore);
    }

    private static MultiProjectionGrain CreateGrain() =>
        new(
            new TestPersistentState<MultiProjectionGrainState>(new MultiProjectionGrainState()),
            new StubProjectionActorHostFactory(),
            new StubEventStore(),
            new DefaultOrleansEventSubscriptionResolver(),
            multiProjectionStateStore: null,
            new NoOpMultiProjectionEventStatistics(),
            actorOptions: new GeneralMultiProjectionActorOptions(),
            tempFileSnapshotManager: null,
            logger: NullLogger<MultiProjectionGrain>.Instance,
            eventStoreFactory: null,
            serviceIdProvider: new DefaultServiceIdProvider());

    private static SerializableEvent CreateSerializableEvent(int seed)
    {
        var eventId = Guid.NewGuid();
        return new SerializableEvent(
            Payload: BitConverter.GetBytes(seed),
            SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(seed), eventId),
            Id: eventId,
            EventMetadata: new EventMetadata(eventId.ToString("N"), eventId.ToString("N"), "test"),
            Tags: new List<string>(),
            EventPayloadName: "TestEvent");
    }

    private static T GetField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<T>(field!.GetValue(target));
    }

    private static void InvokePrivate(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, null);
    }

    private static int GetCollectionCapacity(object collection)
    {
        var ensureCapacity = collection.GetType().GetMethod("EnsureCapacity", new[] { typeof(int) });
        if (ensureCapacity != null)
        {
            return (int)ensureCapacity.Invoke(collection, new object[] { 0 })!;
        }

        return collection switch
        {
            List<SerializableEvent> list => list.Capacity,
            _ => GetArrayFieldLength(collection)
        };
    }

    private static int GetArrayFieldLength(object collection)
    {
        var field = collection.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(candidate => candidate.FieldType.IsArray);
        Assert.NotNull(field);
        return ((Array?)field!.GetValue(collection))?.Length ?? 0;
    }

    private sealed class TestPersistentState<T> : IPersistentState<T> where T : new()
    {
        public TestPersistentState(T state) => State = state;

        public T State { get; set; }
        public string Etag => "test-etag";
        public bool RecordExists => true;
        public Task ClearStateAsync()
        {
            State = new T();
            return Task.CompletedTask;
        }

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task WriteStateAsync() => Task.CompletedTask;
    }

    private sealed class StubProjectionActorHostFactory : IProjectionActorHostFactory
    {
        public IProjectionActorHost Create(
            string projectorName,
            GeneralMultiProjectionActorOptions? options = null,
            Microsoft.Extensions.Logging.ILogger? logger = null) => new StubProjectionActorHost();
    }

    private sealed class StubProjectionActorHost : IProjectionActorHost
    {
        public Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true) =>
            throw new NotSupportedException();

        public Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true) =>
            throw new NotSupportedException();

        public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true) =>
            throw new NotSupportedException();

        public Task<ResultBox<bool>> WriteSnapshotToStreamAsync(Stream target, bool canGetUnsafeState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ResultBox<bool>> WriteSnapshotForPersistenceToStreamAsync(
            Stream target,
            bool canGetUnsafeState,
            int offloadThresholdBytes,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ResultBox<bool>> RestoreSnapshotFromStreamAsync(Stream source, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
            SerializableQueryParameter query,
            int? safeVersion,
            string? safeThreshold,
            DateTime? safeThresholdTime,
            int? unsafeVersion) => throw new NotSupportedException();

        public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
            SerializableQueryParameter query,
            int? safeVersion,
            string? safeThreshold,
            DateTime? safeThresholdTime,
            int? unsafeVersion) => throw new NotSupportedException();

        public void ForcePromoteBufferedEvents() => throw new NotSupportedException();
        public void CompactSafeHistory() => throw new NotSupportedException();
        public void ForcePromoteAllBufferedEvents() => throw new NotSupportedException();
        public Task<string> GetSafeLastSortableUniqueIdAsync() => throw new NotSupportedException();
        public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId) => throw new NotSupportedException();
        public long EstimateStateSizeBytes(bool includeUnsafeDetails) => throw new NotSupportedException();
        public string PeekCurrentSafeWindowThreshold() => throw new NotSupportedException();
        public string GetProjectorVersion() => throw new NotSupportedException();

        public Task<ResultBox<bool>> RewriteSnapshotVersionAsync(
            Stream source,
            Stream target,
            string newVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubEventStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) => throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => throw new NotSupportedException();

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
    }
}
