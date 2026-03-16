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

public class MultiProjectionGrainPersistPolicyTests
{
    [Fact]
    public void ResolvePersistPolicySettings_ShouldUseStorageFriendlyDefaults()
    {
        var grain = CreateGrain();

        var settings = InvokePrivate(
            grain,
            "ResolvePersistPolicySettings",
            ["DefaultProjection"]);
        Assert.NotNull(settings);

        Assert.Equal(10_000, GetProperty<int>(settings!, "PersistBatchSize"));
        Assert.Equal(TimeSpan.FromHours(1), GetProperty<TimeSpan>(settings!, "PersistInterval"));
        Assert.True(GetProperty<bool>(settings!, "SkipPersistWhenSafeCheckpointUnchanged"));
    }

    [Fact]
    public void ResolvePersistPolicySettings_ShouldApplyProjectorOverride()
    {
        var options = new GeneralMultiProjectionActorOptions
        {
            PersistBatchSize = 1000,
            PersistIntervalSeconds = 300,
            SkipPersistWhenSafeCheckpointUnchanged = true,
            ProjectorPersistenceOverrides = new Dictionary<string, MultiProjectionPersistenceOverrideOptions>(StringComparer.Ordinal)
            {
                ["HotProjection"] = new()
                {
                    PersistBatchSize = 250,
                    PersistIntervalSeconds = 3600,
                    SkipPersistWhenSafeCheckpointUnchanged = false
                }
            }
        };
        var grain = CreateGrain(options);

        var settings = InvokePrivate(
            grain,
            "ResolvePersistPolicySettings",
            ["HotProjection"]);
        Assert.NotNull(settings);

        Assert.Equal(250, GetProperty<int>(settings!, "PersistBatchSize"));
        Assert.Equal(TimeSpan.FromHours(1), GetProperty<TimeSpan>(settings!, "PersistInterval"));
        Assert.False(GetProperty<bool>(settings!, "SkipPersistWhenSafeCheckpointUnchanged"));
    }

    [Fact]
    public void ResolvePersistPolicySettings_ShouldAllowDisablingPeriodicAndBatchPersist()
    {
        var grain = CreateGrain(new GeneralMultiProjectionActorOptions
        {
            PersistBatchSize = 0,
            PersistIntervalSeconds = 0,
            SkipPersistWhenSafeCheckpointUnchanged = true
        });

        var settings = InvokePrivate(
            grain,
            "ResolvePersistPolicySettings",
            ["AnyProjection"]);
        Assert.NotNull(settings);

        Assert.Equal(0, GetProperty<int>(settings!, "PersistBatchSize"));
        Assert.Equal(TimeSpan.Zero, GetProperty<TimeSpan>(settings!, "PersistInterval"));
        Assert.True(GetProperty<bool>(settings!, "SkipPersistWhenSafeCheckpointUnchanged"));
    }

    [Fact]
    public void ShouldSkipPersistForUnchangedSafeCheckpoint_ShouldReturnTrue_WhenCheckpointMatches()
    {
        var grain = CreateGrain(
            new GeneralMultiProjectionActorOptions { SkipPersistWhenSafeCheckpointUnchanged = true },
            new MultiProjectionGrainState
            {
                ProjectorVersion = "v1",
                LastSortableUniqueId = "safe-001",
                LastGoodSafeVersion = 10
            });

        var result = Assert.IsType<bool>(
            InvokePrivate(
                grain,
                "ShouldSkipPersistForUnchangedSafeCheckpoint",
                ["v1", "safe-001", 10]));

        Assert.True(result);
    }

    [Fact]
    public void ShouldSkipPersistForUnchangedSafeCheckpoint_ShouldReturnTrue_WhenSafeVersionIsUnavailableButCheckpointMatches()
    {
        var grain = CreateGrain(
            new GeneralMultiProjectionActorOptions { SkipPersistWhenSafeCheckpointUnchanged = true },
            new MultiProjectionGrainState
            {
                ProjectorVersion = "v1",
                LastSortableUniqueId = "safe-001",
                LastGoodSafeVersion = 10
            });

        var result = Assert.IsType<bool>(
            InvokePrivate(
                grain,
                "ShouldSkipPersistForUnchangedSafeCheckpoint",
                ["v1", "safe-001", null]));

        Assert.True(result);
    }

    [Fact]
    public void ShouldSkipPersistForUnchangedSafeCheckpoint_ShouldReturnFalse_WhenSkipDisabledOrCheckpointChanged()
    {
        var disabledGrain = CreateGrain(
            new GeneralMultiProjectionActorOptions { SkipPersistWhenSafeCheckpointUnchanged = false },
            new MultiProjectionGrainState
            {
                ProjectorVersion = "v1",
                LastSortableUniqueId = "safe-001",
                LastGoodSafeVersion = 10
            });

        Assert.False(Assert.IsType<bool>(
            InvokePrivate(
                disabledGrain,
                "ShouldSkipPersistForUnchangedSafeCheckpoint",
                ["v1", "safe-001", 10])));

        var changedCheckpointGrain = CreateGrain(
            new GeneralMultiProjectionActorOptions { SkipPersistWhenSafeCheckpointUnchanged = true },
            new MultiProjectionGrainState
            {
                ProjectorVersion = "v1",
                LastSortableUniqueId = "safe-001",
                LastGoodSafeVersion = 10
            });

        Assert.False(Assert.IsType<bool>(
            InvokePrivate(
                changedCheckpointGrain,
                "ShouldSkipPersistForUnchangedSafeCheckpoint",
                ["v2", "safe-001", 10])));

        Assert.False(Assert.IsType<bool>(
            InvokePrivate(
                changedCheckpointGrain,
                "ShouldSkipPersistForUnchangedSafeCheckpoint",
                ["v1", "safe-002", 10])));

        Assert.False(Assert.IsType<bool>(
            InvokePrivate(
                changedCheckpointGrain,
                "ShouldSkipPersistForUnchangedSafeCheckpoint",
                ["v1", "safe-001", 11])));
    }

    private static MultiProjectionGrain CreateGrain(
        GeneralMultiProjectionActorOptions? options = null,
        MultiProjectionGrainState? state = null) =>
        new(
            new TestPersistentState<MultiProjectionGrainState>(state ?? new MultiProjectionGrainState()),
            new StubProjectionActorHostFactory(),
            new StubEventStore(),
            new DefaultOrleansEventSubscriptionResolver(),
            multiProjectionStateStore: null,
            new NoOpMultiProjectionEventStatistics(),
            actorOptions: options ?? new GeneralMultiProjectionActorOptions(),
            tempFileSnapshotManager: null,
            logger: NullLogger<MultiProjectionGrain>.Instance,
            eventStoreFactory: null,
            serviceIdProvider: new DefaultServiceIdProvider());

    private static object? InvokePrivate(object target, string methodName, object?[]? args = null)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!.Invoke(target, args);
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(target));
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
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            throw new NotSupportedException();
    }
}
