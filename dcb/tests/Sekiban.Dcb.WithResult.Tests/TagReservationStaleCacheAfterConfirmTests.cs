using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Reflection;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using Sekiban.Dcb.TestSupport;
namespace Sekiban.Dcb.Tests;

public class TagReservationStaleCacheAfterConfirmTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScriptedInterleave_YieldsExactlyOneCommit(bool seedExisting)
    {
        var f = new Fixture();
        var expected = seedExisting ? await f.WriteAsync("seed") : string.Empty;
        var winner = (await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(expected))).GetValue();
        await f.WriteAsync("winner");
        var gate = ReservationLock(f.Actor);
        await gate.WaitAsync();
        Task<bool> confirm;
        Task<ResultBox<TagWriteReservation>> loser;
        try
        {
            // Catch-up may now run inside confirm. A transient failed catch-up (still swallowed by design)
            // prevents its publication from taking the test-held lock, so BOTH calls queue directly on
            // _reservationLock in order on main and the fix. The post-confirm authoritative refresh succeeds.
            // Every ungated read completes synchronously (asserted in the wrapper), so an incomplete returned
            // task proves each call has reached its lock wait; no timing delay or CurrentCount inference.
            f.Store.AfterRead = _ => ResultBox.Error<TagState>(new IOException("staging catch-up failure"));
            confirm = f.Actor.ConfirmReservationAsync(winner);
            Assert.False(confirm.IsCompleted);
            loser = ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(expected));
            Assert.False(loser.IsCompleted);
        }
        finally
        {
            f.Store.AfterRead = null;
            gate.Release();
        }
        Assert.True(await confirm.WaitAsync(Timeout));
        var result = await loser.WaitAsync(Timeout);
        if (result.IsSuccess)
        {
            await f.WriteAsync("loser");
            Assert.True(await f.Actor.ConfirmReservationAsync(result.GetValue()));
        }
        var committed = (await f.Inner.ReadEventsByTagAsync(f.Tag).AsResultBoxAsync()).GetValue().Count() - (seedExisting ? 1 : 0);
        Assert.Equal(1, committed);
        Conflict(result);
    }

    [Fact]
    public async Task NotifyDuringRefresh_RetriesAndRejectsStaleExpectation()
    {
        var f = new Fixture();
        var x = await f.WriteAsync("X");
        var reservation = (await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(x))).GetValue();
        Assert.True(await f.Actor.ConfirmReservationAsync(reservation));
        using var read = f.Store.GateNextRead();
        var pending = f.Actor.MakeReservationAsync(x);
        await read.Captured.Task.WaitAsync(Timeout);
        var y = await f.WriteAsync("Y");
        await f.Actor.NotifyEventWrittenAsync();
        var calls = f.Store.Calls;
        read.Release.TrySetResult();
        Conflict(await ResultBox.WrapTry(() => pending.WaitAsync(Timeout)));
        Assert.Equal(calls + 1, f.Store.Calls);
        Assert.Equal(y, Cache(f.Actor));
    }

    [Fact]
    public async Task SekG22Read_WithEqualGenerations_RetriesOnNotify()
    {
        var f = new Fixture();
        Assert.Equal(string.Empty, (await ResultBox.WrapTry(() => f.Actor.GetLatestSortableUniqueIdAsync())).GetValue());
        var x = await f.WriteAsync("X"); // No notification: cached empty and equal generations.
        using var read = f.Store.GateNextRead();
        var pending = f.Actor.MakeReservationAsync(x);
        await read.Captured.Task.WaitAsync(Timeout);
        var y = await f.WriteAsync("Y");
        await f.Actor.NotifyEventWrittenAsync();
        var calls = f.Store.Calls;
        read.Release.TrySetResult();
        Conflict(await ResultBox.WrapTry(() => pending.WaitAsync(Timeout)));
        Assert.Equal(calls + 1, f.Store.Calls);
        Assert.Equal(y, Cache(f.Actor));
    }

    [Fact]
    public async Task DelayedCatchUp_RacingRefresh_NeverLowersCacheOrPublishesCurrentCompletion()
    {
        var f = new Fixture();
        var x = await f.WriteAsync("X");
        await f.Actor.GetLatestSortableUniqueIdAsync();
        var gate = ReservationLock(f.Actor);
        await gate.WaitAsync();
        Task<ResultBox<TagWriteReservation>> reservation;
        Task<ResultBox<string>> getter;
        using var read = f.Store.GateNextRead();
        string y;
        try
        {
            // Pre-stage past EnsureCatchUpCompletedAsync while completion is still current. The synchronous
            // read wrapper means this incomplete task is queued on the test-held reservation lock.
            reservation = ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(x));
            Assert.False(reservation.IsCompleted);
            await f.Actor.NotifyEventWrittenAsync();
            getter = ResultBox.WrapTry(() => f.Actor.GetLatestSortableUniqueIdAsync());
            await read.Captured.Task.WaitAsync(Timeout); // Snapshot X, holding _catchUpLock.
            y = await f.WriteAsync("Y");
            await f.Actor.NotifyEventWrittenAsync();
        }
        finally { gate.Release(); }
        Conflict(await reservation.WaitAsync(Timeout)); // Refresh is allowed while catch-up remains gated.
        Assert.Equal(y, Cache(f.Actor));
        read.Release.TrySetResult();
        Assert.Equal(y, (await getter.WaitAsync(Timeout)).GetValue());
        Assert.Equal(y, Cache(f.Actor));
        Assert.True(Generation(f.Actor, "_catchUpGeneration") < Generation(f.Actor, "_invalidationGeneration"));
        Conflict(await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(x)));
    }

    [Fact]
    public async Task InvalidationBetweenCatchUpReadAndCompletion_GetterRetriesNextCall()
    {
        var f = new Fixture();
        var x = await f.WriteAsync("X");
        await f.Actor.GetLatestSortableUniqueIdAsync();
        await f.Actor.NotifyEventWrittenAsync();
        using var read = f.Store.GateNextRead();
        var getter = f.Actor.GetLatestSortableUniqueIdAsync();
        await read.Captured.Task.WaitAsync(Timeout);
        var y = await f.WriteAsync("Y");
        await f.Actor.NotifyEventWrittenAsync();
        read.Release.TrySetResult();
        Assert.Equal(x, (await ResultBox.WrapTry(() => getter.WaitAsync(Timeout))).GetValue());
        var calls = f.Store.Calls;
        Assert.Equal(y, (await ResultBox.WrapTry(() => f.Actor.GetLatestSortableUniqueIdAsync())).GetValue());
        Assert.Equal(calls + 1, f.Store.Calls);
        Conflict(await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(x)));
    }

    [Fact]
    public async Task UnderLockReadFailure_FailsClosed_ThenRecovers()
    {
        var f = new Fixture();
        var reservation = (await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(string.Empty))).GetValue();
        // The confirm invalidates under the lock, so the next access does a lock-free catch-up read (which succeeds
        // here) and then the authoritative under-lock read (which fails).
        Assert.True(await f.Actor.ConfirmReservationAsync(reservation));
        var calls = f.Store.Calls;
        f.Store.AfterRead = result => ReservationLock(f.Actor).CurrentCount == 0
            ? ResultBox.Error<TagState>(new IOException("under-lock failure")) : result;
        var failed = await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(string.Empty));
        Assert.False(failed.IsSuccess);
        Assert.Equal("under-lock failure", failed.GetException().InnerException!.Message);
        Assert.Equal(calls + 2, f.Store.Calls);
        Assert.Empty(await f.Actor.GetActiveReservationsAsync());
        f.Store.AfterRead = null;
        // Catch-up is already complete for this generation; only the under-lock read is retried.
        Assert.True((await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(string.Empty))).IsSuccess);
        Assert.Equal(calls + 3, f.Store.Calls);
    }

    [Fact]
    public async Task InvalidationOnEveryUnderLockRead_FailsClosedAfterThreeAttempts()
    {
        var f = new Fixture();
        await f.Actor.GetLatestSortableUniqueIdAsync();
        await f.Actor.ConfirmReservationAsync(null!); // Also invalidates a null confirm.
        await f.Actor.GetLatestSortableUniqueIdAsync();
        var calls = f.Store.Calls;
        f.Store.AfterRead = result =>
        {
            Assert.Equal(0, ReservationLock(f.Actor).CurrentCount);
            Assert.True(f.Actor.NotifyEventWrittenAsync().IsCompletedSuccessfully);
            return result;
        };
        var failed = await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(string.Empty));
        Assert.False(failed.IsSuccess);
        Assert.Contains("is being written concurrently", failed.GetException().Message);
        Assert.Equal(calls + 3, f.Store.Calls);
        f.Store.AfterRead = null;
        Assert.Empty(await f.Actor.GetActiveReservationsAsync());
    }

    [Fact]
    public async Task SteadyState_CancelledReservations_DoNotReadAgain()
    {
        var f = new Fixture();
        await f.Actor.GetLatestSortableUniqueIdAsync();
        var calls = f.Store.Calls;
        for (var i = 0; i < 3; i++)
        {
            var reservation = (await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(string.Empty))).GetValue();
            Assert.True(await f.Actor.CancelReservationAsync(reservation));
        }
        Assert.Equal(calls, f.Store.Calls);
    }

    [Fact]
    public async Task Confirm_LeavesCatchUpIncomplete_SoAnUnnotifiedLaterWriteIsObserved()
    {
        // Another cluster's write is never notified to this actor; the first access after a confirm must re-read.
        var f = new Fixture();
        var reservation = (await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(string.Empty))).GetValue();
        await f.WriteAsync("own");
        Assert.True(await f.Actor.ConfirmReservationAsync(reservation));
        var other = await f.WriteAsync("other-cluster");
        Assert.Equal(other, (await ResultBox.WrapTry(() => f.Actor.GetLatestSortableUniqueIdAsync())).GetValue());
    }

    [Fact]
    public async Task Notify_RejectsStaleExpectation()
    {
        var f = new Fixture();
        var x = await f.WriteAsync("X");
        await f.Actor.GetLatestSortableUniqueIdAsync();
        await f.WriteAsync("Y");
        await f.Actor.NotifyEventWrittenAsync();
        Conflict(await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(x)));
    }

    [Fact]
    public async Task SekG30_NullExpectation_SkipsFailingUnderLockRefresh()
    {
        var f = new Fixture();
        await f.Actor.GetLatestSortableUniqueIdAsync();
        await f.Actor.ConfirmReservationAsync(null!);
        await f.Actor.GetLatestSortableUniqueIdAsync();
        var calls = f.Store.Calls;
        f.Store.AfterRead = _ => throw new IOException("must not read");
        var reservation = await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(null));
        Assert.True(reservation.IsSuccess);
        Assert.Equal(calls, f.Store.Calls);
        Assert.True(await f.Actor.CancelReservationAsync(reservation.GetValue()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCatchUp_PublishesNoCompletion_AndRetries(bool throws)
    {
        var f = new Fixture();
        f.Store.AfterRead = _ => throws ? throw new IOException("read failure")
            : ResultBox.Error<TagState>(new IOException("read failure"));
        await f.Actor.GetLatestSortableUniqueIdAsync();
        await f.Actor.GetLatestSortableUniqueIdAsync();
        Assert.Equal(2, f.Store.Calls);
        f.Store.AfterRead = null;
        var x = await f.WriteAsync("X");
        Assert.Equal(x, (await ResultBox.WrapTry(() => f.Actor.GetLatestSortableUniqueIdAsync())).GetValue());
    }

    [Fact]
    public async Task NonMatchingConfirm_InvalidatesBeforeEarlyReturn()
    {
        var f = new Fixture();
        await f.Actor.GetLatestSortableUniqueIdAsync();
        var x = await f.WriteAsync("X");
        Assert.False(await f.Actor.ConfirmReservationAsync(new TagWriteReservation("missing", "", f.Tag.GetTag())));
        Conflict(await ResultBox.WrapTry(() => f.Actor.MakeReservationAsync(string.Empty)));
        Assert.Equal(x, Cache(f.Actor));
    }

    private static void Conflict(ResultBox<TagWriteReservation> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Contains("has been modified", result.GetException().Message);
    }

    private static object Field(GeneralTagConsistentActor actor, string name) =>
        typeof(GeneralTagConsistentActor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(actor)!;
    private static SemaphoreSlim ReservationLock(GeneralTagConsistentActor actor) => (SemaphoreSlim)Field(actor, "_reservationLock");
    private static string Cache(GeneralTagConsistentActor actor) => (string)Field(actor, "_latestSortableUniqueId");
    private static long Generation(GeneralTagConsistentActor actor, string name) => (long)Field(actor, name);

    private sealed class Fixture
    {
        private readonly DcbDomainTypes _types = DomainType.GetDomainTypes();
        public StudentTag Tag { get; } = new(Guid.NewGuid());
        public CoreInMemoryEventStore Inner { get; }
        public ScriptedStore Store { get; }
        public GeneralTagConsistentActor Actor { get; }
        public Fixture()
        {
            Inner = new CoreInMemoryEventStore(_types.EventTypes);
            Store = new ScriptedStore(Inner);
            Actor = new GeneralTagConsistentActor(Tag.GetTag(), Store, new TagConsistentActorOptions(), _types.TagTypes);
        }
        public async Task<string> WriteAsync(string name)
        {
            var ev = EventTestHelper.CreateEvent(new StudentCreated(Guid.NewGuid(), name), Tag);
            Assert.True((await Inner.WriteEventAsync(ev, _types.EventTypes).AsResultBoxAsync()).IsSuccess);
            return ev.SortableUniqueIdValue;
        }
    }

    private sealed class ReadGate : IDisposable
    {
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Release.TrySetResult();
    }

    private sealed class ScriptedStore(IEventStore inner) : IEventStore
    {
        private int _calls;
        private ReadGate? _nextRead;
        public int Calls => Volatile.Read(ref _calls);
        public Func<ResultBox<TagState>, ResultBox<TagState>>? AfterRead { get; set; }
        public ReadGate GateNextRead()
        {
            var gate = new ReadGate();
            Assert.Null(Interlocked.Exchange(ref _nextRead, gate));
            return gate;
        }
        public async Task<TagState> GetLatestTagAsync(ITag tag)
        {
            Interlocked.Increment(ref _calls);
            var snapshot = inner.GetLatestTagAsync(tag);
            // A returned incomplete actor task without a gate proves it reached a semaphore wait, not store I/O.
            Assert.True(snapshot.IsCompleted);
            var result = await snapshot.AsResultBoxAsync();
            // Model a successful empty snapshot for an absent tag.
            // Injected failures below remain errors and exercise the no-completion contract.
            if (!result.IsSuccess && !await inner.TagExistsAsync(tag))
            {
                result = ResultBox.FromValue(TagState.GetEmpty(new TagStateId(tag, "TestProjector")));
            }
            var gate = Interlocked.Exchange(ref _nextRead, null);
            if (gate != null)
            {
                gate.Captured.TrySetResult();
                await gate.Release.Task.WaitAsync(Timeout);
            }
            var outcome = AfterRead == null ? result : AfterRead(result);
            return outcome.IsSuccess ? outcome.GetValue() : throw outcome.GetException();
        }
        public Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
        public Task<bool> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
        public Task<long> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
        public Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null) => inner.GetAllTagsAsync(tagGroup);
        public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            inner.ReadAllSerializableEventsAsync(since);
        public Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) =>
            inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId) =>
            inner.ReadSerializableEventAsync(eventId);
        public Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) =>
            inner.WriteSerializableEventsAsync(events);
        public Task<string> GetLatestSortableUniqueIdAsync() => inner.GetLatestSortableUniqueIdAsync();
    }
}
