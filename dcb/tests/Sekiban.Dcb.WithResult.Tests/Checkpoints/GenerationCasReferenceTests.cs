using System.Text;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 — the InMemory state store is the deterministic REFERENCE for the generation/tombstone/exact-token CAS
///     state machine. These tests pin the fixed transitions, exact-token (not generation-only) semantics, and
///     exactly-one-winner races that every native provider must reproduce.
/// </summary>
public class GenerationCasReferenceTests
{
    private static IGenerationAwareCheckpointStore NewStore() =>
        new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider("svc"));

    private const string Projector = "p";
    private const string Version = "1.0.0";

    private static MultiProjectionStateWriteRequest Req(long eventsProcessed) => new(
        ProjectorName: Projector,
        ProjectorVersion: Version,
        PayloadType: "T",
        LastSortableUniqueId: "s",
        EventsProcessed: eventsProcessed,
        IsOffloaded: false,
        OffloadKey: null,
        OffloadProvider: null,
        OriginalSizeBytes: 1,
        CompressedSizeBytes: 1,
        SafeWindowThreshold: "w",
        CreatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        BuildSource: "test",
        BuildHost: "h");

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));

    private static async Task<CheckpointSlot> ReadAsync(IGenerationAwareCheckpointStore store) =>
        (await store.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    [Fact]
    public void Capability_advertised()
    {
        var d = NewStore().DescribeCheckpointCapability();
        Assert.True(d.Supports(CheckpointCapabilityKind.GenerationTombstoneCas));
        Assert.False(d.Supports(CheckpointCapabilityKind.Unknown));
        Assert.True(CheckpointCapabilityResolver.SupportsGenerationCas(NewStore()));
    }

    [Fact]
    public async Task InitialCreate_ExpectAbsent_Commits_Active_Gen0()
    {
        var store = NewStore();
        Assert.False((await ReadAsync(store)).Exists);

        var outcome = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, outcome.Status);
        Assert.Equal(0, outcome.ResultingSlot!.Generation);
        Assert.True(outcome.ResultingSlot.IsActive);

        var slot = await ReadAsync(store);
        Assert.True(slot.IsActive);
        Assert.Equal(0, slot.Generation);
    }

    [Fact]
    public async Task SecondCreate_ExpectAbsent_WhenExists_Rejected()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);

        var outcome = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, outcome.Status);
        Assert.True(outcome.CurrentSlot!.IsActive);
    }

    [Fact]
    public async Task NormalPersist_StaleToken_Rejected_ExactTokenNotGenerationOnly()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var slot1 = await ReadAsync(store);

        // First persist against slot1 commits and advances the revision (generation UNCHANGED).
        var ok = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.FromSlot(slot1), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, ok.Status);
        Assert.Equal(slot1.Generation, ok.ResultingSlot!.Generation);   // same generation...
        Assert.NotEqual(slot1.Revision, ok.ResultingSlot.Revision);     // ...different exact token

        // A second persist reusing slot1's token is rejected even though the GENERATION still matches — proving the CAS
        // is on the exact per-mutation token, not generation-only.
        var stale = await store.ConditionalUpsertAsync(Req(3), Payload("c"), CheckpointExpectation.FromSlot(slot1), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, stale.Status);
        Assert.Equal(ok.ResultingSlot.Revision, stale.CurrentSlot!.Revision);
    }

    [Fact]
    public async Task Invalidate_Tombstones_BumpsGeneration_And_NormalPersistRejectedOnTombstone()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);

        var inv = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        Assert.Equal(CheckpointCasStatus.Committed, inv.Status);
        Assert.Equal(active.Generation + 1, inv.ResultingSlot!.Generation);
        Assert.True(inv.ResultingSlot.IsTombstoned);

        var tomb = await ReadAsync(store);
        Assert.True(tomb.IsTombstoned);

        // A normal persist cannot advance a tombstoned row (only CommitRebuilt can).
        var blocked = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, blocked.Status);
    }

    [Fact]
    public async Task CommitRebuilt_OnExactTombstoneToken_ClearsTombstone_ActiveAtBumpedGeneration()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        var tomb = await ReadAsync(store);

        // Wrong token rejected.
        var wrong = await store.CommitRebuiltAsync(Req(9), Payload("x"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, wrong.Status);

        // Exact tombstone token wins; one atomic same-row CAS -> Active at the bumped generation.
        var commit = await store.CommitRebuiltAsync(Req(5), Payload("rebuilt"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, commit.Status);
        Assert.True(commit.ResultingSlot!.IsActive);
        Assert.Equal(tomb.Generation, commit.ResultingSlot.Generation);

        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
        Assert.Equal(active.Generation + 1, final.Generation);
    }

    [Fact]
    public async Task ConcurrentInvalidators_SameActiveToken_ExactlyOneWinner()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        var expectation = CheckpointExpectation.FromSlot(active);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            store.InvalidateWithTombstoneAsync(Projector, Version, expectation)));

        Assert.Equal(1, outcomes.Count(o => o.Status == CheckpointCasStatus.Committed));
        Assert.Equal(15, outcomes.Count(o => o.Status == CheckpointCasStatus.ConditionRejected));
    }

    [Fact]
    public async Task ConcurrentNormalPersists_SameToken_ExactlyOneWinner()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var slot = await ReadAsync(store);
        var expectation = CheckpointExpectation.FromSlot(slot);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
            store.ConditionalUpsertAsync(Req(i + 2), Payload("v" + i), expectation, 1_000_000)));

        Assert.Equal(1, outcomes.Count(o => o.Status == CheckpointCasStatus.Committed));
        Assert.Equal(15, outcomes.Count(o => o.Status == CheckpointCasStatus.ConditionRejected));
    }

    [Fact]
    public async Task ConcurrentRebuilders_SameTombstoneToken_ExactlyOneWinner()
    {
        var store = NewStore();
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        var tomb = await ReadAsync(store);
        var expectation = CheckpointExpectation.FromSlot(tomb);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16).Select(i =>
            store.CommitRebuiltAsync(Req(i + 10), Payload("r" + i), expectation, 1_000_000)));

        Assert.Equal(1, outcomes.Count(o => o.Status == CheckpointCasStatus.Committed));
        Assert.Equal(15, outcomes.Count(o => o.Status == CheckpointCasStatus.ConditionRejected));

        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
    }
}
