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

public class AggregateTest<TAggregatePayload, TDependencyDefinition> : IDisposable,
    IAggregateTestHelper<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload, new()
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

    public AggregateTest(IServiceProvider serviceProvider, Guid aggregateId) : this(serviceProvider) =>
        _helper = new AggregateTestHelper<TAggregatePayload>(_serviceProvider, aggregateId);

    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction) => _helper.GivenScenario(initialAction);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev) => _helper.GivenEnvironmentEvent(ev);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events) => _helper.GivenEnvironmentEvents(events);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename) => _helper.GivenEnvironmentEventsFile(filename);

    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new() =>
        _helper.GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(aggregateId);

    public Guid RunEnvironmentCommand<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new() =>
        _helper.RunEnvironmentCommand(command, injectingAggregateId);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev) => _helper.GivenEnvironmentEventWithPublish(ev);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events) =>
        _helper.GivenEnvironmentEventsWithPublish(events);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename) =>
        _helper.GivenEnvironmentEventsFileWithPublish(filename);

    public Guid RunEnvironmentCommandWithPublish<TEnvironmentAggregatePayload>(
        ICommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new() =>
        _helper.RunEnvironmentCommandWithPublish(command, injectingAggregateId);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(
        Action<TestCommandExecutor> action)
    {
        _helper.GivenEnvironmentCommandExecutorAction(action);
        return this;
    }

    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents() => _helper.GetLatestEnvironmentEvents();

    public List<IEvent> GetLatestEvents() => _helper.GetLatestEvents();

    public List<IEvent> GetAllAggregateEvents(int? toVersion = null) => _helper.GetAllAggregateEvents(toVersion);

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<C>(C command) where C : ICommand<TAggregatePayload> => _helper.WhenCommand(command);

    public IAggregateTestHelper<TAggregatePayload> WhenCommand<C>(
        Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ICommand<TAggregatePayload> => _helper.WhenCommand(commandFunc);

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<C>(C changeCommand)
        where C : ICommand<TAggregatePayload> => _helper.WhenCommandWithPublish(changeCommand);

    public IAggregateTestHelper<TAggregatePayload> WhenCommandWithPublish<C>(
        Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ICommand<TAggregatePayload> => _helper.WhenCommandWithPublish(commandFunc);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction) =>
        _helper.ThenGetLatestEvents(checkEventsAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction) =>
        _helper.ThenGetAllAggregateEvents(checkEventsAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction)
        where T : IEventPayloadCommon => _helper.ThenGetLatestSingleEvent(checkEventAction);

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event)
        where T : IEventPayloadCommon => _helper.ThenLastSingleEventIs(@event);

    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload)
        where T : IEventPayloadCommon => _helper.ThenLastSingleEventPayloadIs(payload);

    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction)
        where T : class, IEventPayloadCommon => _helper.ThenGetLatestSingleEventPayload(checkPayloadAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetState(
        Action<AggregateState<TAggregatePayload>> checkStateAction) => _helper.ThenGetState(checkStateAction);

    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState) => _helper.ThenStateIs(expectedState);

    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction) => _helper.ThenGetPayload(payloadAction);

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload) => _helper.ThenPayloadIs(payload);

    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename) => _helper.WriteStateToFile(filename);

    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename) => _helper.WriteStateToFile(filename);

    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson) => _helper.ThenStateIsFromJson(stateJson);

    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName) => _helper.ThenStateIsFromFile(stateFileName);

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson) => _helper.ThenPayloadIsFromJson(payloadJson);

    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName) => _helper.ThenPayloadIsFromFile(payloadFileName);

    public Guid GetAggregateId() => _helper.GetAggregateId();

    public int GetCurrentVersion() => _helper.GetCurrentVersion();

    public AggregateState<TAggregatePayload> GetAggregateState() => _helper.GetAggregateState();

    public Aggregate<TAggregatePayload> GetAggregate() => _helper.GetAggregate();

    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception => _helper.ThenThrows<T>();

    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception =>
        _helper.ThenGetException(checkException);

    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException) => _helper.ThenGetException(checkException);

    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException() => _helper.ThenNotThrowsAnException();

    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException() => _helper.ThenThrowsAnException();

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors) => _helper.ThenHasValidationErrors(validationParameterErrors);

    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors() => _helper.ThenHasValidationErrors();

    public void Dispose()
    {
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
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() => _helper.ThenSingleProjectionStateIs(state);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(
        TSingleProjectionPayload payload)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() => _helper.ThenSingleProjectionPayloadIs(payload);

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction) where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        _helper.ThenGetSingleProjectionPayload(payloadAction);

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() => _helper.ThenGetSingleProjectionState(stateAction);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(
        string payloadJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        _helper.ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(payloadJson);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(
        string payloadFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        _helper.ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(payloadFilename);

    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new() =>
        _helper.WriteSingleProjectionStateToFile<TSingleProjectionPayload>(filename);
    #endregion

    #region Aggregate Query
    public IAggregateTestHelper<TAggregatePayload> WriteAggregateQueryResponseToFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse =>
        _helper.WriteAggregateQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(param, filename);

    public IAggregateTestHelper<TAggregatePayload>
        ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(
            TQueryParameter param,
            TQueryResponse expectedResponse)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse =>
        _helper.ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(param, expectedResponse);

    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateQueryResponse<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse =>
        _helper.ThenGetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param, responseAction);

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromJson<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper.ThenAggregateQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(
        param,
        responseJson);

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper.ThenAggregateQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(
        param,
        responseFilename);
    #endregion

    #region Aggregate　List Query
    public IAggregateTestHelper<TAggregatePayload> WriteAggregateListQueryResponseToFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
        =>
            _helper.WriteAggregateListQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(param, filename);

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIs<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
        =>
            _helper.ThenAggregateListQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(
                param,
                expectedResponse);

    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateListQueryResponse<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
        => _helper
            .ThenGetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(param, responseAction);

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromJson<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
        =>
            _helper.ThenAggregateListQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(
                param,
                responseJson);

    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromFile<TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse
        =>
            _helper.ThenAggregateListQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(
                param,
                responseFilename);
    #endregion

    #region SingleProjection Query
    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>(param, filename);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery,
        TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            expectedResponse);

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            responseAction);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>(param, responseJson);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>(
            param,
            responseFilename);
    #endregion

    #region SingleProjection　List Query
    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionListQueryResponseToFile<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .WriteSingleProjectionListQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>(param, filename);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter, TQueryResponse>(
            param,
            expectedResponse);

    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload,
        TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>(param, responseAction);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromJson<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenSingleProjectionListQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>(
            param,
            responseJson);

    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromFile<
        TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IListQueryParameter<TQueryResponse>
        where TQueryResponse : IQueryResponse => _helper
        .ThenSingleProjectionListQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
            TQueryResponse>(
            param,
            responseFilename);
    #endregion
}
