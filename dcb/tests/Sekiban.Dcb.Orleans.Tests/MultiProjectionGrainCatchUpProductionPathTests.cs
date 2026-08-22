using System.Reflection;
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
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Production-path killing tests for the catch-up completion/persist seam. These tests drive public RefreshAsync,
///     which reaches the real enumerable or streaming reader and the real common post-fetch seam. They deliberately do
///     not call the private persist-decision function in isolation.
/// </summary>
public sealed class MultiProjectionGrainCatchUpProductionPathTests
{
    [Fact]
    public async Task Enumerable_zero_applied_batches_stay_active_and_trigger_once_on_fetched_batch_ten()
    {
        var store = new ProductionCatchUpEventStore();
        var host = new ProductionCatchUpProjectionHost();
        var grain = CreateGrain(store, host);

        await grain.RefreshAsync();

        Assert.Equal(15, store.ReadCalls); // 10 non-empty batches, then the five empty completion reads.
        Assert.Equal(store.Events[499].SortableUniqueIdValue, store.ReadSinceValues[1]);
        Assert.Equal(2, host.StateMetadataCalls); // fetched-triggered attempt plus the final catch-up persist check
        Assert.Equal(1, host.SnapshotWriteCalls); // exactly one fetched-triggered durable snapshot write
        Assert.Equal(1, store.PersistentState.WriteCalls);
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.Equal(
            store.Events[^1].SortableUniqueIdValue,
            GetCatchUpCurrentPosition(grain));
    }

    [Fact]
    public async Task Streaming_zero_applied_batches_stay_active_and_trigger_once_on_fetched_batch_ten()
    {
        var store = new ProductionStreamingCatchUpEventStore();
        var host = new ProductionCatchUpProjectionHost();
        var grain = CreateGrain(store, host);

        await grain.RefreshAsync();

        Assert.Equal(15, store.ReadCalls);
        Assert.Equal(store.Events[499].SortableUniqueIdValue, store.ReadSinceValues[1]);
        Assert.Equal(2, host.StateMetadataCalls);
        Assert.Equal(1, host.SnapshotWriteCalls); // exactly one fetched-triggered durable snapshot write
        Assert.Equal(1, store.PersistentState.WriteCalls);
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.Equal(
            store.Events[^1].SortableUniqueIdValue,
            GetCatchUpCurrentPosition(grain));
    }

    [Fact]
    public async Task Synthetic_id_tracking_only_does_not_advance_c0_and_fresh_activation_reapplies_the_range()
    {
        var c0 = SortableUniqueId.GetTickString(9_000) + SortableUniqueId.GetIdString(Guid.Empty);
        var c0State = new MultiProjectionGrainState
        {
            ProjectorName = "production-catch-up",
            ProjectorVersion = "v1",
            LastSortableUniqueId = c0,
            EventsProcessed = 123,
            LastGoodSafeVersion = 1,
            LastGoodEventsProcessed = 123
        };

        // Synthetic case: the activation-local ID cache says the range was seen, but the host safe checkpoint stays at
        // C0 and no durable snapshot contains those effects.
        var unchangedC0Record = new RecordingPersistentState<MultiProjectionGrainState> { State = c0State };
        var syntheticStore = new ProductionCatchUpEventStore(persistentState: unchangedC0Record);
        var syntheticHost = new ProductionCatchUpProjectionHost
        {
            ProjectionStartPosition = c0,
            SafeVersion = 1,
            SafePosition = c0
        };
        var syntheticGrain = CreateGrain(syntheticStore, syntheticHost, persistentState: unchangedC0Record);
        var authoritativeEventIds = syntheticStore.Events.Select(ev => ev.Id).ToArray();
        var expectedFreshProjection = DeterministicMaterializedProjection.From(c0, authoritativeEventIds);

        Assert.Equal(authoritativeEventIds.Length, GetPrivateField<HashSet<Guid>>(syntheticGrain, "_processedEventIds").Count);
        Assert.Equal(authoritativeEventIds.Length, authoritativeEventIds.Distinct().Count());
        await syntheticGrain.RefreshAsync();

        Assert.Equal(c0, syntheticStore.ReadSinceValues[0]);
        Assert.Equal(2, syntheticHost.StateMetadataCalls); // one start-position inference plus the fetched checkpoint attempt
        Assert.Equal(1, syntheticHost.SafeCheckpointCalls); // the persist checkpoint was captured exactly once
        Assert.Equal(0, syntheticHost.SnapshotWriteCalls);
        Assert.Equal(0, syntheticStore.PersistentState.WriteCalls);
        Assert.Equal("no_durable_write", GetPrivateField<string>(syntheticGrain, "_lastPersistOutcome"));
        Assert.Equal(c0, syntheticStore.PersistentState.State.LastSortableUniqueId);
        Assert.Equal(c0, c0State.LastSortableUniqueId); // traversal never became the committed restart cursor
        Assert.Empty(syntheticHost.AppliedEventIds);
        Assert.Equal(
            DeterministicMaterializedProjection.From(c0, Array.Empty<Guid>()),
            syntheticHost.MaterializedProjection);

        // Fresh activation: it must reuse the same unchanged durable C0 record and authoritative event sequence. The
        // ephemeral ID cache is gone, so the authoritative read from C0 must apply every event exactly once.
        var freshStore = new ProductionCatchUpEventStore(
            events: syntheticStore.Events,
            persistentState: unchangedC0Record);
        var freshHost = new ProductionCatchUpProjectionHost
        {
            AllowApply = true,
            ProjectionStartPosition = c0,
            SafeVersion = 1,
            SafePosition = c0
        };
        var freshGrain = CreateGrain(
            freshStore,
            freshHost,
            processedEvents: Array.Empty<SerializableEvent>(),
            persistentState: unchangedC0Record);
        Assert.Empty(GetPrivateField<HashSet<Guid>>(freshGrain, "_processedEventIds"));
        Assert.Same(syntheticStore.Events, freshStore.Events);
        Assert.Same(syntheticStore.PersistentState, freshStore.PersistentState);
        Assert.Same(c0State, freshStore.PersistentState.State);

        await freshGrain.RefreshAsync();

        Assert.Equal(authoritativeEventIds, freshHost.AppliedEventIds.ToArray());
        Assert.Equal(authoritativeEventIds.Length, freshHost.AppliedEventCount);
        Assert.Equal(syntheticStore.Events[^1].SortableUniqueIdValue, freshHost.LastAppliedPosition);
        Assert.Equal(expectedFreshProjection, freshHost.MaterializedProjection);
        Assert.NotEqual(
            DeterministicMaterializedProjection.From(c0, Array.Empty<Guid>()),
            freshHost.MaterializedProjection);
        Assert.Equal(c0, freshStore.ReadSinceValues[0]);
        Assert.Equal(0, freshStore.PersistentState.WriteCalls);
        Assert.Equal(c0, freshStore.PersistentState.State.LastSortableUniqueId);
    }

    [Fact]
    public async Task Enumerable_partial_filter_uses_last_fetched_for_the_next_read()
    {
        var store = new ProductionCatchUpEventStore([3]);
        var host = new ProductionCatchUpProjectionHost { AllowApply = true };
        var grain = CreateGrain(store, host, processedEvents: store.Events.Skip(2));

        await grain.RefreshAsync();

        Assert.Equal(2, host.AppliedEventCount);
        Assert.Equal(store.Events[1].SortableUniqueIdValue, host.LastAppliedPosition);
        Assert.Equal(store.Events[2].SortableUniqueIdValue, store.ReadSinceValues[1]);
        Assert.Equal(store.Events[2].SortableUniqueIdValue, GetCatchUpCurrentPosition(grain));
    }

    [Fact]
    public async Task Streaming_partial_filter_uses_last_fetched_for_the_next_read()
    {
        var store = new ProductionStreamingCatchUpEventStore([3]);
        var host = new ProductionCatchUpProjectionHost { AllowApply = true };
        var grain = CreateGrain(store, host, processedEvents: store.Events.Skip(2));

        await grain.RefreshAsync();

        Assert.Equal(2, host.AppliedEventCount);
        Assert.Equal(store.Events[1].SortableUniqueIdValue, host.LastAppliedPosition);
        Assert.Equal(store.Events[2].SortableUniqueIdValue, store.ReadSinceValues[1]);
        Assert.Equal(store.Events[2].SortableUniqueIdValue, GetCatchUpCurrentPosition(grain));
    }

    [Fact]
    public async Task Hot_fetched_counter_resets_between_crossings_and_counts_exactly_two_attempts()
    {
        var batchSizes = Enumerable.Repeat(500, 10)
            .Concat([1])
            .Concat(Enumerable.Repeat(500, 10))
            .ToArray();
        var store = new ProductionCatchUpEventStore(batchSizes);
        var host = new ProductionCatchUpProjectionHost();
        var grain = CreateGrain(store, host);

        await grain.RefreshAsync();

        Assert.Equal(26, store.ReadCalls); // 21 non-empty batches, then five empty completion reads.
        Assert.Equal(3, host.StateMetadataCalls);
        Assert.Equal(2, host.SnapshotWriteCalls);
        Assert.Equal(2, store.PersistentState.WriteCalls);
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsProcessedSinceLastCatchUpPersist"));
    }

    [Fact]
    public async Task Hot_time_counter_resets_with_a_non_fire_between_exactly_two_attempts()
    {
        var store = new ProductionCatchUpEventStore();
        var host = new ProductionCatchUpProjectionHost();
        var grain = CreateGrain(store, host);
        var updateMethod = grain.GetType().GetMethod("UpdateCatchUpProgressAfterBatch", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(updateMethod);

        SetPrivateField(grain, "_eventsProcessed", 123L);
        SetPrivateField(grain, "_lastCatchUpPersistUtc", DateTime.UtcNow - TimeSpan.FromMinutes(6));
        await InvokePrivateTaskAsync(
            updateMethod!,
            grain,
            [
                1,
                "beginning",
                Array.Empty<Guid>(),
                SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-3), Guid.Empty),
                null,
                500,
                1,
                1,
                0,
                0L,
                0L,
                0,
                null,
                null,
                "empty"
            ]);

        Assert.Equal(1, store.PersistentState.WriteCalls);
        Assert.Equal(1, host.StateMetadataCalls);
        await InvokePrivateTaskAsync(
            updateMethod!,
            grain,
            [
                2,
                "beginning",
                Array.Empty<Guid>(),
                SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-2), Guid.Empty),
                null,
                500,
                1,
                1,
                0,
                0L,
                0L,
                0,
                null,
                null,
                "empty"
            ]);

        Assert.Equal(1, store.PersistentState.WriteCalls);
        Assert.Equal(1, host.StateMetadataCalls);
        SetPrivateField(grain, "_lastCatchUpPersistUtc", DateTime.UtcNow - TimeSpan.FromMinutes(6));
        await InvokePrivateTaskAsync(
            updateMethod!,
            grain,
            [
                3,
                "beginning",
                Array.Empty<Guid>(),
                SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), Guid.Empty),
                null,
                500,
                1,
                1,
                0,
                0L,
                0L,
                0,
                null,
                null,
                "empty"
            ]);

        Assert.Equal(2, store.PersistentState.WriteCalls);
        Assert.Equal(2, host.StateMetadataCalls);
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsProcessedSinceLastCatchUpPersist"));
    }

    [Fact]
    public async Task Persist_attempt_resets_all_window_fields_and_reports_no_durable_write_for_failed_or_short_circuited_result()
    {
        var failedHost = new ProductionCatchUpProjectionHost { FailSnapshotWrite = true };
        var failedStore = new ProductionCatchUpEventStore();
        var failedGrain = CreateGrain(failedStore, failedHost);
        SetPrivateField(failedGrain, "_eventsProcessed", 123L);
        var failedWindowTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        SetPrivateField(failedGrain, "_lastCatchUpPersistUtc", failedWindowTime);
        await InvokeProgressUpdateAsync(failedGrain, 1, "failed-checkpoint");

        Assert.Equal(0L, GetPrivateField<long>(failedGrain, "_eventsProcessedSinceLastCatchUpPersist"));
        Assert.Equal(0L, GetPrivateField<long>(failedGrain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.True(GetPrivateField<DateTime>(failedGrain, "_lastCatchUpPersistUtc") > failedWindowTime);
        Assert.Equal("no_durable_write", GetPrivateField<string>(failedGrain, "_lastPersistOutcome"));

        var shortCircuitState = new MultiProjectionGrainState
        {
            ProjectorVersion = "v1",
            LastSortableUniqueId = null,
            LastGoodSafeVersion = 1
        };
        var shortCircuitHost = new ProductionCatchUpProjectionHost { SafeVersion = 1 };
        var shortCircuitStore = new ProductionCatchUpEventStore();
        var shortCircuitGrain = CreateGrain(shortCircuitStore, shortCircuitHost, shortCircuitState);
        SetPrivateField(shortCircuitGrain, "_eventsProcessed", 123L);
        var shortCircuitWindowTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        SetPrivateField(shortCircuitGrain, "_lastCatchUpPersistUtc", shortCircuitWindowTime);
        await InvokeProgressUpdateAsync(shortCircuitGrain, 1, "short-circuit-checkpoint");

        Assert.Equal(0, shortCircuitStore.PersistentState.WriteCalls);
        Assert.Equal(0L, GetPrivateField<long>(shortCircuitGrain, "_eventsProcessedSinceLastCatchUpPersist"));
        Assert.Equal(0L, GetPrivateField<long>(shortCircuitGrain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.True(GetPrivateField<DateTime>(shortCircuitGrain, "_lastCatchUpPersistUtc") > shortCircuitWindowTime);
        Assert.Equal("no_durable_write", GetPrivateField<string>(shortCircuitGrain, "_lastPersistOutcome"));
    }

    [Fact]
    public async Task Unchanged_checkpoint_guard_separates_one_attempt_from_durable_writes_when_enabled_or_disabled()
    {
        var unchangedState = new MultiProjectionGrainState
        {
            ProjectorVersion = "v1",
            LastSortableUniqueId = null,
            LastGoodSafeVersion = 1
        };

        var guardedHost = new ProductionCatchUpProjectionHost { SafeVersion = 1 };
        var guardedStore = new ProductionCatchUpEventStore();
        var guardedGrain = CreateGrain(guardedStore, guardedHost, unchangedState);
        await InvokeProgressUpdateAsync(guardedGrain, 1, "guarded-checkpoint");

        Assert.Equal(1, guardedHost.StateMetadataCalls); // exactly one persist attempt
        Assert.Equal(0, guardedHost.SnapshotWriteCalls);
        Assert.Equal(0, guardedStore.PersistentState.WriteCalls);
        Assert.Equal("no_durable_write", GetPrivateField<string>(guardedGrain, "_lastPersistOutcome"));

        var unguardedHost = new ProductionCatchUpProjectionHost { SafeVersion = 1 };
        var unguardedStore = new ProductionCatchUpEventStore();
        var unguardedGrain = CreateGrain(
            unguardedStore,
            unguardedHost,
            unchangedState,
            new GeneralMultiProjectionActorOptions
            {
                PersistIntervalSeconds = 0,
                SkipPersistWhenSafeCheckpointUnchanged = false
            });
        await InvokeProgressUpdateAsync(unguardedGrain, 1, "unguarded-checkpoint");

        Assert.Equal(1, unguardedHost.StateMetadataCalls); // one attempt, independently counted from writes
        Assert.Equal(1, unguardedHost.SnapshotWriteCalls);
        Assert.Equal(1, unguardedStore.PersistentState.WriteCalls);
        Assert.Equal("durable_write", GetPrivateField<string>(unguardedGrain, "_lastPersistOutcome"));
    }

    [Fact]
    public async Task Cold_fetched_counter_resets_between_crossings_and_counts_exactly_two_attempts()
    {
        var store = new ProductionCatchUpEventStore();
        var host = new ProductionCatchUpProjectionHost();
        var grain = CreateGrain(store, host);
        var hybrid = CreateHybrid(maxEvents: 100);
        var coldMetadata = new HybridReadBatchMetadata("cold", UsedCold: true, UsedHot: false, false, 100, 0, 1);

        await InvokeProgressUpdateAsync(
            grain,
            1,
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-3), Guid.Empty),
            fetchedCount: 100,
            hybrid,
            coldMetadata);
        await InvokeProgressUpdateAsync(
            grain,
            2,
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-2), Guid.Empty),
            fetchedCount: 99,
            hybrid,
            coldMetadata);
        Assert.Equal(1, store.PersistentState.WriteCalls);
        Assert.Equal(1, host.StateMetadataCalls);

        await InvokeProgressUpdateAsync(
            grain,
            3,
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), Guid.Empty),
            fetchedCount: 1,
            hybrid,
            coldMetadata);

        Assert.Equal(2, store.PersistentState.WriteCalls);
        Assert.Equal(2, host.StateMetadataCalls);
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
    }

    [Fact]
    public async Task Window_transitions_reset_preserve_or_leave_unchanged_as_defined()
    {
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRunStore = new ProductionCatchUpEventStore(
            firstReadStarted: firstReadStarted,
            releaseFirstRead: releaseFirstRead);
        var newRunGrain = CreateGrain(newRunStore);
        var staleWindowTime = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        SeedWindow(newRunGrain, staleWindowTime, usedCold: true);

        var newRunTask = InvokePrivateTaskAsync(
            newRunGrain,
            "RefreshWithAuthoritativeCursorAsync",
            [false]);
        await firstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        AssertWindowReset(newRunGrain, staleWindowTime);
        releaseFirstRead.TrySetResult();
        await newRunTask;

        var inheritedReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInheritedRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inheritedStore = new ProductionCatchUpEventStore(
            firstReadStarted: inheritedReadStarted,
            releaseFirstRead: releaseInheritedRead);
        var inheritedGrain = CreateGrain(inheritedStore);
        var inheritedTime = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        var inheritedPosition = new SortableUniqueId(SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-3), Guid.Empty));
        SetActiveCatchUp(
            inheritedGrain,
            new CatchUpStartPositionLease(inheritedPosition, CatchUpStartPositionSource.RestoredCheckpoint));
        SeedWindow(inheritedGrain, inheritedTime, usedCold: true);

        var inheritedTask = InvokePrivateTaskAsync(
            inheritedGrain,
            "RefreshWithAuthoritativeCursorAsync",
            [true]);
        await inheritedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        AssertWindowPreserved(inheritedGrain, inheritedTime, usedCold: true);
        releaseInheritedRead.TrySetResult();
        await inheritedTask;

        var earlyReturnGrain = CreateGrain(new ProductionCatchUpEventStore());
        var earlyReturnTime = DateTime.UtcNow - TimeSpan.FromMinutes(3);
        SetActiveCatchUp(
            earlyReturnGrain,
            new CatchUpStartPositionLease(inheritedPosition, CatchUpStartPositionSource.RestoredCheckpoint));
        SeedWindow(earlyReturnGrain, earlyReturnTime, usedCold: true);

        await InvokePrivateTaskAsync(
            earlyReturnGrain,
            "RefreshWithAuthoritativeCursorAsync",
            [false]);

        AssertWindowPreserved(earlyReturnGrain, earlyReturnTime, usedCold: true);

        var completedGrain = CreateGrain(new ProductionCatchUpEventStore());
        var completedTime = DateTime.UtcNow - TimeSpan.FromMinutes(4);
        SetActiveCatchUp(
            completedGrain,
            new CatchUpStartPositionLease(inheritedPosition, CatchUpStartPositionSource.RestoredCheckpoint));
        SeedWindow(completedGrain, completedTime, usedCold: true);

        await InvokePrivateTaskAsync(completedGrain, "CompleteCatchUp");

        AssertWindowReset(completedGrain, completedTime);
    }

    private static async Task InvokeProgressUpdateAsync(
        MultiProjectionGrain grain,
        int batchNumber,
        string position,
        int fetchedCount = 5_000,
        HybridEventStore? hybridCatchUpStore = null,
        HybridReadBatchMetadata? hybridReadBatchMetadata = null)
    {
        var updateMethod = grain.GetType().GetMethod("UpdateCatchUpProgressAfterBatch", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(updateMethod);
        await InvokePrivateTaskAsync(
            updateMethod!,
            grain,
            [
                batchNumber,
                "beginning",
                Array.Empty<Guid>(),
                position,
                null,
                500,
                fetchedCount,
                fetchedCount,
                0,
                0L,
                0L,
                0,
                hybridCatchUpStore,
                hybridReadBatchMetadata,
                "empty"
            ]);
    }

    private static HybridEventStore CreateHybrid(int maxEvents) =>
        new(
            new ProductionCatchUpEventStore(),
            new EmptyColdObjectStorage(),
            new JsonlColdSegmentFormatHandler(),
            new DefaultServiceIdProvider(),
            Options.Create(new ColdEventStoreOptions
            {
                Enabled = true,
                CatchUpPersistMaxEventsWithoutSnapshot = maxEvents,
                CatchUpPersistMaxInterval = TimeSpan.FromHours(1)
            }),
            NullLogger<HybridEventStore>.Instance);

    private static MultiProjectionGrain CreateGrain(
        ProductionCatchUpEventStore store,
        ProductionCatchUpProjectionHost? host = null,
        MultiProjectionGrainState? state = null,
        GeneralMultiProjectionActorOptions? actorOptions = null,
        IEnumerable<SerializableEvent>? processedEvents = null,
        RecordingPersistentState<MultiProjectionGrainState>? persistentState = null)
    {
        host ??= new ProductionCatchUpProjectionHost();
        persistentState ??= new RecordingPersistentState<MultiProjectionGrainState>
        {
            State = state ?? new MultiProjectionGrainState()
        };
        store.PersistentState = persistentState;
        var grain = new MultiProjectionGrain(
            persistentState,
            new ProductionCatchUpProjectionHostFactory(host),
            store,
            new DefaultOrleansEventSubscriptionResolver(),
            multiProjectionStateStore: null,
            eventStats: null,
            actorOptions: actorOptions ?? new GeneralMultiProjectionActorOptions
            {
                PersistIntervalSeconds = 0,
                SkipPersistWhenSafeCheckpointUnchanged = true
            },
            tempFileSnapshotManager: null,
            logger: NullLogger<MultiProjectionGrain>.Instance,
            eventStoreFactory: null,
            serviceIdProvider: new DefaultServiceIdProvider());

        SetPrivateField(grain, "_isInitialized", true);
        SetPrivateField(grain, "_host", host);
        SetPrivateField(grain, "_grainKey", "production-catch-up");
        SetPrivateField(grain, "_projectorName", "production-catch-up");
        SetPrivateField(grain, "_serviceId", DefaultServiceIdProvider.DefaultServiceId);
        SetPrivateField(grain, "_eventsProcessed", 123L); // Irregular residue: old modulo must not fire at batch ten.

        var processedIds = GetPrivateField<HashSet<Guid>>(grain, "_processedEventIds");
        foreach (var ev in processedEvents ?? store.Events)
        {
            processedIds.Add(ev.Id);
        }

        return grain;
    }

    private static string? GetCatchUpCurrentPosition(MultiProjectionGrain grain)
    {
        var progress = GetPrivateField<object>(grain, "_catchUpProgress");
        var current = progress.GetType().GetProperty("CurrentPosition", BindingFlags.Instance | BindingFlags.Public)!.GetValue(progress);
        return current?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)!.GetValue(current) as string;
    }

    private static T GetPrivateField<T>(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<T>(field!.GetValue(target));
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static async Task InvokePrivateTaskAsync(MethodInfo method, object target, object?[] args)
    {
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(target, args));
        await task;
    }

    private static Task InvokePrivateTaskAsync(
        MultiProjectionGrain target,
        string methodName,
        object?[]? args = null)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method!.Invoke(target, args));
    }

    private static void SeedWindow(MultiProjectionGrain grain, DateTime lastPersistUtc, bool usedCold)
    {
        SetPrivateField(grain, "_eventsProcessedSinceLastCatchUpPersist", 7L);
        SetPrivateField(grain, "_eventsFetchedSinceLastCatchUpPersist", 8L);
        SetPrivateField(grain, "_lastCatchUpPersistUtc", lastPersistUtc);
        SetPrivateField(grain, "_lastCatchUpUsedCold", usedCold);
    }

    private static void SetActiveCatchUp(
        MultiProjectionGrain grain,
        CatchUpStartPositionLease? startLease)
    {
        var progressType = grain.GetType().GetNestedType("CatchUpProgress", BindingFlags.NonPublic);
        Assert.NotNull(progressType);
        var progress = Activator.CreateInstance(progressType!);
        Assert.NotNull(progress);
        SetPrivateProperty(progress!, "StartLease", startLease);
        SetPrivateProperty(progress!, "IsActive", startLease is not null);
        SetPrivateProperty(progress!, "HadNewEvents", false);
        SetPrivateProperty(progress!, "StartTime", DateTime.UtcNow);
        SetPrivateProperty(progress!, "LastAttempt", DateTime.UtcNow);
        SetPrivateField(grain, "_catchUpProgress", progress);
        if (startLease is not null)
        {
            SetPrivateField(grain, "_catchUpTimer", new NoOpDisposable());
        }
    }

    private static void SetPrivateProperty(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private static void AssertWindowReset(MultiProjectionGrain grain, DateTime before)
    {
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsProcessedSinceLastCatchUpPersist"));
        Assert.Equal(0L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.True(GetPrivateField<DateTime>(grain, "_lastCatchUpPersistUtc") > before);
        Assert.Null(GetPrivateFieldValue(grain, "_lastCatchUpUsedCold"));
    }

    private static void AssertWindowPreserved(MultiProjectionGrain grain, DateTime expectedTime, bool usedCold)
    {
        Assert.Equal(7L, GetPrivateField<long>(grain, "_eventsProcessedSinceLastCatchUpPersist"));
        Assert.Equal(8L, GetPrivateField<long>(grain, "_eventsFetchedSinceLastCatchUpPersist"));
        Assert.Equal(expectedTime, GetPrivateField<DateTime>(grain, "_lastCatchUpPersistUtc"));
        Assert.Equal(usedCold, GetPrivateField<bool>(grain, "_lastCatchUpUsedCold"));
    }

    private static object? GetPrivateFieldValue(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }

    private sealed class RecordingPersistentState<T> : IPersistentState<T> where T : new()
    {
        public T State { get; set; } = new();
        public string Etag => "test-etag";
        public bool RecordExists => true;
        public int WriteCalls { get; private set; }

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

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class ProductionCatchUpProjectionHostFactory(ProductionCatchUpProjectionHost host) : IProjectionActorHostFactory
    {
        public IProjectionActorHost Create(
            string projectorName,
            GeneralMultiProjectionActorOptions? options = null,
            Microsoft.Extensions.Logging.ILogger? logger = null) => host;
    }

    private sealed record DeterministicMaterializedProjection(
        string StartPosition,
        int EventCount,
        string OrderedEventIdDigest)
    {
        public static DeterministicMaterializedProjection From(string startPosition, IEnumerable<Guid> eventIds)
        {
            var ids = eventIds.ToArray();
            var digestInput = string.Join(
                "|",
                new[] { startPosition }.Concat(ids.Select(id => id.ToString("D"))));
            var digest = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(digestInput)));
            return new DeterministicMaterializedProjection(startPosition, ids.Length, digest);
        }
    }

    private sealed class ProductionCatchUpProjectionHost : IProjectionActorHost
    {
        public bool FailSnapshotWrite { get; init; }
        public bool AllowApply { get; init; }
        public string? ProjectionStartPosition { get; init; }
        public int SafeVersion { get; init; }
        public string? SafePosition { get; init; }
        public int StateMetadataCalls { get; private set; }
        public int SafeCheckpointCalls { get; private set; }
        public int SnapshotWriteCalls { get; private set; }
        public IReadOnlyList<Guid> AppliedEventIds => _appliedEventIds;
        public int AppliedEventCount => _appliedEventIds.Count;
        public string? LastAppliedPosition { get; private set; }
        public DeterministicMaterializedProjection MaterializedProjection =>
            DeterministicMaterializedProjection.From(ProjectionStartPosition ?? string.Empty, _appliedEventIds);

        private readonly List<Guid> _appliedEventIds = [];

        public Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true)
        {
            if (!AllowApply)
            {
                throw new Xunit.Sdk.XunitException("The zero-applied production test unexpectedly applied an event.");
            }

            _appliedEventIds.AddRange(events.Select(ev => ev.Id));
            LastAppliedPosition = events[^1].SortableUniqueIdValue;
            return Task.CompletedTask;
        }

        public Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true)
        {
            StateMetadataCalls++;
            return Task.FromResult(ResultBox.FromValue(new ProjectionStateMetadata(
                    "production-catch-up",
                    "v1",
                    IsCatchedUp: true,
                    UnsafeVersion: 0,
                    UnsafeLastSortableUniqueId: null,
                    UnsafeLastEventId: null,
                    SafeVersion: SafeVersion,
                    SafeLastSortableUniqueId: SafePosition)));
        }

        public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true) =>
            Task.FromResult(ResultBox.Error<MultiProjectionState>(new InvalidOperationException("no state payload")));

        public Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync() =>
            throw new NotSupportedException();

        public Task<ResultBox<bool>> WriteSnapshotToStreamAsync(
            Stream target,
            bool canGetUnsafeState,
            CancellationToken cancellationToken) => WriteSnapshotAsync(target);

        public Task<ResultBox<bool>> WriteSnapshotForPersistenceToStreamAsync(
            Stream target,
            bool canGetUnsafeState,
            int offloadThresholdBytes,
            CancellationToken cancellationToken) => WriteSnapshotAsync(target);

        private async Task<ResultBox<bool>> WriteSnapshotAsync(Stream target)
        {
            SnapshotWriteCalls++;
            if (FailSnapshotWrite)
            {
                return ResultBox.Error<bool>(new InvalidOperationException("snapshot write failed"));
            }

            await target.WriteAsync(new byte[] { 1 });
            return ResultBox.FromValue(true);
        }

        public Task<ResultBox<bool>> RestoreSnapshotFromStreamAsync(Stream source, CancellationToken cancellationToken) =>
            Task.FromResult(ResultBox.FromValue(true));

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

        public void ForcePromoteBufferedEvents() { }
        public void CompactSafeHistory() { }
        public void ForcePromoteAllBufferedEvents() { }
        public Task<string> GetSafeLastSortableUniqueIdAsync()
        {
            SafeCheckpointCalls++;
            return Task.FromResult(SafePosition ?? string.Empty);
        }
        public Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId) => Task.FromResult(false);
        public long EstimateStateSizeBytes(bool includeUnsafeDetails) => 1;
        public string PeekCurrentSafeWindowThreshold() => SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty);
        public string GetProjectorVersion() => "v1";

        public Task<ResultBox<bool>> RewriteSnapshotVersionAsync(
            Stream source,
            Stream target,
            string newVersion,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private class ProductionCatchUpEventStore : IEventStore
    {
        private int _readCalls;
        protected readonly List<string?> ReadSinceValuesStorage = new();

        private readonly IReadOnlyList<int> _batchSizes;
        private readonly TaskCompletionSource? _firstReadStarted;
        private readonly TaskCompletionSource? _releaseFirstRead;
        private int _firstReadBlocked;

        public ProductionCatchUpEventStore(
            IReadOnlyList<int>? batchSizes = null,
            TaskCompletionSource? firstReadStarted = null,
            TaskCompletionSource? releaseFirstRead = null,
            IReadOnlyList<SerializableEvent>? events = null,
            RecordingPersistentState<MultiProjectionGrainState>? persistentState = null)
        {
            _batchSizes = batchSizes ?? Enumerable.Repeat(500, 10).ToArray();
            _firstReadStarted = firstReadStarted;
            _releaseFirstRead = releaseFirstRead;
            var totalEventCount = _batchSizes.Sum();
            Events = events ?? Enumerable.Range(0, totalEventCount)
                .Select(index => new SerializableEvent(
                    new byte[] { 1 },
                    SortableUniqueId.GetTickString(10_000 + index) + SortableUniqueId.GetIdString(Guid.Empty),
                    Guid.CreateVersion7(),
                    new EventMetadata("aggregate", "command", "test"),
                    new List<string>(),
                    "ProductionCatchUpEvent"))
                .ToArray();
            PersistentState = persistentState ?? new RecordingPersistentState<MultiProjectionGrainState>();
        }

        public IReadOnlyList<SerializableEvent> Events { get; }
        public int ReadCalls => Volatile.Read(ref _readCalls);
        public IReadOnlyList<string?> ReadSinceValues => ReadSinceValuesStorage;
        public RecordingPersistentState<MultiProjectionGrainState> PersistentState { get; set; }

        protected IReadOnlyList<SerializableEvent> NextBatch()
        {
            var call = Interlocked.Increment(ref _readCalls);
            var batchIndex = call - 1;
            if (batchIndex >= _batchSizes.Count)
            {
                return Array.Empty<SerializableEvent>();
            }

            var offset = _batchSizes.Take(batchIndex).Sum();
            return Events.Skip(offset).Take(_batchSizes[batchIndex]).ToArray();
        }

        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            ResultBox.FromValue<IEnumerable<SerializableEvent>>(await ReadNextBatchAsync(since));

        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) =>
            ResultBox.FromValue<IEnumerable<SerializableEvent>>(await ReadNextBatchAsync(since));

        private async Task<IReadOnlyList<SerializableEvent>> ReadNextBatchAsync(SortableUniqueId? since)
        {
            ReadSinceValuesStorage.Add(since?.Value);
            if (_firstReadStarted is not null && Interlocked.Exchange(ref _firstReadBlocked, 1) == 0)
            {
                _firstReadStarted.TrySetResult();
                if (_releaseFirstRead is not null)
                {
                    await _releaseFirstRead.Task;
                }
            }
            return NextBatch();
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => throw new NotSupportedException();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => throw new NotSupportedException();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => throw new NotSupportedException();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => throw new NotSupportedException();

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
            IEnumerable<SerializableEvent> events) => throw new NotSupportedException();

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();
    }

    private sealed class ProductionStreamingCatchUpEventStore : ProductionCatchUpEventStore, IStreamingSerializableEventStore
    {
        public ProductionStreamingCatchUpEventStore(IReadOnlyList<int>? batchSizes = null)
            : base(batchSizes)
        {
        }

        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount,
            Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            ReadSinceValuesInternal(since);
            var batch = NextBatch();
            foreach (var ev in batch)
            {
                await onEvent(ev);
            }

            return ResultBox.FromValue(new SerializableEventStreamReadResult(
                batch.Count,
                batch.Count == 0 ? null : batch[^1].SortableUniqueIdValue));
        }

        private void ReadSinceValuesInternal(SortableUniqueId? since) => ReadSinceValuesStorage.Add(since?.Value);

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
