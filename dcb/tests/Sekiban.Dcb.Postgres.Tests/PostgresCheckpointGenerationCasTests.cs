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
