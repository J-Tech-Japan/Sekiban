using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G14 operator-only admin surface: <c>ResetProjectionFaultAsync</c>. Drives the REAL grain method on a faulted
///     grain. The reset closes the early-healthy window by itself (in-activation host recreation + first-query barrier),
///     so these tests do NOT deactivate manually except where the point IS a reactivation restore. The three token
///     fields are validated as one atomic precondition against the persisted descriptor inside the single-writer gate;
///     a correct reset also invalidates the derived external snapshot so a rebuild starts from the beginning.
/// </summary>
public class ProjectionFaultResetOrleansTests : IAsyncLifetime
{
    private static readonly InMemoryEventStore SharedEventStore = new();
    private static readonly InMemoryMultiProjectionStateStore SharedStateStore = new();
    private static readonly FailableStateStore StateStore = new(SharedStateStore);
    internal static volatile bool PoisonActive = true;
    private TestCluster _cluster = null!;
    private ISekibanExecutor _executor = null!;
    private IClusterClient Client => _cluster.Client;

    internal static DcbDomainTypes CreateDomain()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<ResetTriggerEvent>();
        var mp = new SimpleMultiProjectorTypes();
        mp.RegisterProjector<ResettableProjector>();
        var q = new SimpleQueryTypes();
        q.RegisterQuery<ResetCountQuery>();
        q.RegisterListQuery<ResetRowListQuery>();
        return new DcbDomainTypes(
            eventTypes,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            mp,
            q,
            new JsonSerializerOptions());
    }

    public async Task InitializeAsync()
    {
        SharedEventStore.Clear();
        PoisonActive = true;
        await SharedStateStore.DeleteAllAsync(ResettableProjector.MultiProjectorName);
        TogglableGrainStorage.Reset();
        StateStore.FailNextDelete = false;
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var id = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"ResetCluster-{id}";
        builder.Options.ServiceId = $"ResetService-{id}";
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
        _executor = new OrleansDcbExecutor(Client, SharedEventStore, CreateDomain());
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.StopAllSilosAsync();
        }
    }

    private static SerializableEvent Event(bool poison, long tick, Guid id) =>
        new(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ResetTriggerEvent(poison))),
            new SortableUniqueId(SortableUniqueId.GetTickString(tick) + SortableUniqueId.GetIdString(Guid.Empty)).Value,
            id,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
            [],
            nameof(ResetTriggerEvent));

    private static async Task InjectExternalSnapshotAsync(string position)
    {
        var request = new MultiProjectionStateWriteRequest(
            ResettableProjector.MultiProjectorName,
            ResettableProjector.MultiProjectorVersion,
            nameof(ResettableProjector),
            position,
            EventsProcessed: 1,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: 4,
            CompressedSizeBytes: 4,
            SafeWindowThreshold: position,
            CreatedAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            BuildSource: "test",
            BuildHost: null);
        using var payload = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        Assert.True((await SharedStateStore.UpsertFromStreamAsync(request, payload, 1_000_000)).IsSuccess);
    }

    private static async Task PollUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 60; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("condition not met within the poll window");
    }

    private async Task<IMultiProjectionGrain> ReactivateAsync(IMultiProjectionGrain grain, Func<IMultiProjectionGrain, Task<bool>> until)
    {
        await grain.RequestDeactivationAsync();
        var reactivated = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await PollUntilAsync(() => until(reactivated));
        return reactivated;
    }

    private async Task<(IMultiProjectionGrain Grain, ResetProjectionFaultRequest Token, SerializableEvent Poison)> FaultAndTokenAsync(long tick)
    {
        var id = Guid.CreateVersion7();
        var ev = Event(poison: true, tick, id);
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { ev });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess); // faulted
        var token = new ResetProjectionFaultRequest(ResettableProjector.MultiProjectorName, id.ToString(), ev.SortableUniqueIdValue);
        return (grain, token, ev);
    }

    // ---- token validation: each field is part of one atomic precondition; any mismatch rejects with zero effect ----

    [Fact]
    public async Task WrongProjector_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_000);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { ProjectorName = "some-other-projector" });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task WrongEventId_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_100);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { FaultEventId = Guid.NewGuid().ToString() });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task WrongPosition_IsRejected_NoWrite_FaultRetained()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_200);
        var writesBefore = TogglableGrainStorage.WriteCount;

        var result = await grain.ResetProjectionFaultAsync(token with { FaultPosition = "a-different-position" });

        Assert.False(result.IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task SameTokenRace_AtMostOneSucceeds()
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_300);
        PoisonActive = false;

        var a = grain.ResetProjectionFaultAsync(token);
        var b = grain.ResetProjectionFaultAsync(token);
        var results = await Task.WhenAll(a, b);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
    }

    // ---- reset semantics: the reset ALONE closes the early-healthy window (no manual deactivation) ----

    [Fact]
    public async Task CorrectToken_FoldableAfterFix_RebuildsImmediately_ExactRecovery_NoEarlyHealthyWindow()
    {
        var (grain, token, poison) = await FaultAndTokenAsync(2_000);
        PoisonActive = false; // the event now folds cleanly

        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // No manual deactivation: the very next query on the SAME grain rebuilds via the barrier before answering.
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(1, count.GetValue().Count);                                   // exact scalar value

        var list = await _executor.QueryAsync(new ResetRowListQuery());
        Assert.True(list.IsSuccess);
        Assert.Single(list.GetValue().Items);                                      // exact list count

        // The state snapshot serialized successfully (recovered, no fault) and reflects the recovered position.
        var snapshot = await grain.GetSnapshotJsonAsync();
        Assert.True(snapshot.IsSuccess);
        Assert.Contains(poison.SortableUniqueIdValue, snapshot.GetValue());
    }

    [Fact]
    public async Task CorrectToken_PoisonRemains_RebuildReFaultsImmediately_QueriesRejected_DescriptorPersisted()
    {
        var (grain, token, _) = await FaultAndTokenAsync(3_000);

        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // Poison is still poison: the immediate rebuild re-encounters it and re-faults, on the SAME grain.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);

        // The re-fault is persisted: a genuine fresh activation restores it and fails closed.
        var reactivated = await ReactivateAsync(grain, async g => !(await g.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await reactivated.GetSnapshotJsonAsync()).IsSuccess);
    }

    [Fact]
    public async Task FailFirstPersistedClearWrite_QueriesRemainRejected_ThenRetrySucceeds_ExactRecovery()
    {
        var (grain, token, _) = await FaultAndTokenAsync(4_000);
        PoisonActive = false;

        TogglableGrainStorage.FailNextWrite = true;
        Assert.False((await grain.ResetProjectionFaultAsync(token)).IsSuccess);

        // Before storage recovery: descriptor + live fault retained, every surface rejected.
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetCountQuery())).IsSuccess);
        Assert.False((await _executor.QueryAsync(new ResetRowListQuery())).IsSuccess);

        // Retry the SAME correct token through the real method: reset commits and rebuilds immediately.
        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(1, count.GetValue().Count);
    }

    // ---- external snapshot invalidation: a full rebuild cannot restore a pre-poison derived snapshot ----

    [Theory]
    [InlineData(0, false)] // empty projector
    [InlineData(1, false)] // empty event id
    [InlineData(2, false)] // empty position
    [InlineData(0, true)]  // null projector
    [InlineData(1, true)]  // null event id
    [InlineData(2, true)]  // null position
    public async Task MissingOrEmptyTokenField_IsRejected_NoEffect(int field, bool useNull)
    {
        var (grain, token, _) = await FaultAndTokenAsync(1_400 + field + (useNull ? 100 : 0));
        var writesBefore = TogglableGrainStorage.WriteCount;
        var snapshotBefore = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue;

        // Only the field under test is missing (null or empty); the other two carry the real token value.
        var missing = useNull ? null! : "";
        var request = new ResetProjectionFaultRequest(
            field == 0 ? missing : token.ProjectorName,
            field == 1 ? missing : token.FaultEventId,
            field == 2 ? missing : token.FaultPosition);

        var result = await grain.ResetProjectionFaultAsync(request);

        Assert.False(result.IsSuccess);                                   // rejected
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);     // zero provider write
        Assert.Equal(snapshotBefore, (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // zero external delete
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);     // fault retained
    }

    // ---- Blocker 3/4: descriptor persists on the production RefreshAsync path (no timing assistance); a GENUINE
    // product-persisted pre-poison snapshot restores before the poison, then is invalidated by the reset ----

    [Fact]
    public async Task GenuineExternalSnapshot_RestoresHealthy_ThenPoisonFaultsOnProductionPath_ResetInvalidatesAndRebuildsExactly()
    {
        var healthy = Event(poison: false, tick: 6_000, Guid.CreateVersion7());
        var poison = Event(poison: true, tick: 6_001, Guid.CreateVersion7());
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { healthy });
        await grain.RefreshAsync();

        // A GENUINE derived snapshot is persisted by the product (not synthetic bytes).
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        var snap = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue();
        Assert.True(snap.HasValue);
        Assert.Equal(healthy.SortableUniqueIdValue, snap.GetValue().LastSortableUniqueId); // pre-poison position

        // It genuinely restores healthy before the poison: a fresh activation comes up healthy at Count 1.
        var restored = await ReactivateAsync(grain, async g => (await g.GetSnapshotJsonAsync()).IsSuccess);
        Assert.Equal(1, (await _executor.QueryAsync(new ResetCountQuery())).GetValue().Count);

        // Now the poison arrives. A fresh activation restores the pre-poison snapshot, and its FIRST query
        // synchronously catches up through the poison via the barrier, folds it, faults, and persists the descriptor
        // in RefreshAsync's finally — the first query fails without any timing assistance.
        await restored.SeedEventsAsync(new List<SerializableEvent> { poison });
        var faulted = await ReactivateAsync(restored, async g => !(await g.GetSnapshotJsonAsync()).IsSuccess);
        Assert.False((await faulted.GetSnapshotJsonAsync()).IsSuccess); // faulted, descriptor persisted on the production path

        // The fix now lets the projector fold the previously-poison event; reset with the correct token.
        PoisonActive = false;
        var token = new ResetProjectionFaultRequest(ResettableProjector.MultiProjectorName, poison.Id.ToString(), poison.SortableUniqueIdValue);
        Assert.True((await faulted.ResetProjectionFaultAsync(token)).IsSuccess); // token validated against the PERSISTED descriptor

        // The genuine pre-poison snapshot is invalidated — a stale restore is now impossible.
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        // Exact rebuild from the beginning: both events fold now (Count 2), position advanced to the poison.
        var count = await _executor.QueryAsync(new ResetCountQuery());
        Assert.True(count.IsSuccess);
        Assert.Equal(2, count.GetValue().Count);
        var list = await _executor.QueryAsync(new ResetRowListQuery());
        Assert.True(list.IsSuccess);
        Assert.Equal(2, list.GetValue().Items.Count());
        var rebuilt = await faulted.GetSnapshotJsonAsync();
        Assert.True(rebuilt.IsSuccess);
        Assert.Contains(poison.SortableUniqueIdValue, rebuilt.GetValue());
    }

    // ---- cross-store partial failures: external-first ordering keeps every outcome retry-coherent ----

    [Fact]
    public async Task ExternalInvalidationFails_ResetRejected_NothingChanged_ThenRetrySucceeds()
    {
        var (grain, token, _) = await FaultAndTokenAsync(7_000);
        await InjectExternalSnapshotAsync("000000000000000700000000000000"); // a snapshot exists to observe retention
        var snapshotBefore = (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue;
        var writesBefore = TogglableGrainStorage.WriteCount;

        // The external invalidation runs first, under the gate, before the grain write. If it fails, the grain write is
        // skipped: descriptor, live fault and the external snapshot are all retained.
        StateStore.FailNextDelete = true;
        Assert.False((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount);                 // zero grain write
        Assert.Equal(snapshotBefore, (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // snapshot retained
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);                 // still faulted

        // Storage recovers: the same token retries and completes (poison fixed so the rebuild recovers).
        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);
    }

    [Fact]
    public async Task GrainWriteFailsAfterExternalDelete_DescriptorAndFaultRetained_SnapshotDeleted_ThenRetrySucceeds()
    {
        var (grain, token, _) = await FaultAndTokenAsync(7_100);
        await InjectExternalSnapshotAsync("000000000000000710000000000000");
        Assert.True((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        // The external delete succeeds, then the grain-state clear write fails: the grain state rolls back (descriptor
        // and live fault retained, queries stay faulted), but the external snapshot is already gone. Coherent — a
        // rebuild would regenerate the snapshot; the retained descriptor keeps the projection fail-closed until retry.
        TogglableGrainStorage.FailNextWrite = true;
        Assert.False((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue); // snapshot deleted
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);                 // descriptor retained -> still faulted

        // Retry the same token: the reset completes and rebuilds.
        PoisonActive = false;
        Assert.True((await grain.ResetProjectionFaultAsync(token)).IsSuccess);
        Assert.Equal(1, (await _executor.QueryAsync(new ResetCountQuery())).GetValue().Count);
    }

    [Fact]
    public async Task NormalPersist_WhileFaulted_WritesNoExternalSnapshot_AdvancesNoMetadata()
    {
        // A faulted projection makes no safe-checkpoint progress AND the external upsert is fault-gated, so a normal
        // persist reaches NO external upsert (the store delegate is never invoked) and advances no persisted snapshot —
        // there is no upsert to race the reset's delete. Asserting the upsert count is unchanged (not merely "no
        // snapshot exists") makes the rejection non-vacuous, and the projection stays faulted afterwards.
        var (grain, _, _) = await FaultAndTokenAsync(8_000);

        var upsertsBefore = StateStore.UpsertCount;
        Assert.True((await grain.PersistStateAsync()).IsSuccess);         // succeeds by skip, not by writing
        Assert.Equal(upsertsBefore, StateStore.UpsertCount);             // no external upsert reached the store
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess);     // still faulted
    }

    [Fact]
    public async Task VersionRewrite_WhileFaulted_IsRejectedAtFaultGate_NoUpsert_NoVersionWrite_SnapshotRetained()
    {
        // Build + persist cleanly first, so the committed ProjectorVersion is a real "1.0" and a genuine v1.0 external
        // snapshot exists. This is what makes the rewrite below NON-vacuous: OverwritePersistedStateVersionAsync finds
        // a source snapshot and a non-empty current version, so it actually REACHES the coordinated upsert instead of
        // short-circuiting on a missing source.
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: false, 8_100, Guid.CreateVersion7()) });
        await grain.RefreshAsync();
        Assert.True((await grain.PersistStateAsync()).IsSuccess);
        await PollUntilAsync(async () =>
            (await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, "1.0")).GetValue().HasValue);

        // Now fault the projection with a poison event.
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: true, 8_150, Guid.CreateVersion7()) });
        await grain.RefreshAsync();
        Assert.False((await grain.GetSnapshotJsonAsync()).IsSuccess); // faulted

        // The rewrite reaches the coordinated upsert, which returns a fault-blocked Error. So: updated stays false, the
        // store's upsert delegate is never invoked, NO MetadataMaintenance projector-version write happens, no new-version
        // snapshot is created, and the original v1.0 snapshot is retained.
        var upsertsBefore = StateStore.UpsertCount;
        var writesBefore = TogglableGrainStorage.WriteCount;

        var updated = await grain.OverwritePersistedStateVersionAsync("2.0");

        Assert.False(updated);
        Assert.Equal(upsertsBefore, StateStore.UpsertCount);          // upsert never reached the store
        Assert.Equal(writesBefore, TogglableGrainStorage.WriteCount); // no projector-version write
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, "2.0")).GetValue().HasValue); // no new-version snapshot
        Assert.True((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, "1.0")).GetValue().HasValue);  // original retained
    }

    [Fact]
    public async Task DeleteExternalStateAsync_RoutedThroughCoordinator_RemovesTheSnapshot()
    {
        // Path coverage for the public DeleteExternalStateAsync, which now routes its delete through the same
        // coordinator as every other external mutation (no direct-DeleteAsync bypass remains). Same-grain calls are
        // serialised by Orleans non-reentrancy, so the delete-vs-upsert race the coordinator actually guards can only be
        // driven by the interleaving catch-up timer; that serialisation is pinned deterministically by the
        // ExternalStoreCoordinator friend tests (delete-waits-for-upsert and upsert-waits-for-delete). Here we assert the
        // end-to-end effect: a routed delete removes the derived snapshot.
        var grain = Client.GetGrain<IMultiProjectionGrain>(ResettableProjector.MultiProjectorName);
        await grain.SeedEventsAsync(new List<SerializableEvent> { Event(poison: false, 8_200, Guid.CreateVersion7()) });
        await grain.RefreshAsync(); // initialise host so DeleteExternalStateAsync has projector name + version
        await InjectExternalSnapshotAsync("000000000000000820000000000000");
        Assert.True((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);

        var deleted = await grain.DeleteExternalStateAsync();

        Assert.True(deleted);
        Assert.False((await SharedStateStore.GetLatestForVersionAsync(ResettableProjector.MultiProjectorName, ResettableProjector.MultiProjectorVersion)).GetValue().HasValue);
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ => CreateDomain());
                    services.AddSingleton<IEventStore>(SharedEventStore);
                    services.AddSingleton<IMultiProjectionStateStore>(StateStore);
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor, MockBlobStorageSnapshotAccessor>();
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 1 });
                    services.AddSekibanDcbNativeRuntime();
                    services.AddGrainStorage("OrleansStorage", (_, _) => new TogglableGrainStorage());
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }

    /// <summary>Delegates to a real in-memory state store, but can fail the next DeleteAsync to model an external-store failure.</summary>
    private sealed class FailableStateStore : IMultiProjectionStateStore
    {
        private readonly IMultiProjectionStateStore _inner;
        public volatile bool FailNextDelete;
        // Counts real upsert-delegate invocations. A fault-gated persist must NEVER reach the store, so an unchanged
        // count across a persist proves rejection at the coordinator — stronger than merely "no v2 snapshot exists".
        private int _upsertCount;
        public int UpsertCount => Volatile.Read(ref _upsertCount);

        public FailableStateStore(IMultiProjectionStateStore inner) => _inner = inner;

        public Task<ResultBox<bool>> DeleteAsync(string projectorName, string projectorVersion, CancellationToken cancellationToken = default)
        {
            if (FailNextDelete)
            {
                FailNextDelete = false;
                return Task.FromResult(ResultBox.Error<bool>(new InvalidOperationException("injected: external snapshot delete failure")));
            }

            return _inner.DeleteAsync(projectorName, projectorVersion, cancellationToken);
        }

        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(string projectorName, string projectorVersion, CancellationToken cancellationToken = default) =>
            _inner.GetLatestForVersionAsync(projectorName, projectorVersion, cancellationToken);
        public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(string projectorName, CancellationToken cancellationToken = default) =>
            _inner.GetLatestAnyVersionAsync(projectorName, cancellationToken);
        public Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord record, int offloadThresholdBytes = 1_000_000, CancellationToken cancellationToken = default) =>
            _inner.UpsertAsync(record, offloadThresholdBytes, cancellationToken);
        public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken cancellationToken = default) =>
            _inner.ListAllAsync(cancellationToken);
        public Task<ResultBox<int>> DeleteAllAsync(string projectorName, CancellationToken cancellationToken = default) =>
            _inner.DeleteAllAsync(projectorName, cancellationToken);
        public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record, CancellationToken cancellationToken = default) =>
            _inner.OpenStateDataReadStreamAsync(record, cancellationToken);
        public Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest request, Stream stream, int offloadThresholdBytes, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _upsertCount);
            return _inner.UpsertFromStreamAsync(request, stream, offloadThresholdBytes, cancellationToken);
        }
    }

    /// <summary>An in-memory grain storage that really persists (so reactivation restores) and can fail the next write.</summary>
    private sealed class TogglableGrainStorage : IGrainStorage
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, object?> Store = new();
        public static int WriteCount;
        public static bool FailNextWrite;

        public static void Reset()
        {
            lock (Sync)
            {
                Store.Clear();
                WriteCount = 0;
                FailNextWrite = false;
            }
        }

        public Task ReadStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                if (Store.TryGetValue(grainId.ToString(), out var saved) && saved is T typed)
                {
                    grainState.State = typed;
                    grainState.RecordExists = true;
                }
                else
                {
                    grainState.RecordExists = false;
                }
            }

            return Task.CompletedTask;
        }

        public Task WriteStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                if (FailNextWrite)
                {
                    FailNextWrite = false;
                    throw new InvalidOperationException("injected: reset persisted clear write failure");
                }

                Store[grainId.ToString()] = grainState.State;
                WriteCount++;
            }

            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string grainType, GrainId grainId, IGrainState<T> grainState)
        {
            lock (Sync)
            {
                Store.Remove(grainId.ToString());
            }

            return Task.CompletedTask;
        }
    }

    internal record ResetTriggerEvent(bool Poison) : IEventPayload;

    internal record ResetCountResult(int Count);

    internal record ResetCountQuery : IMultiProjectionQuery<ResettableProjector, ResetCountQuery, ResetCountResult>
    {
        public static ResultBox<ResetCountResult> HandleQuery(
            ResettableProjector projector,
            ResetCountQuery query,
            IQueryContext context) => ResultBox.FromValue(new ResetCountResult(projector.Count));
    }

    internal record ResetRow(int Value);

    internal record ResetRowListQuery :
        IMultiProjectionListQuery<ResettableProjector, ResetRowListQuery, ResetRow>,
        IQueryPagingParameter
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        public static ResultBox<IEnumerable<ResetRow>> HandleFilter(
            ResettableProjector projector,
            ResetRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(Enumerable.Range(0, projector.Count).Select(i => new ResetRow(i)));

        public static ResultBox<IEnumerable<ResetRow>> HandleSort(
            IEnumerable<ResetRow> filtered,
            ResetRowListQuery query,
            IQueryContext context) => ResultBox.FromValue(filtered);
    }

    /// <summary>A projector that folds a poison event only while <see cref="PoisonActive" /> is set — so a test can "fix" it.</summary>
    internal record ResettableProjector : IMultiProjector<ResettableProjector>
    {
        public int Count { get; init; }
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "resettable-fault-projector";
        public static ResettableProjector GenerateInitialPayload() => new();

        public static ResultBox<ResettableProjector> Project(
            ResettableProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold)
        {
            if (ev.Payload is ResetTriggerEvent { Poison: true } && PoisonActive)
            {
                throw new InvalidOperationException("poison event: refuses to fold while poison is active");
            }

            return ResultBox.FromValue(payload with { Count = payload.Count + 1 });
        }
    }
}
