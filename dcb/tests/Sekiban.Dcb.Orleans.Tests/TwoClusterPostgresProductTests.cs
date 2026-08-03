using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Testcontainers.PostgreSql;
using Xunit;
using static Sekiban.Dcb.Orleans.Tests.G20Shared;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G20 AUTHORITATIVE PRODUCT vector: two INDEPENDENT Orleans clusters (separate cluster-local grain state) sharing
///     ONE real Postgres (Testcontainers) — the same serviceId, the same authoritative event log AND the same external
///     checkpoint row — with a forced-offload snapshot (content-addressed blob). It proves that the DI/factory wiring,
///     fresh-activation token adoption, transactional CAS, and offload row+blob identity COMPOSE correctly through product
///     code, which neither the store-level Postgres tests nor the InMemory product test can prove alone:
///     <list type="number">
///         <item>both clusters converge Active(gen0), winner "later", the snapshot OFFLOADED to a blob;</item>
///         <item>B's REAL grain persist is parked at the store boundary holding the gen0 token + a DIFFERENT stale blob;</item>
///         <item>A retrograde-rebuilds: durable Invalidate CAS opens a tombstone (held open by a parked rebuilt commit);</item>
///         <item>releasing B => ConditionRejected against the tombstone => B's query barrier arms, and the Postgres row's
///         generation/revision/lifecycle AND OffloadKey + blob bytes are byte-identical (B's stale candidate applied
///         nothing);</item>
///         <item>A's rebuilt commit wins on the exact tombstone token; BOTH clusters restart on NEW store instances and
///         converge to the EXACT globally-earliest winner + safe state + position, with a readable winning blob.</item>
///     </list>
///     One container (reused), deterministic gates (no race-window polling), marked an integration test but kept in dcb CI.
/// </summary>
[Trait("Category", "Integration")]
[Collection("PostgresProductTests")]
public class TwoClusterPostgresProductTests : IAsyncLifetime
{
    private const string Projector = "g20-retro-first-wins";
    private const string Version = "1.0.0";

    private PostgreSqlContainer _container = null!;
    private string _conn = null!;
    private IDbContextFactory<SekibanDcbDbContext> _dbFactory = null!;
    private ServiceProvider _rootSp = null!;
    private TestCluster _clusterA = null!;
    private TestCluster _clusterB = null!;

    // Shared across both clusters' silos (static so each cluster's Configurator reaches the SAME connection + blob store).
    internal static string? SharedConn;
    internal static string? CurrentClusterName;
    internal static readonly ContentAddressedBlobAccessor Blob = new();
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GatingCheckpointStore> Gates = new();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("sekiban_g20").WithUsername("t").WithPassword("t").Build();
        await _container.StartAsync();
        _conn = _container.GetConnectionString();

        // Root SP: apply migrations once + expose the DbContextFactory for direct row/blob assertions.
        var services = new ServiceCollection();
        services.AddSekibanDcbPostgres(_conn);
        services.AddSingleton(G20Shared.BuildDomain());
        _rootSp = services.BuildServiceProvider();
        await using (var ctx = await _rootSp.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>().CreateDbContextAsync())
        {
            await ctx.Database.MigrateAsync();
        }
        _dbFactory = _rootSp.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>();

        SharedConn = _conn;
        Gates.Clear();
        CurrentClusterName = null;
        _clusterA = await BuildClusterAsync("A");
        _clusterB = await BuildClusterAsync("B");
    }

    public async Task DisposeAsync()
    {
        await _clusterA.StopAllSilosAsync(); _clusterA.Dispose();
        await _clusterB.StopAllSilosAsync(); _clusterB.Dispose();
        await _rootSp.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static async Task<TestCluster> BuildClusterAsync(string name)
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uid = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"G20-pg-{name}-{uid}";
        builder.Options.ServiceId = $"G20-pg-{name}-{uid}";
        builder.AddSiloBuilderConfigurator<Configurator>();
        CurrentClusterName = name;   // deploys are sequential + awaited, so this reliably names the silo being configured
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }

    // A direct (ungated) Postgres checkpoint store for authoritative row/offload assertions, using the SAME serviceId as
    // the clusters (DefaultServiceIdProvider) so it resolves the SAME shared row.
    private PostgresMultiProjectionStateStore AssertionStore() =>
        new(_dbFactory, new DefaultServiceIdProvider(), Blob);

    private async Task<CheckpointSlot> ReadRowAsync() =>
        (await AssertionStore().ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    private async Task WriteEventAsync(string id, string value, int secondsAgo)
    {
        var ev = CreateEvent(new CreatedWithId(id, value), DateTime.UtcNow.AddSeconds(-secondsAgo));
        await _rootSp.GetRequiredService<IEventStore>().WriteSerializableEventsAsync(new[] { ToSerializable(ev) });
    }

    [Fact]
    public async Task StaleEmptyReservation_OnTwoClusters_SharedPostgres_BothUpsertsSucceed_AndConverge()
    {
        var domain = G20Shared.BuildDomain();
        var eventStore = _rootSp.GetRequiredService<IEventStore>();
        var execA = new OrleansDcbExecutor(_clusterA.Client, eventStore, domain);
        var execB = new OrleansDcbExecutor(_clusterB.Client, eventStore, domain);
        var id = Guid.NewGuid();
        var tag = new G22ReservationTag(id);
        var tagStateId = new TagStateId(tag, G22ReservationProjector.ProjectorName);
        var serviceId = new DefaultServiceIdProvider().GetCurrentServiceId();

        // Pin A's tag-consistent actor to a successfully caught-up EMPTY cache before B writes. The exact grain key is
        // the one OrleansActorObjectAccessor uses for command reservations; no timing window or sleep is involved.
        var reservationA = _clusterA.Client.GetGrain<ITagConsistentGrain>(
            ServiceIdGrainKey.Build(serviceId, tag.GetTag()));
        Assert.Equal(string.Empty, (await reservationA.GetLatestSortableUniqueIdAsync()).GetValue());

        var b = await execB.ExecuteAsync(new G22UpsertCommand(id, "B"));
        Assert.True(b.IsSuccess, b.IsSuccess ? string.Empty : b.GetException().ToString());

        // Force A's command fold to observe B's durable version while reservationA remains pinned empty. Before SEK-G22,
        // the following command deterministically failed G19's non-empty-expected/empty-current comparison.
        var foldedOnA = await execA.GetTagStateAsync(tagStateId);
        Assert.True(foldedOnA.IsSuccess, foldedOnA.IsSuccess ? string.Empty : foldedOnA.GetException().ToString());
        Assert.Equal(1, foldedOnA.GetValue().Version);

        var a = await execA.ExecuteAsync(new G22UpsertCommand(id, "A"));
        Assert.True(a.IsSuccess, a.IsSuccess ? string.Empty : a.GetException().ToString());

        var durable = (await eventStore.ReadSerializableEventsByTagAsync(tag)).GetValue().ToList();
        Assert.Equal(2, durable.Count);

        // Deterministic convergence: clear both cluster-local tag-state caches, then each folds the same two Postgres
        // events. These are explicit cache barriers, not sleeps or eventual polling.
        var grainStateId = ServiceIdGrainKey.Build(serviceId, tagStateId.GetTagStateId());
        await _clusterA.Client.GetGrain<ITagStateGrain>(grainStateId).ClearCacheAsync();
        await _clusterB.Client.GetGrain<ITagStateGrain>(grainStateId).ClearCacheAsync();
        var finalA = await execA.GetTagStateAsync(tagStateId);
        var finalB = await execB.GetTagStateAsync(tagStateId);
        Assert.True(finalA.IsSuccess);
        Assert.True(finalB.IsSuccess);
        Assert.Equal(2, finalA.GetValue().Version);
        Assert.Equal(2, finalB.GetValue().Version);
        Assert.Equal(
            ((G22ReservationState)finalA.GetValue().Payload).Values.OrderBy(x => x),
            ((G22ReservationState)finalB.GetValue().Payload).Values.OrderBy(x => x));
    }

    [Fact]
    public async Task ParkedStaleWriter_OnRealPostgres_WithOffload_RejectedAgainstTombstone_RowAndBlobUnchanged_BothRestartConverge()
    {
        var grainA = _clusterA.Client.GetGrain<IMultiProjectionGrain>(Projector);
        var grainB = _clusterB.Client.GetGrain<IMultiProjectionGrain>(Projector);
        var gateB = Gates["B"];
        var gateA = Gates["A"];
        var eventStore = _rootSp.GetRequiredService<IEventStore>();

        // (1) One already-safe event; both clusters catch up + persist the CAS. The forced-low offload threshold offloads
        //     the snapshot to a content-addressed blob. Shared row => Active(gen0), winner "later", IsOffloaded.
        await WriteEventAsync("team-1", "later", 30);
        await grainA.RefreshAsync();
        Assert.True((await grainA.PersistStateAsync()).IsSuccess);
        await grainB.RefreshAsync();
        _ = await grainB.PersistStateAsync();
        var gen0 = await ReadRowAsync();
        Assert.True(gen0.IsActive);
        Assert.Equal(0, gen0.Generation);
        Assert.True(gen0.Record!.IsOffloaded, "the snapshot must be offloaded to a blob (forced-low threshold)");
        var gen0Key = gen0.Record.OffloadKey!;
        Assert.NotNull(Blob.TryGet(gen0Key));

        // (2) A B-only safe event so B has a genuine stale persist on the gen0 token (its snapshot would offload to a
        //     DIFFERENT content-addressed key).
        await grainB.AddEventsAsync(new[] { ToSerializable(CreateEvent(new CreatedWithId("team-2", "B-stale"), DateTime.UtcNow.AddSeconds(-20))) });
        await PollUnsafeContainsAsync(grainB, "team-2");

        // (3) Arm B's upsert gate; launch B's REAL grain persist — it parks at the Postgres store boundary holding gen0.
        var bUpsert = new GatingCheckpointStore.Gate { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        gateB.UpsertGate = bUpsert;
        var bPersist = grainB.PersistStateAsync();
        await bUpsert.Arrived.Task;

        // (4) Arm A's rebuilt-commit gate; deliver the retrograde "earlier" to A and drive its rebuild FIRE-AND-FORGET. A
        //     durably Invalidates (tombstone gen1 in Postgres) then parks at its rebuilt commit, holding the tombstone open.
        var aCommit = new GatingCheckpointStore.Gate { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        gateA.CommitRebuiltGate = aCommit;
        await WriteEventAsync("team-1", "earlier", 31);
        await grainA.AddEventsAsync(new[] { ToSerializable(CreateEvent(new CreatedWithId("team-1", "earlier"), DateTime.UtcNow.AddSeconds(-31))) });
        var aDrive = grainA.GetStateAsync(canGetUnsafeState: true, waitForCatchUp: false);
        await aCommit.Arrived.Task;
        var tomb = await PollUntilTombstoneAsync();
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(1, tomb.Generation);
        Assert.Equal(gen0Key, tomb.Record!.OffloadKey);   // invalidate retains the prior payload/offload under the tombstone

        // (5) Release B's parked stale persist. Its CAS on the gen0 token meets the tombstone => ConditionRejected. The
        //     Postgres row is byte-identical: same generation/revision/lifecycle AND same OffloadKey + blob bytes — B's
        //     stale candidate (a different content-addressed blob) applied NOTHING.
        bUpsert.Release!.SetResult();
        _ = await bPersist;
        var afterB = await ReadRowAsync();
        Assert.True(afterB.IsTombstoned, $"the shared Postgres row was re-contaminated: {afterB.Lifecycle} gen{afterB.Generation}");
        Assert.Equal(1, afterB.Generation);
        Assert.Equal(tomb.Revision, afterB.Revision);
        Assert.Equal(gen0Key, afterB.Record!.OffloadKey);                 // OffloadKey unchanged
        Assert.Equal(Blob.TryGet(gen0Key), Blob.TryGet(afterB.Record.OffloadKey!));   // blob bytes unchanged

        // (5b) STALE-QUERY WINDOW — A's rebuilt commit stays PARKED (the shared row is held OPEN at the tombstone). Deliver
        //      the retrograde "earlier" to B and start B's product state + scalar + list queries as background tasks. B's
        //      armed barrier drives a from-scratch rebuild; B's rebuilt commit PARKS at its own gate (a deterministic
        //      barrier-entry signal — NO sleeps). While parked, none of B's queries may normal-succeed with the stale
        //      "later"/"team-2": each is either still pending on the shared barrier or has surfaced the fail-closed channel.
        await grainB.AddEventsAsync(new[] { ToSerializable(CreateEvent(new CreatedWithId("team-1", "earlier"), DateTime.UtcNow.AddSeconds(-31))) });
        var bCommit = new GatingCheckpointStore.Gate { Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        gateB.CommitRebuiltGate = bCommit;
        var execB = new OrleansDcbExecutor(_clusterB.Client, eventStore, G20Shared.BuildDomain());
        var bState = grainB.GetStateAsync(canGetUnsafeState: false, waitForCatchUp: false);
        var bScalar = execB.QueryAsync(new WinnerQuery("team-1"));
        var bList = execB.QueryAsync(new WinnerListQuery());
        await bCommit.Arrived.Task;   // deterministic barrier-entry: B's rebuild reached its rebuilt-commit boundary (parked)

        // With BOTH A and B rebuilt commits parked on the tombstone, none of B's queries has served a stale success — each
        // is pending on the shared barrier OR surfaced the fail-closed channel; NONE normal-succeeded with "later"/"team-2".
        Assert.False(IsStaleStateSuccess(bState), "B state query normal-succeeded STALE while the barrier/tombstone was open");
        Assert.False(IsStaleScalarSuccess(bScalar), "B scalar query normal-succeeded STALE while the barrier/tombstone was open");
        Assert.False(IsStaleListSuccess(bList), "B list query normal-succeeded STALE while the barrier/tombstone was open");

        // (6) TWO-SIDED rebuilt-commit race on the SAME exact tombstone token: release A and B together. Exactly ONE wins
        //     (row advances Active(gen1) once), the loser is ConditionRejected and refetches/rebuilds.
        var rowBeforeRace = await ReadRowAsync();
        Assert.True(rowBeforeRace.IsTombstoned);   // still open right up to the release
        aCommit.Release!.SetResult();
        bCommit.Release!.SetResult();
        _ = await aDrive;
        _ = await bState;   // now B's queries may resolve
        var final = await ReadRowAsync();
        Assert.True(final.IsActive);                                     // exactly one winner advanced the row
        Assert.Equal(1, final.Generation);
        // The revision advanced EXACTLY ONCE from the tombstone token. Two commits winning the same token (i.e. the token
        // CAS removed) would advance it TWICE — this pins one-winner at the product level, so removing the token CAS fails.
        Assert.Equal((long.Parse(tomb.Revision) + 1).ToString(), final.Revision);
        Assert.Equal(2, final.Record!.EventsProcessed);
        Assert.True(final.Record.IsOffloaded);
        Assert.NotNull(Blob.TryGet(final.Record.OffloadKey!));          // the winning blob is readable

        // (7) After the release, B converges to the exact globally-earliest winner + safe state; the stale "team-2" is gone.
        var safeB = await PollSafeWinnerAsync(grainB, "earlier");
        Assert.Equal("earlier", WinnerOf(safeB));
        Assert.True(safeB.IsSafeState);
        Assert.False(((FirstWinsProjector)safeB.Payload).Winners.ContainsKey("team-2"));

        // (8) Restart BOTH clusters (fresh activations = NEW store instances). Both read the control plane before binding
        //     payload and converge to the EXACT globally-earliest winner + safe state + position; the winning blob reads.
        await grainA.RequestDeactivationAsync();
        await grainB.RequestDeactivationAsync();
        await Task.Delay(1500);
        var a2 = _clusterA.Client.GetGrain<IMultiProjectionGrain>(Projector);
        var b2 = _clusterB.Client.GetGrain<IMultiProjectionGrain>(Projector);
        var ra = await PollSafeWinnerAsync(a2, "earlier");
        var rb = await PollSafeWinnerAsync(b2, "earlier");
        Assert.Equal("earlier", WinnerOf(ra));
        Assert.Equal("earlier", WinnerOf(rb));
        Assert.True(ra.IsSafeState);
        Assert.True(rb.IsSafeState);
        Assert.Equal(ra.LastSortableUniqueId, rb.LastSortableUniqueId);   // same converged position on both clusters
        await AssertScalarAndListAsync(new OrleansDcbExecutor(_clusterA.Client, eventStore, G20Shared.BuildDomain()), "earlier");
        await AssertScalarAndListAsync(new OrleansDcbExecutor(_clusterB.Client, eventStore, G20Shared.BuildDomain()), "earlier");
        var afterRestart = await ReadRowAsync();
        Assert.NotNull(Blob.TryGet(afterRestart.Record!.OffloadKey!));    // the winning blob is still readable after restart
    }

    // A standalone DbContextFactory over a connection string (no ServiceProvider needed), so the checkpoint store + gate
    // can be constructed eagerly in ConfigureServices. Every cluster points at the SAME connection => one shared row.
    private sealed class PgDbContextFactory : IDbContextFactory<SekibanDcbDbContext>
    {
        private readonly string _conn;
        public PgDbContextFactory(string conn) => _conn = conn;
        public SekibanDcbDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<SekibanDcbDbContext>().UseNpgsql(_conn).Options);
    }

    // A query is a STALE success only if it COMPLETED successfully AND carries the pre-rebuild value ("later" winner or the
    // "team-2" phantom). A pending task (blocked on the barrier) or a fail-closed error is NOT a stale success.
    private static bool IsStaleStateSuccess(Task<ResultBoxes.ResultBox<MultiProjectionState>> t) =>
        t.IsCompletedSuccessfully && t.Result.IsSuccess
        && (WinnerOf(t.Result.GetValue()) == "later" || ((FirstWinsProjector)t.Result.GetValue().Payload).Winners.ContainsKey("team-2"));

    private static bool IsStaleScalarSuccess(Task<ResultBoxes.ResultBox<WinnerResult>> t) =>
        t.IsCompletedSuccessfully && t.Result.IsSuccess && t.Result.GetValue().Value == "later";

    private static bool IsStaleListSuccess(Task<ResultBoxes.ResultBox<Sekiban.Dcb.Queries.ListQueryResult<WinnerRow>>> t) =>
        t.IsCompletedSuccessfully && t.Result.IsSuccess
        && t.Result.GetValue().Items.Any(r => r.Id == "team-2" || (r.Id == "team-1" && r.Value == "later"));

    private async Task<CheckpointSlot> PollUntilTombstoneAsync()
    {
        for (var i = 0; i < 200; i++)
        {
            var slot = await ReadRowAsync();
            if (slot.IsTombstoned)
            {
                return slot;
            }
            await Task.Delay(50);
        }
        return await ReadRowAsync();
    }

    private class Configurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var clusterName = CurrentClusterName ?? Guid.NewGuid().ToString("N");
            siloBuilder
                .ConfigureServices(services =>
                {
                    services.AddSingleton(G20Shared.BuildDomain());
                    // Shared REAL Postgres event store + checkpoint store (same serviceId => shared authoritative row).
                    services.AddSekibanDcbPostgres(SharedConn!);
                    // Content-addressed blob accessor SHARED across clusters, + forced-low snapshot size so the checkpoint
                    // payload OFFLOADS (exercises row+blob identity).
                    services.AddSingleton<IBlobStorageSnapshotAccessor>(Blob);
                    // Cluster's checkpoint store is a real PostgresMultiProjectionStateStore wrapped in the store-agnostic
                    // gate so its REAL grain persist can be parked deterministically. Registered EAGERLY (not lazily inside
                    // the resolve factory) so the test can reach the gate before any grain activates. A standalone
                    // DbContextFactory over the SAME connection keeps the row shared.
                    var pg = new PostgresMultiProjectionStateStore(new PgDbContextFactory(SharedConn!), new DefaultServiceIdProvider(), Blob);
                    var gate = Gates.GetOrAdd(clusterName, _ => new GatingCheckpointStore(pg));
                    services.AddSingleton<IMultiProjectionStateStore>(gate);
                    services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
                    services.AddSingleton<IEventSubscriptionResolver>(
                        new DefaultOrleansEventSubscriptionResolver("EventStreamProvider", "AllEvents", Guid.Empty));
                    services.AddTransient<IMultiProjectionEventStatistics, NoOpMultiProjectionEventStatistics>();
                    // Forced-low snapshot envelope size => the derived offload threshold is tiny => the snapshot offloads.
                    services.AddTransient(_ => new GeneralMultiProjectionActorOptions { SafeWindowMs = 3000, MaxSnapshotSerializedSizeBytes = 128 });
                    services.AddSekibanDcbNativeRuntime();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }
}
