using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G22: a non-empty command expectation may be newer than an actor's successfully cached empty state when another
///     cluster wrote to the shared store. Only that shape gets one authoritative read; every successful observation is
///     adopted before G19's exact-match decision.
/// </summary>
public class SekG22StaleEmptyReservationRefreshTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();

    [Fact]
    public async Task StaleEmptyCache_AuthoritativeVersionMatches_ReservationSucceeds_WithOneFallbackRead()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        var inner = new CoreInMemoryEventStore(_domainTypes.EventTypes);
        var store = new CountingLatestTagEventStore(inner);
        var actor = NewActor(tag, store);

        Assert.Equal(string.Empty, (await actor.GetLatestSortableUniqueIdAsync()).GetValue());
        store.ResetLatestTagCalls();

        var committed = EventTestHelper.CreateEvent(new StudentCreated(studentId, "remote"), tag);
        await inner.WriteEventAsync(committed, _domainTypes.EventTypes);

        var result = await actor.MakeReservationAsync(committed.SortableUniqueIdValue);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.LatestTagCalls);
        Assert.Equal(committed.SortableUniqueIdValue, (await actor.GetLatestSortableUniqueIdAsync()).GetValue());
    }

    [Fact]
    public async Task StaleEmptyCache_AuthoritativeStillEmpty_Conflicts_WithOneFallbackRead()
    {
        var tag = new StudentTag(Guid.NewGuid());
        var store = new CountingLatestTagEventStore(new CoreInMemoryEventStore(_domainTypes.EventTypes));
        var actor = NewActor(tag, store);
        await actor.GetLatestSortableUniqueIdAsync();
        store.ResetLatestTagCalls();

        var result = await actor.MakeReservationAsync(SortableUniqueId.GenerateNew());

        Assert.False(result.IsSuccess);
        Assert.Contains("has been modified", result.GetException().Message);
        Assert.Equal(1, store.LatestTagCalls);
        Assert.Empty(await actor.GetActiveReservationsAsync());
    }

    [Fact]
    public async Task OtherAuthoritativeVersion_IsAdoptedBeforeConflict_AndBlocksLaterExpectEmptyWithoutAnotherRead()
    {
        var tag = new StudentTag(Guid.NewGuid());
        var store = new CountingLatestTagEventStore(new CoreInMemoryEventStore(_domainTypes.EventTypes));
        var actor = NewActor(tag, store);
        await actor.GetLatestSortableUniqueIdAsync();
        store.ResetLatestTagCalls();

        var expected = SortableUniqueId.GenerateNew();
        var authoritative = SortableUniqueId.GenerateNew();
        Assert.NotEqual(expected, authoritative);
        store.LatestTagOutcome = _ => ResultBox.FromValue(StateFor(tag, authoritative));

        var mismatch = await actor.MakeReservationAsync(expected);
        var laterExpectEmpty = await actor.MakeReservationAsync(string.Empty);

        Assert.False(mismatch.IsSuccess);
        Assert.False(laterExpectEmpty.IsSuccess);
        Assert.Equal(authoritative, (await actor.GetLatestSortableUniqueIdAsync()).GetValue());
        Assert.Equal(1, store.LatestTagCalls);
        Assert.Empty(await actor.GetActiveReservationsAsync());
    }

    [Fact]
    public async Task AuthoritativeReadFailure_FailsClosed_AndCreatesNoReservation()
    {
        var tag = new StudentTag(Guid.NewGuid());
        var store = new CountingLatestTagEventStore(new CoreInMemoryEventStore(_domainTypes.EventTypes));
        var actor = NewActor(tag, store);
        await actor.GetLatestSortableUniqueIdAsync();
        store.ResetLatestTagCalls();
        var failure = new IOException("authoritative read failed");
        store.LatestTagOutcome = _ => ResultBox.Error<TagState>(failure);

        var result = await actor.MakeReservationAsync(SortableUniqueId.GenerateNew());

        Assert.False(result.IsSuccess);
        Assert.Contains("has been modified", result.GetException().Message);
        Assert.Equal(1, store.LatestTagCalls);
        Assert.Empty(await actor.GetActiveReservationsAsync());
    }

    [Fact]
    public async Task NormalAndEmptyExpectedPaths_PerformNoFallbackRead()
    {
        var populatedId = Guid.NewGuid();
        var populatedTag = new StudentTag(populatedId);
        var inner = new CoreInMemoryEventStore(_domainTypes.EventTypes);
        var committed = EventTestHelper.CreateEvent(new StudentCreated(populatedId, "existing"), populatedTag);
        await inner.WriteEventAsync(committed, _domainTypes.EventTypes);

        var matchingStore = new CountingLatestTagEventStore(inner);
        var matchingActor = NewActor(populatedTag, matchingStore);
        await matchingActor.GetLatestSortableUniqueIdAsync();
        matchingStore.ResetLatestTagCalls();
        Assert.True((await matchingActor.MakeReservationAsync(committed.SortableUniqueIdValue)).IsSuccess);
        Assert.Equal(0, matchingStore.LatestTagCalls);

        var mismatchStore = new CountingLatestTagEventStore(inner);
        var mismatchActor = NewActor(populatedTag, mismatchStore);
        await mismatchActor.GetLatestSortableUniqueIdAsync();
        mismatchStore.ResetLatestTagCalls();
        Assert.False((await mismatchActor.MakeReservationAsync(SortableUniqueId.GenerateNew())).IsSuccess);
        Assert.Equal(0, mismatchStore.LatestTagCalls);

        var emptyTag = new StudentTag(Guid.NewGuid());
        var emptyStore = new CountingLatestTagEventStore(inner);
        var emptyActor = NewActor(emptyTag, emptyStore);
        await emptyActor.GetLatestSortableUniqueIdAsync();
        emptyStore.ResetLatestTagCalls();
        Assert.True((await emptyActor.MakeReservationAsync(string.Empty)).IsSuccess);
        Assert.Equal(0, emptyStore.LatestTagCalls);

        var populatedEmptyExpectedStore = new CountingLatestTagEventStore(inner);
        var populatedEmptyExpectedActor = NewActor(populatedTag, populatedEmptyExpectedStore);
        await populatedEmptyExpectedActor.GetLatestSortableUniqueIdAsync();
        populatedEmptyExpectedStore.ResetLatestTagCalls();
        Assert.False((await populatedEmptyExpectedActor.MakeReservationAsync(string.Empty)).IsSuccess);
        Assert.Equal(0, populatedEmptyExpectedStore.LatestTagCalls);
    }

    [Fact]
    public async Task NonEmptyExpected_WithoutEventStore_FailsClosed()
    {
        var tag = new StudentTag(Guid.NewGuid());
        var actor = new GeneralTagConsistentActor(
            tag.GetTag(),
            null,
            new TagConsistentActorOptions(),
            _domainTypes.TagTypes);

        var result = await actor.MakeReservationAsync(SortableUniqueId.GenerateNew());

        Assert.False(result.IsSuccess);
        Assert.Contains("has been modified", result.GetException().Message);
        Assert.Empty(await actor.GetActiveReservationsAsync());
    }

    private GeneralTagConsistentActor NewActor(StudentTag tag, IEventStore store) =>
        new(tag.GetTag(), store, new TagConsistentActorOptions(), _domainTypes.TagTypes);

    private static TagState StateFor(StudentTag tag, string version)
    {
        var id = new TagStateId(tag, "TestProjector");
        return TagState.GetEmpty(id) with { LastSortedUniqueId = version, Version = version.Length == 0 ? 0 : 1 };
    }

    private sealed class CountingLatestTagEventStore : IEventStore
    {
        private readonly IEventStore _inner;
        private int _latestTagCalls;

        public CountingLatestTagEventStore(IEventStore inner) => _inner = inner;

        public int LatestTagCalls => Volatile.Read(ref _latestTagCalls);

        public Func<ITag, ResultBox<TagState>>? LatestTagOutcome { get; set; }

        public void ResetLatestTagCalls() => Interlocked.Exchange(ref _latestTagCalls, 0);

        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
        {
            Interlocked.Increment(ref _latestTagCalls);
            return LatestTagOutcome == null
                ? _inner.GetLatestTagAsync(tag)
                : Task.FromResult(LatestTagOutcome(tag));
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            _inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            _inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            _inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) =>
            _inner.WriteSerializableEventsAsync(events);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();
    }
}
