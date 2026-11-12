using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Orleans;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public sealed partial class MultiProjectionGrainCatchUpProductionPathTests
{
    [Theory]
    [InlineData("timer")]
    [InlineData("completion")]
    [InlineData("persist")]
    [InlineData("pending")]
    public async Task Promotion_signal_rebuilds_in_same_turn_without_snapshot_or_completion_pulse(string path)
    {
        var log = new CatchUpCompletionLogger();
        var host = new ProductionCatchUpProjectionHost
        {
            AllowApply = true, FailInitialStateRead = false,
            SignalOnPromotion = path is "timer" or "completion",
            SignalOnPromotionCall = path == "completion" ? 3 : 0,
            SignalOnForcePromotion = path == "persist",
            RebuildRequired = path == "pending"
        };
        var store = new ProductionCatchUpEventStore([0]);
        var grain = CreateGrain(store, host, checkpointStore: new InMemoryMultiProjectionStateStore(), logger: log);
        AttachDeactivationRuntime(grain);
        if (path == "timer") await InvokePrivateTaskAsync(grain, "FlushEventBufferAsync", [false, false]);
        else if (path == "completion") await grain.RefreshAsync();
        else await grain.PersistStateAsync();

        Assert.Equal(0, host.SnapshotWriteCalls);
        Assert.DoesNotContain(host.AddCalls, call => call.FinishedCatchUp);
        var replacement = GetPrivateField<IProjectionActorHost>(grain, "_host");
        Assert.NotSame(host, replacement);
        Assert.Empty(Assert.IsType<ProductionCatchUpProjectionHost>(replacement).AddCalls);
        Assert.True(store.PersistentState.State.RebuildRequired);
        Assert.True(GetPrivateField<FirstQueryCatchUpGate>(grain, "_firstQueryGate").IsPending);
        var entry = Assert.Single(log.Rebuilds);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(1031, entry.EventId.Id);
        Assert.Equal(host.RebuildOffendingEventId, entry.Fields["OffendingEventId"]);
        Assert.Equal(host.RebuildOffendingPosition, entry.Fields["OffendingPosition"]);
        Assert.Equal(host.RebuildReason, entry.Fields["Reason"]);
        Assert.Equal(host.RebuildSafeHeadPosition, entry.Fields["SafeHeadPosition"]);
        Assert.Equal(host.RebuildSafeHeadEventId, entry.Fields["SafeHeadEventId"]);
        Assert.Equal(host.RebuildSignaledAtUtc, entry.Fields["SignalTimeUtc"]);
        Assert.Equal(123L, entry.Fields["EventsProcessed"]);
        Assert.Equal(path == "timer" ? "timer/empty-flush" : path == "completion" ? "catch-up completion" : "persist force-promotion",
            entry.Fields["ObservingPath"]);
    }

    [Theory]
    [InlineData("capable")]
    [InlineData("none")]
    [InlineData("non-capable")]
    public async Task Deactivation_pending_signal_saves_marker_or_fault_without_snapshot(string capability)
    {
        var host = new ProductionCatchUpProjectionHost { RebuildRequired = true };
        var log = new CatchUpCompletionLogger();
        IMultiProjectionStateStore? checkpointStore = capability switch
        {
            "capable" => new InMemoryMultiProjectionStateStore(),
            "non-capable" => new CheckpointNonCapableFailClosedTests.NonCapableStore(),
            _ => null
        };
        var store = new ProductionCatchUpEventStore();
        var grain = CreateGrain(store, host, checkpointStore: checkpointStore, logger: log);
        AttachDeactivationRuntime(grain);
        await grain.OnDeactivateAsync(default, CancellationToken.None);
        Assert.Equal(0, host.SnapshotWriteCalls);
        Assert.Same(host, GetPrivateField<IProjectionActorHost>(grain, "_host"));
        if (capability == "non-capable")
        {
            Assert.False(store.PersistentState.State.RebuildRequired);
            Assert.Contains("SEK-G20", store.PersistentState.State.FaultMessage);
            Assert.Equal(host.RebuildOffendingEventId, store.PersistentState.State.FaultEventId);
        }
        else
        {
            Assert.True(store.PersistentState.State.RebuildRequired);
            Assert.Equal(host.RebuildOffendingEventId, store.PersistentState.State.RebuildOffendingEventId);
            Assert.Equal(host.RebuildOffendingPosition, store.PersistentState.State.RebuildOffendingPosition);
        }
        Assert.Single(log.Rebuilds);
    }

    [Fact]
    public async Task Deactivation_pending_signal_preserves_existing_fault()
    {
        var state = new MultiProjectionGrainState { FaultMessage = "existing fault", FaultEventId = Guid.NewGuid().ToString() };
        var host = new ProductionCatchUpProjectionHost { RebuildRequired = true };
        var store = new ProductionCatchUpEventStore();
        var grain = CreateGrain(store, host, state,
            checkpointStore: new CheckpointNonCapableFailClosedTests.NonCapableStore());
        AttachDeactivationRuntime(grain);
        await grain.OnDeactivateAsync(default, CancellationToken.None);
        Assert.Equal("existing fault", store.PersistentState.State.FaultMessage);
        Assert.Equal(state.FaultEventId, store.PersistentState.State.FaultEventId);
        Assert.False(store.PersistentState.State.RebuildRequired);
        Assert.Equal(0, host.SnapshotWriteCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Failed_streaming_read_records_applied_chunk_ids_and_counts_once_before_retry(bool throws, bool restartInvocation)
    {
        var store = new ChunkFailureEventStore(throws);
        var host = new ProductionCatchUpProjectionHost
        {
            AllowApply = true, FailInitialStateRead = false, SafeVersion = 4096,
            SafePosition = store.Events[^1].SortableUniqueIdValue
        };
        var log = new CatchUpCompletionLogger();
        var grain = CreateGrain(store, host, processedEvents: [], logger: log,
            actorOptions: new GeneralMultiProjectionActorOptions { SkipPersistWhenSafeCheckpointUnchanged = false });
        SetPrivateField(grain, "_eventsProcessed", 0L);
        var progress = GetPrivateField<object>(grain, "_catchUpProgress");
        progress.GetType().GetProperty("IsActive")!.SetValue(progress, true);
        progress.GetType().GetProperty("StartTime")!.SetValue(progress, DateTime.UtcNow);
        if (throws)
            await Assert.ThrowsAsync<IOException>(() => InvokePrivateTaskAsync(grain, "ProcessSingleCatchUpBatch", []));
        else
            await InvokePrivateTaskAsync(grain, "ProcessSingleCatchUpBatch", []);
        Assert.Null(GetCatchUpCurrentPosition(grain));
        Assert.Equal(4096, GetPrivateField<HashSet<Guid>>(grain, "_processedEventIds").Count);
        Assert.Equal(4096L, GetPrivateField<long>(grain, "_eventsProcessed"));
        Assert.Equal(4096L, GetPrivateField<long>(grain, "_eventsProcessedSinceLastCatchUpPersist"));
        Assert.True((bool)progress.GetType().GetProperty("HadNewEvents")!.GetValue(progress)!);
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        var snapshotsBeforeCompletion = host.SnapshotWriteCalls;
        if (restartInvocation)
        {
            // A bounded invocation may release active progress after a failed provider read.
            progress.GetType().GetProperty("IsActive")!.SetValue(progress, false);
            await grain.RefreshAsync();
        }
        else await InvokePrivateTaskAsync(grain, "ProcessSingleCatchUpBatch", []);
        Assert.Equal(4096, host.AppliedEventCount);
        Assert.Equal(4096, host.AppliedEventIds.Distinct().Count());
        Assert.Equal(4096L, GetPrivateField<long>(grain, "_eventsProcessed"));
        if (!restartInvocation) await InvokePrivateTaskAsync(grain, "CompleteCatchUp", []);
        Assert.True(host.SnapshotWriteCalls > snapshotsBeforeCompletion);
        Assert.Equal(new[] { "catch_up_complete" }, log.Reasons);
        Assert.Empty(log.Rebuilds);
    }

    [Fact]
    public async Task Stream_buffer_then_already_safe_catch_up_arrival_persists_without_runtime_rebuild()
    {
        ForceFullResumeClusterTests.Env.Reset();
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(ForceFullResumeClusterTests.Env.Domain).AddSekibanDcbNativeRuntime().BuildServiceProvider();
        var options = new GeneralMultiProjectionActorOptions { SafeWindowMs = 20_000 };
        var host = services.GetRequiredService<IProjectionActorHostFactory>().Create(
            ForceFullResumeClusterTests.CountProjector.MultiProjectorName, options);
        var now = DateTime.UtcNow;
        var events = new[] { now.AddMinutes(-1), now.AddSeconds(-2), now.AddSeconds(-1) }
            .Select((time, i) => new SerializableEvent(
                System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new ForceFullResumeClusterTests.Counted($"e{i}"))),
                SortableUniqueId.Generate(time, Guid.NewGuid()), Guid.NewGuid(),
                new EventMetadata("aggregate", "command", "test"), [], "Counted")).ToArray();
        var log = new CatchUpCompletionLogger();
        var store = new CursorAwareProductionCatchUpEventStore(events);
        var grain = CreateGrain(store, host, processedEvents: [], logger: log);
        SetPrivateField(grain, "_eventsProcessed", 0L);
        await grain.ProcessEventBatch([events[0]]);
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        await grain.ProcessEventBatch([events[1]]);
        // Advance the safe threshold deterministically, as if catch-up ran after the window expired.
        options.SafeWindowMs = 0;
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        SetPrivateField(grain, "_subscriptionResolver", new InProcessSubscriptionResolver());
        // The completed replay is the subject; park the query's optional next background poll.
        SetActiveCatchUp(grain, new CatchUpStartPositionLease(null, CatchUpStartPositionSource.InferredCheckpoint));
        SetPrivateField(grain, "_catchUpTimer", new NoOpDisposable());
        var state = (await grain.GetStateAsync(canGetUnsafeState: false)).GetValue();
        Assert.Equal(3, ((ForceFullResumeClusterTests.CountProjector)state.Payload).Count);
        Assert.Equal(3L, GetPrivateField<long>(grain, "_eventsProcessed"));
        Assert.Empty(log.Rebuilds);
        Assert.False(store.PersistentState.State.RebuildRequired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task First_query_completion_rebuild_never_answers_or_pulses_replacement_before_its_replay(int waitMs)
    {
        var ev = CreatePendingEvent(DateTime.UtcNow.AddMinutes(-1).Ticks);
        var store = new CursorAwareProductionCatchUpEventStore([ev]);
        var host = new ProductionCatchUpProjectionHost
        {
            AllowApply = true, FailInitialStateRead = false, SignalOnPromotion = true, SignalOnPromotionCall = 3
        };
        var grain = CreateGrain(store, host, processedEvents: []);
        AttachDeactivationRuntime(grain);
        SetPrivateField(grain, "_firstQueryCatchUpMaxWaitMs", waitMs);
        var gate = GetPrivateField<FirstQueryCatchUpGate>(grain, "_firstQueryGate");
        gate.Arm();
        var replacementRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        using var hook = CatchUpProductionTestHooks.Register(
            Sekiban.Dcb.ServiceId.DefaultServiceIdProvider.DefaultServiceId, "production-catch-up",
            async (point, _) =>
            {
                if (point == CatchUpProductionHookPoint.HostRecreated && waitMs > 0)
                {
                    // A parked replacement timer owns progress until the test resumes its replay below.
                    var progress = GetPrivateField<object>(grain, "_catchUpProgress");
                    progress.GetType().GetProperty("IsActive")!.SetValue(progress, true);
                    SetPrivateField(grain, "_catchUpTimer", new NoOpDisposable());
                }
                if (point == CatchUpProductionHookPoint.InvocationBeforeRead && ++invocations == 2)
                {
                    replacementRead.TrySetResult();
                    await release.Task;
                }
            });
        var query = InvokePrivateTaskAsync(grain, "AwaitFirstQueryCatchUpAsync", []);
        Task? background = null;
        try
        {
            if (waitMs > 0)
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => query);
                Assert.Contains(MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix, error.Message);
                background = InvokePrivateTaskAsync(grain, "EnsureFirstQuerySyncCatchUpAsync", []);
            }
            await replacementRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(gate.IsPending);
            Assert.Equal(0, host.SnapshotWriteCalls);
            Assert.DoesNotContain(host.AddCalls, call => call.FinishedCatchUp);
            var replacement = Assert.IsType<ProductionCatchUpProjectionHost>(GetPrivateField<IProjectionActorHost>(grain, "_host"));
            Assert.Empty(replacement.AddCalls);
            Assert.NotSame(host, replacement);
            if (waitMs == 0) Assert.False(query.IsCompleted);
            release.TrySetResult();
            await (background ?? query).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(gate.IsPending);
            Assert.Equal(new[] { ev.Id }, replacement.AppliedEventIds);
            Assert.Contains(replacement.AddCalls, call => call is { EventCount: 0, FinishedCatchUp: true });
        }
        finally { release.TrySetResult(); }
    }

    private sealed class InProcessSubscriptionResolver : IEventSubscriptionResolver
    {
        public ISekibanStream Resolve(string projectorName) => new InMemorySekibanStream();
    }

    private static void AttachDeactivationRuntime(MultiProjectionGrain grain) =>
        typeof(Grain).GetField("<Runtime>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(grain, DispatchProxy.Create<IGrainRuntime, DeactivationOnlyRuntime>());

    private sealed class ChunkFailureEventStore(bool throws) : ProductionCatchUpEventStore([4096]), IStreamingSerializableEventStore
    {
        private int _attempt;
        public async Task<ResultBox<SerializableEventStreamReadResult>> StreamAllSerializableEventsAsync(
            SortableUniqueId? since, int? maxCount, Func<SerializableEvent, ValueTask> onEvent,
            CancellationToken cancellationToken = default)
        {
            _attempt++;
            var events = Events.Where(ev => since is null || string.CompareOrdinal(ev.SortableUniqueIdValue, since.Value) > 0).ToArray();
            foreach (var ev in events) await onEvent(ev);
            if (_attempt == 1)
            {
                if (throws) throw new IOException("read failed after applied chunk");
                return ResultBox.Error<SerializableEventStreamReadResult>(new IOException("read failed after applied chunk"));
            }
            return ResultBox.FromValue(new SerializableEventStreamReadResult(events.Length, events.LastOrDefault()?.SortableUniqueIdValue));
        }
    }
}
