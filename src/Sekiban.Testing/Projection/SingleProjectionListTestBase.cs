using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
using Sekiban.Testing.Command;
using Sekiban.Testing.Queries;
using System.Text.Json;
using Xunit;
namespace Sekiban.Testing.Projection;

public class
    // ReSharper disable once ClassNeverInstantiated.Global
    SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> : IMultiProjectTestBase
    where TAggregatePayload : IAggregatePayload, new()
    where TSingleProjection : SingleProjectionBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>, new()
    where TSingleProjectionPayload : ISingleProjectionPayload
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly TestCommandExecutor _commandExecutor;
    private readonly TestEventHandler _eventHandler;
    protected IServiceProvider _serviceProvider;

    public SingleProjectionListTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
        _eventHandler = new TestEventHandler(_serviceProvider);
    }
    public SingleProjectionListTestBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _commandExecutor = new TestCommandExecutor(_serviceProvider);
        _eventHandler = new TestEventHandler(_serviceProvider);
    }



    public MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> State { get; protected set; }
        = new(new SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>(), Guid.Empty, string.Empty, 0, 0);
    protected Exception? _latestException { get; set; }



    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
            TDependencyDefinition>
        WhenProjection()
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider not set");
        }
        var multiProjectionService
            = _serviceProvider.GetRequiredService(typeof(IMultiProjectionService)) as IMultiProjectionService;
        if (multiProjectionService is null) { throw new Exception("Failed to get multiProjectionService "); }
        try
        {
            State = multiProjectionService
                .GetSingleProjectionListObject<TAggregatePayload, TSingleProjection, TSingleProjectionPayload>()
                .Result;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        return this;
    }

    public void Dispose()
    {
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenPayloadIsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection = JsonSerializer.Deserialize<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenPayloadIs(projection);
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetPayload(Action<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> payloadAction)
    {
        payloadAction(State.Payload);
        return this;
    }

    public Guid RunCreateCommand<TCommandAggregatePayload>(ICreateCommand<TCommandAggregatePayload> command, Guid? injectingAggregateId = null)
        where TCommandAggregatePayload : IAggregatePayload, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommand(command, injectingAggregateId);
        return aggregateId;
    }
    public void RunChangeCommand<TCommandAggregatePayload>(ChangeCommandBase<TCommandAggregatePayload> command)
        where TCommandAggregatePayload : IAggregatePayload, new()
    {
        var events = _commandExecutor.ExecuteChangeCommand(command);
    }
    public Guid RunCreateCommandWithPublish<TCommandAggregatePayload>(
        ICreateCommand<TCommandAggregatePayload> command,
        Guid? injectingAggregateId = null)
        where TCommandAggregatePayload : IAggregatePayload, new()
    {
        var (events, aggregateId) = _commandExecutor.ExecuteCreateCommandWithPublish(command, injectingAggregateId);
        return aggregateId;
    }
    public void RunChangeCommandWithPublish<TCommandAggregatePayload>(ChangeCommandBase<TCommandAggregatePayload> command)
        where TCommandAggregatePayload : IAggregatePayload, new()
    {
        var events = _commandExecutor.ExecuteChangeCommandWithPublish(command);
    }

    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        var singleProjectionService = _serviceProvider.GetRequiredService(typeof(ISingleProjectionService)) as ISingleProjectionService;
        if (singleProjectionService is null) { throw new Exception("Failed to get single aggregate service"); }
        var aggregate = singleProjectionService.GetAggregateStateAsync<TEnvironmentAggregatePayload>(aggregateId).Result;
        return aggregate ?? throw new SekibanAggregateNotExistsException(aggregateId, typeof(TEnvironmentAggregatePayload).Name);
    }
    public IReadOnlyCollection<IEvent> GetLatestEvents() => _commandExecutor.LatestEvents;
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenNotThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.Null(exception);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenThrowsAnException()
    {
        var exception = _latestException is AggregateException aggregateException ? aggregateException.InnerExceptions.First() : _latestException;
        Assert.NotNull(exception);
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetState(
        Action<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>> stateAction)
    {
        stateAction(State);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenStateIs(
        MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>> state)
    {
        var actual = State;
        var expected = state with { LastEventId = actual.LastEventId, LastSortableUniqueId = actual.LastSortableUniqueId, Version = actual.Version };
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenPayloadIs(SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>> payload)
    {
        var actual = State.Payload;
        var expected = payload;
        var actualJson = SekibanJsonHelper.Serialize(actual);
        var expectedJson = SekibanJsonHelper.Serialize(expected);
        Assert.Equal(expectedJson, actualJson);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenStateIsFromFile(string filename)
    {
        using var openStream = File.OpenRead(filename);
        var projection =
            JsonSerializer.Deserialize<MultiProjectionState<SingleProjectionListState<SingleProjectionState<TSingleProjectionPayload>>>>(openStream);
        if (projection is null) { throw new InvalidDataException("JSON のでシリアライズに失敗しました。"); }
        return ThenStateIs(projection);
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> WriteProjectionToFile(string filename)
    {
        var json = SekibanJsonHelper.Serialize(State);
        File.WriteAllTextAsync(filename, json);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenQueryTest(
        IQueryTest test)
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        test.QueryService = _serviceProvider.GetService<IQueryService>();
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenScenario(Action initialAction)
    {
        initialAction();
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenCommandExecutorAction(
        Action<TestCommandExecutor> action)
    {
        action(_commandExecutor);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetQueryTest<TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TQuery : ISingleProjectionQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new SingleProjectionQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> ThenGetListQueryTest<TQuery, TQueryParameter, TQueryResponse>(
        Action<SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection, TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>> queryTestAction)
        where TQuery : ISingleProjectionListQuery<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        var queryTest = new SingleProjectionListQueryTest<TAggregatePayload, TSingleProjection,
            TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>();
        GivenQueryTest(queryTest);
        queryTestAction(queryTest);
        return this;
    }
    public T GetService<T>() where T : notnull
    {
        if (_serviceProvider is null) { throw new Exception("Service provider is null. Please setup service provider."); }
        return _serviceProvider.GetRequiredService<T>() ?? throw new Exception($"Service {typeof(T)} not found");
    }
    
        #region GivenEvents
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEvents(events);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsWithPublish(IEnumerable<IEvent> events)
    {
        _eventHandler.GivenEventsWithPublish(events);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(params IEvent[] events) =>
        GivenEvents(events.AsEnumerable());
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsWithPublish(params IEvent[] events) =>
        GivenEventsWithPublish(events.AsEnumerable());
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsFromJson(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJson(jsonEvents);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsFromJsonWithPublish(string jsonEvents)
    {
        _eventHandler.GivenEventsFromJsonWithPublish(jsonEvents);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples)
    {
        _eventHandler.GivenEvents(eventTouples);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples)
    {
        _eventHandler.GivenEventsWithPublish(eventTouples);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples)
    {
        _eventHandler.GivenEvents(eventTouples);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsWithPublish(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples)
    {
        _eventHandler.GivenEventsWithPublish(eventTouples);
        return this;
    }

    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsFromFile(string filename)
    {
        _eventHandler.GivenEventsFromFile(filename);
        return this;
    }
    public SingleProjectionListTestBase<TAggregatePayload, TSingleProjection, TSingleProjectionPayload,
        TDependencyDefinition> GivenEventsFromFileWithPublish(string filename)
    {
        _eventHandler.GivenEventsFromFileWithPublish(filename);
        return this;
    }
    #endregion

    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {

    }

    private void ResetBeforeCommand()
    {
        _latestException = null;
    }
}
