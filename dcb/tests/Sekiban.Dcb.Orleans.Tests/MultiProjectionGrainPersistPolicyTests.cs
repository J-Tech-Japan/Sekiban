using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.ColdEvents;
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

    [Fact]
    public async Task PersistStateAsync_WhenDeferredRepairFails_DoesNotWriteOrCompactAndReturnsTheOriginalFailure()
    {
        // The real wrapper-level tests establish the deferred-repair failure and its retained cause. This is the
        // production persistence boundary: a host reporting that repair failure must stop before snapshot capture,
        // the grain-state provider write, or the irreversible compaction call.
        var persistentState = new TestPersistentState<MultiProjectionGrainState>(new MultiProjectionGrainState());
        var repairFailure = new InvalidOperationException("deferred repair failed");
        var host = new DeferredRepairFailingHost(repairFailure);
        var grain = CreateGrain(persistentState: persistentState);
        SetPrivateField(grain, "_grainKey", "projection");
        SetPrivateField(grain, "_projectorName", "projection");
        SetPrivateField(grain, "_host", host);

        var result = await grain.PersistStateAsync();

        Assert.False(result.IsSuccess);
        Assert.Same(repairFailure, result.GetException());
        Assert.Equal(1, host.ForcePromoteCalls);
        Assert.Equal(0, host.SnapshotWriteCalls);
        Assert.Equal(0, host.CompactCalls);
        Assert.Equal(0, persistentState.WriteCalls);
    }

    [Fact]
    public async Task ProjectionStatusHeartbeat_PinsHostVersionAndRebasesMissingRowsWithoutUnconditionalCreate()
    {
        // This deliberately invokes the production grain writer against the real in-memory status store. A fake result
        // cannot prove the two regression boundaries: persisted state changing inside an activation must not change the
        // physical key, and an expected>0 write to a removed row must not create in the same operation.
        var host = new MutableProjectionActorHost("registered-v1");
        var hostFactory = new MutableProjectionActorHostFactory(host);
        var serviceIdProvider = new FixedServiceIdProvider("svc");
        var statusStore = new Sekiban.Dcb.Testing.InMemoryMultiProjectionStateStore(serviceIdProvider);
        var grain = CreateGrain(
            state: new MultiProjectionGrainState { ProjectorVersion = "mutable-v1" },
            actorHostFactory: hostFactory,
            projectionStatusStore: statusStore,
            projectionStatusOptions: new ProjectionStatusOptions
            {
                ClusterId = "test-cluster",
                HeartbeatWriteTimeout = TimeSpan.FromSeconds(1)
            });

        SetPrivateField(grain, "_serviceId", "svc");
        SetPrivateField(grain, "_grainKey", "projection");
        SetPrivateField(grain, "_projectorName", "projection");
        SetPrivateField(grain, "_host", host);
        InvokePrivate(grain, "CaptureProjectionStatusWriterIdentity", ["projection"]);

        await InvokePrivateTaskAsync(grain, "WriteProjectionStatusHeartbeatAsync");
        var first = Assert.Single((await statusStore.ListAsync("projection", "registered-v1")).GetValue());
        Assert.Equal(1, first.Sequence);
        Assert.Equal("registered-v1", first.ProjectorVersion);

        var coordinated = Assert.IsType<CoordinatedGrainStateStore>(GetPrivateField(grain, "_stateStore"));
        await coordinated.ExecuteWriteAsync(
            GrainStateWriteKind.MetadataMaintenance,
            state => state.ProjectorVersion = "mutable-v2");

        // Recreating the host with different registered metadata verifies/logs disagreement but must retain the pin.
        host.ProjectorVersion = "registered-v2";
        InvokePrivate(grain, "RecreateHostForFullRebuild");
        var pinned = Assert.IsType<ProjectionStatusWriterIdentity>(
            GetPrivateField(grain, "_projectionStatusWriterIdentity"));
        Assert.Equal("registered-v1", pinned.ProjectorVersion);

        await InvokePrivateTaskAsync(grain, "WriteProjectionStatusHeartbeatAsync");
        var advanced = Assert.Single((await statusStore.ListAsync("projection", "registered-v1")).GetValue());
        Assert.Equal(2, advanced.Sequence);
        Assert.Empty((await statusStore.ListAsync("projection", "mutable-v2")).GetValue());

        // Simulate external row loss. The expected=2 write must report RowAbsent and only reset the local fence; no
        // unconditional insert is allowed in this operation.
        statusStore.Clear();
        await InvokePrivateTaskAsync(grain, "WriteProjectionStatusHeartbeatAsync");
        Assert.Empty((await statusStore.ListAsync("projection", "registered-v1")).GetValue());
        Assert.Equal(0L, GetPrivateField<long>(grain, "_projectionStatusSequence"));

        // A competing writer can win the later conditional create. The grain must adopt that fence, then use the
        // reachable expected=1 update path, while still writing the activation-pinned v1 identity.
        var competing = first with
        {
            ActivationId = "competing-activation",
            Sequence = 1,
            RecordedAtUtc = DateTimeOffset.UtcNow
        };
        Assert.True((await statusStore.UpsertAsync(competing, 0)).GetValue().Committed);
        SetPrivateField(grain, "_projectionStatusNextAttemptUtc", DateTimeOffset.MinValue);
        await InvokePrivateTaskAsync(grain, "WriteProjectionStatusHeartbeatAsync");
        var afterCreateRace = Assert.Single((await statusStore.ListAsync("projection", "registered-v1")).GetValue());
        Assert.Equal("competing-activation", afterCreateRace.ActivationId);
        Assert.Equal(1, afterCreateRace.Sequence);
        Assert.Equal(1L, GetPrivateField<long>(grain, "_projectionStatusSequence"));

        SetPrivateField(grain, "_projectionStatusNextAttemptUtc", DateTimeOffset.MinValue);
        await InvokePrivateTaskAsync(grain, "WriteProjectionStatusHeartbeatAsync");
        var recovered = Assert.Single((await statusStore.ListAsync("projection", "registered-v1")).GetValue());
        Assert.Equal(2, recovered.Sequence);
        Assert.Empty((await statusStore.ListAsync("projection", "mutable-v2")).GetValue());
    }

    [Fact]
    public void Hot_thresholds_are_inclusive_and_preserve_legacy_modulo_precedence()
    {
        var grain = CreateGrain();

        SetPrivateField(grain, "_eventsProcessed", 4_999L);
        SetPrivateField(grain, "_eventsProcessedSinceLastCatchUpPersist", 0L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 4_999L);
        AssertDecision(grain, null, null, shouldPersist: false, reason: "none");

        SetPrivateField(grain, "_eventsProcessed", 5_000L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 1L);
        AssertDecision(grain, null, null, shouldPersist: true, reason: "event_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsProcessed", 5_001L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 4_999L);
        AssertDecision(grain, null, null, shouldPersist: false, reason: "none");

        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_000L);
        AssertDecision(grain, null, null, shouldPersist: true, reason: "fetched_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsProcessed", 123L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_001L);
        AssertDecision(grain, null, null, shouldPersist: true, reason: "fetched_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsProcessed", 123L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_000L);
        AssertDecision(grain, null, null, shouldPersist: true, reason: "fetched_count_checkpoint");
    }

    [Fact]
    public void Routing_uses_UsedCold_and_not_hybrid_store_presence()
    {
        var grain = CreateGrain();
        var hybrid = CreateHybrid(maxEvents: 100);

        SetPrivateField(grain, "_eventsProcessed", 123L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_000L);
        AssertDecision(
            grain,
            hybrid,
            new HybridReadBatchMetadata("hot-only", UsedCold: false, UsedHot: true, false, 0, 500, 0),
            shouldPersist: true,
            reason: "fetched_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 100L);
        AssertDecision(
            grain,
            hybrid,
            new HybridReadBatchMetadata("cold", UsedCold: true, UsedHot: false, false, 100, 0, 1),
            shouldPersist: true,
            reason: "fetched_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_000L);
        AssertDecision(
            grain,
            hybrid,
            metadata: null,
            shouldPersist: true,
            reason: "fetched_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_000L);
        AssertDecision(
            grain,
            hybridCatchUpStore: null,
            new HybridReadBatchMetadata("cold-without-store", UsedCold: true, UsedHot: false, false, 100, 0, 1),
            shouldPersist: false,
            reason: "none");
    }

    [Fact]
    public void Cold_legacy_precedence_and_fetched_only_boundary_are_preserved()
    {
        var grain = CreateGrain();
        var hybrid = CreateHybrid(maxEvents: 100, interval: TimeSpan.FromHours(1));
        var cold = new HybridReadBatchMetadata("cold", UsedCold: true, UsedHot: false, false, 100, 0, 1);

        SetPrivateField(grain, "_eventsProcessedSinceLastCatchUpPersist", 99L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 100L);
        AssertDecision(grain, hybrid, cold, shouldPersist: true, reason: "fetched_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsProcessedSinceLastCatchUpPersist", 100L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 1L);
        AssertDecision(grain, hybrid, cold, shouldPersist: true, reason: "max_events_since_last_persist");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        AssertDecision(
            grain,
            hybrid,
            cold with { ReachedColdSegmentBoundary = true },
            shouldPersist: true,
            reason: "cold_segment_boundary");
    }

    [Fact]
    public void Counter_and_time_windows_reset_only_after_a_discriminating_non_fire()
    {
        var grain = CreateGrain();

        SetPrivateField(grain, "_eventsProcessed", 123L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_000L);
        AssertDecision(grain, null, null, shouldPersist: true, reason: "fetched_count_checkpoint");
        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 4_999L);
        AssertDecision(grain, null, null, shouldPersist: false, reason: "none");
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 5_000L);
        AssertDecision(grain, null, null, shouldPersist: true, reason: "fetched_count_checkpoint");

        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_lastCatchUpPersistUtc", DateTime.UtcNow - TimeSpan.FromMinutes(6));
        AssertDecision(grain, null, null, shouldPersist: true, reason: "time_checkpoint");
        InvokePrivate(grain, "ResetCatchUpPersistWindow", [false]);
        SetPrivateField(grain, "_lastCatchUpPersistUtc", DateTime.UtcNow);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 1L);
        AssertDecision(grain, null, null, shouldPersist: false, reason: "none");
        SetPrivateField(grain, "_lastCatchUpPersistUtc", DateTime.UtcNow - TimeSpan.FromMinutes(6));
        AssertDecision(grain, null, null, shouldPersist: true, reason: "time_checkpoint");
    }

    [Fact]
    public void Read_path_transition_resets_the_three_window_fields_but_same_path_does_not_mutate_them()
    {
        var grain = CreateGrain();
        var hot = new HybridReadBatchMetadata("hot", UsedCold: false, UsedHot: true, false, 0, 1, 0);
        var cold = new HybridReadBatchMetadata("cold", UsedCold: true, UsedHot: false, false, 1, 0, 1);

        InvokePrivate(grain, "ObserveCatchUpReadPath", [hot]);
        SetPrivateField(grain, "_eventsProcessedSinceLastCatchUpPersist", 7L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 8L);
        var before = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        SetPrivateField(grain, "_lastCatchUpPersistUtc", before);

        InvokePrivate(grain, "ObserveCatchUpReadPath", [hot]);
        Assert.Equal(7L, GetPrivateField<long>(grain, "_eventsProcessedSinceLastCatchUpPersist"));
        Assert.Equal(8L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.Equal(before, GetPrivateField<DateTime>(grain, "_lastCatchUpPersistUtc"));

        InvokePrivate(grain, "ObserveCatchUpReadPath", [cold]);
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsProcessedSinceLastCatchUpPersist"));
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.True(GetPrivateField<DateTime>(grain, "_lastCatchUpPersistUtc") > before);
        Assert.True(GetPrivateField<bool>(grain, "_lastCatchUpUsedCold"));
    }

    private static void AssertDecision(
        MultiProjectionGrain grain,
        HybridEventStore? hybridCatchUpStore,
        HybridReadBatchMetadata? metadata,
        bool shouldPersist,
        string reason)
    {
        var decision = InvokePrivate(grain, "GetCatchUpPersistDecision", [hybridCatchUpStore, metadata]);
        Assert.NotNull(decision);
        Assert.Equal(shouldPersist, GetProperty<bool>(decision!, "ShouldPersist"));
        Assert.Equal(reason, GetProperty<string>(decision!, "Reason"));
    }

    private static HybridEventStore CreateHybrid(
        int maxEvents,
        TimeSpan? interval = null,
        bool persistOnBoundary = true) =>
        new(
            new StubEventStore(),
            new EmptyColdObjectStorage(),
            new JsonlColdSegmentFormatHandler(),
            new DefaultServiceIdProvider(),
            Options.Create(new ColdEventStoreOptions
            {
                Enabled = true,
                CatchUpPersistMaxEventsWithoutSnapshot = maxEvents,
                CatchUpPersistMaxInterval = interval ?? TimeSpan.FromMinutes(5),
                PersistSnapshotOnColdSegmentBoundary = persistOnBoundary
            }),
            NullLogger<HybridEventStore>.Instance);

    private static MultiProjectionGrain CreateGrain(
        GeneralMultiProjectionActorOptions? options = null,
        MultiProjectionGrainState? state = null,
        IProjectionActorHostFactory? actorHostFactory = null,
        IProjectionStatusStore? projectionStatusStore = null,
        ProjectionStatusOptions? projectionStatusOptions = null,
        TestPersistentState<MultiProjectionGrainState>? persistentState = null) =>
        new(
            persistentState ?? new TestPersistentState<MultiProjectionGrainState>(state ?? new MultiProjectionGrainState()),
            actorHostFactory ?? new StubProjectionActorHostFactory(),
            new StubEventStore(),
            new DefaultOrleansEventSubscriptionResolver(),
            multiProjectionStateStore: null,
            new NoOpMultiProjectionEventStatistics(),
            actorOptions: options ?? new GeneralMultiProjectionActorOptions(),
            tempFileSnapshotManager: null,
            logger: NullLogger<MultiProjectionGrain>.Instance,
            eventStoreFactory: null,
            serviceIdProvider: new DefaultServiceIdProvider(),
            projectionStatusStore: projectionStatusStore,
            projectionStatusOptions: projectionStatusOptions);

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

    private static object? GetPrivateField(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    private static T GetPrivateField<T>(object target, string fieldName) =>
        Assert.IsType<T>(GetPrivateField(target, fieldName));

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static async Task InvokePrivateTaskAsync(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(target, null));
        await task;
    }

    private sealed class TestPersistentState<T> : IPersistentState<T> where T : new()
    {
        public TestPersistentState(T state) => State = state;

        public T State { get; set; }
        public int WriteCalls { get; private set; }
        public string Etag => "test-etag";
        public bool RecordExists => true;

        public Task ClearStateAsync()
        {
            State = new T();
            return Task.CompletedTask;
        }

        public Task ReadStateAsync() => Task.CompletedTask;
        public Task WriteStateAsync()
        {
            WriteCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubProjectionActorHostFactory : IProjectionActorHostFactory
    {
        public IProjectionActorHost Create(
            string projectorName,
            GeneralMultiProjectionActorOptions? options = null,
            Microsoft.Extensions.Logging.ILogger? logger = null) => new StubProjectionActorHost();
    }

    private sealed class MutableProjectionActorHostFactory(MutableProjectionActorHost host) : IProjectionActorHostFactory
    {
        public IProjectionActorHost Create(
            string projectorName,
            GeneralMultiProjectionActorOptions? options = null,
            Microsoft.Extensions.Logging.ILogger? logger = null) => host;
    }

    private class StubProjectionActorHost : IProjectionActorHost
    {
        public Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true) =>
            throw new NotSupportedException();

        public Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true) =>
            throw new NotSupportedException();

        public Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync() =>
            throw new NotSupportedException();

        public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true) =>
            throw new NotSupportedException();

        public Task<ResultBox<bool>> WriteSnapshotToStreamAsync(Stream target, bool canGetUnsafeState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public virtual Task<ResultBox<bool>> WriteSnapshotForPersistenceToStreamAsync(
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

        public virtual void ForcePromoteBufferedEvents() => throw new NotSupportedException();
        public virtual void CompactSafeHistory() => throw new NotSupportedException();
        public void ForcePromoteAllBufferedEvents() => throw new NotSupportedException();
        public Task<string> GetSafeLastSortableUniqueIdAsync() => throw new NotSupportedException();
        public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId) => throw new NotSupportedException();
        public long EstimateStateSizeBytes(bool includeUnsafeDetails) => throw new NotSupportedException();
        public string PeekCurrentSafeWindowThreshold() => throw new NotSupportedException();
        public virtual string GetProjectorVersion() => "registered-v1";

        public Task<ResultBox<bool>> RewriteSnapshotVersionAsync(
            Stream source,
            Stream target,
            string newVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class MutableProjectionActorHost(string projectorVersion) : StubProjectionActorHost
    {
        public string ProjectorVersion { get; set; } = projectorVersion;

        public override string GetProjectorVersion() => ProjectorVersion;
    }

    private sealed class DeferredRepairFailingHost(Exception repairFailure) : StubProjectionActorHost
    {
        public int ForcePromoteCalls { get; private set; }
        public int SnapshotWriteCalls { get; private set; }
        public int CompactCalls { get; private set; }

        public override void ForcePromoteBufferedEvents()
        {
            ForcePromoteCalls++;
            throw repairFailure;
        }

        public override void CompactSafeHistory() => CompactCalls++;

        public override Task<ResultBox<bool>> WriteSnapshotForPersistenceToStreamAsync(
            Stream target,
            bool canGetUnsafeState,
            int offloadThresholdBytes,
            CancellationToken cancellationToken)
        {
            SnapshotWriteCalls++;
            return Task.FromResult(ResultBox.FromValue(true));
        }
    }

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    private sealed class StubEventStore : IEventStore
    {
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
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

    private sealed class EmptyColdObjectStorage : IColdObjectStorage
    {
        public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct) =>
            Task.FromResult(ResultBox.Error<ColdStorageObject>(new FileNotFoundException(path)));

        public Task<ResultBox<bool>> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct) =>
            Task.FromResult(ResultBox.FromValue(true));

        public Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct) =>
            Task.FromResult(ResultBox.FromValue(true));

        public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct) =>
            Task.FromResult(ResultBox.FromValue<IReadOnlyList<string>>(Array.Empty<string>()));

        public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct) =>
            Task.FromResult(ResultBox.FromValue(true));
    }
}
