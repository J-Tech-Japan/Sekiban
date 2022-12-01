using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;

namespace Sekiban.Testing.SingleProjections;

public class AggregateTestBase<TAggregatePayload, TDependencyDefinition> : IDisposable,
    IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    private readonly IAggregateTestHelper<TAggregatePayload> _helper;
    protected readonly IServiceProvider _serviceProvider;

    public AggregateTestBase()
    {
        var services = new ServiceCollection();
        // ReSharper disable once VirtualMemberCallInConstructor
        SetupDependency(services);
        services.AddQueriesFromDependencyDefinition(new TDependencyDefinition());
        services.AddSekibanCoreForAggregateTestWithDependency(new TDependencyDefinition());
        _serviceProvider = services.BuildServiceProvider();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }

    public AggregateTestBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new InvalidOperationException();
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider);
    }

    public AggregateTestBase(IServiceProvider serviceProvider, Guid aggregateId) : this(serviceProvider)
    {
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider, aggregateId);
    }

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
        where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        return _helper.GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(aggregateId);
    }

    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new()
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
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new()
    {
        return _helper.RunEnvironmentCommandWithPublish(command, injectingAggregateId);
    }

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(
        Action<TestCommandExecutor> action)
    {
        _helper.GivenEnvironmentCommandExecutorAction(action);
        return this;
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

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<C>(C command) where C : ICommand<TAggregatePayload>
    {
        return _helper.WhenCommand(command);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<C>(
        Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ICommand<TAggregatePayload>
    {
        return _helper.WhenCommand(commandFunc);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<C>(C changeCommand)
        where C : ICommand<TAggregatePayload>
    {
        return _helper.WhenCommandWithPublish(changeCommand);
    }

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<C>(
        Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ICommand<TAggregatePayload>
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
    }


    protected virtual void SetupDependency(IServiceCollection serviceCollection)
    {
    }

    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn is null) throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        return toreturn;
    }

    #region SingleProjection

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        return _helper.ThenSingleProjectionStateIs(state);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(
        TSingleProjectionPayload payload)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        return _helper.ThenSingleProjectionPayloadIs(payload);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction) where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        return _helper.ThenGetSingleProjectionPayload(payloadAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        return _helper.ThenGetSingleProjectionState(stateAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(
        string payloadJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        return _helper.ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(payloadJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(
        string payloadFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        return _helper.ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(payloadFilename);
    }

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
    {
        return _helper.WriteSingleProjectionStateToFile<TSingleProjectionPayload>(filename);
    }

    #endregion

    #region Aggregate Query

    public IAggregateTestHelper<TAggregatePayload> WriteAggregateQueryResponseToFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.WriteAggregateQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(param, filename);
    }

    public IAggregateTestHelper<TAggregatePayload>
        ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            TQueryResponse expectedResponse)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(param, expectedResponse);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateQueryResponse<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.ThenGetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param, responseAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromJson<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.ThenAggregateQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(param,
            responseJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.ThenAggregateQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(param,
            responseFilename);
    }

    #endregion

    #region Aggregate　List Query

    public IAggregateTestHelper<TAggregatePayload> WriteAggregateListQueryResponseToFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.WriteAggregateListQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(param, filename);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIs<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.ThenAggregateListQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(param,
            expectedResponse);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateListQueryResponse<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenGetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param, responseAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromJson<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.ThenAggregateListQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(param,
            responseJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper.ThenAggregateListQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(param,
            responseFilename);
    }

    #endregion

    #region SingleProjection Query

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(param, filename);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param, expectedResponse);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param, responseAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(param, responseJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(
                param,
                responseFilename);
    }

    #endregion

    #region SingleProjection　List Query

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionListQueryResponseToFile<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .WriteSingleProjectionListQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(param, filename);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
                param, expectedResponse);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(param, responseAction);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromJson<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenSingleProjectionListQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(
                param,
                responseJson);
    }

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromFile<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter
    {
        return _helper
            .ThenSingleProjectionListQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
                TQueryResponse>(
                param,
                responseFilename);
    }

    #endregion
}
