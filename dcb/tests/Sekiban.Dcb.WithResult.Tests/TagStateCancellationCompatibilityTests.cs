using System.Reflection;
using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
using CoreTagStateService = Sekiban.Dcb.Services.TagStateService;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using SqliteTagStateService = Sekiban.Dcb.Sqlite.Services.TagStateService;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     API and dispatch contract for SEK-G55. The token-less implementations below deliberately model downstream code
///     compiled before the additive members existed: if a default body is deleted or made abstract, this fixture no
///     longer builds; if it stops delegating, the exactly-once assertions fail.
/// </summary>
public class TagStateCancellationCompatibilityTests
{
    [Fact]
    public async Task TokenlessActorAndExecutor_UseTheDefaultCompatibilityFallbackExactlyOnce()
    {
        ITagStateActorCommon actor = new TokenlessActor();
        var actorState = await actor.GetStateAsync(new CancellationTokenSource().Token);
        Assert.Equal(1, ((TokenlessActor)actor).GetStateCalls);
        Assert.Equal("Legacy", actorState.TagGroup);

        ISekibanExecutor executor = new TokenlessExecutor();
        var tagState = await executor.GetTagStateAsync(
            TagStateId.Parse("Legacy:executor:Projector"),
            new CancellationTokenSource().Token);
        Assert.True(tagState.IsSuccess, tagState.IsSuccess ? string.Empty : tagState.GetException().ToString());
        Assert.Equal(1, ((TokenlessExecutor)executor).GetTagStateCalls);
    }

    [Fact]
    public async Task Frozen_1018_actor_binary_loads_and_default_cancellation_member_delegates_once()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Sekiban.Dcb.TagStateCancellation.Legacy1018Fixture.dll");
        var assembly = Assembly.LoadFrom(fixturePath);
        var type = assembly.GetType(
            "Sekiban.Dcb.TagStateCancellation.Legacy1018Fixture.Legacy1018TagStateActor",
            throwOnError: true)!;
        var concrete = Activator.CreateInstance(type)!;
        var actor = Assert.IsAssignableFrom<ITagStateActorCommon>(concrete);

        var state = await actor.GetStateAsync(new CancellationTokenSource().Token);

        Assert.Equal("Legacy", state.TagGroup);
        Assert.Equal(1, (int)type.GetProperty("GetStateCalls")!.GetValue(concrete)!);
    }

    [Fact]
    public async Task BuiltInResultExecutor_PreservesTheCallerTokenToTheActor()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var actor = new TokenTrackingActor();
        var executor = new Sekiban.Dcb.Actors.GeneralSekibanExecutor(
            new CoreInMemoryEventStore(domainTypes.EventTypes),
            new TokenTrackingActorAccessor(actor),
            domainTypes);
        using var cancellation = new CancellationTokenSource();

        var result = await executor.GetTagStateAsync(
            TagStateId.Parse("Student:relay:StudentProjector"),
            cancellation.Token);

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(cancellation.Token, actor.ReceivedCancellationToken);
    }

    [Fact]
    public void PublicSurface_PreservesLegacyMembers_AndAddsOnlyTheExpectedCancellationOverloads()
    {
        AssertDefaultMember(typeof(ITagStateActorCommon), "GetStateAsync", typeof(CancellationToken));
        AssertMethod(typeof(ITagStateActorCommon), "GetStateAsync");
        AssertMethod(typeof(ISekibanExecutor), "GetTagStateAsync", typeof(TagStateId));
        AssertDefaultMember(typeof(ISekibanExecutor), "GetTagStateAsync", typeof(TagStateId), typeof(CancellationToken));
        Assert.Contains(
            typeof(ISekibanExecutor).GetMethods(),
            method => method.Name == "GetTagStateAsync" && method.IsGenericMethodDefinition &&
                      method.GetParameters().Select(parameter => parameter.ParameterType)
                          .SequenceEqual(new[] { typeof(ITag), typeof(CancellationToken) }) && !method.IsAbstract);

        // Orleans cancellation must be a real grain method, not a default interface member. Existing token-less calls
        // remain present for C# binary compatibility.
        AssertMethod(typeof(ITagStateGrain), "GetStateAsync");
        AssertMethod(typeof(ITagStateGrain), "GetTagStateAsync");
        AssertAbstractGrainMethod(typeof(ITagStateGrain), "GetStateAsync", typeof(CancellationToken));
        AssertAbstractGrainMethod(typeof(ITagStateGrain), "GetTagStateAsync", typeof(CancellationToken));

        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(string), typeof(string));
        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(string));
        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(ITag));
        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(ITag), typeof(string));
        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(string), typeof(string), typeof(CancellationToken));
        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(string), typeof(CancellationToken));
        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(ITag), typeof(CancellationToken));
        AssertMethod(typeof(CoreTagStateService), "ProjectTagStateAsync", typeof(ITag), typeof(string), typeof(CancellationToken));
        AssertMethod(typeof(SqliteTagStateService), "ProjectTagStateAsync", typeof(string), typeof(string), typeof(CancellationToken));
        AssertMethod(typeof(SqliteTagStateService), "ProjectTagStateAsync", typeof(string), typeof(CancellationToken));
        AssertMethod(typeof(SqliteTagStateService), "ProjectTagStateAsync", typeof(ITag), typeof(CancellationToken));
        AssertMethod(typeof(SqliteTagStateService), "ProjectTagStateAsync", typeof(ITag), typeof(string), typeof(CancellationToken));

        Assert.Null(typeof(CoreTagStateService).GetMethod(
            "GetLatestTagStateAsync",
            [typeof(ITag), typeof(CancellationToken)]));
        Assert.Null(typeof(CoreTagStateService).GetMethod(
            "GetLatestTagStateByStringAsync",
            [typeof(string), typeof(CancellationToken)]));
    }

    [Theory]
    [InlineData(typeof(Sekiban.Dcb.Actors.GeneralSekibanExecutor))]
    [InlineData(typeof(Sekiban.Dcb.Orleans.OrleansDcbExecutor))]
    public void BuiltInResultExecutors_OverrideTheCancellationRelay(Type executorType)
    {
        Assert.NotNull(executorType.GetMethod(
            "GetTagStateAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            [typeof(TagStateId), typeof(CancellationToken)]));
    }

    private static void AssertDefaultMember(Type type, string name, params Type[] parameterTypes)
    {
        var method = type.GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        Assert.False(method!.IsAbstract);
        Assert.NotNull(method.GetMethodBody());
    }

    private static void AssertAbstractGrainMethod(Type type, string name, params Type[] parameterTypes)
    {
        var method = type.GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        Assert.True(method!.IsAbstract);
        Assert.Null(method.GetMethodBody());
    }

    private static void AssertMethod(Type type, string name, params Type[] parameterTypes) =>
        Assert.NotNull(type.GetMethod(name, parameterTypes));

    private sealed class TokenlessActor : ITagStateActorCommon
    {
        public int GetStateCalls { get; private set; }

        public Task<SerializableTagState> GetStateAsync()
        {
            GetStateCalls++;
            return Task.FromResult(new SerializableTagState(
                Array.Empty<byte>(),
                0,
                string.Empty,
                "Legacy",
                "actor",
                "Projector",
                nameof(EmptyTagStatePayload),
                string.Empty,
                nameof(EmptyTagStatePayload)));
        }

        public Task<string> GetTagStateActorIdAsync() => Task.FromResult("Legacy:actor:Projector");
    }

    private sealed class TokenlessExecutor : ISekibanExecutor
    {
        public int GetTagStateCalls { get; private set; }

        public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId)
        {
            GetTagStateCalls++;
            return Task.FromResult(ResultBox.FromValue(TagState.GetEmpty(tagStateId)));
        }

        public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
            TCommand command,
            Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
            CancellationToken cancellationToken = default) where TCommand : ICommand =>
            throw new NotSupportedException();

        public Task<ResultBox<ExecutionResult>> ExecuteCommandAsync(
            Func<ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
            TCommand command,
            CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
            throw new NotSupportedException();

        public Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
            throw new NotSupportedException();

        public Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
            where TResult : notnull =>
            throw new NotSupportedException();

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();

        public Task<ResultBox<ProjectionHeadStatus>> GetProjectionHeadStatusAsync(
            string projectorName,
            string? expectedProjectorVersion = null) =>
            throw new NotSupportedException();

        public Task<ResultBox<EventStoreHeadStatus>> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false) =>
            throw new NotSupportedException();
    }

    private sealed class TokenTrackingActor : ITagStateActorCommon
    {
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<SerializableTagState> GetStateAsync() =>
            Task.FromResult(EmptyState());

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
