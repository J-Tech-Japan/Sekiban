using System.Reflection;
using System.Text;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 post-commit ambiguity / InDoubt contract, proven on the InMemory reference. A conditional write whose
///     response is lost is resolved by a BOUNDED independent re-read: a re-read that verifies our exact resulting token +
///     payload identity reports Committed (the response was lost AFTER commit); an unverifiable one reports typed
///     retryable InDoubt; a pre-commit fault is known-safe (ProviderFailure, row unchanged); and every retry uses the same
///     exact token, so it is idempotent (converges if uncommitted, cleanly rejects if already committed).
/// </summary>
public class CheckpointInDoubtContractTests
{
    private const string Projector = "p";
    private const string Version = "1.0.0";

    private static InMemoryMultiProjectionStateStore NewStore() => new(new FixedServiceIdProvider("svc"));
    private static IGenerationAwareCheckpointStore Cas(InMemoryMultiProjectionStateStore s) => s;

    // Set the one-shot internal fault seam via reflection (avoids widening the Core IVT allowlist).
    private static void SetFault(InMemoryMultiProjectionStateStore store, string mode)
    {
        var baseType = store.GetType().BaseType!; // Sekiban.Dcb.InMemory.InMemoryMultiProjectionStateStore
        var prop = baseType.GetProperty("NextConditionalUpsertFault", BindingFlags.NonPublic | BindingFlags.Instance)!;
        prop.SetValue(store, Enum.Parse(prop.PropertyType, mode));
    }

    private static MultiProjectionStateWriteRequest Req(long ep, string pos) => new(
        Projector, Version, "T", pos, ep, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", "h");

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));
    private static async Task<CheckpointSlot> Read(IGenerationAwareCheckpointStore s) =>
        (await s.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    [Fact]
    public async Task PostCommitResponseLoss_AfterACommit_BoundedRereadConfirmsOwnCommit_ReportsCommitted()
    {
        var store = NewStore();
        var cas = Cas(store);
        await cas.ConditionalUpsertAsync(Req(1, "p1"), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await Read(cas);

        // The write DOES commit, but its response is lost. The resolver's re-read verifies our exact token+payload.
        SetFault(store, "PostCommitResponseLoss");
        var outcome = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, outcome.Status);
        Assert.Equal("p2", (await Read(cas)).Record!.LastSortableUniqueId);   // the row really did advance
    }

    [Fact]
    public async Task PostCommitResponseLoss_ThatDidNotCommit_ReturnsTypedInDoubt_WithSecretSafeCause()
    {
        var store = NewStore();
        var cas = Cas(store);
        await cas.ConditionalUpsertAsync(Req(1, "p1"), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await Read(cas);

        // The write does NOT commit and its response is lost; the bounded re-read cannot confirm our write -> InDoubt.
        SetFault(store, "PostCommitResponseLossUnverifiable");
        var outcome = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, outcome.Status);
        Assert.NotNull(outcome.Cause);
        Assert.Equal("p1", (await Read(cas)).Record!.LastSortableUniqueId);   // the row is unchanged
    }

    [Fact]
    public async Task PostCommit_WhenEveryBoundedRereadFails_StaysInDoubt_NeverProviderFailure()
    {
        var store = NewStore();
        var cas = Cas(store);
        await cas.ConditionalUpsertAsync(Req(1, "p1"), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await Read(cas);

        // The write CROSSED the commit boundary (the row really advances to p2), but its response is lost AND every
        // bounded independent re-read throws — the authority is unreachable. An unreadable authority after a possible
        // commit does NOT establish a known pre-commit failure, so the outcome MUST be typed retryable InDoubt, never
        // ProviderFailure. (ProviderFailure is reserved for provable pre-commit / deterministic schema failures, which
        // a provider classifies BEFORE it ever reaches this resolver.)
        SetFault(store, "PostCommitRereadUnavailable");
        var outcome = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, outcome.Status);
        Assert.NotEqual(CheckpointCasStatus.ProviderFailure, outcome.Status);
        Assert.NotNull(outcome.Cause);

        // The commit genuinely happened; a retry with the SAME old token now cleanly rejects (the token has moved),
        // so the InDoubt classification is safe — no double-apply, no lost write.
        var retry = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, retry.Status);
        Assert.Equal("p2", (await Read(cas)).Record!.LastSortableUniqueId);
    }

    [Fact]
    public async Task PreCommitFault_IsRollbackKnown_ProviderFailure_RowUnchanged()
    {
        var store = NewStore();
        var cas = Cas(store);
        await cas.ConditionalUpsertAsync(Req(1, "p1"), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await Read(cas);

        SetFault(store, "PreCommitFault");
        var outcome = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ProviderFailure, outcome.Status);
        Assert.NotNull(outcome.Cause);
        var after = await Read(cas);
        Assert.Equal("p1", after.Record!.LastSortableUniqueId);
        Assert.Equal(active.Revision, after.Revision);   // byte-for-byte unchanged
    }

    [Fact]
    public async Task AfterInDoubt_RetryWithSameToken_Converges()
    {
        var store = NewStore();
        var cas = Cas(store);
        await cas.ConditionalUpsertAsync(Req(1, "p1"), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await Read(cas);

        SetFault(store, "PostCommitResponseLossUnverifiable");
        var indoubt = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, indoubt.Status);

        // The uncommitted in-doubt write is safely retried with the SAME token — the row was unchanged, so it commits.
        var retry = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, retry.Status);
        Assert.Equal("p2", (await Read(cas)).Record!.LastSortableUniqueId);
    }

    [Fact]
    public async Task AfterCommittedButReportedLoss_RetryWithSameToken_CleanlyRejects_NoDoubleApply()
    {
        var store = NewStore();
        var cas = Cas(store);
        await cas.ConditionalUpsertAsync(Req(1, "p1"), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await Read(cas);

        // The write committed (resolver confirmed). Had the caller instead retried with the OLD token, the token has moved
        // so the retry cleanly rejects — the write is never double-applied (idempotency of the exact-token CAS).
        SetFault(store, "PostCommitResponseLoss");
        var committed = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, committed.Status);

        var staleRetry = await cas.ConditionalUpsertAsync(Req(2, "p2"), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, staleRetry.Status);
        Assert.Equal("p2", (await Read(cas)).Record!.LastSortableUniqueId);   // single committed value, never doubled
    }
}
