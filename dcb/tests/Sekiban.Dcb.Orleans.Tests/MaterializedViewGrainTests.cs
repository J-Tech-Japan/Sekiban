using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Streams;
using Orleans.TestingHost;
using Orleans.Runtime;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Orleans;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class MaterializedViewGrainTests : IAsyncLifetime
{
    private static readonly FakeMvRegistryStore SharedRegistry = new();
    private static readonly FakeMvExecutor SharedExecutor = new(SharedRegistry);
    private static readonly TestProjectionStatusStore SharedStatusStore = new();

    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        SharedRegistry.Reset();
        SharedExecutor.Reset();
        SharedStatusStore.Reset();
        SharedExecutor.SeedInitial(CreateSerializableEvent(1, DateTime.UtcNow.AddSeconds(-5)));

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Options.ClusterId = $"mv-grain-{Guid.NewGuid():N}";
        builder.Options.ServiceId = $"mv-grain-{Guid.NewGuid():N}";
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public void MaterializedViewGrain_LegacyPublicConstructor_RemainsAvailable()
    {
        var legacy = typeof(MaterializedViewGrain).GetConstructor(
        [
            typeof(IMvApplyHostFactory),
            typeof(IMvExecutor),
            typeof(IMvRegistryStore),
            typeof(IEventSubscriptionResolver),
            typeof(Microsoft.Extensions.Options.IOptions<MvOptions>),
            typeof(Microsoft.Extensions.Logging.ILogger<MaterializedViewGrain>)
        ]);

        Assert.NotNull(legacy);
    }

    [Fact]
    public async Task PassiveG24Read_DoesNotActivateMvGrain_ButDirectStartActivatesAndPublishes()
    {
        var management = _cluster.Client.GetGrain<IManagementGrain>(0);
        var before = await CountMaterializedViewActivationsAsync(management);
        Assert.Equal(0, before);

        var identity = MvProjectionStatusIdentity.Create(TestMaterializedViewProjector.ViewNameConst, 1);
        var serviceIdProvider = new FixedServiceIdProvider("orders");
        var reader = new ProjectionStatusReader(
            SharedStatusStore,
            new InMemoryEventStore(serviceIdProvider),
            serviceIdProvider,
            new ProjectionStatusOptions { SamplingWindow = TimeSpan.Zero });
        var passive = await reader.ReadAsync(new ProjectionStatusReadRequest(
            "orders",
            identity.ProjectorName,
            identity.ProjectorVersion));

        Assert.True(passive.IsSuccess);
        Assert.Empty(passive.GetValue());
        Assert.Equal(0, await CountMaterializedViewActivationsAsync(management));

        var grainKey = MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1);
        var grain = _cluster.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await grain.EnsureStartedAsync();
        await SharedStatusStore.Written.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, await CountMaterializedViewActivationsAsync(management));
        var published = await reader.ReadAsync(new ProjectionStatusReadRequest(
            "orders",
            identity.ProjectorName,
            identity.ProjectorVersion));
        Assert.True(published.IsSuccess);
        var snapshot = Assert.Single(published.GetValue());
        Assert.StartsWith("mv-orleans-", snapshot.ActivationId, StringComparison.Ordinal);
        Assert.Equal("orders", SharedStatusStore.LastHeartbeat?.ServiceId);
    }

    private static async Task<int> CountMaterializedViewActivationsAsync(IManagementGrain management)
    {
        var statistics = await management.GetDetailedGrainStatistics(null!, null!);
        return statistics.Count(statistic =>
            statistic.GrainType.Contains(nameof(MaterializedViewGrain), StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaterializedViewGrain_Should_CatchUp_Then_Process_Stream_Events()
    {
        var grainKey = MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1);
        var grain = _cluster.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await grain.EnsureStartedAsync();

        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            return status.CurrentPosition == SharedExecutor.InitialEvents[0].SortableUniqueIdValue;
        });

        var streamedEvent = CreateSerializableEvent(2, DateTime.UtcNow);
        // Stream delivery is a wake-up hint only. The event must already be durable before the notification;
        // catch-up owns ordered application and the direct stream-DML seam must remain unused.
        SharedExecutor.InitialEvents.Add(streamedEvent);
        var stream = _cluster.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(
                ServiceIdGrainKey.BuildStreamNamespace("AllEvents", "orders"),
                Guid.Empty));
        await stream.OnNextAsync(streamedEvent);

        await WaitUntilAsync(() => grain.IsSortableUniqueIdReceived(streamedEvent.SortableUniqueIdValue));

        var statusAfterStream = await grain.GetStatusAsync();
        Assert.True(statusAfterStream.Started);
        Assert.True(statusAfterStream.SubscriptionActive);
        Assert.Equal(streamedEvent.SortableUniqueIdValue, statusAfterStream.CurrentPosition);
        Assert.Contains(streamedEvent.Id, SharedExecutor.AppliedEventIds);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
        Assert.Contains("orders", SharedExecutor.ServiceIds);
    }

    [Fact]
    public async Task DuplicateAndOverlappingHints_DoNotReapplyTheSameEvent()
    {
        var grainKey = MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1);
        var grain = _cluster.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await grain.EnsureStartedAsync();

        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            return status.CurrentPosition == SharedExecutor.InitialEvents[0].SortableUniqueIdValue;
        });

        var appliedBefore = SharedExecutor.AppliedEventExecutionCount;
        var streamedEvent = CreateSerializableEvent(2, DateTime.UtcNow);
        SharedExecutor.InitialEvents.Add(streamedEvent);
        var stream = GetEventStream();

        await stream.OnNextAsync(streamedEvent);
        await stream.OnNextAsync(streamedEvent);

        var catchUpCallsBeforeHints = SharedExecutor.CatchUpCalls;
        await WaitUntilAsync(() => grain.IsSortableUniqueIdReceived(streamedEvent.SortableUniqueIdValue));
        await WaitUntilAsync(async () =>
            SharedExecutor.CatchUpCalls > catchUpCallsBeforeHints &&
            !(await grain.GetStatusAsync()).IsCatchUpActive);

        Assert.Equal(appliedBefore + 1, SharedExecutor.AppliedEventExecutionCount);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
    }

    [Fact]
    public async Task FixedAgedDescendingSixtyFourPairs_UseStoreOrderAndDoNotInlineApply()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await SharedRegistry.ActiveStatusRestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var fixedTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var durableEvents = Enumerable.Range(0, 64)
            .SelectMany(pair => new[]
            {
                CreateFixedAgedEvent(pair * 2, fixedTimestamp),
                CreateFixedAgedEvent(pair * 2 + 1, fixedTimestamp)
            })
            .OrderBy(item => item.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToArray();

        SharedExecutor.ExpectAppliedEventCount(durableEvents.Length);
        SharedExecutor.InitialEvents.AddRange(durableEvents);
        var stream = GetEventStream();
        foreach (var item in durableEvents.Reverse())
        {
            await stream.OnNextAsync(item);
        }

        await SharedExecutor.AppliedEventCountReached.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var status = await grain.GetStatusAsync();
        Assert.Equal(durableEvents.Length, SharedExecutor.AppliedEventExecutionCount);
        Assert.Equal(durableEvents[^1].SortableUniqueIdValue, status.CurrentPosition);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
        Assert.Equal(durableEvents.Select(item => item.Id).ToHashSet(), SharedExecutor.AppliedEventIds);
    }

    [Fact]
    public async Task StreamHintModes_VerifyOnlyHasNoWrites_AndVerifyAndExecuteOnlyRecordsReceipt()
    {
        SharedRegistry.Reset();
        SharedExecutor.Reset();
        var streamedEvent = CreateFixedAgedEvent(
            704,
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var key = MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1);

        var verifyOnly = CreateDirectTestGrain(
            key,
            new MvOptions
            {
                AllowDefaultServiceId = true,
                InitializationMode = MvInitializationMode.VerifyOnly
            });
        await verifyOnly.OnStreamBatchAsync([streamedEvent]);

        Assert.Equal(0, SharedRegistry.RegisterCalls);
        Assert.Equal(0, SharedRegistry.MarkStreamReceivedCalls);
        Assert.Equal(0, SharedRegistry.UpdatePositionCalls);
        Assert.Equal(0, SharedRegistry.UpdateStatusCalls);
        Assert.Equal(0, SharedExecutor.InitializeCalls);
        Assert.Equal(0, SharedExecutor.CatchUpCalls);

        var verifyAndExecute = CreateDirectTestGrain(
            key,
            new MvOptions
            {
                AllowDefaultServiceId = true,
                InitializationMode = MvInitializationMode.VerifyAndExecute,
                SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
                SqlStatementPolicy = AllowTestSqlPolicy.Instance
            });
        await verifyAndExecute.OnStreamBatchAsync([streamedEvent]);

        Assert.Equal(1, SharedRegistry.MarkStreamReceivedCalls);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
        Assert.Equal(0, SharedExecutor.CatchUpCalls);
        Assert.Equal(0, SharedRegistry.UpdatePositionCalls);
        Assert.Equal(0, SharedRegistry.UpdateStatusCalls);
    }

    private static MaterializedViewGrain CreateDirectTestGrain(string key, MvOptions options) =>
        new(
            hostFactory: null!,
            executor: SharedExecutor,
            registryStore: SharedRegistry,
            subscriptionResolver: null!,
            options: Options.Create(options),
            logger: NullLogger<MaterializedViewGrain>.Instance,
            testGrainKey: key);

    private sealed class AllowTestSqlPolicy : IMvSqlStatementPolicy
    {
        public static AllowTestSqlPolicy Instance { get; } = new();

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MvSqlPolicyDecision.Allow("test-proof"));
    }

    [Fact]
    public async Task ReceiptBeforeApply_AndCommitBeforeRestart_DoNotReplayNonIdempotentCounter()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await SharedRegistry.ActiveStatusRestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var durableEvent = CreateFixedAgedEvent(
            700,
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        SharedExecutor.InitialEvents.Add(durableEvent);
        SharedExecutor.ExpectAppliedEventCount(1);
        SharedExecutor.BlockNextCatchUp();

        var publish = GetEventStream().OnNextAsync(durableEvent);
        await SharedExecutor.CatchUpEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, SharedExecutor.AppliedEventExecutionCount);

        SharedExecutor.ReleaseBlockedCatchUp();
        await publish;
        await SharedExecutor.AppliedEventCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, SharedExecutor.AppliedEventExecutionCount);

        SharedExecutor.ExpectTargetCaptureCallCount(2);
        await grain.RequestDeactivationAsync();
        var restarted = _cluster.Client.GetGrain<IMaterializedViewGrain>(
            MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1));
        await restarted.EnsureStartedAsync();
        await SharedExecutor.TargetCaptureCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await GetEventStream().OnNextAsync(durableEvent);
        await GetEventStream().OnNextAsync(durableEvent);
        await restarted.RefreshAsync();

        Assert.Equal(1, SharedExecutor.AppliedEventExecutionCount);
        Assert.Equal(durableEvent.SortableUniqueIdValue, (await restarted.GetStatusAsync()).CurrentPosition);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
    }

    [Fact]
    public async Task ReceiptBeforeApply_DeactivationBeforeFirstApply_RestartRecoversExactlyOnce()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await SharedRegistry.ActiveStatusRestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var durableEvent = CreateFixedAgedEvent(
            703,
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        SharedExecutor.InitialEvents.Add(durableEvent);
        SharedExecutor.ExpectAppliedEventCount(1);

        var receiptObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (MaterializedViewGrain.PushAfterStreamReceiptTestHook(candidate =>
               {
                   receiptObserved.TrySetResult();
                   return true;
               }))
        {
            await GetEventStream().OnNextAsync(durableEvent);

            await receiptObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, SharedRegistry.MarkStreamReceivedCalls);
            Assert.Equal(0, SharedExecutor.AppliedEventExecutionCount);

            await grain.RequestDeactivationAsync();
        }

        SharedExecutor.ExpectTargetCaptureCallCount(2);
        var restarted = _cluster.Client.GetGrain<IMaterializedViewGrain>(
            MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1));
        await restarted.EnsureStartedAsync();
        await SharedExecutor.TargetCaptureCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SharedExecutor.AppliedEventCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await GetEventStream().OnNextAsync(durableEvent);
        await restarted.RefreshAsync();

        Assert.Equal(1, SharedExecutor.AppliedEventExecutionCount);
        Assert.Equal(durableEvent.SortableUniqueIdValue, (await restarted.GetStatusAsync()).CurrentPosition);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
    }

    [Fact]
    public async Task IdleDurableProbe_RecoversAgedEventWithoutHint()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await SharedRegistry.ActiveStatusRestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var durableEvent = CreateFixedAgedEvent(
            701,
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        SharedExecutor.InitialEvents.Add(durableEvent);
        SharedExecutor.ExpectAppliedEventCount(1);

        await SharedExecutor.AppliedEventCountReached.Task.WaitAsync(TimeSpan.FromSeconds(12));

        var status = await grain.GetStatusAsync();
        Assert.Equal(1, SharedExecutor.AppliedEventExecutionCount);
        Assert.Equal(durableEvent.SortableUniqueIdValue, status.CurrentPosition);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
    }

    [Fact]
    public async Task DuplicateAndIdleObservations_DoNotRepeatActiveStatusRestore()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await SharedRegistry.ActiveStatusRestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var durableEvent = CreateFixedAgedEvent(
            702,
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        SharedExecutor.InitialEvents.Add(durableEvent);
        SharedExecutor.ExpectAppliedEventCount(1);
        SharedRegistry.ExpectActiveStatusRestoreCallCount(SharedRegistry.ActiveStatusRestoreCalls + 1);
        await GetEventStream().OnNextAsync(durableEvent);
        await SharedExecutor.AppliedEventCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SharedRegistry.ActiveStatusRestoreCallCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);
        var restoreCallsAfterProgress = SharedRegistry.ActiveStatusRestoreCalls;

        SharedExecutor.ExpectCatchUpCallCount(SharedExecutor.CatchUpCalls + 1);
        await GetEventStream().OnNextAsync(durableEvent);
        await GetEventStream().OnNextAsync(durableEvent);
        await SharedExecutor.CatchUpCallCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);

        Assert.Equal(restoreCallsAfterProgress, SharedRegistry.ActiveStatusRestoreCalls);
        Assert.Equal(1, SharedExecutor.AppliedEventExecutionCount);
    }

    [Fact]
    public async Task SettledEpochMutation_TriggersOneGuardedRestore_AndUnchangedIdleDoesNot()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await SharedRegistry.ActiveStatusRestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);
        var restoreCallsAtSettlement = SharedRegistry.ActiveStatusRestoreCalls;

        SharedExecutor.ExpectCatchUpCallCount(SharedExecutor.CatchUpCalls + 1);
        await SharedExecutor.CatchUpCallCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);
        Assert.Equal(restoreCallsAtSettlement, SharedRegistry.ActiveStatusRestoreCalls);

        SharedRegistry.ExpectActiveStatusRestoreCallCount(restoreCallsAtSettlement + 1);
        SharedExecutor.ExpectCatchUpCallCount(SharedExecutor.CatchUpCalls + 1);
        SharedRegistry.MutateActiveGeneration();
        await SharedExecutor.CatchUpCallCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SharedRegistry.ActiveStatusRestoreCallCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);
        Assert.Equal(restoreCallsAtSettlement + 1, SharedRegistry.ActiveStatusRestoreCalls);

        SharedExecutor.ExpectCatchUpCallCount(SharedExecutor.CatchUpCalls + 1);
        await SharedExecutor.CatchUpCallCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);
        Assert.Equal(restoreCallsAtSettlement + 1, SharedRegistry.ActiveStatusRestoreCalls);
        _ = grain;
    }

    [Fact]
    public async Task StreamHintModes_VerifyOnlyHasNoWrites_AndVerifyAndExecuteOnlyRecordsReceipt()
    {
        SharedRegistry.Reset();
        SharedExecutor.Reset();
        var streamedEvent = CreateFixedAgedEvent(
            704,
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var key = MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1);

        var verifyOnly = CreateDirectTestGrain(
            key,
            new MvOptions
            {
                AllowDefaultServiceId = true,
                InitializationMode = MvInitializationMode.VerifyOnly
            });
        await verifyOnly.OnStreamBatchAsync([streamedEvent]);

        Assert.Equal(0, SharedRegistry.RegisterCalls);
        Assert.Equal(0, SharedRegistry.MarkStreamReceivedCalls);
        Assert.Equal(0, SharedRegistry.UpdatePositionCalls);
        Assert.Equal(0, SharedRegistry.UpdateStatusCalls);
        Assert.Equal(0, SharedExecutor.InitializeCalls);
        Assert.Equal(0, SharedExecutor.CatchUpCalls);

        var verifyAndExecute = CreateDirectTestGrain(
            key,
            new MvOptions
            {
                AllowDefaultServiceId = true,
                InitializationMode = MvInitializationMode.VerifyAndExecute,
                SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
                SqlStatementPolicy = AllowTestSqlPolicy.Instance
            });
        await verifyAndExecute.OnStreamBatchAsync([streamedEvent]);

        Assert.Equal(1, SharedRegistry.MarkStreamReceivedCalls);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
        Assert.Equal(0, SharedExecutor.CatchUpCalls);
        Assert.Equal(0, SharedRegistry.UpdatePositionCalls);
        Assert.Equal(0, SharedRegistry.UpdateStatusCalls);
    }

    private static MaterializedViewGrain CreateDirectTestGrain(string key, MvOptions options) =>
        new(
            hostFactory: null!,
            executor: SharedExecutor,
            registryStore: SharedRegistry,
            subscriptionResolver: null!,
            options: Options.Create(options),
            logger: NullLogger<MaterializedViewGrain>.Instance,
            testGrainKey: key);

    private sealed class AllowTestSqlPolicy : IMvSqlStatementPolicy
    {
        public static AllowTestSqlPolicy Instance { get; } = new();

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MvSqlPolicyDecision.Allow("test-proof"));
    }

    [Fact]
    public async Task InFlightHintBurst_UsesSingleDurableCatchUpAndCoalescesHints()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await SharedRegistry.ActiveStatusRestoreCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var fixedTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var durableEvents = new[]
        {
            CreateFixedAgedEvent(710, fixedTimestamp),
            CreateFixedAgedEvent(711, fixedTimestamp)
        };
        SharedExecutor.InitialEvents.AddRange(durableEvents);
        SharedExecutor.ExpectAppliedEventCount(durableEvents.Length);
        SharedExecutor.BlockNextCatchUp();

        var stream = GetEventStream();
        var publishes = durableEvents.Select(item => stream.OnNextAsync(item)).ToArray();
        await SharedExecutor.CatchUpEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, SharedExecutor.MaxConcurrentCatchUpCalls);

        SharedExecutor.ReleaseBlockedCatchUp();
        await Task.WhenAll(publishes);
        await SharedExecutor.AppliedEventCountReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, SharedExecutor.MaxConcurrentCatchUpCalls);
        Assert.Equal(durableEvents.Length, SharedExecutor.AppliedEventExecutionCount);
        Assert.Equal(0, SharedExecutor.ApplySerializableEventsCalls);
        Assert.Equal(durableEvents[^1].SortableUniqueIdValue, (await grain.GetStatusAsync()).CurrentPosition);
    }

    [Fact]
    public async Task MvOrleansQueryAccessor_Should_Return_Runtime_Context()
    {
        await SharedRegistry.RegisterAsync(new MvRegistryEntry
        {
            ServiceId = DefaultServiceIdProvider.DefaultServiceId,
            ViewName = TestMaterializedViewProjector.ViewNameConst,
            ViewVersion = 1,
            LogicalTable = "main",
            PhysicalTable = "test_mv_v1_main",
            Status = MvStatus.Ready,
            CurrentCheckpointTruth = MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp)),
            TargetCheckpointTruth = MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture()),
            LastUpdated = DateTimeOffset.UtcNow
        });
        await SharedRegistry.RegisterAsync(new MvRegistryEntry
        {
            ServiceId = DefaultServiceIdProvider.DefaultServiceId,
            ViewName = TestMaterializedViewProjector.ViewNameConst,
            ViewVersion = 2,
            LogicalTable = "main",
            PhysicalTable = "test_mv_v2_main",
            Status = MvStatus.Ready,
            CurrentCheckpointTruth = MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp)),
            TargetCheckpointTruth = MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture()),
            LastUpdated = DateTimeOffset.UtcNow
        });
        await SharedRegistry.SetActiveAsync(
            DefaultServiceIdProvider.DefaultServiceId,
            TestMaterializedViewProjector.ViewNameConst,
            2);
        var accessor = new MvOrleansQueryAccessor(
            _cluster.Client,
            SharedRegistry,
            new DefaultServiceIdProvider(),
            new MvStorageInfoProvider(new MvStorageInfo(MvDbType.Postgres, "Host=test;Database=mv;")));

        var callerVersionOne = new TestMaterializedViewProjector();
        var context = await accessor.GetAsync(callerVersionOne);
        var table = context.GetRequiredTable("main");

        Assert.Equal(DefaultServiceIdProvider.DefaultServiceId, context.ServiceId);
        Assert.Equal(MvDbType.Postgres, context.DatabaseType);
        Assert.Equal("Host=test;Database=mv;", context.ConnectionString);
        Assert.True(context.Entries.Count > 0);
        Assert.Equal("main", table.LogicalTable);
        Assert.Equal(2, context.ViewVersion);
        Assert.Equal(2, Assert.IsType<MvActiveEntry>(context.ActivePointer).ActiveVersion);
        Assert.Equal("test_mv_v2_main", table.PhysicalTable);

        var pinned = await accessor.GetPinnedAsync(callerVersionOne);
        Assert.Equal(1, pinned.ViewVersion);
        Assert.Null(pinned.ActivePointer);
    }

    [Fact]
    public async Task UnknownCheckpoint_DoesNotPromoteGrainToReady()
    {
        SharedExecutor.InitialEvents.Clear();
        SharedRegistry.ForceUnknownCheckpointReads = true;

        var grainKey = MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1);
        var grain = _cluster.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await grain.EnsureStartedAsync();
        await grain.RefreshAsync();

        var entries = await SharedRegistry.GetEntriesAsync(
            "orders",
            TestMaterializedViewProjector.ViewNameConst,
            1);
        var entry = Assert.Single(entries);
        Assert.True(
            entry.CurrentCheckpointTruth.IsUnknown,
            $"state={entry.CurrentCheckpointTruth.State}, position={entry.CurrentCheckpointTruth.PositionValue}, status={entry.Status}, initial={SharedExecutor.InitialEvents.Count}");
        Assert.NotEqual(MvStatus.Ready, entry.Status);
    }

    [Fact]
    public async Task ActiveStatusRestore_NotSupported_StopsCatchUpWithoutSyntheticCompletion()
    {
        var grain = await StartServingGrainAsync();

        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);

        var status = await grain.GetStatusAsync();
        Assert.False(status.IsCatchUpActive);
        Assert.Null(status.LastCatchUpCompletedAt);
        Assert.Contains("unsupported", status.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var calls = SharedRegistry.ActiveStatusRestoreCalls;
        Assert.True(calls >= 1);
        await Task.Delay(100);
        Assert.Equal(calls, SharedRegistry.ActiveStatusRestoreCalls);
    }

    [Fact]
    public async Task ActiveStatusRestore_NonRetryableRejection_StopsCatchUpAndRetainsError()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Rejected(
            MvActivationFailureReason.UnsafeLifecycle,
            "permanent lifecycle rejection");
        var grain = await StartServingGrainAsync();

        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);

        var status = await grain.GetStatusAsync();
        Assert.False(status.IsCatchUpActive);
        Assert.Null(status.LastCatchUpCompletedAt);
        Assert.Contains("UnsafeLifecycle", status.LastError ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(1, SharedRegistry.ActiveStatusRestoreCalls);
        await Task.Delay(100);
        Assert.Equal(1, SharedRegistry.ActiveStatusRestoreCalls);
    }

    [Fact]
    public async Task ActiveStatusRestore_RetryableGenerationConflict_IsBoundedAndExposesExhaustion()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Rejected(
            MvActivationFailureReason.ExpectedGenerationConflict,
            "the serving pointer generation is still changing");
        var grain = await StartServingGrainAsync();

        await WaitUntilAsync(async () => !(await grain.GetStatusAsync()).IsCatchUpActive);

        var status = await grain.GetStatusAsync();
        Assert.False(status.IsCatchUpActive);
        Assert.Null(status.LastCatchUpCompletedAt);
        Assert.Contains("exhausted", status.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, SharedRegistry.ActiveStatusRestoreCalls);
    }

    [Fact]
    public async Task PermanentCatchUpOutcome_HaltsWithoutSyntheticCompletion()
    {
        SharedExecutor.ScriptedCatchUpResults.Enqueue(new MvCatchUpResult(0, false)
        {
            Outcome = MvCatchUpOutcome.PermanentUnsupported,
            ErrorCode = "unsupported",
            ErrorMessage = "Catch-up is unsupported by the configured provider or policy."
        });

        var grain = await StartServingGrainAsync();
        await WaitUntilAsync(async () => (await grain.GetStatusAsync()).CatchUpHalted);

        var status = await grain.GetStatusAsync();
        Assert.True(status.CatchUpHalted);
        Assert.False(status.IsCatchUpActive);
        Assert.Null(status.LastCatchUpCompletedAt);
        Assert.Equal("Catch-up is unsupported by the configured provider or policy.", status.LastError);
        Assert.Equal(1, SharedExecutor.CatchUpCalls);
        Assert.Equal(0, SharedRegistry.ActiveStatusRestoreCalls);
    }

    [Fact]
    public async Task RetryableCatchUpOutcome_StopsAtTheConfiguredFailureBound()
    {
        for (var i = 0; i < 3; i++)
        {
            SharedExecutor.ScriptedCatchUpResults.Enqueue(new MvCatchUpResult(0, false)
            {
                Outcome = MvCatchUpOutcome.RetryableFailure,
                ErrorCode = "catch-up-failed",
                ErrorMessage = "Catch-up failed and is eligible for bounded retry.",
                IsRetryable = true
            });
        }

        var grain = await StartServingGrainAsync();
        await WaitUntilAsync(async () => (await grain.GetStatusAsync()).CatchUpHalted);

        var status = await grain.GetStatusAsync();
        Assert.True(status.CatchUpHalted);
        Assert.False(status.IsCatchUpActive);
        Assert.Contains("bounded retry", status.LastError ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(3, SharedExecutor.CatchUpCalls);
        Assert.Equal(0, SharedRegistry.ActiveStatusRestoreCalls);
    }

    [Fact]
    public async Task SafeEligibleOutstandingHint_HaltsWithHintAndObservationTime()
    {
        SharedRegistry.ActiveStatusRestoreResult = MvActivationResult.Success(1);
        var grain = await StartServingGrainAsync();
        await WaitUntilAsync(async () => (await grain.GetStatusAsync()).LastCatchUpCompletedAt is not null);
        var startupCompletion = (await grain.GetStatusAsync()).LastCatchUpCompletedAt;
        Assert.NotNull(startupCompletion);
        var missingHint = CreateSerializableEvent(99, DateTime.UtcNow.AddSeconds(-30));

        // The receipt is deliberately not inserted into the durable source. It is already older than the safe window,
        // so repeated successful empty observations must exhaust the bounded missing-hint budget without synthetic
        // completion.
        await GetEventStream().OnNextAsync(missingHint);
        await WaitUntilAsync(async () => (await grain.GetStatusAsync()).CatchUpHalted, timeoutMs: 5000);

        var status = await grain.GetStatusAsync();
        Assert.True(status.CatchUpHalted);
        Assert.False(status.IsCatchUpActive);
        Assert.Equal(startupCompletion, status.LastCatchUpCompletedAt);
        Assert.Contains(missingHint.SortableUniqueIdValue, status.LastError ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("first observed at", status.LastError ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task<IMaterializedViewGrain> StartServingGrainAsync()
    {
        SharedExecutor.InitialEvents.Clear();
        await SharedRegistry.SetActiveAsync(
            "orders",
            TestMaterializedViewProjector.ViewNameConst,
            1);

        var grainKey = MvGrainKey.Build("orders", TestMaterializedViewProjector.ViewNameConst, 1);
        var grain = _cluster.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await grain.EnsureStartedAsync();
        return grain;
    }

    private static SerializableEvent CreateSerializableEvent(int ordinal, DateTime timestampUtc) =>
        new(
            Payload: [],
            SortableUniqueIdValue: SortableUniqueId.Generate(timestampUtc, Guid.Parse($"00000000-0000-0000-0000-{ordinal:D12}")),
            Id: Guid.Parse($"10000000-0000-0000-0000-{ordinal:D12}"),
            EventMetadata: new EventMetadata("test-command", "test-user", "test"),
            Tags: [],
            EventPayloadName: $"TestEvent{ordinal}");

    private static SerializableEvent CreateFixedAgedEvent(int ordinal, DateTime timestampUtc) =>
        new(
            Payload: [],
            SortableUniqueIdValue: SortableUniqueId.Generate(timestampUtc.AddMilliseconds(ordinal), Guid.Empty),
            Id: Guid.Parse($"20000000-0000-0000-0000-{ordinal:D12}"),
            EventMetadata: new EventMetadata("test-command", "test-user", "test"),
            Tags: [],
            EventPayloadName: $"FixedAgedEvent{ordinal}");

    private IAsyncStream<SerializableEvent> GetEventStream() => _cluster.Client
        .GetStreamProvider("EventStreamProvider")
        .GetStream<SerializableEvent>(StreamId.Create(
            ServiceIdGrainKey.BuildStreamNamespace("AllEvents", "orders"),
            Guid.Empty));

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, int timeoutMs = 5000, int pollMs = 50)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(pollMs);
        }

        Assert.Fail("Condition was not satisfied before timeout.");
    }

    private sealed class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSekibanDcbMaterializedView();
                    services.AddSingleton<IEventTypes>(_ => new SimpleEventTypes());
                    services.Configure<MvOptions>(options =>
                    {
                        options.AllowDefaultServiceId = true;
                        options.PollInterval = TimeSpan.FromMilliseconds(20);
                        options.SafeWindowMs = 100;
                        options.StreamReorderWindow = TimeSpan.FromMilliseconds(10);
                        options.CatchUpStallThreshold = TimeSpan.FromMilliseconds(150);
                    });
                    services.AddMaterializedView<TestMaterializedViewProjector>();
                    services.AddMaterializedView<TestMaterializedViewProjectorV2>();
                    services.AddSekibanDcbMaterializedViewOrleans(activateOnStartup: false);
                    services.AddSingleton<IMvRegistryStore>(SharedRegistry);
                    services.AddSingleton<IMvExecutor>(SharedExecutor);
                    services.AddSingleton<IProjectionStatusStore>(SharedStatusStore);
                    services.AddSingleton(new ProjectionStatusOptions
                    {
                        ClusterId = "mv-test-cluster",
                        HeartbeatInterval = TimeSpan.FromMilliseconds(20),
                        HeartbeatWriteTimeout = TimeSpan.FromSeconds(1),
                        SamplingWindow = TimeSpan.Zero
                    });
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
                    services.AddSingleton<IMvStorageInfoProvider>(
                        new MvStorageInfoProvider(new MvStorageInfo(MvDbType.Postgres, "Host=test;Database=mv;")));
                })
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    private sealed class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams("EventStreamProvider");
        }
    }

    private sealed class FixedServiceIdProvider(string serviceId) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => serviceId;
    }

    private sealed class TestProjectionStatusStore : IProjectionStatusStore
    {
        private InMemoryMultiProjectionStateStore _inner = new(new FixedServiceIdProvider("orders"));

        public TaskCompletionSource Written { get; private set; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProjectionStatusHeartbeat? LastHeartbeat { get; private set; }

        public void Reset()
        {
            _inner = new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider("orders"));
            Written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            LastHeartbeat = null;
        }

        public async Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
            ProjectionStatusHeartbeat heartbeat,
            long expectedSequence,
            CancellationToken cancellationToken = default)
        {
            var result = await _inner.UpsertAsync(heartbeat, expectedSequence, cancellationToken);
            if (result.IsSuccess && result.GetValue().Committed)
            {
                LastHeartbeat = heartbeat;
                Written.TrySetResult();
            }

            return result;
        }

        public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
            string? projectorName = null,
            string? projectorVersion = null,
            CancellationToken cancellationToken = default) =>
            _inner.ListAsync(projectorName, projectorVersion, cancellationToken);
    }

    private sealed class TestMaterializedViewProjector : IMaterializedViewProjector
    {
        public const string ViewNameConst = "TestMv";

        public string ViewName => ViewNameConst;
        public int ViewVersion => 1;

        public Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
            Event ev,
            IMvApplyContext ctx,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
    }

    private sealed class TestMaterializedViewProjectorV2 : IMaterializedViewProjector
    {
        public string ViewName => TestMaterializedViewProjector.ViewNameConst;
        public int ViewVersion => 2;

        public Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
            Event ev,
            IMvApplyContext ctx,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
    }

    private sealed class FakeMvExecutor : IMvExecutor, IMvOrleansCatchUpExecutor, IMvActivationExecutor
    {
        private readonly FakeMvRegistryStore _registry;

        public FakeMvExecutor(FakeMvRegistryStore registry) => _registry = registry;

        public List<SerializableEvent> InitialEvents { get; } = [];
        public HashSet<Guid> AppliedEventIds { get; } = [];
        public int AppliedEventExecutionCount { get; private set; }
        public List<string> ServiceIds { get; } = [];
        public Queue<MvCatchUpResult> ScriptedCatchUpResults { get; } = [];
        public int CatchUpCalls { get; private set; }
        public int ApplySerializableEventsCalls { get; private set; }
        public int InitializeCalls { get; private set; }
        public int TargetCaptureCalls { get; private set; }
        public int MaxConcurrentCatchUpCalls { get; private set; }
        public TaskCompletionSource AppliedEventCountReached { get; private set; } = NewSignal();
        public TaskCompletionSource CatchUpEntered { get; private set; } = NewSignal();
        public TaskCompletionSource CatchUpCallCountReached { get; private set; } = NewSignal();
        private TaskCompletionSource BlockedCatchUpRelease { get; set; } = NewSignal();
        public TaskCompletionSource TargetCaptureCountReached { get; private set; } = NewSignal();

        private int _expectedAppliedEventCount = -1;
        private int _expectedCatchUpCalls = -1;
        private int _expectedTargetCaptureCalls = -1;
        private int _blockNextCatchUp;
        private int _inFlightCatchUpCalls;

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Reset()
        {
            InitialEvents.Clear();
            AppliedEventIds.Clear();
            AppliedEventExecutionCount = 0;
            ServiceIds.Clear();
            ScriptedCatchUpResults.Clear();
            CatchUpCalls = 0;
            ApplySerializableEventsCalls = 0;
            InitializeCalls = 0;
            TargetCaptureCalls = 0;
            MaxConcurrentCatchUpCalls = 0;
            AppliedEventCountReached = NewSignal();
            CatchUpEntered = NewSignal();
            CatchUpCallCountReached = NewSignal();
            BlockedCatchUpRelease = NewSignal();
            TargetCaptureCountReached = NewSignal();
            _expectedAppliedEventCount = -1;
            _expectedCatchUpCalls = -1;
            _expectedTargetCaptureCalls = -1;
            _blockNextCatchUp = 0;
            _inFlightCatchUpCalls = 0;
        }

        public void SeedInitial(params SerializableEvent[] events) => InitialEvents.AddRange(events);

        public void ExpectAppliedEventCount(int expected)
        {
            _expectedAppliedEventCount = expected;
            if (AppliedEventExecutionCount >= expected)
            {
                AppliedEventCountReached.TrySetResult();
            }
        }

        public void ExpectTargetCaptureCallCount(int expected)
        {
            _expectedTargetCaptureCalls = expected;
            if (TargetCaptureCalls >= expected)
            {
                TargetCaptureCountReached.TrySetResult();
            }
        }

        public void ExpectCatchUpCallCount(int expected)
        {
            _expectedCatchUpCalls = expected;
            CatchUpCallCountReached = NewSignal();
            if (CatchUpCalls >= expected)
            {
                CatchUpCallCountReached.TrySetResult();
            }
        }

        public void BlockNextCatchUp()
        {
            CatchUpEntered = NewSignal();
            BlockedCatchUpRelease = NewSignal();
            Interlocked.Exchange(ref _blockNextCatchUp, 1);
        }

        public void ReleaseBlockedCatchUp() => BlockedCatchUpRelease.TrySetResult();

        public Task InitializeAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            ServiceIds.Add(serviceId);
            return _registry.RegisterViewAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken);
        }

        public async Task<MvCatchUpResult> CatchUpOnceAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            ServiceIds.Add(serviceId);
            CatchUpCalls++;
            if (_expectedCatchUpCalls >= 0 && CatchUpCalls >= _expectedCatchUpCalls)
            {
                CatchUpCallCountReached.TrySetResult();
            }
            var concurrent = Interlocked.Increment(ref _inFlightCatchUpCalls);
            MaxConcurrentCatchUpCalls = Math.Max(MaxConcurrentCatchUpCalls, concurrent);
            try
            {
                await InitializeAsync(host, serviceId, cancellationToken);

                if (Interlocked.Exchange(ref _blockNextCatchUp, 0) == 1)
                {
                    CatchUpEntered.TrySetResult();
                    await BlockedCatchUpRelease.Task.WaitAsync(cancellationToken);
                }

                if (ScriptedCatchUpResults.TryDequeue(out var scripted))
                {
                    return scripted;
                }

                var currentPosition = await _registry.GetCurrentPositionAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken);
                var batch = InitialEvents
                    .Where(serializableEvent =>
                        string.IsNullOrWhiteSpace(currentPosition) ||
                        string.Compare(serializableEvent.SortableUniqueIdValue, currentPosition, StringComparison.Ordinal) > 0)
                    .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
                    .Take(100)
                    .ToList();

                if (batch.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(currentPosition))
                    {
                        await _registry.UpdatePositionAsync(
                            new MvPositionUpdate(
                                serviceId,
                                host.ViewName,
                                host.ViewVersion,
                                SortableUniqueId.MinValue.Value,
                                MvApplySource.CatchUp,
                                AppliedEventVersionDelta: 0)
                            {
                                CheckpointTruth = MvCheckpointTruth.KnownZero()
                            },
                            cancellationToken: cancellationToken);
                    }

                    return new MvCatchUpResult(0, false);
                }

                foreach (var serializableEvent in batch)
                {
                    AppliedEventIds.Add(serializableEvent.Id);
                }
                AppliedEventExecutionCount += batch.Count;
                if (_expectedAppliedEventCount >= 0 && AppliedEventExecutionCount >= _expectedAppliedEventCount)
                {
                    AppliedEventCountReached.TrySetResult();
                }

                await _registry.UpdatePositionAsync(
                    new MvPositionUpdate(
                        serviceId,
                        host.ViewName,
                        host.ViewVersion,
                        batch[^1].SortableUniqueIdValue,
                        MvApplySource.CatchUp,
                        batch.Count),
                    cancellationToken: cancellationToken);
                return new MvCatchUpResult(batch.Count, false, batch[^1].SortableUniqueIdValue);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlightCatchUpCalls);
            }
        }

        Task<MvCatchUpResult> IMvOrleansCatchUpExecutor.CatchUpOnceForOrleansAsync(
            IMvApplyHost host,
            string? serviceId,
            CancellationToken cancellationToken) =>
            CatchUpOnceAsync(host, serviceId, cancellationToken);

        public async Task<int> ApplySerializableEventsAsync(
            IMvApplyHost host,
            IReadOnlyList<SerializableEvent> events,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            ApplySerializableEventsCalls++;
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            ServiceIds.Add(serviceId);
            await InitializeAsync(host, serviceId, cancellationToken);

            var currentPosition = await _registry.GetCurrentPositionAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken);
            var ordered = events
                .Where(serializableEvent =>
                    string.IsNullOrWhiteSpace(currentPosition) ||
                    string.Compare(serializableEvent.SortableUniqueIdValue, currentPosition, StringComparison.Ordinal) > 0)
                .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
                .ToList();

            if (ordered.Count == 0)
            {
                return 0;
            }

            foreach (var serializableEvent in ordered)
            {
                AppliedEventIds.Add(serializableEvent.Id);
            }

            await _registry.UpdatePositionAsync(
                new MvPositionUpdate(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    ordered[^1].SortableUniqueIdValue,
                    MvApplySource.Stream,
                    ordered.Count),
                cancellationToken: cancellationToken);
            return ordered.Count;
        }

        public async Task<MvCheckpointTruth> CaptureTargetCheckpointAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            TargetCaptureCalls++;
            if (_expectedTargetCaptureCalls >= 0 && TargetCaptureCalls >= _expectedTargetCaptureCalls)
            {
                TargetCaptureCountReached.TrySetResult();
            }
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            var latest = InitialEvents
                .Select(serializableEvent => serializableEvent.SortableUniqueIdValue)
                .OrderBy(value => value, StringComparer.Ordinal)
                .LastOrDefault();
            var target = string.IsNullOrWhiteSpace(latest)
                ? MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture())
                : MvCheckpointTruth.Known(
                    new SortableUniqueId(latest),
                    MvCheckpointProvenance.AuthoritativeTargetCapture());
            await _registry.SetTargetCheckpointAsync(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    target,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return target;
        }

        public async Task<MvActivationResult> TryActivateAsync(
            IMvApplyHost host,
            string? serviceId = null,
            CancellationToken cancellationToken = default)
        {
            serviceId ??= DefaultServiceIdProvider.DefaultServiceId;
            var entries = await _registry.GetEntriesAsync(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            var active = await _registry.GetActiveAsync(serviceId, host.ViewName, cancellationToken).ConfigureAwait(false);
            var (eligibility, request) = MvActivationEligibility.Evaluate(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                entries,
                active);
            if (!eligibility.IsEligible || request is null)
            {
                return MvActivationResult.Rejected(eligibility.FailureReason, eligibility.Message);
            }

            return await _registry.TryActivateAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class FakeMvRegistryStore : IMvRegistryStore
    {
        private readonly Dictionary<(string ServiceId, string ViewName, int ViewVersion, string LogicalTable), MvRegistryEntry> _entries = [];
        private readonly Dictionary<(string ServiceId, string ViewName), MvActiveEntry> _active = [];

        public bool ForceUnknownCheckpointReads { get; set; }
        public MvActivationResult? ActiveStatusRestoreResult { get; set; }
        public int ActiveStatusRestoreCalls { get; private set; }
        public TaskCompletionSource ActiveStatusRestoreCompleted { get; private set; } = NewSignal();
        public TaskCompletionSource ActiveStatusRestoreCallCountReached { get; private set; } = NewSignal();
        public int RegisterCalls { get; private set; }
        public int MarkStreamReceivedCalls { get; private set; }
        public int UpdatePositionCalls { get; private set; }
        public int UpdateStatusCalls { get; private set; }
        private int _expectedActiveStatusRestoreCalls = -1;

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Reset()
        {
            _entries.Clear();
            _active.Clear();
            ForceUnknownCheckpointReads = false;
            ActiveStatusRestoreResult = null;
            ActiveStatusRestoreCalls = 0;
            ActiveStatusRestoreCompleted = NewSignal();
            ActiveStatusRestoreCallCountReached = NewSignal();
            RegisterCalls = 0;
            MarkStreamReceivedCalls = 0;
            UpdatePositionCalls = 0;
            UpdateStatusCalls = 0;
            _expectedActiveStatusRestoreCalls = -1;
        }

        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RegisterAsync(MvRegistryEntry entry, System.Data.IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            _entries[(entry.ServiceId, entry.ViewName, entry.ViewVersion, entry.LogicalTable)] = entry;
            return Task.CompletedTask;
        }

        public Task UpdatePositionAsync(
            MvPositionUpdate update,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            UpdatePositionCalls++;
            foreach (var key in _entries.Keys.Where(key =>
                         key.ServiceId == update.ServiceId &&
                         key.ViewName == update.ViewName &&
                         key.ViewVersion == update.ViewVersion).ToList())
            {
                var entry = _entries[key];
                var checkpointTruth = MvCheckpointTruth.FromPositionUpdate(update);
                _entries[key] = entry with
                {
                    CurrentPosition = update.SortableUniqueId,
                    CurrentCheckpointTruth = checkpointTruth,
                    LastSortableUniqueId = update.SortableUniqueId,
                    AppliedEventVersion = entry.AppliedEventVersion + update.AppliedEventVersionDelta,
                    LastAppliedSource = update.Source == MvApplySource.Stream ? "stream" : "catchup",
                    LastAppliedAt = DateTimeOffset.UtcNow,
                    LastStreamAppliedSortableUniqueId = update.Source == MvApplySource.Stream ? update.SortableUniqueId : entry.LastStreamAppliedSortableUniqueId,
                    LastCatchUpSortableUniqueId = update.Source == MvApplySource.CatchUp && update.AppliedEventVersionDelta > 0 ? update.SortableUniqueId : entry.LastCatchUpSortableUniqueId,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }

        public Task MarkStreamReceivedAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            string sortableUniqueId,
            DateTimeOffset receivedAt,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            MarkStreamReceivedCalls++;
            foreach (var key in _entries.Keys.Where(key => key.ServiceId == serviceId && key.ViewName == viewName && key.ViewVersion == viewVersion).ToList())
            {
                var entry = _entries[key];
                _entries[key] = entry with
                {
                    LastStreamReceivedSortableUniqueId = string.IsNullOrWhiteSpace(entry.LastStreamReceivedSortableUniqueId) ||
                                                         string.Compare(entry.LastStreamReceivedSortableUniqueId, sortableUniqueId, StringComparison.Ordinal) < 0
                        ? sortableUniqueId
                        : entry.LastStreamReceivedSortableUniqueId,
                    LastStreamReceivedAt = receivedAt,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvStatus status,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            UpdateStatusCalls++;
            foreach (var key in _entries.Keys.Where(key => key.ServiceId == serviceId && key.ViewName == viewName && key.ViewVersion == viewVersion).ToList())
            {
                var entry = _entries[key];
                _entries[key] = entry with
                {
                    Status = status,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MvRegistryEntry> entries = _entries
                .Where(pair => pair.Key.ServiceId == serviceId && pair.Key.ViewName == viewName && pair.Key.ViewVersion == viewVersion)
                .Select(pair => ForceUnknownCheckpointReads
                    ? pair.Value with
                    {
                        CurrentPosition = null,
                        CurrentCheckpointTruth = MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable),
                        Status = MvStatus.CatchingUp
                    }
                    : pair.Value)
                .OrderBy(entry => entry.LogicalTable, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(entries);
        }

        public Task<MvActiveEntry?> GetActiveAsync(string serviceId, string viewName, CancellationToken cancellationToken = default)
        {
            _active.TryGetValue((serviceId, viewName), out var entry);
            return Task.FromResult(entry);
        }

        public Task<MvActivationResult> TryRestoreActiveStatusAsync(
            MvActiveStatusRestoreRequest request,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            ActiveStatusRestoreCalls++;
            if (_expectedActiveStatusRestoreCalls >= 0 && ActiveStatusRestoreCalls >= _expectedActiveStatusRestoreCalls)
            {
                ActiveStatusRestoreCallCountReached.TrySetResult();
            }
            if (ActiveStatusRestoreResult is { } result)
            {
                if (result.Succeeded)
                {
                    ActiveStatusRestoreCompleted.TrySetResult();
                }

                return Task.FromResult(result);
            }

            throw new NotSupportedException("The fake registry does not support atomic serving-status restoration.");
        }

        public void ExpectActiveStatusRestoreCallCount(int expected)
        {
            _expectedActiveStatusRestoreCalls = expected;
            ActiveStatusRestoreCallCountReached = NewSignal();
            if (ActiveStatusRestoreCalls >= expected)
            {
                ActiveStatusRestoreCallCountReached.TrySetResult();
            }
        }

        public void MutateActiveGeneration()
        {
            var key = _active.Keys.Single();
            var active = _active[key];
            _active[key] = active with { Generation = active.Generation + 1 };
        }

        public Task SetActiveAsync(
            string serviceId,
            string viewName,
            int activeVersion,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            _active[(serviceId, viewName)] = new MvActiveEntry(serviceId, viewName, activeVersion, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task SetTargetCheckpointAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvCheckpointTruth targetCheckpointTruth,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var key in _entries.Keys.Where(key =>
                         key.ServiceId == serviceId && key.ViewName == viewName && key.ViewVersion == viewVersion).ToList())
            {
                var entry = _entries[key];
                _entries[key] = entry with
                {
                    TargetPosition = targetCheckpointTruth.PositionValue,
                    TargetCheckpointTruth = targetCheckpointTruth,
                    LastUpdated = DateTimeOffset.UtcNow
                };
            }

            return Task.CompletedTask;
        }

        public Task<MvActivationResult> TryActivateAsync(
            MvActivationRequest request,
            System.Data.IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default)
        {
            if (!_active.TryGetValue((request.ServiceId, request.ViewName), out var active))
            {
                if (request.ExpectedActiveVersion is not null || request.ExpectedActiveGeneration != 0)
                {
                    return Task.FromResult(MvActivationResult.Rejected(
                        MvActivationFailureReason.ConcurrentSuperseded,
                        "The expected active pointer changed."));
                }
            }
            else if (active.ActiveVersion != request.ExpectedActiveVersion ||
                     active.Generation != request.ExpectedActiveGeneration)
            {
                return Task.FromResult(MvActivationResult.Rejected(
                    MvActivationFailureReason.ConcurrentSuperseded,
                    "The expected active pointer or generation changed."));
            }

            var keys = _entries.Keys.Where(key =>
                key.ServiceId == request.ServiceId &&
                key.ViewName == request.ViewName &&
                key.ViewVersion == request.ViewVersion).ToList();
            if (keys.Count != request.CandidateCount || keys.Any(key => _entries[key].Status != request.ExpectedStatus))
            {
                return Task.FromResult(MvActivationResult.Rejected(
                    MvActivationFailureReason.ConcurrentSuperseded,
                    "The candidate snapshot changed."));
            }

            var generation = request.ExpectedActiveGeneration + 1;
            _active[(request.ServiceId, request.ViewName)] = new MvActiveEntry(
                request.ServiceId,
                request.ViewName,
                request.ViewVersion,
                DateTimeOffset.UtcNow)
            {
                Generation = generation
            };
            foreach (var key in keys)
            {
                _entries[key] = _entries[key] with { Status = MvStatus.Active };
            }

            return Task.FromResult(MvActivationResult.Success(generation));
        }

        public async Task RegisterViewAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken)
        {
            var key = (serviceId, viewName, viewVersion, "main");
            if (_entries.ContainsKey(key))
            {
                return;
            }

            await RegisterAsync(
                new MvRegistryEntry
                {
                    ServiceId = serviceId,
                    ViewName = viewName,
                    ViewVersion = viewVersion,
                    LogicalTable = "main",
                    PhysicalTable = $"{viewName.ToLowerInvariant()}_main",
                    Status = MvStatus.CatchingUp,
                    AppliedEventVersion = 0,
                    LastUpdated = DateTimeOffset.UtcNow
                },
                cancellationToken: cancellationToken);
        }

        public async Task<string?> GetCurrentPositionAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken)
        {
            var entries = await GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);
            return entries.Select(entry => entry.CurrentPosition).FirstOrDefault(position => !string.IsNullOrWhiteSpace(position));
        }
    }
}
