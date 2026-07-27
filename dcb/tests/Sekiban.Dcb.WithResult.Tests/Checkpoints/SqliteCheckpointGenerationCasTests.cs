using System.Text;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage.Checkpoints;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 SQLite native exact-token CAS against a real file-backed database: the same state machine + exactly-one-
///     winner race as the reference, the additive-schema baseline (a legacy write reads as generation 0/Active), AND the
///     documented INSERT-OR-REPLACE mixed-version hazard (a legacy writer erases a tombstone).
/// </summary>
public class SqliteCheckpointGenerationCasTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"g20-cas-{Guid.NewGuid():N}.db");
    private const string Projector = "g20-p";
    private const string Version = "1.0.0";

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private SqliteMultiProjectionStateStore NewStore() =>
        new(_dbPath, logger: null, serviceIdProvider: new FixedServiceIdProvider("svc"));

    private static MultiProjectionStateWriteRequest Req(long ep) => new(
        Projector, Version, "T", "s", ep, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", "h");

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));
    private static async Task<CheckpointSlot> ReadAsync(IGenerationAwareCheckpointStore s) =>
        (await s.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    [Fact]
    public void Capability_advertised() =>
        Assert.True(CheckpointCapabilityResolver.SupportsGenerationCas(NewStore()));

    [Fact]
    public async Task FullLifecycle_WithExactTokenCas()
    {
        var store = NewStore();
        var create = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, create.Status);
        var active = await ReadAsync(store);
        Assert.True(active.IsActive);
        Assert.Equal(0, active.Generation);

        var persist = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, persist.Status);

        // Stale token (same generation, old revision) rejected — exact-token, not generation-only.
        var stale = await store.ConditionalUpsertAsync(Req(3), Payload("c"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, stale.Status);

        var current = await ReadAsync(store);
        var inv = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(current));
        Assert.Equal(CheckpointCasStatus.Committed, inv.Status);
        var tomb = await ReadAsync(store);
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(current.Generation + 1, tomb.Generation);

        var blocked = await store.ConditionalUpsertAsync(Req(4), Payload("d"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, blocked.Status);

        var commit = await store.CommitRebuiltAsync(Req(10), Payload("rebuilt"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, commit.Status);
        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
        Assert.Equal(tomb.Generation, final.Generation);
    }

    [Fact]
    public async Task ConcurrentInvalidators_NeverTwoWinners()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        var expectation = CheckpointExpectation.FromSlot(active);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Task.Run(() => NewStore().InvalidateWithTombstoneAsync(Projector, Version, expectation))));

        // The DB CAS guarantees at most one winner; contention may surface as Rejected or a transient provider failure,
        // but never a second Committed.
        Assert.Equal(1, outcomes.Count(o => o.Status == CheckpointCasStatus.Committed));
        Assert.True((await ReadAsync(store)).IsTombstoned);
    }

    [Fact]
    public async Task LegacyInsertOrReplace_ErasesTombstone_DocumentedMixedVersionHazard()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        Assert.True((await ReadAsync(store)).IsTombstoned);

        // A legacy WRITER (INSERT OR REPLACE) deletes+reinserts the row, resetting the control columns to defaults — the
        // tombstone is erased back to Active(0/0). This is the documented hazard: protection is complete only when every
        // writer is upgraded.
        Assert.True((await store.UpsertFromStreamAsync(Req(2), Payload("legacy"), 1_000_000)).GetValue());
        var afterLegacy = await ReadAsync(store);
        Assert.True(afterLegacy.IsActive);
        Assert.Equal(0, afterLegacy.Generation);
    }
}
