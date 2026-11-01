using System.Collections.Concurrent;
using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public sealed class TypedCommitReservationCancellationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CancellationWhileRequestsAreInFlight_CancelsEverySuccessfulReservation()
    {
        var (executor, accessor, tags) = CreateCommit();
        using var cancellation = new CancellationTokenSource();
        var commit = ExecuteAsync(executor, tags, cancellation.Token);
        try
        {
            await accessor.AllStarted.Task.WaitAsync(Timeout);
            Assert.Empty(accessor.Reservations);
            cancellation.Cancel();
        }
        finally
        {
            accessor.Release.TrySetResult();
            await commit.WaitAsync(Timeout);
            // On the broken path the commit can return before the reservation requests finish.
            await accessor.AllCompleted.Task.WaitAsync(Timeout);
        }

        var result = await commit;
        Assert.False(result.IsSuccess);
        Assert.IsAssignableFrom<OperationCanceledException>(result.GetException());
        Assert.Equal(tags.Length, accessor.Cancellations.Count);
        Assert.Equal(tags.Length, accessor.Reservations.Count);
        AssertAllSuccessfulReservationsCancelled(accessor);
    }

    [Fact]
    public async Task FailedReservation_CancelsSuccessfulReservations_AndPreservesError()
    {
        var (executor, accessor, tags) = CreateCommit(failingIndex: 1);
        var commit = ExecuteAsync(executor, tags);
        try
        {
            await accessor.AllStarted.Task.WaitAsync(Timeout);
        }
        finally
        {
            accessor.Release.TrySetResult();
            await commit.WaitAsync(Timeout);
            await accessor.AllCompleted.Task.WaitAsync(Timeout);
        }

        var result = await commit;
        Assert.False(result.IsSuccess);
        var error = Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Equal($"Failed to reserve tags: Tag {tags[1].GetTag()}: reservation rejected", error.Message);
        Assert.Equal(tags.Length - 1, accessor.Reservations.Count);
        Assert.Equal(tags.Length - 1, accessor.Cancellations.Count);
        AssertAllSuccessfulReservationsCancelled(accessor);
    }

    [Fact]
    public async Task CancellationBeforeStart_RequestsNoReservations()
    {
        var (executor, accessor, tags) = CreateCommit();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await ExecuteAsync(executor, tags, cancellation.Token).WaitAsync(Timeout);

        Assert.False(result.IsSuccess);
        Assert.IsAssignableFrom<OperationCanceledException>(result.GetException());
        Assert.Empty(accessor.Started);
        Assert.Empty(accessor.Reservations);
        Assert.Empty(accessor.Cancellations);
    }

    private static void AssertAllSuccessfulReservationsCancelled(GatedActorAccessor accessor) =>
        Assert.Equal(accessor.Reservations.OrderBy(r => r.Tag), accessor.Cancellations.OrderBy(r => r.Tag));

    private static Task<ResultBox<ExecutionResult>> ExecuteAsync(
        GeneralSekibanExecutor executor, StudentTag[] tags, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new TestCommand(),
            (_, _) => Task.FromResult(EventOrNone.EventWithTags(
                new StudentCreated(tags[0].StudentId, "Reservation", 5), tags)), cancellationToken);

    private static (GeneralSekibanExecutor Executor, GatedActorAccessor Accessor, StudentTag[] Tags)
        CreateCommit(int? failingIndex = null)
    {
        var domain = DomainType.GetDomainTypes();
        var tags = Enumerable.Range(0, 3).Select(_ => new StudentTag(Guid.NewGuid())).ToArray();
        var accessor = new GatedActorAccessor(tags.Select(tag => tag.GetTag()).ToArray(), failingIndex);
        var executor = new GeneralSekibanExecutor(
            new Sekiban.Dcb.Testing.InMemoryEventStore(domain.EventTypes), accessor, domain);
        return (executor, accessor, tags);
    }

    private sealed record TestCommand : ICommand;

    private sealed class GatedActorAccessor(string[] tags, int? failingIndex) : IActorObjectAccessor
    {
        private int _completed;
        private int TagCount => tags.Length;
        private string? FailingTag => failingIndex is { } index ? tags[index] : null;

        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Started { get; } = new();
        public ConcurrentQueue<TagWriteReservation> Reservations { get; } = new();
        public ConcurrentQueue<TagWriteReservation> Cancellations { get; } = new();

        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class =>
            Task.FromResult(ResultBox.FromValue((T)(object)new GatedActor(this, actorId)));

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(tags.Contains(actorId));

        private sealed class GatedActor(GatedActorAccessor owner, string tag) : ITagConsistentActorCommon
        {
            public Task<string> GetTagActorIdAsync() => Task.FromResult(tag);
            public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => Task.FromResult(ResultBox.FromValue(""));

            public async Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId)
            {
                owner.Started.Enqueue(tag);
                if (owner.Started.Count == owner.TagCount) owner.AllStarted.TrySetResult();
                await owner.Release.Task.WaitAsync(TimeSpan.FromSeconds(20));
                try
                {
                    if (tag == owner.FailingTag)
                        return ResultBox.Error<TagWriteReservation>(new InvalidOperationException("reservation rejected"));
                    var reservation = new TagWriteReservation(Guid.NewGuid().ToString(), "", tag);
                    owner.Reservations.Enqueue(reservation);
                    return ResultBox.FromValue(reservation);
                }
                finally
                {
                    if (Interlocked.Increment(ref owner._completed) == owner.TagCount)
                        owner.AllCompleted.TrySetResult();
                }
            }

            public Task<bool> CancelReservationAsync(TagWriteReservation reservation)
            {
                Assert.Equal(tag, reservation.Tag);
                owner.Cancellations.Enqueue(reservation);
                return Task.FromResult(true);
            }

            public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation)
            {
                Assert.Equal(tag, reservation.Tag);
                return Task.FromResult(true);
            }

            public Task NotifyEventWrittenAsync() => Task.CompletedTask;
        }
    }
}
