using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     The command context is where the swallowed-failure bug actually bit: <c>TagExistsAsync</c> hands back a
///     <c>bool</c>, so before this change a storage failure came back as <c>false</c> — indistinguishable from
///     "no, that tag does not exist". A command handler would then happily create the student that already existed.
/// </summary>
public class CommandContextBoundaryTests
{
    private static readonly StudentTag Tag = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task TagExistsAsync_WhenTheStoreFails_Throws_InsteadOfAnsweringFalse()
    {
        var failure = new InvalidOperationException("event store unreachable");
        var context = new FailingCoreCommandContext(failure);

        // Before this change the adapter returned `false` here and the handler carried on.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CommandContextAdapter(context).TagExistsAsync(Tag));

        Assert.Same(failure, thrown);
        Assert.Equal("ICommandContext.TagExistsAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
        Assert.Equal(Tag.GetTag(), thrown.Data[GuardedUnwrap.TargetDataKey]);
    }

    [Fact]
    public async Task TagExistsAsync_WhenTheCallIsCancelled_ThrowsCancellation_WithItsToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancellation = new OperationCanceledException("cancelled", cts.Token);

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => new CommandContextAdapter(new FailingCoreCommandContext(cancellation)).TagExistsAsync(Tag));

        // Same instance and same token: cancellation stays cancellation, at a value-typed boundary too.
        Assert.Same(cancellation, thrown);
        Assert.Equal(cts.Token, thrown.CancellationToken);
    }

    [Fact]
    public async Task TagExistsAsync_WhenAnInternalPathReturnsNoTask_NamesTheBoundary()
    {
        // The issue #1045 shape: a missing result used to become a NullReferenceException with no message.
        var thrown = await Assert.ThrowsAsync<SekibanBoundaryException>(
            () => new CommandContextAdapter(new NullReturningCoreCommandContext()).TagExistsAsync(Tag));

        Assert.Equal("ICommandContext.TagExistsAsync", thrown.Operation);
        Assert.Equal(Tag.GetTag(), thrown.Target);
        Assert.Contains("ICommandContext.TagExistsAsync", thrown.Message);
    }

    [Fact]
    public async Task GetTagLatestSortableUniqueIdAsync_WhenTheStoreFails_RethrowsTheOriginal()
    {
        var failure = new InvalidOperationException("event store unreachable");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CommandContextAdapter(new FailingCoreCommandContext(failure))
                .GetTagLatestSortableUniqueIdAsync(Tag));

        Assert.Same(failure, thrown);
        Assert.Equal("ICommandContext.GetTagLatestSortableUniqueIdAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
    }

    [Fact]
    public async Task GetStateAsync_WhenAnInternalPathReturnsNoTask_NamesTheProjectorAndTheTag()
    {
        var thrown = await Assert.ThrowsAsync<SekibanBoundaryException>(
            () => new CommandContextAdapter(new NullReturningCoreCommandContext()).GetStateAsync<StudentProjector>(Tag));

        Assert.Equal("ICommandContext.GetStateAsync", thrown.Operation);
        Assert.Equal($"{nameof(StudentProjector)} on {Tag.GetTag()}", thrown.Target);
    }

    [Fact]
    public async Task AppendEvent_WhenAnInternalPathReturnsNoTask_NamesTheEvent()
    {
        var payload = new StudentCreated(Tag.StudentId, "Test Student", 2);

        var thrown = await Assert.ThrowsAsync<SekibanBoundaryException>(
            () => new CommandContextAdapter(new NullReturningCoreCommandContext()).AppendEvent(payload, Tag));

        Assert.Equal("ICommandContext.AppendEvent", thrown.Operation);
        Assert.Equal(nameof(StudentCreated), thrown.Target);
    }

    /// <summary>Every operation fails with the exception it was handed.</summary>
    private sealed class FailingCoreCommandContext(Exception failure) : ICoreCommandContext
    {
        public Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
            where TState : ITagStatePayload where TProjector : ITagProjector<TProjector> =>
            Task.FromException<TagStateTyped<TState>>(failure);

        public Task<TagState> GetStateAsync<TProjector>(ITag tag)
            where TProjector : ITagProjector<TProjector> =>
            Task.FromException<TagState>(failure);

        public Task<bool> TagExistsAsync(ITag tag) => Task.FromException<bool>(failure);

        public Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag) => Task.FromException<string>(failure);

        public Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags) =>
            Task.FromException<EventOrNone>(failure);

        public Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags) =>
            Task.FromException<EventOrNone>(failure);
    }

    /// <summary>
    ///     Every operation returns no Task at all. This is the internal-bug shape the WithoutResult facade could not
    ///     describe before: it dereferenced the null and the caller got a NullReferenceException.
    /// </summary>
    private sealed class NullReturningCoreCommandContext : ICoreCommandContext
    {
        public Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag)
            where TState : ITagStatePayload where TProjector : ITagProjector<TProjector> => null!;

        public Task<TagState> GetStateAsync<TProjector>(ITag tag)
            where TProjector : ITagProjector<TProjector> => null!;

        public Task<bool> TagExistsAsync(ITag tag) => null!;

        public Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag) => null!;

        public Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags) => null!;

        public Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags) => null!;
    }
}
