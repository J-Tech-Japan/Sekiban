using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Validation;
using Sekiban.Testing.Command;
namespace Sekiban.Testing.SingleProjections;

public interface IAggregateTestHelper<TAggregatePayload> where TAggregatePayload : IAggregatePayload, new()
{
    #region given and setup
    public IAggregateTestHelper<TAggregatePayload> GivenScenario(Action initialAction);

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvent(IEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEvents(IEnumerable<IEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFile(string filename);
    public Guid RunEnvironmentCreateCommand<TEnvironmentAggregatePayload>(
        ICreateCommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new();
    public void RunEnvironmentChangeCommand<TEnvironmentAggregatePayload>(ChangeCommandBase<TEnvironmentAggregatePayload> command)
        where TEnvironmentAggregatePayload : IAggregatePayload, new();

    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventWithPublish(IEvent ev);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsWithPublish(IEnumerable<IEvent> events);
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentEventsFileWithPublish(string filename);
    public Guid RunEnvironmentCreateCommandWithPublish<TEnvironmentAggregatePayload>(
        ICreateCommand<TEnvironmentAggregatePayload> command,
        Guid? injectingAggregateId = null) where TEnvironmentAggregatePayload : IAggregatePayload, new();
    public void RunEnvironmentChangeCommandWithPublish<TEnvironmentAggregatePayload>(ChangeCommandBase<TEnvironmentAggregatePayload> command)
        where TEnvironmentAggregatePayload : IAggregatePayload, new();
    public IAggregateTestHelper<TAggregatePayload> GivenEnvironmentCommandExecutorAction(Action<TestCommandExecutor> action);
    #endregion

    #region When
    public IAggregateTestHelper<TAggregatePayload> WhenCreate<C>(C createCommand) where C : ICreateCommand<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(C changeCommand) where C : ChangeCommandBase<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenChange<C>(Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ChangeCommandBase<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenCreateWithPublish<C>(C createCommand) where C : ICreateCommand<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenChangeWithPublish<C>(C changeCommand) where C : ChangeCommandBase<TAggregatePayload>;
    public IAggregateTestHelper<TAggregatePayload> WhenChangeWithPublish<C>(Func<AggregateState<TAggregatePayload>, C> commandFunc)
        where C : ChangeCommandBase<TAggregatePayload>;
    #endregion

    #region Then
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestEvents(Action<List<IEvent>> checkEventsAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetAllAggregateEvents(Action<List<IEvent>> checkEventsAction);
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEvent<T>(Action<Event<T>> checkEventAction) where T : IEventPayload;
    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventIs<T>(Event<T> @event) where T : IEventPayload;
    public IAggregateTestHelper<TAggregatePayload> ThenLastSingleEventPayloadIs<T>(T payload) where T : IEventPayload;
    public IAggregateTestHelper<TAggregatePayload> ThenGetLatestSingleEventPayload<T>(Action<T> checkPayloadAction) where T : class, IEventPayload;
    public IAggregateTestHelper<TAggregatePayload> ThenGetState(Action<AggregateState<TAggregatePayload>> checkStateAction);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIs(AggregateState<TAggregatePayload> expectedState);
    public IAggregateTestHelper<TAggregatePayload> ThenGetPayload(Action<TAggregatePayload> payloadAction);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIs(TAggregatePayload payload);
    public IAggregateTestHelper<TAggregatePayload> WriteStateToFile(string filename);
    public IAggregateTestHelper<TAggregatePayload> WritePayloadToFile(string filename);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromJson(string stateJson);
    public IAggregateTestHelper<TAggregatePayload> ThenStateIsFromFile(string stateFileName);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromJson(string payloadJson);
    public IAggregateTestHelper<TAggregatePayload> ThenPayloadIsFromFile(string payloadFileName);
    public IAggregateTestHelper<TAggregatePayload> ThenThrows<T>() where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenGetException<T>(Action<T> checkException) where T : Exception;
    public IAggregateTestHelper<TAggregatePayload> ThenGetException(Action<Exception> checkException);
    public IAggregateTestHelper<TAggregatePayload> ThenNotThrowsAnException();
    public IAggregateTestHelper<TAggregatePayload> ThenThrowsAnException();
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors(
        IEnumerable<SekibanValidationParameterError> validationParameterErrors);
    public IAggregateTestHelper<TAggregatePayload> ThenHasValidationErrors();
    #endregion

    #region Get
    public Guid GetAggregateId();
    public int GetCurrentVersion();
    public AggregateState<TAggregatePayload> GetAggregateState();
    public Aggregate<TAggregatePayload> GetAggregate();
    public AggregateState<TEnvironmentAggregatePayload>
        GetEnvironmentAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new();
    public IReadOnlyCollection<IEvent> GetLatestEnvironmentEvents();
    public List<IEvent> GetLatestEvents();
    public List<IEvent> GetAllAggregateEvents();
    #endregion

    #region Single Projection
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionStateIs<TSingleProjectionPayload>(
        SingleProjectionState<TSingleProjectionPayload> state) where TSingleProjectionPayload : ISingleProjectionPayload, new();
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIs<TSingleProjectionPayload>(TSingleProjectionPayload payload)
        where TSingleProjectionPayload : ISingleProjectionPayload, new();
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionPayload<TSingleProjectionPayload>(
        Action<TSingleProjectionPayload> payloadAction) where TSingleProjectionPayload : ISingleProjectionPayload, new();
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionState<TSingleProjectionPayload>(
        Action<SingleProjectionState<TSingleProjectionPayload>> stateAction) where TSingleProjectionPayload : ISingleProjectionPayload, new();
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromJson<TSingleProjectionPayload>(string payloadJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new();
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionPayloadIsFromFile<TSingleProjectionPayload>(string payloadFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new();
    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionStateToFile<TSingleProjectionPayload>(string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new();
    #endregion

    #region Aggregate Query
    public IAggregateTestHelper<TAggregatePayload> WriteAggregateQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateQueryResponse<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    #endregion
    #region Aggregate　List Query
    public IAggregateTestHelper<TAggregatePayload> WriteAggregateListQueryResponseToFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIs<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenGetAggregateListQueryResponse<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromJson<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseJson) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenAggregateListQueryResponseIsFromFile<TQuery, TQueryParameter, TQueryResponse>(
        TQueryParameter param,
        string responseFilename) where TQuery : IAggregateListQuery<TAggregatePayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    #endregion

    #region SingleProjection Query
    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        TQueryResponse expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<TQueryResponse> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    #endregion

    #region SingleProjection　List Query
    public IAggregateTestHelper<TAggregatePayload> WriteSingleProjectionListQueryResponseToFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string filename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIs<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        ListQueryResult<TQueryResponse> expectedResponse)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenGetSingleProjectionListQueryResponse<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        Action<ListQueryResult<TQueryResponse>> responseAction)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromJson<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseJson)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    public IAggregateTestHelper<TAggregatePayload> ThenSingleProjectionListQueryResponseIsFromFile<TSingleProjectionPayload, TQuery, TQueryParameter,
        TQueryResponse>(
        TQueryParameter param,
        string responseFilename)
        where TSingleProjectionPayload : ISingleProjectionPayload, new()
        where TQuery : ISingleProjectionListQuery<TSingleProjectionPayload, TQueryParameter, TQueryResponse>
        where TQueryParameter : IQueryParameter;
    #endregion
}
