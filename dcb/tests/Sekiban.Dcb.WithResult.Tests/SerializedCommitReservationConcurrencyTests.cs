using System.Collections.Concurrent;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Tests;

public sealed class SerializedCommitReservationConcurrencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AllConsistencyReservations_AreInFlightBeforeAnyCompletes()
    {
        var (executor, accessor, request) = CreateCommit();
        var commit = executor.CommitSerializableEventsAsync(request);
        bool allStarted;
        try
        {
            allStarted = await Task.WhenAny(accessor.AllStarted.Task, Task.Delay(Timeout)) == accessor.AllStarted.Task;
            Assert.Empty(accessor.Reservations);
        }
        finally
        {
            accessor.Release.TrySetResult();
            await commit.WaitAsync(Timeout);
        }

        Assert.True(allStarted, "All consistency-tag requests must start while the reservation gate is closed.");
        Assert.True((await commit).IsSuccess);
        Assert.Equal(request.ConsistencyTags.Select(tag => tag.Tag), accessor.Started);
        Assert.Equal(3, accessor.Reservations.Count);
    }

    [Fact]
    public async Task FailedReservation_CancelsSuccessfulReservations_AndPreservesError()
    {
        var (executor, accessor, request) = CreateCommit(failingIndex: 1);
        var commit = executor.CommitSerializableEventsAsync(request);
        try
        {
            // Wait for the first request only so the sequential baseline can finish too.
            await accessor.FirstStarted.Task.WaitAsync(Timeout);
        }
        finally
        {
            accessor.Release.TrySetResult();
            await commit.WaitAsync(Timeout);
        }

        var result = await commit;
        Assert.False(result.IsSuccess);
        var error = Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Equal($"Failed to reserve tags: Tag {request.ConsistencyTags[1].Tag}: reservation rejected", error.Message);
        Assert.Equal(2, accessor.Reservations.Count);
        AssertAllSuccessfulReservationsCancelled(accessor);
    }

    [Fact]
    public async Task CancellationWhileFirstRequestIsBlocked_CancelsEverySuccessfulReservation()
    {
        var (executor, accessor, request) = CreateCommit();
        Assert.True(request.ConsistencyTags.Count >= 2);
        using var cancellation = new CancellationTokenSource();
        var commit = executor.CommitSerializableEventsAsync(request, cancellation.Token);
        try
        {
            await accessor.FirstStarted.Task.WaitAsync(Timeout);
            Assert.Empty(accessor.Reservations);
            cancellation.Cancel();
        }
        finally
        {
            accessor.Release.TrySetResult();
            await commit.WaitAsync(Timeout);
        }

        var result = await commit;
        Assert.False(result.IsSuccess);
        Assert.IsAssignableFrom<OperationCanceledException>(result.GetException());
        Assert.NotEmpty(accessor.Reservations);
        AssertAllSuccessfulReservationsCancelled(accessor);
        Assert.Equal(3, accessor.Reservations.Count);
    }

    private static void AssertAllSuccessfulReservationsCancelled(GatedActorAccessor accessor) =>
        Assert.Equal(accessor.Reservations.OrderBy(r => r.Tag), accessor.Cancellations.OrderBy(r => r.Tag));

    private static (GeneralSekibanExecutor Executor, GatedActorAccessor Accessor, SerializedCommitRequest Request)
        CreateCommit(int? failingIndex = null)
    {
        var domain = DomainType.GetDomainTypes();
        string[] tags = Enumerable.Range(0, 3).Select(_ => $"Student:{Guid.NewGuid()}").ToArray();
        var accessor = new GatedActorAccessor(tags, failingIndex);
        var executor = new GeneralSekibanExecutor(new Sekiban.Dcb.Testing.InMemoryEventStore(domain.EventTypes), accessor, domain);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new StudentCreated(Guid.NewGuid(), "Reservation", 5),
            domain.JsonSerializerOptions);
        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(StudentCreated), tags)],
            tags.Select(tag => new ConsistencyTagEntry(tag, "")).ToArray());
        return (executor, accessor, request);
    }

    private sealed class GatedActorAccessor(string[] tags, int? failingIndex) : IActorObjectAccessor
    {
        private int TagCount => tags.Length;
        private string? FailingTag => failingIndex is { } index ? tags[index] : null;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                owner.FirstStarted.TrySetResult();
                if (owner.Started.Count == owner.TagCount) owner.AllStarted.TrySetResult();
                await owner.Release.Task.WaitAsync(TimeSpan.FromSeconds(20));
                if (tag == owner.FailingTag)
                    return ResultBox.Error<TagWriteReservation>(new InvalidOperationException("reservation rejected"));
                var reservation = new TagWriteReservation(Guid.NewGuid().ToString(), "", tag);
                owner.Reservations.Enqueue(reservation);
                return ResultBox.FromValue(reservation);
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
