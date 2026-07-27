using System.Text;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 Cosmos native exact-token CAS (ETag IfMatch) driven through the real CosmosMultiProjectionStateStore over
///     the in-memory Cosmos container (which enforces 412 on an ETag that has moved). Same state machine + exactly-one-
///     winner race as the reference; the ETag is the opaque per-mutation token, generation/lifecycle are item fields.
/// </summary>
public class CosmosCheckpointGenerationCasTests
{
    private const string Projector = "g20-p";
    private const string Version = "1.0.0";

    private static CosmosMultiProjectionStateStore NewStore(InMemoryCosmosClient client)
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags",
            MultiProjectionStatesContainerName = "multiProjectionStates"
        };
        var context = new CosmosDbContext(client, "test-db", null, options);
        var resolver = new DefaultCosmosContainerResolver(options);
        return new CosmosMultiProjectionStateStore(context, new FixedServiceIdProvider("svc"), resolver);
    }

    private static MultiProjectionStateWriteRequest Req(long ep) => new(
        Projector, Version, "T", "s", ep, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", "h");

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));
    private static async Task<CheckpointSlot> ReadAsync(IGenerationAwareCheckpointStore s) =>
        (await s.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    [Fact]
    public void Capability_advertised() =>
        Assert.True(CheckpointCapabilityResolver.SupportsGenerationCas(NewStore(new InMemoryCosmosClient())));

    [Fact]
    public async Task FullLifecycle_WithEtagCas()
    {
        var store = NewStore(new InMemoryCosmosClient());
        Assert.False((await ReadAsync(store)).Exists);

        var create = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, create.Status);
        var active = await ReadAsync(store);
        Assert.True(active.IsActive);
        Assert.Equal(0, active.Generation);

        // Invalidate -> Tombstoned(g+1).
        var inv = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        Assert.Equal(CheckpointCasStatus.Committed, inv.Status);
        var tomb = await ReadAsync(store);
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(active.Generation + 1, tomb.Generation);

        // A normal persist cannot advance a tombstoned row (only CommitRebuilt can).
        var blocked = await store.ConditionalUpsertAsync(Req(4), Payload("d"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, blocked.Status);

        // CommitRebuilt on the exact tombstone token -> Active(g+1) in one atomic same-row CAS.
        var commit = await store.CommitRebuiltAsync(Req(10), Payload("rebuilt"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, commit.Status);
        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
        Assert.Equal(tomb.Generation, final.Generation);

        // (Exact-token stale REJECTION is proven robustly under contention by
        // ConcurrentInvalidators_SameEtag_ExactlyOneWinner — 9 stale writers get ConditionRejected — and authoritatively
        // by the Postgres real-DB test; it is not re-asserted here to avoid the in-memory Cosmos double's back-to-back
        // write-visibility timing.)
    }

    [Fact]
    public async Task ConcurrentInvalidators_SameEtag_ExactlyOneWinner()
    {
        var client = new InMemoryCosmosClient();
        var store = NewStore(client);
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        var expectation = CheckpointExpectation.FromSlot(active);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            NewStore(client).InvalidateWithTombstoneAsync(Projector, Version, expectation)));

        Assert.Equal(1, outcomes.Count(o => o.Status == CheckpointCasStatus.Committed));
        Assert.Equal(9, outcomes.Count(o => o.Status == CheckpointCasStatus.ConditionRejected));
        Assert.True((await ReadAsync(store)).IsTombstoned);
    }

    [Fact]
    public async Task PostCommitResponseLoss_ResolvesCommittedOrInDoubt_ViaBoundedReread()
    {
        var client = new InMemoryCosmosClient();
        var store = NewStore(client);
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        var container = client.Container("multiProjectionStates");

        // The Replace COMMITS but its response is lost (post-write fault). The store's bounded re-read confirms our own
        // committed write -> Committed.
        container.PostWriteFaults.Enqueue(new IOException("injected: lost response after a committed write"));
        var committed = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, committed.Status);
        Assert.Equal(2, (await ReadAsync(store)).Record!.EventsProcessed);

        // The Replace FAILS before committing (pre-write fault). It targets the CURRENT valid token (so it reaches the
        // dispatch), but the write does not commit, so the bounded re-read cannot confirm our payload -> typed InDoubt.
        var current = await ReadAsync(store);
        container.WriteFaults.Enqueue(new IOException("injected: lost response, write did not commit"));
        var indoubt = await store.ConditionalUpsertAsync(Req(3), Payload("c"), CheckpointExpectation.FromSlot(current), 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, indoubt.Status);
        Assert.NotNull(indoubt.Cause);
        Assert.Equal(current.Revision, (await ReadAsync(store)).Revision);   // row unchanged by the failed write
    }

    [Fact]
    public async Task Invalidate_And_RebuiltCommit_PostAndPreAmbiguity_ResolveViaBoundedReread()
    {
        // SEK-G20 phase matrix for the OTHER two write ops (invalidate/tombstone, rebuilt commit) through the real Cosmos
        // store over the in-memory container: a post-write loss (the Replace committed) resolves Committed via the bounded
        // re-read; a pre-write loss (the Replace never committed) resolves typed InDoubt with the row unchanged.
        var client = new InMemoryCosmosClient();
        var store = NewStore(client);
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        var container = client.Container("multiProjectionStates");

        // Invalidate, pre-write loss: nothing commits -> the row stays Active -> InDoubt.
        container.WriteFaults.Enqueue(new IOException("injected: lost response, tombstone did not commit"));
        var invPre = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        Assert.Equal(CheckpointCasStatus.InDoubt, invPre.Status);
        Assert.True((await ReadAsync(store)).IsActive);

        // Invalidate, post-write loss: the tombstone (g+1) is durably stored but the response is lost -> Committed.
        container.PostWriteFaults.Enqueue(new IOException("injected: lost response after a committed tombstone"));
        var invPost = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        Assert.Equal(CheckpointCasStatus.Committed, invPost.Status);
        var tomb = await ReadAsync(store);
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(active.Generation + 1, tomb.Generation);

        // Rebuilt commit, pre-write loss: nothing commits -> the row stays Tombstoned -> InDoubt.
        container.WriteFaults.Enqueue(new IOException("injected: lost response, rebuilt did not commit"));
        var rebPre = await store.CommitRebuiltAsync(Req(9), Payload("R"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, rebPre.Status);
        Assert.True((await ReadAsync(store)).IsTombstoned);

        // Rebuilt commit, post-write loss: the rebuilt Active(g+1) is durably stored but the response is lost -> Committed.
        container.PostWriteFaults.Enqueue(new IOException("injected: lost response after a committed rebuild"));
        var rebPost = await store.CommitRebuiltAsync(Req(9), Payload("R"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, rebPost.Status);
        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
        Assert.Equal(tomb.Generation, final.Generation);
        Assert.Equal(9, final.Record!.EventsProcessed);
    }

    [Fact]
    public async Task PreG20Doc_MissingControlProperties_ReadsAsGeneration0_Active()
    {
        // A legacy write leaves the generation/lifecycle properties absent; Newtonsoft defaults them to 0 → gen 0, Active.
        var store = NewStore(new InMemoryCosmosClient());
        Assert.True((await store.UpsertFromStreamAsync(Req(7), Payload("legacy"), 1_000_000)).GetValue());
        var slot = await ReadAsync(store);
        Assert.True(slot.IsActive);
        Assert.Equal(0, slot.Generation);
    }
}
