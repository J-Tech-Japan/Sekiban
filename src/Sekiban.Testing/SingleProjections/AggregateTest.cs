using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Events;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleProjections;

public class AggregateTest<TAggregatePayload, TDependencyDefinition> : IDisposable,
    IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadCommon
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly IAggregateTestHelper<TAggregatePayload> _helper;
    protected readonly IServiceProvider _serviceProvider;

    public AggregateTest()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }

    public AggregateTest(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new InvalidOperationException();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }

    public AggregateTest(IServiceProvider serviceProvider, Guid aggregateId) : this(serviceProvider)
    {
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider, aggregateId);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadTypeIs<TAggregatePayloadExpected>()
        where TAggregatePayloadExpected : IAggregatePayloadCommon
    {
        return _helper.ThenPayloadTypeIs<TAggregatePayloadExpected>();
    }
    public IAggregateTestHelper<TAggregateSubtypePayload> Subtype<TAggregateSubtypePayload>()
        where TAggregateSubtypePayload : IAggregatePayloadCommon, IApplicableAggregatePayload<TAggregatePayload>
    {
        return _helper.Subtype<TAggregateSubtypePayload>();
    }
    // public IAggregateTestHelper<TAggregatePayload> Subtype<TAggregateSubtypePayload>(
    //     Action<IAggregateTestHelper<TAggregateSubtypePayload>> subtypeTestHelperAction)
    //     where TAggregateSubtypePayload : IAggregatePayloadCommon, IApplicableAggregatePayload<TAggregatePayload> =>
    //     _helper.Subtype(subtypeTestHelperAction);

    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction)
    {
        return _helper.GivenScenario(initialAction);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev)
    {
        return _helper.GivenEnvironmentEvent(ev);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events)
    {
        return _helper.GivenEnvironmentEvents(events);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename)
    {
        return _helper.GivenEnvironmentEventsFile(filename);
    }

    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayloadCommon
    {
        return _helper.GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(aggregateId);
    }

    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon
    {
        return _helper.RunEnvironmentCommand(command, injectingAggregateId);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev)
    {
        return _helper.GivenEnvironmentEventWithPublish(ev);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events)
    {
        return _helper.GivenEnvironmentEventsWithPublish(events);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename)
    {
        return _helper.GivenEnvironmentEventsFileWithPublish(filename);
    }

    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayloadCommon
    {
        return _helper.RunEnvironmentCommandWithPublish(command, injectingAggregateId);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(
        Action<TestCommandExecutor> action)
    {
        _helper.GivenEnvironmentCommandExecutorAction(action);
        return this;
    }
    public void ThrowIfTestHasUnhandledErrors()
    {
        _helper.ThrowIfTestHasUnhandledErrors();
    }

    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents()
    {
        return _helper.GetLatestEnvironmentEvents();
    }

    public List<IEvent> GetLatestEvents()
    {
        return _helper.GetLatestEvents();
    }

    public List<IEvent> GetAllAggregateEvents(int? toVersion = null)
    {
        return _helper.GetAllAggregateEvents(toVersion);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(TCommand command) where TCommand : ICommand<TAggregatePayload>
    {
        return _helper.WhenCommand(command);
    }
    public IAggregateTestHelper<TAggregatePayload> WhenSubtypeCommand<TAggregateSubtypePayload, TCommand>(TCommand changeCommand)
        where TAggregateSubtypePayload : TAggregatePayload, IAggregatePayloadCommon where TCommand : ICommand<TAggregateSubtypePayload>
    {
        return _helper.WhenSubtypeCommand<TAggregateSubtypePayload, TCommand>(changeCommand);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommand<TAggregatePayload>
    {
        return _helper.WhenCommand(commandFunc);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(TCommand changeCommand)
        where TCommand : ICommand<TAggregatePayload>
    {
        return _helper.WhenCommandWithPublish(changeCommand);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<TCommand>(
        Func<AggregateState<TAggregatePayload>, TCommand> commandFunc)
        where TCommand : ICommand<TAggregatePayload>
    {
        return _helper.WhenCommandWithPublish(commandFunc);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction)
    {
        return _helper.ThenGetLatestEvents(checkEventsAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction)
    {
        return _helper.ThenGetAllAggregateEvents(checkEventsAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction)
        where T : IEventPayloadCommon
    {
        return _helper.ThenGetLatestSingleEvent(checkEventAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event)
        where T : IEventPayloadCommon
    {
        return _helper.ThenLastSingleEventIs(@event);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload)
        where T : IEventPayloadCommon
    {
        return _helper.ThenLastSingleEventPayloadIs(payload);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction)
        where T : class, IEventPayloadCommon
    {
        return _helper.ThenGetLatestSingleEventPayload(checkPayloadAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetState(
        Action<AggregateState<TAggregatePayload>> checkStateAction)
    {
        return _helper.ThenGetState(checkStateAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState)
    {
        return _helper.ThenStateIs(expectedState);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction)
    {
        return _helper.ThenGetPayload(payloadAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload)
    {
        return _helper.ThenPayloadIs(payload);
    }

    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename)
    {
        return _helper.WriteStateToFile(filename);
    }

    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename)
    {
        return _helper.WriteStateToFile(filename);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson)
    {
        return _helper.ThenStateIsFromJson(stateJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName)
    {
        return _helper.ThenStateIsFromFile(stateFileName);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson)
    {
        return _helper.ThenPayloadIsFromJson(payloadJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName)
    {
        return _helper.ThenPayloadIsFromFile(payloadFileName);
    }

    public Guid GetAggregateId()
    {
        return _helper.GetAggregateId();
    }

    public int GetCurrentVersion()
    {
        return _helper.GetCurrentVersion();
    }

    public AggregateState<TAggregatePayload> GetAggregateState()
    {
        return _helper.GetAggregateState();
    }

    public Aggregate<TAggregatePayload> GetAggregate()
    {
        return _helper.GetAggregate();
    }

    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception
    {
        return _helper.ThenThrows<T>();
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception
    {
        return _helper.ThenGetException(checkException);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException)
    {
        return _helper.ThenGetException(checkException);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException()
    {
        return _helper.ThenNotThrowsAnException();
    }

    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException()
    {
        return _helper.ThenThrowsAnException();
    }

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors)
    {
        return _helper.ThenHasValidationErrors(validationParameterErrors);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors()
    {
        return _helper.ThenHasValidationErrors();
    }

    public void Dispose()
    {
        ThrowIfTestHasUnhandledErrors();
    }


    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {
    }

    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn is null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }

    #region SingleProjection
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return _helper.ThenSingleProjectionStateIs(state);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(
        TSingleProjectionPayload payload)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return _helper.ThenSingleProjectionPayloadIs(payload);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction) where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return _helper.ThenGetSingleProjectionPayload(payloadAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return _helper.ThenGetSingleProjectionState(stateAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(
        string payloadJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return _helper.ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(payloadJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(
        string payloadFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return _helper.ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(payloadFilename);
    }

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
    {
        return _helper.WriteSingleProjectionStateToFile<TSingleProjectionPayload>(filename);
    }
    #endregion


    #region General List Query Test
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenQueryResponseIs(param, expectedResponse);
    }
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string filename)
        where TQueryResponse : IQueryResponse
    {
        return _helper.WriteQueryResponseToFile(param, filename);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenGetQueryResponse(param, responseAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenQueryResponseIsFromJson(param, responseJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IListQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenQueryResponseIsFromFile(param, responseFilename);
    }
    #endregion

    #region Query Test (not list)
    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIs<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        TQueryResponse expectedResponse)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenQueryResponseIs(param, expectedResponse);
    }
    public IAggregateTestHelper<TAggregatePayload> WriteQueryResponseToFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string filename)
        where TQueryResponse : IQueryResponse
    {
        return _helper.WriteQueryResponseToFile(param, filename);
    }
    public IAggregateTestHelper<TAggregatePayload> ThenGetQueryResponse<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        Action<TQueryResponse> responseAction)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenGetQueryResponse(param, responseAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromJson<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseJson)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenQueryResponseIsFromJson(param, responseJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenQueryResponseIsFromFile<TQueryResponse>(
        IQueryInput<TQueryResponse> param,
        string responseFilename)
        where TQueryResponse : IQueryResponse
    {
        return _helper.ThenQueryResponseIsFromFile(param, responseFilename);
    }
    #endregion
}
