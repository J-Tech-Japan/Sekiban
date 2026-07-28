using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using Xunit;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G19: <c>GeneralTagConsistentActor.MakeReservationAsync</c> first-write reservation correctness. An empty caller
///     <c>lastSortableUniqueId</c> means "I expect this tag to be EMPTY" (a first write) — evaluated as an EXACT-MATCH
///     comparison after null/empty normalization, in-lock and post-catch-up. This pins all five comparison classes, the
///     deterministic non-overlapping double-create reproduction (#1085), overlap races, the catch-up interaction, the
///     cancellation/expiry no-lockout property, and the honest PER-CLUSTER guarantee boundary (cross-cluster uniqueness is
///     the storage layer's job — G15/G16 unique-append — and SEK-G18 owns convergence over any durable duplicates).
///     Conflicts surface through the EXISTING <c>ResultBox.Error</c> channel; no new public exception type is added.
/// </summary>
public class SekG19TagFirstWriteReservationTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();

    private IEventStore NewStore() => new CoreInMemoryEventStore(_domainTypes.EventTypes);

    private GeneralTagConsistentActor NewActor(string tagName, IEventStore store, TagConsistentActorOptions? options = null) =>
        new(tagName, store, options ?? new TagConsistentActorOptions(), _domainTypes.TagTypes);

    // Writes a StudentCreated event for the tag (its committed state becomes the tag's latest version).
    private async Task<string> CommitCreateAsync(IEventStore store, Guid studentId, StudentTag tag, string name)
    {
        var ev = EventTestHelper.CreateEvent(new StudentCreated(studentId, name), tag);
        await store.WriteEventAsync(ev, _domainTypes.EventTypes);
        return ev.SortableUniqueIdValue;
    }

    [Fact]
    public async Task FiveComparisonClasses_BehaveAsSpecified_AfterNormalization()
    {
        // Class 1 — empty expected / empty current => PASS (first write on an empty tag).
        {
            var id = Guid.NewGuid();
            var actor = NewActor(new StudentTag(id).GetTag(), NewStore());
            Assert.True((await actor.MakeReservationAsync("")).IsSuccess);
        }
        // Class 3 — non-empty expected / empty current => CONFLICT (the tag never had that version).
        {
            var id = Guid.NewGuid();
            var actor = NewActor(new StudentTag(id).GetTag(), NewStore());
            var r = await actor.MakeReservationAsync(SortableUniqueId.GenerateNew());
            Assert.False(r.IsSuccess);
            Assert.Contains("has been modified", r.GetException().Message);   // stable safe context
        }
        // Classes 2/4/5 need a tag WITH committed state.
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var store = NewStore();
        var committed = await CommitCreateAsync(store, studentId, tag, "John");

        // Class 2 — empty expected / non-empty current => CONFLICT (a second first-write against existing state; #1085).
        {
            var actor = NewActor(tag.GetTag(), store);
            var r = await actor.MakeReservationAsync("");
            Assert.False(r.IsSuccess);
            Assert.Contains("has been modified", r.GetException().Message);
        }
        // Class 4 — non-empty expected mismatch / non-empty current => CONFLICT.
        {
            var actor = NewActor(tag.GetTag(), store);
            var r = await actor.MakeReservationAsync(SortableUniqueId.GenerateNew());
            Assert.False(r.IsSuccess);
        }
        // Class 5 — non-empty expected match / non-empty current => PASS (an ordinary update reservation).
        {
            var actor = NewActor(tag.GetTag(), store);
            Assert.True((await actor.MakeReservationAsync(committed)).IsSuccess);
        }
    }

    [Fact]
    public async Task ActiveReservationRejection_Unchanged()
    {
        var id = Guid.NewGuid();
        var actor = NewActor(new StudentTag(id).GetTag(), NewStore());
        Assert.True((await actor.MakeReservationAsync("")).IsSuccess);        // first write holds the reservation
        var second = await actor.MakeReservationAsync("");                    // a second attempt while one is active
        Assert.False(second.IsSuccess);
        Assert.Contains("currently reserved", second.GetException().Message); // the pre-existing active-reservation guard
    }

    [Fact]
    public async Task SequentialNonOverlappingDoubleCreate_ExactlyOneSucceeds_TenOfTen()
    {
        // The upstream reproduction (#1085): two NON-overlapping create flows for the same tag. Flow A reserves-empty,
        // commits, and confirms (releasing its reservation); flow B then reserves-empty and — post-catch-up, in-lock —
        // CONFLICTS against A's committed state. Exactly one success + one ResultBox.Error, deterministically 10/10.
        for (var i = 0; i < 10; i++)
        {
            var studentId = Guid.NewGuid();
            var tag = new StudentTag(studentId);
            var store = NewStore();
            var actor = NewActor(tag.GetTag(), store);

            var a = await actor.MakeReservationAsync("");                     // flow A: first write on an empty tag
            Assert.True(a.IsSuccess);
            await CommitCreateAsync(store, studentId, tag, "A");              // A durably commits its create
            Assert.True(await actor.ConfirmReservationAsync(a.GetValue()));   // A's reservation released; forces re-catch-up

            var b = await actor.MakeReservationAsync("");                     // flow B: a second, non-overlapping first write
            Assert.False(b.IsSuccess);                                        // conflicts against A's committed state
            Assert.Contains("has been modified", b.GetException().Message);
        }
    }

    [Fact]
    public async Task ConcurrentOverlappingFirstWrites_ExactlyOneSucceeds()
    {
        var id = Guid.NewGuid();
        var actor = NewActor(new StudentTag(id).GetTag(), NewStore());
        var attempts = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => actor.MakeReservationAsync("")));
        Assert.Equal(1, attempts.Count(r => r.IsSuccess));                   // the reservation lock admits exactly one
        Assert.Equal(15, attempts.Count(r => !r.IsSuccess));
    }

    [Fact]
    public async Task PostRestartCatchUp_LateFirstWrite_ConflictsAgainstCommittedWinner()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var store = NewStore();

        var actor1 = NewActor(tag.GetTag(), store);
        var a = await actor1.MakeReservationAsync("");
        Assert.True(a.IsSuccess);
        await CommitCreateAsync(store, studentId, tag, "winner");
        await actor1.ConfirmReservationAsync(a.GetValue());

        // "Restart": a fresh activation catches up from the store and a late first-write reservation must CONFLICT.
        var actor2 = NewActor(tag.GetTag(), store);
        var late = await actor2.MakeReservationAsync("");
        Assert.False(late.IsSuccess);
        Assert.Contains("has been modified", late.GetException().Message);
    }

    [Fact]
    public async Task Cancellation_RestoresFirstWriteAbility_NoLockout()
    {
        var id = Guid.NewGuid();
        var actor = NewActor(new StudentTag(id).GetTag(), NewStore());
        var a = await actor.MakeReservationAsync("");
        Assert.True(a.IsSuccess);
        Assert.True(await actor.CancelReservationAsync(a.GetValue()));        // the winner never committed; cancel releases it

        // The tag is still empty, so a fresh first-write reservation succeeds — no permanent lockout.
        Assert.True((await actor.MakeReservationAsync("")).IsSuccess);
    }

    [Fact]
    public async Task Expiry_RestoresFirstWriteAbility_NoLockout()
    {
        // A zero-second cancellation window makes the reservation expire immediately; the next reservation's expiry cleanup
        // removes it and the first-write ability is restored — a lost/expired winner never locks the tag out.
        var id = Guid.NewGuid();
        var actor = NewActor(new StudentTag(id).GetTag(), NewStore(), new TagConsistentActorOptions { CancellationWindowSeconds = 0 });
        Assert.True((await actor.MakeReservationAsync("")).IsSuccess);        // immediately-expiring reservation
        Assert.True((await actor.MakeReservationAsync("")).IsSuccess);        // expiry cleanup restores first-write
    }

    [Fact]
    public async Task CrossClusterFlow_BothActorsReserveAndDurablyAppend_StorageIsTheCrossClusterAuthority()
    {
        // GUARANTEE BOUNDARY (honest): the actor fix holds at-most-one first write PER CLUSTER (Orleans single activation
        // per tag). Two INDEPENDENT activations (two clusters) over the SAME shared store BOTH reserve-empty and BOTH
        // durably append a create — the actor layer does NOT prevent cross-cluster duplicates. Cross-cluster uniqueness is
        // the storage layer's job (G15/G16 conditional unique-append); SEK-G18 owns convergence over the durable duplicates.
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var store = NewStore();

        var clusterA = NewActor(tag.GetTag(), store);   // activation on cluster A
        var clusterB = NewActor(tag.GetTag(), store);   // independent activation on cluster B (separate actor instance)

        var ra = await clusterA.MakeReservationAsync("");
        var rb = await clusterB.MakeReservationAsync("");
        Assert.True(ra.IsSuccess);
        Assert.True(rb.IsSuccess);   // BOTH per-cluster reservations succeed — this is expected, not a regression

        // Both flows durably append their create to the shared store: the duplicates land in storage (the cross-cluster
        // authority) exactly as the boundary documents.
        await CommitCreateAsync(store, studentId, tag, "A");
        await CommitCreateAsync(store, studentId, tag, "B");
        var latest = await store.GetLatestTagAsync(_domainTypes.TagTypes.GetTag(tag.GetTag()));
        Assert.True(latest.IsSuccess);   // the shared store carries the duplicate-create state; storage unique-append is the fix vector
    }
}
