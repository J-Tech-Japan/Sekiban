using System.Reflection;
using Dcb.Domain.WithoutResult;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Exception-surface compatibility checks for SEK-G55. The token-less implementation is intentionally complete
///     enough to compile as a downstream pre-change implementor, exercising the default interface fallback.
/// </summary>
public class TagStateCancellationCompatibilityTests
{
    [Fact]
    public async Task TokenlessExecutor_UsesTheDefaultCompatibilityFallbackExactlyOnce()
    {
        ISekibanExecutor executor = new TokenlessExecutor();
        var state = await executor.GetTagStateAsync(
            TagStateId.Parse("Legacy:without-result:Projector"),
            new CancellationTokenSource().Token);

        Assert.Equal("Legacy", state.TagGroup);
        Assert.Equal(1, ((TokenlessExecutor)executor).GetTagStateCalls);
    }

    [Fact]
    public async Task GuardedUnwrap_RethrowsTheOriginalCancellationException()
    {
        var cancellation = new OperationCanceledException("expected cancellation");
        var result = Task.FromResult(ResultBox.Error<TagState>(cancellation));

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await GuardedUnwrap.UnwrapAsync(
                result,
                new BoundaryContext("ISekibanExecutor.GetTagStateAsync", "Legacy:without-result:Projector")));

        Assert.Same(cancellation, observed);
    }

    [Fact]
    public async Task BuiltInExceptionExecutor_PreservesTheCallerTokenToTheActor()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var actor = new TokenTrackingActor();
        var executor = new Sekiban.Dcb.Actors.GeneralSekibanExecutor(
            new CoreInMemoryEventStore(domainTypes.EventTypes),
            new TokenTrackingActorAccessor(actor),
            domainTypes);
        using var cancellation = new CancellationTokenSource();

        var state = await executor.GetTagStateAsync(
            TagStateId.Parse("Student:relay:StudentProjector"),
            cancellation.Token);

        Assert.Equal("Student", state.TagGroup);
        Assert.Equal(cancellation.Token, actor.ReceivedCancellationToken);
    }

    [Theory]
    [InlineData(typeof(Sekiban.Dcb.Actors.GeneralSekibanExecutor))]
    [InlineData(typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor))]
    public void BuiltInExceptionExecutors_OverrideTheCancellationRelay(Type executorType)
    {
        var method = executorType.GetMethod(
            "GetTagStateAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            [typeof(TagStateId), typeof(CancellationToken)]);
        Assert.NotNull(method);
    }

    [Fact]
    public void CancellationMember_RemainsADefaultInterfaceMember()
    {
        var method = typeof(ISekibanExecutor).GetMethod(
            "GetTagStateAsync",
            [typeof(TagStateId), typeof(CancellationToken)]);
        Assert.NotNull(method);
        Assert.False(method!.IsAbstract);
        Assert.NotNull(method.GetMethodBody());
    }

    private sealed class TokenlessExecutor : ISekibanExecutor
    {
        public int GetTagStateCalls { get; private set; }

        public Task<TagState> GetTagStateAsync(TagStateId tagStateId)
        {
            GetTagStateCalls++;
            return Task.FromResult(TagState.GetEmpty(tagStateId));
        }

        public Task<ExecutionResult> ExecuteAsync<TCommand>(
            TCommand command,
            Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
            CancellationToken cancellationToken = default) where TCommand : ICommand =>
            throw new NotSupportedException();

        public Task<ExecutionResult> ExecuteCommandAsync(
            Func<ICommandContext, Task<EventOrNone>> handlerFunc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ExecutionResult> ExecuteAsync<TCommand>(
            TCommand command,
            CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
            throw new NotSupportedException();

        public Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
            throw new NotSupportedException();

        public Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
            where TResult : notnull =>
            throw new NotSupportedException();

        public Task<string> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();

        public Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync(
            string projectorName,
            string? expectedProjectorVersion = null) =>
            throw new NotSupportedException();

        public Task<EventStoreHeadStatus> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false) =>
            throw new NotSupportedException();
    }

    private sealed class TokenTrackingActor : ITagStateActorCommon
    {
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<SerializableTagState> GetStateAsync() => Task.FromResult(EmptyState());

        public Task<SerializableTagState> GetStateAsync(CancellationToken cancellationToken)
        {
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(EmptyState());
        }

        public Task<string> GetTagStateActorIdAsync() => Task.FromResult("Student:relay:StudentProjector");

        private static SerializableTagState EmptyState() =>
            new(
                Array.Empty<byte>(),
                0,
                string.Empty,
                "Student",
                "relay",
                "StudentProjector",
                nameof(EmptyTagStatePayload),
                string.Empty,
                nameof(EmptyTagStatePayload));
    }

    private sealed class TokenTrackingActorAccessor(TokenTrackingActor actor) : IActorObjectAccessor
    {
        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class =>
            typeof(T) == typeof(ITagStateActorCommon)
                ? Task.FromResult(ResultBox.FromValue((T)(object)actor))
                : Task.FromResult(ResultBox.Error<T>(new NotSupportedException()));

        public Task<bool> ActorExistsAsync(string actorId) => Task.FromResult(true);
    }
}
