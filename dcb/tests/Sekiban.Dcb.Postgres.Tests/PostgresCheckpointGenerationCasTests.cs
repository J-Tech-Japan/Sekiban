using System.Text;
using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage.Checkpoints;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     SEK-G20 AUTHORITATIVE provider evidence — the generation/tombstone/exact-token CAS state machine against a REAL
///     Postgres (Testcontainers). Pins the same transitions and exactly-one-winner races proven for the InMemory
///     reference, plus the additive-schema truthfulness (a pre-G20 row — written with no control columns — reads as
///     generation 0, revision 0, Active).
/// </summary>
[Collection("PostgresTests")]
public class PostgresCheckpointGenerationCasTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private const string Projector = "g20-p";
    private const string Version = "1.0.0";

    public PostgresCheckpointGenerationCasTests(PostgresTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM dcb_multi_projection_states");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class FixedServiceId : IServiceIdProvider
    {
        private readonly string _id;
        public FixedServiceId(string id) => _id = id;
        public string GetCurrentServiceId() => _id;
    }

    private PostgresMultiProjectionStateStore NewStore(string serviceId = "svc") =>
        new(_fixture.DbContextFactory, new FixedServiceId(serviceId));

    private static MultiProjectionStateWriteRequest Req(long ep) => new(
        Projector, Version, "T", "s", ep, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", "h");

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));
    private static async Task<CheckpointSlot> ReadAsync(IGenerationAwareCheckpointStore s) =>
        (await s.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    [Fact]
    public async Task Capability_advertised()
    {
        Assert.True(CheckpointCapabilityResolver.SupportsGenerationCas(NewStore()));
        Assert.True(NewStore().DescribeCheckpointCapability().Supports(CheckpointCapabilityKind.GenerationTombstoneCas));
    }

    [Fact]
    public async Task FullLifecycle_Create_Invalidate_Rebuild_WithExactTokenCas()
    {
        var store = NewStore();
        Assert.False((await ReadAsync(store)).Exists);

        // Create (expected-absence CAS)
        var create = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, create.Status);
        var active = await ReadAsync(store);
        Assert.True(active.IsActive);
        Assert.Equal(0, active.Generation);

        // Normal persist on exact token advances revision, same generation
        var persist = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, persist.Status);
        Assert.Equal(active.Generation, persist.ResultingSlot!.Generation);
        Assert.NotEqual(active.Revision, persist.ResultingSlot.Revision);

        // Stale token (generation still matches) rejected — exact-token, not generation-only
        var stale = await store.ConditionalUpsertAsync(Req(3), Payload("c"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, stale.Status);

        // Invalidate -> Tombstoned(g+1)
        var current = await ReadAsync(store);
        var inv = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(current));
        Assert.Equal(CheckpointCasStatus.Committed, inv.Status);
        var tomb = await ReadAsync(store);
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(current.Generation + 1, tomb.Generation);

        // Normal persist blocked on tombstone
        var blocked = await store.ConditionalUpsertAsync(Req(4), Payload("d"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, blocked.Status);

        // CommitRebuilt on exact tombstone token -> Active(g+1)
        var commit = await store.CommitRebuiltAsync(Req(10), Payload("rebuilt"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, commit.Status);
        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
        Assert.Equal(tomb.Generation, final.Generation);
    }

    [Fact]
    public async Task TwoClusters_SharedRow_RetrogradeRebuild_StaleCasRejected_NoRecontamination_Converges()
    {
        // AUTHORITATIVE two-cluster re-contamination proof against a REAL Postgres row. Cluster A and cluster B are two
        // independent store instances (separate DbContext lifetimes) resolving the SAME shared checkpoint row.
        var clusterA = NewStore();
        var clusterB = NewStore();

        // Both converge on an Active checkpoint; B adopts the Active token.
        Assert.Equal(CheckpointCasStatus.Committed,
            (await clusterA.ConditionalUpsertAsync(Req(1), Payload("v1"), CheckpointExpectation.Absent, 1_000_000)).Status);
        var bAdopted = await ReadAsync(clusterB);
        Assert.True(bAdopted.IsActive);
        Assert.Equal(0, bAdopted.Generation);

        // A performs a retrograde full rebuild: durable bump + tombstone (generation 1), cross-cluster visible.
        var aActive = await ReadAsync(clusterA);
        Assert.Equal(CheckpointCasStatus.Committed,
            (await clusterA.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(aActive))).Status);
        var tomb = await ReadAsync(clusterA);
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(1, tomb.Generation);

        // B releases its parked STALE normal persist on the old Active token — REJECTED at the database; the row is
        // byte-for-byte unchanged (still A's tombstone at generation 1). NO re-contamination.
        var bStale = await clusterB.ConditionalUpsertAsync(Req(99, "STALE"), Payload("STALE"), CheckpointExpectation.FromSlot(bAdopted), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, bStale.Status);
        var afterStale = await ReadAsync(clusterA);
        Assert.True(afterStale.IsTombstoned);
        Assert.Equal(tomb.Revision, afterStale.Revision);   // exact same token — the row was not touched

        // Exactly one rebuilt-commit winner on the exact tombstone token; both clusters converge Active at generation 1.
        var winners = await Task.WhenAll(
            clusterA.CommitRebuiltAsync(Req(2, "A"), Payload("REBUILT"), CheckpointExpectation.FromSlot(tomb), 1_000_000),
            clusterB.CommitRebuiltAsync(Req(2, "B"), Payload("REBUILT"), CheckpointExpectation.FromSlot(tomb), 1_000_000));
        Assert.Equal(1, winners.Count(w => w.Status == CheckpointCasStatus.Committed));
        Assert.Equal(1, winners.Count(w => w.Status == CheckpointCasStatus.ConditionRejected));

        var final = await ReadAsync(clusterA);
        Assert.True(final.IsActive);
        Assert.Equal(1, final.Generation);
    }

    private static MultiProjectionStateWriteRequest Req(long ep, string tag) => new(
        Projector, Version, "T", $"pos-{tag}", ep, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", tag);

    [Fact]
    public async Task ConcurrentInvalidators_SameActiveToken_ExactlyOneWinner_AtTheDatabase()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        var expectation = CheckpointExpectation.FromSlot(active);

        // Each call opens its own DbContext; the conditional row-count UPDATE resolves the race in Postgres.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            NewStore().InvalidateWithTombstoneAsync(Projector, Version, expectation)));

        Assert.Equal(1, outcomes.Count(o => o.Status == CheckpointCasStatus.Committed));
        Assert.Equal(11, outcomes.Count(o => o.Status == CheckpointCasStatus.ConditionRejected));
        Assert.True((await ReadAsync(store)).IsTombstoned);
    }

    [Fact]
    public async Task PostCommitResponseLoss_OnCreate_ResolvesCommittedOrInDoubt_ViaBoundedReread_RealDb()
    {
        // Inject a lost response at the EF SaveChanges boundary on the create path against a REAL Postgres:
        // a fault AFTER the INSERT commits -> the store's bounded re-read confirms our own commit -> Committed;
        // a fault BEFORE the INSERT commits -> the re-read cannot confirm -> typed InDoubt (row absent).
        string conn;
        await using (var ctx = await _fixture.DbContextFactory.CreateDbContextAsync())
        {
            conn = ctx.Database.GetConnectionString()!;
        }

        var interceptor = new SaveChangesFaultInterceptor();
        var faultFactory = new InterceptingDbContextFactory(conn, interceptor);
        var store = new PostgresMultiProjectionStateStore(faultFactory, new FixedServiceId("svc"));

        interceptor.PostCommitFault = true;
        var committed = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, committed.Status);
        Assert.True((await ReadAsync(store)).IsActive);   // the row really committed despite the lost response

        // Fresh row for the in-doubt case.
        await using (var ctx = await _fixture.DbContextFactory.CreateDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM dcb_multi_projection_states");
        }
        interceptor.PreCommitFault = true;
        var indoubt = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, indoubt.Status);
        Assert.NotNull(indoubt.Cause);
        Assert.False((await ReadAsync(store)).Exists);    // the write did not commit
    }

    private sealed class InterceptingDbContextFactory : Microsoft.EntityFrameworkCore.IDbContextFactory<SekibanDcbDbContext>
    {
        private readonly string _conn;
        private readonly Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor _interceptor;
        public InterceptingDbContextFactory(string conn, Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor interceptor)
        {
            _conn = conn;
            _interceptor = interceptor;
        }
        public SekibanDcbDbContext CreateDbContext()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<SekibanDcbDbContext>()
                .UseNpgsql(_conn).AddInterceptors(_interceptor).Options;
            return new SekibanDcbDbContext(options);
        }
    }

    private sealed class SaveChangesFaultInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool PreCommitFault;
        public bool PostCommitFault;

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (PreCommitFault) { PreCommitFault = false; throw new IOException("injected: lost response, write did not commit"); }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (PostCommitFault) { PostCommitFault = false; throw new IOException("injected: lost response after a committed write"); }
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task UnappliedSchema_MissingControlColumns_FailsClosed_NoSilentSuccess()
    {
        // Simulate a database on which the G20 additive migration has NOT been applied: drop the control columns, then
        // prove that every CAS operation FAILS CLOSED (no Committed, no silent legacy fallback). Restore the columns
        // afterwards so the shared fixture stays consistent for other tests.
        await using (var ctx = await _fixture.DbContextFactory.CreateDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "ALTER TABLE dcb_multi_projection_states DROP COLUMN IF EXISTS \"Generation\", "
                + "DROP COLUMN IF EXISTS \"Revision\", DROP COLUMN IF EXISTS \"Lifecycle\"");
        }
        try
        {
            var store = NewStore();
            var read = await store.ReadCheckpointSlotAsync(Projector, Version);
            Assert.False(read.IsSuccess);   // read fails closed (column does not exist)

            var create = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
            Assert.NotEqual(CheckpointCasStatus.Committed, create.Status);   // never a silent success
            Assert.Equal(CheckpointCasStatus.ProviderFailure, create.Status);
        }
        finally
        {
            await using var ctx = await _fixture.DbContextFactory.CreateDbContextAsync();
            await ctx.Database.ExecuteSqlRawAsync(
                "ALTER TABLE dcb_multi_projection_states "
                + "ADD COLUMN IF NOT EXISTS \"Generation\" bigint NOT NULL DEFAULT 0, "
                + "ADD COLUMN IF NOT EXISTS \"Revision\" bigint NOT NULL DEFAULT 0, "
                + "ADD COLUMN IF NOT EXISTS \"Lifecycle\" integer NOT NULL DEFAULT 0");
        }
    }

    [Fact]
    public async Task PreG20Row_WrittenViaLegacyUpsert_ReadsAsGeneration0_Revision0Baseline_Active()
    {
        // A legacy write (UpsertFromStreamAsync) does not touch the new control columns; the additive migration defaults
        // them to 0/0/0. The slot must read as generation 0, Active — the accepted baseline for existing rows.
        var store = NewStore();
        Assert.True((await store.UpsertFromStreamAsync(Req(7), Payload("legacy"), 1_000_000)).GetValue());

        var slot = await ReadAsync(store);
        Assert.True(slot.IsActive);
        Assert.Equal(0, slot.Generation);
        Assert.Equal("0", slot.Revision);

        // And it can then be invalidated on that exact baseline token.
        var inv = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(slot));
        Assert.Equal(CheckpointCasStatus.Committed, inv.Status);
    }
}
