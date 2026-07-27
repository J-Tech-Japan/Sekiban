using System.Text;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 — the deterministic cross-cluster re-contamination proof deferred from SEK-G18, at the protocol level.
///     Two independent clusters A and B share ONE external checkpoint row (same store instance, same key). The exact
///     sequence the packet requires is orchestrated step-by-step: B holds a stale token while A performs a retrograde
///     bump+tombstone rebuild; B's released stale writes are ConditionRejected and NEVER re-contaminate the row; there is
///     exactly one rebuilt-commit winner; and both clusters converge to the authoritative rebuilt state. (The grain-level
///     end-to-end integration — normal-path two-cluster convergence with the CAS store active — is covered by the Orleans
///     TwoClusterGraduationReconcileConvergenceTests suite. Offloaded payloads are exercised by the provider CAS tests;
///     the protocol here is payload-storage agnostic.)
/// </summary>
public class TwoClusterRecontaminationProofTests
{
    private const string Projector = "p";
    private const string Version = "1.0.0";

    // A and B are independent clusters that resolve the SAME shared checkpoint row (same service, same store instance).
    private static IGenerationAwareCheckpointStore SharedStore() =>
        new InMemoryMultiProjectionStateStore(new FixedServiceIdProvider("svc"));

    private static MultiProjectionStateWriteRequest Req(long ep, string tag) => new(
        Projector, Version, "T", $"pos-{tag}", ep, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", tag);

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));
    private static async Task<CheckpointSlot> Read(IGenerationAwareCheckpointStore s) =>
        (await s.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    [Fact]
    public async Task RetrogradeRebuild_StalePeerCasRejected_NoRecontamination_OneRebuiltWinner_Converges()
    {
        var store = SharedStore();

        // --- Both clusters converged on an Active checkpoint. B has read (adopted) the Active token. ---
        Assert.Equal(CheckpointCasStatus.Committed,
            (await store.ConditionalUpsertAsync(Req(1, "A-initial"), Payload("v1"), CheckpointExpectation.Absent, 1_000_000)).Status);
        var bAdopted = await Read(store);                     // cluster B's adopted (Active gen 0) token
        Assert.True(bAdopted.IsActive);
        Assert.Equal(0, bAdopted.Generation);

        // --- A performs a retrograde full rebuild: durable bump + tombstone (generation 1). Cross-cluster visible. ---
        var aActive = await Read(store);
        var tombstone = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(aActive));
        Assert.Equal(CheckpointCasStatus.Committed, tombstone.Status);
        var tombSlot = await Read(store);
        Assert.True(tombSlot.IsTombstoned);
        Assert.Equal(1, tombSlot.Generation);

        // --- B releases its parked STALE normal persist on the old Active token. It is REJECTED and the row is
        //     unchanged (still A's tombstone at generation 1) — NO re-contamination. ---
        var bStale = await store.ConditionalUpsertAsync(Req(99, "B-stale"), Payload("STALE"), CheckpointExpectation.FromSlot(bAdopted), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, bStale.Status);
        var afterBStale = await Read(store);
        Assert.True(afterBStale.IsTombstoned);                // still tombstoned — not resurrected
        Assert.Equal(tombSlot.Revision, afterBStale.Revision); // exact same token — the row was not touched
        Assert.True(bStale.CurrentSlot!.IsTombstoned);         // B's refetch signal shows the tombstone -> B must rebuild

        // --- Exactly one rebuilt-commit winner on the exact tombstone token. A and B both try to commit their rebuilt
        //     result (both replayed the full authoritative history, so the payload is equivalent). ---
        var winners = await Task.WhenAll(
            store.CommitRebuiltAsync(Req(2, "A-rebuilt"), Payload("REBUILT"), CheckpointExpectation.FromSlot(tombSlot), 1_000_000),
            store.CommitRebuiltAsync(Req(2, "B-rebuilt"), Payload("REBUILT"), CheckpointExpectation.FromSlot(tombSlot), 1_000_000));
        Assert.Equal(1, winners.Count(w => w.Status == CheckpointCasStatus.Committed));
        Assert.Equal(1, winners.Count(w => w.Status == CheckpointCasStatus.ConditionRejected));

        // --- Converged: the shared row is Active at the bumped generation, never the stale "STALE" payload. The loser
        //     refetches the same authoritative Active slot. ---
        var final = await Read(store);
        Assert.True(final.IsActive);
        Assert.Equal(1, final.Generation);
        Assert.NotEqual(bAdopted.Revision, final.Revision);   // the row moved past B's stale token

        // A further stale write from B on ANY pre-rebuild token is still rejected (no late recontamination).
        var bLate = await store.ConditionalUpsertAsync(Req(98, "B-late"), Payload("LATE-STALE"), CheckpointExpectation.FromSlot(bAdopted), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, bLate.Status);
        Assert.Equal(final.Revision, (await Read(store)).Revision);
    }
}
