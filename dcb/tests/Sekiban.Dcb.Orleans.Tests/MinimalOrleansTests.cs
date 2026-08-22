using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Testing;
using System.Text;
using System.Text.Json;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Minimal Orleans tests to verify Orleans integration works
/// </summary>
public class MinimalOrleansTests : IAsyncLifetime
{
    private static readonly CountingInMemoryEventStore SharedEventStore = CreateSharedEventStore();
    private static readonly Sekiban.Dcb.Testing.InMemoryMultiProjectionStateStore SharedStateStore = new();
    private static readonly TestProjectionStatusStore SharedStatusStore = new(SharedStateStore);
    private static readonly ProjectionStatusOptions SharedStatusOptions = new();
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        ResetStatusOptions();
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"TestCluster-{uniqueId}";
        builder.Options.ServiceId = $"TestService-{uniqueId}";
        // Use real networking with explicit fixed ports to avoid client assuming 30000 while silo chooses dynamic port.
        var portBase = 20_000 + (Environment.ProcessId % 5_000) * 4;
        builder.PortAllocator = new FixedPortAllocator(portBase, portBase + 100);
        builder.Options.BaseSiloPort = portBase;
        builder.Options.BaseGatewayPort = portBase + 1;
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();

        _cluster = builder.Build();
        await _cluster.DeployAsync();
        SharedEventStore.Clear();
        SharedEventStore.ClearReadAllEventsTracking();
        SharedStateStore.Clear();
        SharedStatusStore.Reset();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Orleans_TestCluster_Should_Start_Successfully()
    {
        // Assert
        Assert.NotNull(_cluster);
        Assert.NotNull(_client);
        Assert.NotNull(_cluster.ServiceProvider);
    }

    [Fact]
    public async Task Orleans_Should_Activate_MultiProjectionGrain()
    {
        // Arrange & Act
        var grain = _client.GetGrain<IMultiProjectionGrain>("test-projector");
        var status = await grain.GetStatusAsync();

        // Assert
        Assert.NotNull(status);
        Assert.Equal("test-projector", status.ProjectorName);
        Assert.Equal(0, status.EventsProcessed);
        Assert.True(status.IsSubscriptionActive); // Subscription auto-starts on activation
    }

    [Fact]
    public async Task PassiveStatusReader_DoesNotActivateGrain_WhileDirectGrainCallDoes()
    {
        const string projectorName = "test-projector";
        var activationCount = 0;
        var backgroundCatchUpCount = 0;
        var activationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lifecycleObserver = CatchUpProductionTestHooks.Register(
            DefaultServiceIdProvider.DefaultServiceId,
            projectorName,
            (point, _) =>
            {
                if (point == CatchUpProductionHookPoint.ActivationLifecycleStarted)
                {
                    Interlocked.Increment(ref activationCount);
                    activationStarted.TrySetResult(true);
                }
                else if (point == CatchUpProductionHookPoint.BackgroundBeforeGate)
                {
                    Interlocked.Increment(ref backgroundCatchUpCount);
                    backgroundStarted.TrySetResult(true);
                }

                return Task.CompletedTask;
            });

        var reader = new ProjectionStatusReader(
            SharedStateStore,
            SharedEventStore,
            new DefaultServiceIdProvider(),
            new ProjectionStatusOptions { SamplingWindow = TimeSpan.Zero });

        var before = await reader.ReadAsync(new ProjectionStatusReadRequest(ProjectorName: projectorName));
        Assert.True(before.IsSuccess);
        Assert.Empty(before.GetValue());
        Assert.Empty((await SharedStateStore.ListAsync(projectorName, "1.0")).GetValue());

        // Wait through the lifecycle/background-start interval. A passive reader must not cause even the grain
        // lifecycle participant to run, so this is stronger than observing an empty heartbeat table.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(activationStarted.Task.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref activationCount));
        Assert.Equal(0, Volatile.Read(ref backgroundCatchUpCount));

        // This is the contrast case: obtaining and calling a grain is the operation that creates the activation and
        // its dedicated heartbeat row.
        var grain = _client.GetGrain<IMultiProjectionGrain>(projectorName);
        await grain.GetStatusAsync();

        await activationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await backgroundStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref activationCount));
        Assert.Equal(1, Volatile.Read(ref backgroundCatchUpCount));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<ProjectionStatusHeartbeat> rows = Array.Empty<ProjectionStatusHeartbeat>();
        while (DateTime.UtcNow < deadline)
        {
            rows = (await SharedStateStore.ListAsync(projectorName, "1.0")).GetValue();
            if (rows.Count > 0)
            {
                break;
            }

            await Task.Delay(50);
        }

        var heartbeat = Assert.Single(rows);
        Assert.Contains(heartbeat.Phase, new[] { ProjectionStatusPhases.Active, ProjectionStatusPhases.CatchingUp });
        Assert.False(heartbeat.IsFaulted);
    }

    [Fact]
    public async Task SlowHeartbeatUpsert_IsBoundedAndDoesNotBlockGrainCalls()
    {
        SharedStatusStore.Delay = TimeSpan.FromSeconds(2);
        try
        {
            var grain = _client.GetGrain<IMultiProjectionGrain>("slow-heartbeat");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var status = await grain.GetStatusAsync();
            stopwatch.Stop();

            Assert.NotNull(status);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"grain call took {stopwatch.Elapsed}");

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!SharedStatusStore.SawCancelableToken && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.True(SharedStatusStore.SawCancelableToken);
        }
        finally
        {
            SharedStatusStore.Reset();
        }
    }

    [Fact]
    public async Task FailingStatusRegistry_IsolatedFromRealGrainWork_AndRetriesWithCappedBackoff()
    {
        SharedStatusOptions.HeartbeatRetryBase = TimeSpan.FromSeconds(2);
        SharedStatusOptions.HeartbeatRetryCap = TimeSpan.FromSeconds(3);
        SharedStatusStore.ThrowOnUpsert = true;

        try
        {
            var grain = _client.GetGrain<IMultiProjectionGrain>("test-projector");
            var initialStatus = await grain.GetStatusAsync();
            Assert.False(initialStatus.HasError);

            await WaitUntilAsync(
                () => SharedStatusStore.UpsertCalls >= 1,
                TimeSpan.FromSeconds(2));
            var callsBeforeProjectionWork = SharedStatusStore.UpsertCalls;

            var baseTick = DateTime.UtcNow.Ticks;
            var position = SortableUniqueId.GetTickString(baseTick) + SortableUniqueId.GetIdString(Guid.Empty);
            var serializableEvent = new SerializableEvent(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new TestProjectionEvent(42))),
                position,
                Guid.CreateVersion7(),
                new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "registry-failure-test"),
                new List<string>(),
                nameof(TestProjectionEvent));

            // This is a real grain event/catch-up path. It must not synchronously write the passive registry; only the
            // dedicated heartbeat timer is allowed to call the failing store.
            await grain.SeedEventsAsync(new[] { serializableEvent });
            MultiProjectionHeadStatusSnapshot projectionHead = await grain.GetProjectionHeadStatusAsync();
            var projectionDeadline = DateTime.UtcNow.AddSeconds(5);
            while (!string.Equals(projectionHead.CurrentLastSortableUniqueId, position, StringComparison.Ordinal) &&
                   DateTime.UtcNow < projectionDeadline)
            {
                await grain.RefreshAsync();
                projectionHead = await grain.GetProjectionHeadStatusAsync();
                if (!string.Equals(projectionHead.CurrentLastSortableUniqueId, position, StringComparison.Ordinal))
                {
                    await Task.Delay(50);
                }
            }

            var afterWorkStatus = await grain.GetStatusAsync();
            var health = await grain.GetHealthStatusAsync();
            var snapshot = await grain.GetSnapshotJsonAsync(canGetUnsafeState: false);
            var received = await grain.IsSortableUniqueIdReceived(position);

            Assert.True(
                string.Equals(projectionHead.CurrentLastSortableUniqueId, position, StringComparison.Ordinal),
                $"Expected current head {position}, actual current={projectionHead.CurrentLastSortableUniqueId}, " +
                $"consistent={projectionHead.ConsistentLastSortableUniqueId}, " +
                $"eventReads={SharedEventStore.ReadAllSerializableEventsCallCount}.");
            Assert.True(SharedEventStore.ReadAllSerializableEventsCallCount > 0);
            Assert.True(afterWorkStatus.EventsProcessed >= 1);
            Assert.False(afterWorkStatus.HasError);
            Assert.True(health.IsHealthy);
            Assert.Null(health.LastError);
            Assert.True(snapshot.IsSuccess, snapshot.IsSuccess ? string.Empty : snapshot.GetException().ToString());
            Assert.True(received);
            Assert.Equal(callsBeforeProjectionWork, SharedStatusStore.UpsertCalls);

            // The failed write leaves the dirty/retry path armed. With a 2s base and 3s cap, later timer ticks
            // provide observable intervals of approximately 2s, then 3s, and remain capped at 3s.
            await WaitUntilAsync(
                () => SharedStatusStore.UpsertCalls >= 4,
                TimeSpan.FromSeconds(12));
            var callTimes = SharedStatusStore.UpsertCallTimes;
            Assert.True(callTimes.Count >= 4);
            var intervals = callTimes
                .Zip(callTimes.Skip(1), (first, second) => second - first)
                .ToArray();
            Assert.InRange(intervals[0], TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4));
            Assert.InRange(intervals[1], TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5));
            Assert.InRange(intervals[2], TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(5));
        }
        finally
        {
            SharedStatusStore.Reset();
        }
    }

    [Fact]
    public async Task Orleans_MultiProjectionCatchUp_Should_Read_With_BatchLimit()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>("test-projector");
        SharedEventStore.ClearReadAllEventsTracking();

        var baseTick = DateTime.UtcNow.Ticks;
        var events = Enumerable.Range(0, 3501)
            .Select(i => new Event(
                new TestProjectionEvent(i),
                new SortableUniqueId(
                    SortableUniqueId.GetTickString(baseTick + i) + SortableUniqueId.GetIdString(Guid.Empty)),
                nameof(TestProjectionEvent),
                Guid.CreateVersion7(),
                new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
                new List<string>()))
            .ToList();

        await grain.SeedEventsAsync(ToSerializableEvents(events));
        await grain.RefreshAsync();

        var due = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < due && SharedEventStore.ReadAllSerializableEventsCallCount == 0)
        {
            await Task.Delay(100);
        }

        Assert.True(SharedEventStore.ReadAllSerializableEventsCallCount > 0);
        Assert.All(SharedEventStore.ReadAllSerializableEventsMaxCounts, maxCount => Assert.Equal(500, maxCount));
    }

    [Fact]
    public async Task ProjectionHeartbeat_RealGrainAdvancesFetchedCursorPastFilteredDuplicate()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>("test-projector");
        var eventId = Guid.CreateVersion7();
        var baseTick = DateTime.UtcNow.Ticks;
        var metadata = new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test");
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new TestProjectionEvent(1)));
        var positions = Enumerable.Range(0, 501)
            .Select(index => SortableUniqueId.GetTickString(baseTick + index) + SortableUniqueId.GetIdString(Guid.Empty))
            .ToArray();
        var events = positions.Select((position, index) => new SerializableEvent(
                payload,
                position,
                index is 0 or 500 ? eventId : Guid.CreateVersion7(),
                metadata,
                new List<string>(),
                nameof(TestProjectionEvent)))
            .ToArray();

        await grain.SeedEventsAsync(events);
        await grain.RefreshAsync();

        var deadline = DateTime.UtcNow.AddSeconds(8);
        ProjectionStatusHeartbeat? heartbeat = null;
        while (DateTime.UtcNow < deadline)
        {
            heartbeat = (await SharedStateStore.ListAsync("test-projector", "1.0")).GetValue().SingleOrDefault();
            if (heartbeat?.LastTraversedSortableUniqueId == positions[^1])
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.NotNull(heartbeat);
        Assert.Equal(positions[^1], heartbeat!.LastTraversedSortableUniqueId);
        Assert.Equal(positions[^2], heartbeat.LastAppliedSortableUniqueId);
        Assert.Equal(500, heartbeat.AppliedEventCount);
    }

    [Fact]
    public async Task Orleans_Should_Support_Multiple_Grain_Instances()
    {
        // Arrange & Act
        var grain1 = _client.GetGrain<IMultiProjectionGrain>("projector-1");
        var grain2 = _client.GetGrain<IMultiProjectionGrain>("projector-2");

        var status1 = await grain1.GetStatusAsync();
        var status2 = await grain2.GetStatusAsync();

        // Assert
        Assert.Equal("projector-1", status1.ProjectorName);
        Assert.Equal("projector-2", status2.ProjectorName);
    }

    [Fact]
    public async Task Orleans_Grain_Should_Manage_Subscription_State()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("subscription-test");

        // Act - Get initial status (subscription auto-starts on activation)
        var initialStatus = await grain.GetStatusAsync();

        // Start subscription explicitly (idempotent)
        await grain.StartSubscriptionAsync();
        var activeStatus = await grain.GetStatusAsync();

        // Stop subscription
        await grain.StopSubscriptionAsync();
        var stoppedStatus = await grain.GetStatusAsync();

        // Start subscription again
        await grain.StartSubscriptionAsync();
        var reactivatedStatus = await grain.GetStatusAsync();

        // Assert
        Assert.True(initialStatus.IsSubscriptionActive); // Auto-started on activation
        Assert.True(activeStatus.IsSubscriptionActive);
        Assert.False(stoppedStatus.IsSubscriptionActive);
        Assert.True(reactivatedStatus.IsSubscriptionActive);
    }

    [Fact]
    public async Task Orleans_Grain_Should_Return_Snapshot_Envelope()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("serialization-test");

        // Act
        var stateResult = await grain.GetSnapshotJsonAsync();

        // Assert
        Assert.NotNull(stateResult);
        Assert.True(stateResult.IsSuccess);
        var env = JsonSerializer.Deserialize<Sekiban.Dcb.Snapshots.SerializableMultiProjectionStateEnvelope>(stateResult.GetValue(), new JsonSerializerOptions());
        Assert.NotNull(env);
    }

    [Fact]
    public async Task Orleans_Grain_Should_Handle_Persistence()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("persistence-test");

        // Act
        var persistResult = await grain.PersistStateAsync();

        // Assert
        Assert.NotNull(persistResult);
        Assert.True(persistResult.IsSuccess);

        // Get status to verify persistence details
        var status = await grain.GetStatusAsync();
        Assert.NotNull(status.LastPersistTime);
    }

    [Fact]
    public async Task Hot_fetched_checkpoint_is_committed_and_consumed_by_fresh_activation()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(PersistenceTestMulti.MultiProjectorName);
        var baseTick = DateTime.UtcNow.AddMinutes(-2).Ticks;
        var firstEvents = Enumerable.Range(0, 123)
            .Select(index => CreatePersistenceEvent(baseTick + index, index))
            .ToList();

        await grain.SeedEventsAsync(ToSerializableEvents(firstEvents));
        await WaitUntilCatchUpIdleAsync(grain);
        await grain.RefreshAsync();
        var initialPersistResult = await grain.PersistStateAsync();
        Assert.True(initialPersistResult.IsSuccess);

        var c0Result = await SharedStateStore.GetLatestForVersionAsync(
            PersistenceTestMulti.MultiProjectorName,
            PersistenceTestMulti.MultiProjectorVersion);
        Assert.True(c0Result.IsSuccess);
        Assert.True(c0Result.GetValue().HasValue);
        var c0 = c0Result.GetValue().GetValue();
        Assert.Equal(123, c0.EventsProcessed);

        // Start at an arbitrary residue so the legacy exact modulo trigger cannot fire on the tenth 500-event batch.
        var secondEvents = Enumerable.Range(0, 5_000)
            .Select(index => CreatePersistenceEvent(baseTick + 123 + index, 123 + index))
            .ToList();
        await WaitUntilCatchUpIdleAsync(grain);
        await grain.SeedEventsAsync(ToSerializableEvents(secondEvents));
        await grain.RefreshAsync();

        var c1Result = await SharedStateStore.GetLatestForVersionAsync(
            PersistenceTestMulti.MultiProjectorName,
            PersistenceTestMulti.MultiProjectorVersion);
        Assert.True(c1Result.IsSuccess);
        Assert.True(c1Result.GetValue().HasValue);
        var c1 = c1Result.GetValue().GetValue();
        Assert.Equal(5_123, c1.EventsProcessed);
        Assert.True(
            string.Compare(c1.LastSortableUniqueId, c0.LastSortableUniqueId, StringComparison.Ordinal) > 0,
            $"Expected C1 {c1.LastSortableUniqueId} to be after C0 {c0.LastSortableUniqueId}.");

        // The next activation must consume the committed restart cursor as its authoritative exclusive read position.
        await grain.RequestDeactivationAsync();
        SharedEventStore.ClearReadAllEventsTracking();
        var freshGrain = _client.GetGrain<IMultiProjectionGrain>(PersistenceTestMulti.MultiProjectorName);
        await freshGrain.GetStatusAsync();
        await WaitUntilAsync(
            () => SharedEventStore.ReadAllSerializableEventSinceValues.Count > 0,
            TimeSpan.FromSeconds(10));

        var firstFreshRead = SharedEventStore.ReadAllSerializableEventSinceValues[0];
        Assert.Equal(c1.LastSortableUniqueId, firstFreshRead);
        var freshStatus = await freshGrain.GetStatusAsync();
        Assert.True(freshStatus.EventsProcessed >= c1.EventsProcessed);
    }

    [Fact]
    public async Task Hot_fetched_checkpoint_for_stream_preapplied_range_is_durable_and_consumed_by_fresh_activation()
    {
        var grain = _client.GetGrain<IMultiProjectionGrain>(PersistenceTestMulti.MultiProjectorName);
        var baseTick = DateTime.UtcNow.AddMinutes(-2).Ticks;
        var firstEvents = Enumerable.Range(0, 123)
            .Select(index => CreatePersistenceEvent(baseTick + index, index))
            .ToList();

        await grain.GetStatusAsync();
        await grain.SeedEventsAsync(ToSerializableEvents(firstEvents));
        await grain.RefreshAsync();
        await WaitUntilCatchUpIdleAsync(grain);
        var initialPersistResult = await grain.PersistStateAsync();
        Assert.True(initialPersistResult.IsSuccess);

        var c0Result = await SharedStateStore.GetLatestForVersionAsync(
            PersistenceTestMulti.MultiProjectorName,
            PersistenceTestMulti.MultiProjectorVersion);
        Assert.True(c0Result.IsSuccess);
        Assert.True(c0Result.GetValue().HasValue);
        var c0 = c0Result.GetValue().GetValue();
        Assert.Equal(123, c0.EventsProcessed);

        // Deliver the range through the production stream subscription before it exists in the authoritative store.
        // The old sortable timestamps make the stream-applied host state eligible for safe-window promotion; delivery
        // alone is not treated as durable evidence.
        var streamBaseTick = DateTime.UtcNow.AddSeconds(-5).Ticks;
        var secondEvents = Enumerable.Range(0, 5_000)
            .Select(index => CreatePersistenceEvent(streamBaseTick + index, 123 + index))
            .ToList();
        var secondSerializableEvents = ToSerializableEvents(secondEvents);
        var stream = _client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(
                ServiceIdGrainKey.BuildStreamNamespace("AllEvents", DefaultServiceIdProvider.DefaultServiceId),
                Guid.Empty));

        await stream.OnNextBatchAsync(secondSerializableEvents);

        await WaitUntilEventsProcessedAsync(
            grain,
            c0.EventsProcessed + secondSerializableEvents.Count,
            TimeSpan.FromSeconds(30));
        var streamedStatus = await grain.GetStatusAsync();
        Assert.Equal(5_123, streamedStatus.EventsProcessed);

        // Put the exact stream-delivered events into the authoritative store only after stream application has finished.
        // The subsequent refresh must therefore fetch a non-empty range whose every event is already ID-filtered.
        var writeResult = await SharedEventStore.WriteSerializableEventsAsync(secondSerializableEvents);
        Assert.True(writeResult.IsSuccess, writeResult.IsSuccess ? string.Empty : writeResult.GetException().ToString());
        SharedEventStore.ClearReadAllEventsTracking();
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SharedEventStore.GateNextSerializableRead(firstReadStarted, releaseFirstRead);
        var refreshTask = grain.RefreshAsync();
        await firstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // The range was unsafe when the refresh captured its C0 start. Let the normal safe-window threshold make it
        // eligible before releasing the first authoritative batch; PersistStateAsync must then promote it at the
        // fetched-count checkpoint.
        await Task.Delay(TimeSpan.FromSeconds(18));
        releaseFirstRead.TrySetResult();
        await refreshTask;
        await WaitUntilCatchUpIdleAsync(grain);

        var c1Result = await SharedStateStore.GetLatestForVersionAsync(
            PersistenceTestMulti.MultiProjectorName,
            PersistenceTestMulti.MultiProjectorVersion);
        Assert.True(c1Result.IsSuccess);
        Assert.True(c1Result.GetValue().HasValue);
        var c1 = c1Result.GetValue().GetValue();

        // These assertions use the committed restart record and the activation-local processed count. They do not
        // infer durability from the catch-up telemetry's PersistTriggered flag.
        Assert.Equal(5_123, streamedStatus.EventsProcessed);
        Assert.Equal(5_123, c1.EventsProcessed);
        Assert.Equal(secondSerializableEvents[^1].SortableUniqueIdValue, c1.LastSortableUniqueId);
        Assert.True(
            string.Compare(c1.LastSortableUniqueId, c0.LastSortableUniqueId, StringComparison.Ordinal) > 0,
            $"Expected durable C1 {c1.LastSortableUniqueId} to be after C0 {c0.LastSortableUniqueId}.");

        // A genuinely fresh activation must consume the committed C1, not the ephemeral ID cache from the previous
        // activation. Its first authoritative read is recorded by the real event store.
        await grain.RequestDeactivationAsync();
        SharedEventStore.ClearReadAllEventsTracking();
        var freshGrain = _client.GetGrain<IMultiProjectionGrain>(PersistenceTestMulti.MultiProjectorName);
        await freshGrain.GetStatusAsync();
        await WaitUntilAsync(
            () => SharedEventStore.ReadAllSerializableEventSinceValues.Count > 0,
            TimeSpan.FromSeconds(10));

        var firstFreshRead = SharedEventStore.ReadAllSerializableEventSinceValues[0];
        // ReadAllSerializableEventsAsync treats `since` as an exclusive cursor, so this proves the returned
        // authoritative range begins strictly after committed C1 rather than replaying the committed tail.
        Assert.Equal(c1.LastSortableUniqueId, firstFreshRead);
        var freshStatus = await freshGrain.GetStatusAsync();
        Assert.True(freshStatus.EventsProcessed >= c1.EventsProcessed);
    }

    [Fact]
    public async Task Orleans_Should_Isolate_TagConsistentGrain_By_ServiceId()
    {
        var tagId = "order:123";
        var tenantA = ServiceIdGrainKey.Build("tenant-a", tagId);
        var tenantB = ServiceIdGrainKey.Build("tenant-b", tagId);

        var grainA = _client.GetGrain<ITagConsistentGrain>(tenantA);
        var grainB = _client.GetGrain<ITagConsistentGrain>(tenantB);

        var reservationA = await grainA.MakeReservationAsync(string.Empty);
        var reservationB = await grainB.MakeReservationAsync(string.Empty);

        Assert.True(reservationA.IsSuccess);
        Assert.True(reservationB.IsSuccess);
        Assert.Equal(tagId, reservationA.GetValue().Tag);
        Assert.Equal(tagId, reservationB.GetValue().Tag);
        Assert.Equal(tagId, await grainA.GetTagActorIdAsync());
        Assert.Equal(tagId, await grainB.GetTagActorIdAsync());
    }

    [Fact]
    public async Task Orleans_Should_Isolate_TagStateGrain_By_ServiceId()
    {
        var tagStateId = "order:123:projector";
        var tenantA = ServiceIdGrainKey.Build("tenant-a", tagStateId);
        var tenantB = ServiceIdGrainKey.Build("tenant-b", tagStateId);

        var grainA = _client.GetGrain<ITagStateGrain>(tenantA);
        var grainB = _client.GetGrain<ITagStateGrain>(tenantB);

        Assert.Equal(tagStateId, await grainA.GetTagStateActorIdAsync());
        Assert.Equal(tagStateId, await grainB.GetTagStateActorIdAsync());
    }

    [Fact]
    public void DefaultOrleansEventSubscriptionResolver_Should_Separate_StreamNamespace_By_ServiceId()
    {
        var resolver = new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty);
        var tenantKey = ServiceIdGrainKey.Build("tenant-a", "projector");

        var tenantStream = resolver.Resolve(tenantKey) as OrleansSekibanStream;
        var defaultStream = resolver.Resolve("projector") as OrleansSekibanStream;

        Assert.NotNull(tenantStream);
        Assert.NotNull(defaultStream);
        Assert.Equal("tenant-a|AllEvents", tenantStream!.StreamNamespace);
        Assert.Equal("AllEvents", defaultStream!.StreamNamespace);
    }

    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    // Add required domain types for Orleans
                    services.AddSingleton<DcbDomainTypes>(provider =>
                    {
                        var eventTypes = new SimpleEventTypes();
                        eventTypes.RegisterEventType<TestProjectionEvent>();
                        var tagTypes = new SimpleTagTypes();
                        var tagProjectorTypes = new SimpleTagProjectorTypes();
                        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
                        var multiProjectorTypes = new SimpleMultiProjectorTypes();
                        var queryTypes = new SimpleQueryTypes();
                        multiProjectorTypes.RegisterProjector<EmptyTestMultiProjector>();
                        multiProjectorTypes.RegisterProjector<TestProjectorMulti>();
                        multiProjectorTypes.RegisterProjector<Projector1Multi>();
                        multiProjectorTypes.RegisterProjector<Projector2Multi>();
                        multiProjectorTypes.RegisterProjector<SubscriptionTestMulti>();
                        multiProjectorTypes.RegisterProjector<SerializationTestMulti>();
                        multiProjectorTypes.RegisterProjector<PersistenceTestMulti>();

                        return new DcbDomainTypes(
                            eventTypes,
                            tagTypes,
                            tagProjectorTypes,
                            tagStatePayloadTypes,
                            multiProjectorTypes,
                            queryTypes,
                            new JsonSerializerOptions());
                    });

                    // Add storage
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton(SharedStateStore);
                    services.AddSingleton<IMultiProjectionStateStore>(provider => provider.GetRequiredService<Sekiban.Dcb.Testing.InMemoryMultiProjectionStateStore>());
                    services.AddSingleton<IProjectionStatusStore>(SharedStatusStore);
                    services.AddSingleton(SharedStatusOptions);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    // Add mock IBlobStorageSnapshotAccessor for tests
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    // Add event statistics for MultiProjectionGrain
                    services.AddTransient<Sekiban.Dcb.MultiProjections.IMultiProjectionEventStatistics, Sekiban.Dcb.MultiProjections.NoOpMultiProjectionEventStatistics>();
                    // Add actor options for MultiProjectionGrain
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
    }

    private sealed class FixedPortAllocator(int baseSiloPort, int baseGatewayPort) : ITestClusterPortAllocator
    {
        public (int, int) AllocateConsecutivePortPairs(int numPorts) => (baseSiloPort, baseGatewayPort);

        public void Dispose() { }
    }

    private sealed class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddMemoryStreams("EventStreamProvider");
        }
    }


    private record EmptyTestMultiProjector : IMultiProjector<EmptyTestMultiProjector>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "empty-test";
        public static EmptyTestMultiProjector GenerateInitialPayload() => new();
        public static ResultBox<EmptyTestMultiProjector> Project(
            EmptyTestMultiProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record TestProjectorMulti : IMultiProjector<TestProjectorMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "test-projector";
        public static TestProjectorMulti GenerateInitialPayload() => new();
        public static ResultBox<TestProjectorMulti> Project(TestProjectorMulti payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
                ResultBox.FromValue(payload);
    }

    private record Projector1Multi : IMultiProjector<Projector1Multi>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "projector-1";
        public static Projector1Multi GenerateInitialPayload() => new();
        public static ResultBox<Projector1Multi> Project(Projector1Multi payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
                ResultBox.FromValue(payload);
    }

    private record Projector2Multi : IMultiProjector<Projector2Multi>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "projector-2";
        public static Projector2Multi GenerateInitialPayload() => new();
        public static ResultBox<Projector2Multi> Project(Projector2Multi payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
                ResultBox.FromValue(payload);
    }

    private record SubscriptionTestMulti : IMultiProjector<SubscriptionTestMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "subscription-test";
        public static SubscriptionTestMulti GenerateInitialPayload() => new();
        public static ResultBox<SubscriptionTestMulti>
            Project(SubscriptionTestMulti payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record SerializationTestMulti : IMultiProjector<SerializationTestMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "serialization-test";
        public static SerializationTestMulti GenerateInitialPayload() => new();
        public static ResultBox<SerializationTestMulti> Project(
            SerializationTestMulti payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record PersistenceTestMulti : IMultiProjector<PersistenceTestMulti>
    {
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "persistence-test";
        public static PersistenceTestMulti GenerateInitialPayload() => new();
        public static ResultBox<PersistenceTestMulti>
            Project(PersistenceTestMulti payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    private record TestProjectionEvent(int Value) : IEventPayload;

    private static Event CreatePersistenceEvent(long tick, int value) => new(
        new TestProjectionEvent(value),
        new SortableUniqueId(
            SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty)),
        nameof(TestProjectionEvent),
        Guid.CreateVersion7(),
        new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "persistence-test"),
        new List<string>());

    private static CountingInMemoryEventStore CreateSharedEventStore()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<TestProjectionEvent>();
        return new CountingInMemoryEventStore(eventTypes);
    }

    private class CountingInMemoryEventStore : IEventStore
    {
        private readonly InMemoryEventStore _inner;
        private readonly object _lock = new();
        private readonly List<int?> _readAllEventsMaxCounts = new();
        private readonly List<int?> _readAllSerializableEventsMaxCounts = new();
        private readonly List<string?> _readAllSerializableEventSinceValues = new();
        private TaskCompletionSource? _nextSerializableReadStarted;
        private TaskCompletionSource? _nextSerializableReadRelease;
        private bool _nextSerializableReadGated;

        public CountingInMemoryEventStore(IEventTypes eventTypes)
        {
            _inner = new InMemoryEventStore(eventTypes);
        }

        public int ReadAllEventsCallCount { get; private set; }
        public int ReadAllSerializableEventsCallCount { get; private set; }

        public IReadOnlyList<int?> ReadAllEventsMaxCounts
        {
            get
            {
                lock (_lock)
                {
                    return _readAllEventsMaxCounts.ToList();
                }
            }
        }

        public IReadOnlyList<int?> ReadAllSerializableEventsMaxCounts
        {
            get
            {
                lock (_lock)
                {
                    return _readAllSerializableEventsMaxCounts.ToList();
                }
            }
        }

        public IReadOnlyList<string?> ReadAllSerializableEventSinceValues
        {
            get
            {
                lock (_lock)
                {
                    return _readAllSerializableEventSinceValues.ToList();
                }
            }
        }

        public void Clear() => _inner.Clear();

        public void ClearReadAllEventsTracking()
        {
            lock (_lock)
            {
                ReadAllEventsCallCount = 0;
                _readAllEventsMaxCounts.Clear();
                ReadAllSerializableEventsCallCount = 0;
                _readAllSerializableEventsMaxCounts.Clear();
                _readAllSerializableEventSinceValues.Clear();
            }
        }

        public void GateNextSerializableRead(
            TaskCompletionSource readStarted,
            TaskCompletionSource releaseRead)
        {
            lock (_lock)
            {
                _nextSerializableReadStarted = readStarted;
                _nextSerializableReadRelease = releaseRead;
                _nextSerializableReadGated = true;
            }
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null, int? maxCount = null)
        {
            lock (_lock)
            {
                ReadAllEventsCallCount++;
                _readAllEventsMaxCounts.Add(maxCount);
            }

            return _inner.ReadAllEventsAsync(since, maxCount);
        }

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null)
            => ReadAllSerializableEventsAsync(since, maxCount: null);

        public async Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount)
        {
            lock (_lock)
            {
                ReadAllSerializableEventsCallCount++;
                _readAllSerializableEventsMaxCounts.Add(maxCount);
                _readAllSerializableEventSinceValues.Add(since?.Value);
            }

            TaskCompletionSource? readStarted = null;
            TaskCompletionSource? releaseRead = null;
            lock (_lock)
            {
                if (_nextSerializableReadGated)
                {
                    _nextSerializableReadGated = false;
                    readStarted = _nextSerializableReadStarted;
                    releaseRead = _nextSerializableReadRelease;
                    _nextSerializableReadStarted = null;
                    _nextSerializableReadRelease = null;
                }
            }

            if (readStarted is not null && releaseRead is not null)
            {
                readStarted.TrySetResult();
                await releaseRead.Task;
            }

            return await _inner.ReadAllSerializableEventsAsync(since, maxCount);
        }

        public Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null) =>
            _inner.ReadEventsByTagAsync(tag, since);

        public Task<ResultBox<Event>> ReadEventAsync(Guid eventId) => _inner.ReadEventAsync(eventId);

        public Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
            IEnumerable<Event> events) => _inner.WriteEventsAsync(events);

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);

        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);

        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);

        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId)
            => _inner.ReadSerializableEventAsync(eventId);

        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => _inner.WriteSerializableEventsAsync(events);
    }

    private static IReadOnlyList<SerializableEvent> ToSerializableEvents(IEnumerable<Event> events) =>
        events
            .Select(e => new SerializableEvent(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
                e.SortableUniqueIdValue,
                e.Id,
                e.EventMetadata,
                e.Tags.ToList(),
                e.EventType))
            .ToList();

    private sealed class TestProjectionStatusStore : IProjectionStatusStore
    {
        private readonly IProjectionStatusStore _inner;
        private readonly ConcurrentQueue<DateTimeOffset> _upsertCallTimes = new();
        private int _upsertCalls;
        private int _sawCancelableToken;
        private int _throwOnUpsert;

        public TestProjectionStatusStore(IProjectionStatusStore inner) => _inner = inner;

        public TimeSpan Delay { get; set; }
        public bool ThrowOnUpsert
        {
            get => Volatile.Read(ref _throwOnUpsert) != 0;
            set => Volatile.Write(ref _throwOnUpsert, value ? 1 : 0);
        }

        public int UpsertCalls => Volatile.Read(ref _upsertCalls);
        public bool SawCancelableToken => Volatile.Read(ref _sawCancelableToken) != 0;
        public IReadOnlyList<DateTimeOffset> UpsertCallTimes => _upsertCallTimes.ToArray();

        public async Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
            ProjectionStatusHeartbeat heartbeat,
            long expectedSequence,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _upsertCalls);
            _upsertCallTimes.Enqueue(DateTimeOffset.UtcNow);
            if (cancellationToken.CanBeCanceled)
            {
                Interlocked.Exchange(ref _sawCancelableToken, 1);
            }

            if (ThrowOnUpsert)
            {
                throw new InvalidOperationException("Injected projection status registry failure.");
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return await _inner.UpsertAsync(heartbeat, expectedSequence, cancellationToken);
        }

        public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
            string? projectorName = null,
            string? projectorVersion = null,
            CancellationToken cancellationToken = default) =>
            _inner.ListAsync(projectorName, projectorVersion, cancellationToken);

        public void Reset()
        {
            Delay = TimeSpan.Zero;
            ThrowOnUpsert = false;
            Interlocked.Exchange(ref _upsertCalls, 0);
            Interlocked.Exchange(ref _sawCancelableToken, 0);
            while (_upsertCallTimes.TryDequeue(out _))
            {
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), $"Condition was not met within {timeout}.");
    }

    private static async Task WaitUntilCatchUpIdleAsync(IMultiProjectionGrain grain)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (!(await grain.GetCatchUpStatusAsync()).IsActive)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.False((await grain.GetCatchUpStatusAsync()).IsActive, "Catch-up did not become idle in time.");
    }

    private static async Task WaitUntilEventsProcessedAsync(
        IMultiProjectionGrain grain,
        long expectedEventsProcessed,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((await grain.GetStatusAsync()).EventsProcessed >= expectedEventsProcessed)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(
            (await grain.GetStatusAsync()).EventsProcessed >= expectedEventsProcessed,
            $"Expected at least {expectedEventsProcessed} stream-applied events within {timeout}.");
    }

    private static void ResetStatusOptions()
    {
        SharedStatusOptions.HeartbeatInterval = TimeSpan.FromMilliseconds(100);
        SharedStatusOptions.HeartbeatWriteTimeout = TimeSpan.FromMilliseconds(50);
        SharedStatusOptions.HeartbeatFailureLogInterval = TimeSpan.FromSeconds(1);
        SharedStatusOptions.HeartbeatRetryBase = TimeSpan.FromSeconds(1);
        SharedStatusOptions.HeartbeatRetryCap = TimeSpan.FromSeconds(30);
        SharedStatusOptions.Enabled = true;
    }
}
